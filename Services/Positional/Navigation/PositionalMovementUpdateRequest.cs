using System.Numerics;
using Olympus.Services.Action;

namespace Olympus.Services.Positional.Navigation;

/// <summary>Target snapshot for stand-point calculation (no game object dependency).</summary>
public readonly record struct PositionalMovementTarget(
    Vector3 Position,
    float HitboxRadius,
    float RotationRadians,
    bool HasPositionalImmunity);

/// <summary>
/// Per-frame inputs for <see cref="IPositionalMovementService.Update"/>.
/// </summary>
public readonly record struct PositionalMovementUpdateRequest(
    IPositionalAnticipationProvider? AnticipationProvider,
    PositionalAnticipationContext AnticipationContext,
    Vector3 PlayerPosition,
    float PlayerHitboxRadius,
    PositionalMovementTarget? Target,
    IActionService ActionService,
    bool InCombat,
    bool EnableMovement = true);
