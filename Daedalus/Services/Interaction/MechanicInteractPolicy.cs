namespace Daedalus.Services.Interaction;

/// <summary>
/// Everything the mechanic-interact decision needs, gathered by <see cref="MechanicInteractService"/> from live
/// game state. Pure data so the policy is unit-testable.
/// </summary>
public readonly record struct MechanicInteractSituation(
    bool Enabled,
    ulong TargetId,
    bool TargetFound,
    bool InRange,
    bool SelfAlive,
    bool SelfCasting,
    bool AnimationLocked,
    double SecondsSinceLastAttempt);

/// <summary>Why the interact did or did not happen this frame; the first thing that would have to change.</summary>
public enum MechanicInteractDecision
{
    Interact,
    Disabled,
    NoTarget,
    TargetMissing,
    Dead,
    OutOfRange,
    Busy,
    Throttled,
}

/// <summary>
/// Clicking the object a fight needs clicked -- Aulus mal Asina's Empty Vessel, Aurum Vale's fruit, Naadam's ovoo.
/// The boss engine names the object (<c>Minerva.Hints.InteractTarget</c>) and walks the toon there where the fight
/// requires it; the click is ours, because the engine presses nothing. Targetability is not asked: the module named the
/// object, and some (Aulus mal Asina's Empty Vessel) spawn untargetable. Mirrors BossmodReborn's own interact: the
/// game's interact-range check, never mid-cast or animation-locked, and at most one attempt per 1.1s, since many
/// interactions start a cast of their own a server round trip later and a second click cancels it.
/// </summary>
public static class MechanicInteractPolicy
{
    /// <summary>Between attempts, as BossmodReborn throttles its own.</summary>
    public const double RetrySeconds = 1.1;

    public static MechanicInteractDecision Decide(in MechanicInteractSituation s)
        => !s.Enabled ? MechanicInteractDecision.Disabled
            : s.TargetId == 0 ? MechanicInteractDecision.NoTarget
            : !s.TargetFound ? MechanicInteractDecision.TargetMissing
            : !s.SelfAlive ? MechanicInteractDecision.Dead
            : !s.InRange ? MechanicInteractDecision.OutOfRange
            : s.SelfCasting || s.AnimationLocked ? MechanicInteractDecision.Busy
            : s.SecondsSinceLastAttempt < RetrySeconds ? MechanicInteractDecision.Throttled
            : MechanicInteractDecision.Interact;
}
