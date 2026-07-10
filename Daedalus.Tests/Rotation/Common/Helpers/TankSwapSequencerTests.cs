using System;
using Daedalus.Rotation.Common.Helpers;
using Xunit;

namespace Daedalus.Tests.Rotation.Common.Helpers;

/// <summary>
/// The tank-swap choreography state machine. Verifies the two-sided handshake — taker waits for the
/// giver's confirm before Provoking; giver holds Shirk until the aggro flip — plus the taker tiebreak
/// and the guards that stop a swap from starting.
/// </summary>
public class TankSwapSequencerTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private const uint Boss = 0x4000_0001u;

    private static TankSwapInputs Base() => new()
    {
        SwapEnabled = true,
        TargetEntityId = Boss,
        HasRemoteTank = true,
        ProvokeReady = true,
        ShirkReady = true,
    };

    // ---- Taker path ----

    [Fact]
    public void Taker_Triggered_WithCoTankAggro_RequestsThenWaitsForConfirm()
    {
        var seq = new TankSwapSequencer();
        var x = Base() with { CoTankHoldsAggro = true, ManualTriggerActive = true };

        Assert.Equal(TankSwapDecision.PreMitigateAndRequest, seq.Evaluate(x, T0));
        seq.NotifyRequested(Boss, T0);
        Assert.Equal(TankSwapPhase.AwaitingConfirm, seq.Phase);

        // No confirmation yet, still inside the timeout → hold, do NOT Provoke next frame.
        var justAfter = T0.AddSeconds(0.5);
        Assert.Equal(TankSwapDecision.None, seq.Evaluate(x, justAfter));
    }

    [Fact]
    public void Taker_ProvokesOnConfirmation()
    {
        var seq = new TankSwapSequencer();
        var x = Base() with { CoTankHoldsAggro = true, ManualTriggerActive = true };
        seq.Evaluate(x, T0);
        seq.NotifyRequested(Boss, T0);

        var confirmed = x with { HasConfirmation = true };
        Assert.Equal(TankSwapDecision.Provoke, seq.Evaluate(confirmed, T0.AddSeconds(0.3)));
        seq.NotifyProvoked();
        Assert.Equal(TankSwapPhase.Idle, seq.Phase);
    }

    [Fact]
    public void Taker_ProvokesAfterConfirmTimeout_WhenNoConfirmArrives()
    {
        var seq = new TankSwapSequencer();
        var x = Base() with { CoTankHoldsAggro = true, ManualTriggerActive = true };
        seq.Evaluate(x, T0);
        seq.NotifyRequested(Boss, T0);

        var afterTimeout = T0 + TankSwapSequencer.ConfirmTimeout + TimeSpan.FromSeconds(0.1);
        Assert.Equal(TankSwapDecision.Provoke, seq.Evaluate(x, afterTimeout));
    }

    [Fact]
    public void Taker_HoldsProvoke_WhenProvokeNotReady()
    {
        var seq = new TankSwapSequencer();
        var x = Base() with { CoTankHoldsAggro = true, ManualTriggerActive = true };
        seq.Evaluate(x, T0);
        seq.NotifyRequested(Boss, T0);

        var confirmedNotReady = x with { HasConfirmation = true, ProvokeReady = false };
        Assert.Equal(TankSwapDecision.None, seq.Evaluate(confirmedNotReady, T0.AddSeconds(0.3)));
    }

    // ---- Giver path ----

    [Fact]
    public void Giver_ConfirmsTakeRequest_ThenHoldsShirkUntilFlip()
    {
        var seq = new TankSwapSequencer();
        var x = Base() with { LocalHoldsAggro = true, PendingTakeRequestFromCoTank = true };

        Assert.Equal(TankSwapDecision.Confirm, seq.Evaluate(x, T0));
        seq.NotifyConfirmed(Boss, T0);
        Assert.Equal(TankSwapPhase.AwaitingFlip, seq.Phase);

        // Boss hasn't flipped to the co-tank yet → hold Shirk.
        Assert.Equal(TankSwapDecision.None, seq.Evaluate(x, T0.AddSeconds(0.5)));

        // Flip observed (boss now on the co-tank) → Shirk.
        var flipped = x with { CoTankHoldsAggro = true };
        Assert.Equal(TankSwapDecision.Shirk, seq.Evaluate(flipped, T0.AddSeconds(0.8)));
        seq.NotifyShirked();
        Assert.Equal(TankSwapPhase.Idle, seq.Phase);
    }

    [Fact]
    public void Giver_ShirksAfterFlipTimeout_WhenFlipMissed()
    {
        var seq = new TankSwapSequencer();
        var x = Base() with { LocalHoldsAggro = true, PendingTakeRequestFromCoTank = true };
        seq.Evaluate(x, T0);
        seq.NotifyConfirmed(Boss, T0);

        var afterTimeout = T0 + TankSwapSequencer.FlipTimeout + TimeSpan.FromSeconds(0.1);
        Assert.Equal(TankSwapDecision.Shirk, seq.Evaluate(x, afterTimeout));
    }

    // ---- Taker tiebreak + guards ----

    [Fact]
    public void AmbiguousAggro_OnlyDesignatedOffTankInitiates()
    {
        // Neither tank holds the boss (messy pull). The designated off-tank is the taker...
        var otSeq = new TankSwapSequencer();
        var ot = Base() with { ManualTriggerActive = true, IsDesignatedOffTank = true };
        Assert.Equal(TankSwapDecision.PreMitigateAndRequest, otSeq.Evaluate(ot, T0));

        // ...the other tank does not initiate, so they can't cross-Provoke.
        var mtSeq = new TankSwapSequencer();
        var mt = Base() with { ManualTriggerActive = true, IsDesignatedOffTank = false };
        Assert.Equal(TankSwapDecision.None, mtSeq.Evaluate(mt, T0));
    }

    [Fact]
    public void RecentGiver_ManualTriggerBypassesHold_SwapBackWorks()
    {
        // The operator pressing "Swap tanks" is explicit intent — a fresh press right after a swap
        // must reverse it even though this tank just gave aggro away. (The arm is consumed on swap
        // completion service-side, so a LINGERING press can't bounce; only a new press re-arms.)
        var seq = new TankSwapSequencer();
        var x = Base() with { CoTankHoldsAggro = true, ManualTriggerActive = true, WasRecentGiver = true };
        Assert.Equal(TankSwapDecision.PreMitigateAndRequest, seq.Evaluate(x, T0));
    }

    [Fact]
    public void RecentGiver_AutoTriggerRespectsHold()
    {
        // Automation (buster stacks) must never ping-pong: the recent-giver hold blocks auto takes.
        var seq = new TankSwapSequencer();
        var x = Base() with { CoTankHoldsAggro = true, AutoTriggerActive = true, WasRecentGiver = true };
        Assert.Equal(TankSwapDecision.None, seq.Evaluate(x, T0));

        // Once the hold expires, the same auto trigger initiates normally.
        var released = x with { WasRecentGiver = false };
        Assert.Equal(TankSwapDecision.PreMitigateAndRequest, seq.Evaluate(released, T0));
    }

    [Fact]
    public void SwapInProgress_DoesNotInitiate()
    {
        var seq = new TankSwapSequencer();
        var x = Base() with { CoTankHoldsAggro = true, ManualTriggerActive = true, SwapInProgress = true };
        Assert.Equal(TankSwapDecision.None, seq.Evaluate(x, T0));
    }

    [Fact]
    public void Disabled_OrNoRemoteTank_ResetsAndReturnsNone()
    {
        var seq = new TankSwapSequencer();
        var x = Base() with { CoTankHoldsAggro = true, ManualTriggerActive = true };
        seq.Evaluate(x, T0);
        seq.NotifyRequested(Boss, T0);
        Assert.True(seq.IsActive);

        // Config turned off mid-swap → sequence resets, nothing to do.
        Assert.Equal(TankSwapDecision.None, seq.Evaluate(x with { SwapEnabled = false }, T0.AddSeconds(0.2)));
        Assert.False(seq.IsActive);

        Assert.Equal(TankSwapDecision.None, seq.Evaluate(x with { HasRemoteTank = false }, T0));
    }

    [Fact]
    public void StalledSequence_ExpiresAndResets()
    {
        var seq = new TankSwapSequencer();
        var x = Base() with { CoTankHoldsAggro = true, ManualTriggerActive = true };
        seq.Evaluate(x, T0);
        seq.NotifyRequested(Boss, T0);

        // Never confirmed, never provoked (e.g. Provoke stuck not-ready) → past the hard expiry it
        // abandons the swap instead of wedging.
        var wayLater = T0 + TankSwapSequencer.PhaseExpiry + TimeSpan.FromSeconds(0.1);
        seq.Evaluate(x with { ProvokeReady = false }, wayLater);
        Assert.False(seq.IsActive);
    }
}
