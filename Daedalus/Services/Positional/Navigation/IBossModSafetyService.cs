using System.Numerics;

namespace Daedalus.Services.Positional.Navigation;

/// <summary>
/// BossModReborn IPC adapter for positional safety queries and telegraph abort detection.
/// </summary>
public interface IBossModSafetyService
{
    /// <summary>BossModReborn plugin is installed and loaded.</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Capture mechanic timers at the start of a movement update tick.
    /// <see cref="ShouldAbortMovement"/> compares against this snapshot.
    /// </summary>
    void BeginUpdateSnapshot();

    /// <summary>
    /// True when a new telegraph appeared since <see cref="BeginUpdateSnapshot"/>.
    /// </summary>
    bool ShouldAbortMovement();

    /// <summary>
    /// Query stand-position safety. Returns <see cref="PositionSafety.Safe"/> when BMR is unavailable (fail-open).
    /// </summary>
    PositionSafety QueryPositionSafety(Vector3 destination, float imminentWindowSeconds = PositionalMovementConstants.DefaultImminentWindowSeconds);

    /// <summary><c>BossMod.Hints.IsDashSafe</c> from player to destination.</summary>
    bool IsSegmentSafe(Vector3 from, Vector3 to);

    /// <summary><c>BossMod.Hints.NextDamageIn</c> in seconds, or <see cref="float.MaxValue"/> when unavailable.</summary>
    float NextDamageInSeconds { get; }

    /// <summary><c>BossMod.Hints.ForbiddenZonesNextActivation</c> in seconds, or <see cref="float.MaxValue"/> when unavailable.</summary>
    float ForbiddenZoneActivationInSeconds { get; }

    /// <summary><c>BossMod.Hints.ForbiddenZonesCount</c> — live danger zones; 0 when unavailable (fail-open).</summary>
    int ForbiddenZonesCount { get; }

    /// <summary><c>BossMod.AI.IsNavigating</c> — BMR AI has a nav target (actively steering); false when unavailable.</summary>
    bool IsBmrNavigating { get; }

    /// <summary><c>BossMod.AI.NaviTargetPos</c> — where BMR AI is steering to; null when idle or unavailable. Observability only.</summary>
    Vector3? BmrNaviTarget { get; }

    /// <summary>Enemies the fight says never to attack: a phase-invulnerable boss, a decoy, the wrong
    /// half of a two-floor boss. Empty when the engine has no opinion (BossMod publishes none).</summary>
    ulong[] ForbiddenTargets => [];

    /// <summary>Enemies the fight says to attack ahead of everything else, best first: adds during an
    /// invulnerability phase. Empty when the engine has no opinion.</summary>
    ulong[] PriorityTargets => [];

    /// <summary>The boss this toon has to pull because nobody else will (Minerva's <c>Minerva.Hints.PullTarget</c>):
    /// a boss fight not started, no other player in the party. 0 for none, and always 0 under BossMod.</summary>
    ulong PullTargetId => 0;

    /// <summary>What the character you are playing in a quest battle should use now, best first (Minerva's
    /// <c>Minerva.Hints.RoleplayActions</c>). Empty outside such a fight, and always empty under BossMod.</summary>
    Daedalus.Rotation.Roleplay.RoleplayRequest[] RoleplayActions => [];
}
