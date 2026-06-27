using System.Numerics;
using Daedalus.Services.Action;

namespace Daedalus.Services.Positional.Navigation;

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
    bool EnableMovement = true,
    /// <summary>NIN: instant weaponskills/mudras — do not defer vNav for animation lock.</summary>
    bool AllowMovementDuringActionLock = false,
    /// <summary>
    /// Step back to the outer melee edge when hugging the target. Independent of <see cref="EnableMovement"/>
    /// (which gates the positional flank/rear arcs) so range-keeping works even for jobs without positional
    /// repositioning and while solo.
    /// </summary>
    bool MaintainMaxMelee = false,
    /// <summary>
    /// Target the max-melee maintenance keeps range on — the player's <em>current</em> (hard) target, so we
    /// never path toward a strategy-selected or merely-aggroed enemy that isn't being attacked. When null the
    /// maintenance falls back to <see cref="Target"/>.
    /// </summary>
    PositionalMovementTarget? MaxMeleeTarget = null,
    /// <summary>
    /// True when the max-melee target is currently targeting the player (solo, or the player is tanking it).
    /// Such a mob walks into the player every frame, so the back-off is suppressed — stepping out only starts
    /// a kite-bounce as the mob re-closes. The approach (walk back in when knocked out of range) still runs.
    /// </summary>
    bool MaxMeleeTargetFollowsPlayer = false,
    /// <summary>
    /// Grace dead-band (yalms) around the max-melee stand distance. vNav is only called when the character
    /// leaves <c>standDistance ± VNavFlex</c>; inside the band the call is suppressed (anti-twitch).
    /// </summary>
    float VNavFlex = PositionalMovementConstants.DefaultVNavFlexYalms,
    /// <summary>
    /// When > 0, stand points target the flank/rear boundary ± this margin instead of the arc center.
    /// Set to ~10° (π/18) for MNK to minimize travel between back-to-back Rear/Flank form switches.
    /// </summary>
    float PositionalBoundaryBiasRadians = 0f);
