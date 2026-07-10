using System;

namespace Daedalus.Rotation.Common.Helpers;

/// <summary>
/// The choreography phase of a coordinated tank swap for the local toon on one target.
/// </summary>
public enum TankSwapPhase
{
    /// <summary>No swap in flight.</summary>
    Idle,

    /// <summary>Taker: pre-mitigated + broadcast a take request, waiting for the giver's confirm.</summary>
    AwaitingConfirm,

    /// <summary>Giver: confirmed the take request, holding Shirk until aggro actually flips.</summary>
    AwaitingFlip,
}

/// <summary>What the module should do this frame for the deliberate swap.</summary>
public enum TankSwapDecision
{
    None,

    /// <summary>Taker: pop a personal mitigation and broadcast the take request.</summary>
    PreMitigateAndRequest,

    /// <summary>Taker: confirmation received (or timed out) — Provoke the boss.</summary>
    Provoke,

    /// <summary>Giver: a take request arrived — confirm it (unblocks the taker).</summary>
    Confirm,

    /// <summary>Giver: aggro has flipped to the taker — Shirk to cement their lead.</summary>
    Shirk,
}

/// <summary>Per-frame snapshot the sequencer reasons over (module builds it from live state).</summary>
public readonly struct TankSwapInputs
{
    /// <summary>Config master toggle — coordinated swaps are opt-in.</summary>
    public bool SwapEnabled { get; init; }

    /// <summary>The boss/target the swap is on.</summary>
    public uint TargetEntityId { get; init; }

    /// <summary>Boss is currently pointed at the local toon.</summary>
    public bool LocalHoldsAggro { get; init; }

    /// <summary>Boss is currently pointed at the co-tank.</summary>
    public bool CoTankHoldsAggro { get; init; }

    /// <summary>Another Daedalus tank is present to coordinate with.</summary>
    public bool HasRemoteTank { get; init; }

    /// <summary>This toon is the window-designated off-tank (tiebreaker when aggro is ambiguous).</summary>
    public bool IsDesignatedOffTank { get; init; }

    /// <summary>The operator pressed "Swap tanks" (armed window). Explicit intent — overrides the
    /// recent-giver anti-ping-pong hold, so back-to-back manual swaps work in either direction.</summary>
    public bool ManualTriggerActive { get; init; }

    /// <summary>An automatic trigger fired (buster stacks / prediction). Respects the recent-giver
    /// hold so automation can never ping-pong the boss.</summary>
    public bool AutoTriggerActive { get; init; }

    /// <summary>The co-tank has broadcast a request to TAKE aggro from us (we are the giver).</summary>
    public bool PendingTakeRequestFromCoTank { get; init; }

    /// <summary>The giver has confirmed our take request.</summary>
    public bool HasConfirmation { get; init; }

    /// <summary>We recently gave aggro away here — suppresses immediate re-initiation.</summary>
    public bool WasRecentGiver { get; init; }

    /// <summary>A swap reservation already exists (local or remote) for this target.</summary>
    public bool SwapInProgress { get; init; }

    public bool ProvokeReady { get; init; }
    public bool ShirkReady { get; init; }
}

/// <summary>
/// Per-target state machine for a DELIBERATE tank swap, shared by all four tank EnmityModules
/// (previously 4× copy-paste). Encodes the ordered handshake so the two tanks never cross-Provoke:
///
///   Taker (will take aggro): trigger → pre-mit + take-request → wait for confirm → Provoke.
///   Giver (holds aggro):     sees take-request → confirm → wait for the boss to flip → Shirk.
///
/// "Taker" is whoever lacks aggro while the co-tank holds it; when neither holds it (messy pull) the
/// window-designated off-tank breaks the tie. The reactive lost-aggro / off-tank-drop behaviour stays
/// in the modules — this only owns the planned swap. Pure + deterministic (clock passed in) so it is
/// unit-testable without the game.
/// </summary>
public sealed class TankSwapSequencer
{
    /// <summary>How long the taker waits for a confirm before Provoking anyway (mixed/non-Daedalus co-tank).</summary>
    public static readonly TimeSpan ConfirmTimeout = TimeSpan.FromSeconds(2.0);

