namespace Olympus.Services.Positional.Navigation;

/// <summary>
/// Shared constants for positional vNav movement (burn-reference parity).
/// Tier-1 tuning (2026-06): earlier movement start after Jinpu/Shifu without touching rotation GCD/oGCD logic.
/// </summary>
public static class PositionalMovementConstants
{
    /// <summary>
    /// Conservative on-foot speed for move-duration budgeting in <see cref="PositionalMovementService"/>.
    /// Tuned 6 → 5 y/s: vNav paths and combat movement are often slower than straight-line run speed;
    /// over-estimating duration forces paths to queue earlier so finishers land after arrival.
    /// </summary>
    public const float MoveSpeedYalmsPerSecond = 5f;

    /// <summary>
    /// Fallback horizontal move cap when <c>GcdRemaining</c> is not supplied to
    /// <see cref="PositionalStandCalculator"/>. ~10y covers front→rear on a typical boss
    /// (hitbox ~2y, stand ring ~5.5y) in one GCD window at <see cref="MoveSpeedYalmsPerSecond"/>.
    /// </summary>
    public const float MaxMoveYalmsPerGcdWindow = 10f;

    /// <summary>Default stand-ring offset from target center (BossMod / DrawCanvas pattern).</summary>
    public const float DefaultStandRadiusOffset = 3.5f;

    /// <summary>
    /// Safety margin subtracted from <c>GcdRemaining</c> when deciding if a path can finish before the
    /// next GCD queue window. Tuned 0.075 → 0.10s: start reposition slightly earlier so Gekko/Kasha
    /// fire after vNav arrival. Positional-movement only — does not affect ActionService weave/GCD dispatch.
    /// </summary>
    public const float GcdClipBufferSeconds = 0.10f;

    /// <summary>
    /// Block starting a new vNav path while weaponskill animation lock exceeds this value.
    /// Tuned 0.075 → 0.20s: allow movement to begin before lock fully clears (~0.45s earlier vs Tier 0).
    /// Positional-movement start gate only — unrelated to oGCD weave or GCD queue logic in ActionService.
    /// </summary>
    public const float MovementStartMaxAnimationLockSeconds = 0.20f;

    /// <summary>Mechanic imminent window when BMR reports damage/forbidden zones within this many seconds.</summary>
    public const float DefaultImminentWindowSeconds = 3f;

    /// <summary>Epsilon for telegraph abort comparisons (seconds).</summary>
    public const float TelegraphAbortEpsilonSeconds = 0.05f;

    /// <summary>
    /// vNav <c>PathfindAndMoveCloseTo</c> arrival tolerance (yalms).
    /// Rear/flank zones are ~90° arcs at the stand ring; 0.35y keeps the player inside
    /// the positional hit cone without overshooting through the boss or past the arc edge.
    /// </summary>
    public const float PositionalArrivalToleranceYalms = 0.35f;
}
