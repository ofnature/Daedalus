using System.Numerics;
using Olympus.Services.Positional;

namespace Olympus.Services.Positional.Navigation;

/// <summary>Lifecycle phase for an in-flight positional reposition.</summary>
public enum PositionalMovementPhase
{
    Idle,
    Moving,
    Skipped,
    Aborted,
}

/// <summary>
/// Last-tick positional movement decision state (for debug and future UI).
/// </summary>
public readonly record struct PositionalMovementState(
    PositionalMovementPhase Phase,
    PositionalType? TargetZone = null,
    Vector3? Destination = null,
    string? SkipReason = null);
