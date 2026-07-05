using System.Numerics;

namespace Daedalus.Services.Positional.Navigation;

/// <summary>What a movement submission is for — arcs get a narrower yield gate than range-keeping.</summary>
public enum MovementIntent
{
    /// <summary>Range-keeping / approach / roam. Fully yields to BMR (steering, danger, regrab).</summary>
    MaxMelee,

    /// <summary>
    /// GCD-budgeted flank/rear hop. Allowed during BMR sticky steering (BMR owns range, Daedalus owns
    /// the angle — that's the boundary-camping division of labor); still denied during real zone/damage
    /// danger and the real-danger regrab cooldown.
    /// </summary>
    PositionalArc,
}

/// <summary>Who currently owns character steering, as far as the arbiter can tell.</summary>
public enum MovementOwner
{
    None,
    Daedalus,
    BossMod,
}

/// <summary>Why the arbiter last denied (or would deny) a movement submission.</summary>
public enum MovementSuppression
{
    None,

    /// <summary>Danger live/imminent (forbidden zones up or damage inside the yield window).</summary>
    BmrDanger,

    /// <summary>BMR AI is actively steering toward a nav target.</summary>
    BmrNavigating,

    /// <summary>Danger cleared recently — waiting out the re-grab cooldown.</summary>
    RegrabCooldown,

    /// <summary>Another submission landed within the minimum re-path interval.</summary>
    RepathInterval,

    /// <summary>The running path is younger than the commitment window.</summary>
    PathCommitment,

    /// <summary>New destination is too close to the running path's goal to matter.</summary>
    DestinationDelta,
}

/// <summary>Read-only view of the arbiter's last frame, for the Nav Control / debug windows.</summary>
public readonly record struct MovementArbiterSnapshot(
    MovementOwner Owner,
    MovementSuppression Suppression,
    int ForbiddenZonesCount,
    bool BmrNavigating,
    float NextDamageInSeconds,
    float ForbiddenZoneInSeconds,
    double RegrabCooldownRemainingSeconds,
    double SecondsSinceLastGrant,
    Vector3? BmrNaviTarget,
    MovementIntent LastGrantIntent);
