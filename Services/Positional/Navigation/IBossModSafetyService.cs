using System.Numerics;

namespace Olympus.Services.Positional.Navigation;

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
}
