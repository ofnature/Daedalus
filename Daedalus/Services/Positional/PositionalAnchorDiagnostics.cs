using System;

namespace Daedalus.Services.Positional;

/// <summary>
/// Live gate-chain + state snapshot for the positional anchor (boundary camping), written by the
/// active melee rotation every frame and rendered in the Nav Control window. STATIC-BACKED on
/// purpose (the config-copy lesson: transient values a window reads from a rotation must go
/// through statics, not captured copies).
/// Field origin (2026-07-20): boundary camping was switched on for a SAM box and "nothing moved,
/// no debugs" — the actual blocker (the solo party gate) was invisible. This panel makes every
/// gate and the movement service's live verdict readable in-game.
/// </summary>
public static class PositionalAnchorDiagnostics
{
    public static string JobName = "";
    public static DateTime UpdatedUtc;

    // Gate chain, in evaluation order.
    public static bool CampingSwitchOn;
    public static bool RolloutEnabled;
    public static bool JobToggleOn;
    public static bool AutoMovementOn;
    public static bool HasParty;
    public static bool SingleTargetOk;
    public static int EngagedEnemies;
    public static bool HasTarget;

    public static float BiasDegrees;
    public static string Anticipation = "";
    public static string ServiceState = "";

    /// <summary>True when this snapshot is fresh enough to trust (a melee rotation is running).</summary>
    public static bool IsFresh => (DateTime.UtcNow - UpdatedUtc).TotalSeconds < 2.0;

    /// <summary>All gates open — the anchor system is live for the current job.</summary>
    public static bool AnchorLive =>
        CampingSwitchOn && RolloutEnabled && JobToggleOn
        && AutoMovementOn && HasParty && SingleTargetOk && HasTarget;

    /// <summary>First failing gate in evaluation order; null when the anchor is live.</summary>
    public static string? BlockedBy =>
        !CampingSwitchOn ? "boundary camping switch is OFF"
        : !RolloutEnabled ? "job not in the anchor rollout yet (NIN/SAM only)"
        : !JobToggleOn ? "job's positional movement toggle is OFF"
        : !AutoMovementOn ? "auto movement master toggle is OFF"
        : !HasParty ? "solo — auto movement requires a party (by design)"
        : !SingleTargetOk ? $"multi-enemy pull ({EngagedEnemies} engaged)"
        : !HasTarget ? "no target"
        : null;
}