    /// <summary>How long the giver waits for the aggro flip before Shirking anyway (missed read).</summary>
    public static readonly TimeSpan FlipTimeout = TimeSpan.FromSeconds(3.0);

    /// <summary>Overall safety expiry — a stuck sequence resets so it can't wedge the module.</summary>
    public static readonly TimeSpan PhaseExpiry = TimeSpan.FromSeconds(6.0);

    private TankSwapPhase _phase = TankSwapPhase.Idle;
    private uint _target;
    private DateTime _phaseSince = DateTime.MinValue;

    public TankSwapPhase Phase => _phase;
    public uint Target => _target;

    /// <summary>True whenever a deliberate swap is mid-flight (module suppresses reactive branches).</summary>
    public bool IsActive => _phase != TankSwapPhase.Idle;

    /// <summary>Decides the next deliberate-swap action for this frame. Does not mutate on its own —
    /// the module calls the matching Notify* after it actually dispatches, so a not-ready action is
    /// simply retried next frame.</summary>
    public TankSwapDecision Evaluate(in TankSwapInputs x, DateTime now)
    {
        // Bail the whole sequence if it wandered off target or stalled.
        if (_phase != TankSwapPhase.Idle && (x.TargetEntityId != _target || now - _phaseSince > PhaseExpiry))
            Reset();

        if (!x.SwapEnabled || !x.HasRemoteTank || x.TargetEntityId == 0)
        {
            Reset();
            return TankSwapDecision.None;
        }

        switch (_phase)
        {
            case TankSwapPhase.Idle:
                // Giver path: the co-tank asked to take aggro and we hold it → confirm (a broadcast,
                // not the Shirk itself, so it isn't gated on Shirk readiness).
                if (x.PendingTakeRequestFromCoTank && x.LocalHoldsAggro)
                    return TankSwapDecision.Confirm;

                // Taker path: a trigger fired and we are the one who should take it. Manual presses
                // bypass the recent-giver hold (explicit operator intent — the arm is consumed on
                // swap completion, so a lingering command can't bounce); auto triggers respect it.
                if (!x.SwapInProgress
                    && AmTaker(x)
                    && (x.ManualTriggerActive || (x.AutoTriggerActive && !x.WasRecentGiver)))
                    return TankSwapDecision.PreMitigateAndRequest;

                return TankSwapDecision.None;

            case TankSwapPhase.AwaitingConfirm:
                if (!x.ProvokeReady)
                    return TankSwapDecision.None;
                if (x.HasConfirmation || now - _phaseSince >= ConfirmTimeout)
                    return TankSwapDecision.Provoke;
                return TankSwapDecision.None;

            case TankSwapPhase.AwaitingFlip:
                if (!x.ShirkReady)
                    return TankSwapDecision.None;
                // Flip observed = the boss now points at the co-tank (the taker).
                if (x.CoTankHoldsAggro || now - _phaseSince >= FlipTimeout)
                    return TankSwapDecision.Shirk;
                return TankSwapDecision.None;

            default:
                return TankSwapDecision.None;
        }
    }

    /// <summary>The local toon is the one who should Provoke: it lacks aggro and either the co-tank
    /// holds it (straight swap) or nobody holds it and we're the designated off-tank (tiebreak).</summary>
    private static bool AmTaker(in TankSwapInputs x)
        => !x.LocalHoldsAggro && (x.CoTankHoldsAggro || x.IsDesignatedOffTank);

    public void NotifyRequested(uint target, DateTime now)
    {
        _phase = TankSwapPhase.AwaitingConfirm;
        _target = target;
        _phaseSince = now;
    }

    public void NotifyConfirmed(uint target, DateTime now)
    {
        _phase = TankSwapPhase.AwaitingFlip;
        _target = target;
        _phaseSince = now;
    }

    public void NotifyProvoked() => Reset();

    public void NotifyShirked() => Reset();

    public void Reset()
    {
        _phase = TankSwapPhase.Idle;
        _target = 0;
        _phaseSince = DateTime.MinValue;
    }
}
