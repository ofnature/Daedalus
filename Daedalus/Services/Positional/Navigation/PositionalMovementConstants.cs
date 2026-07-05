namespace Daedalus.Services.Positional.Navigation;

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

    /// <summary>FFXIV melee weaponskill reach (edge-to-edge) in yalms. A melee GCD lands while the gap
    /// between the player and target hitboxes is within this distance.</summary>
    public const float MeleeActionRangeYalms = 3f;

    /// <summary>
    /// Margin pulled inside the absolute max-melee edge when computing a melee stand point, so navmesh
    /// arrival tolerance / target jitter never drifts the character out of range (which would divert the
    /// rotation to a ranged filler such as Throwing Dagger). We stand at max-melee minus this buffer.
    /// </summary>
    public const float MaxMeleeSafetyBufferYalms = 0.5f;

    /// <summary>
    /// Default grace dead-band (yalms) around the max-melee stand distance, used when a request does not
    /// supply <c>VNavFlex</c>. The character only repaths once it leaves <c>standDistance ± flex</c>;
    /// inside the band the vNav call is suppressed, which is what stops the move-in/move-out twitching.
    /// Mirrors <see cref="Daedalus.Config.NavConfig.VNavFlex"/>'s default. User-tunable 0.0–2.0.
    /// </summary>
    public const float DefaultVNavFlexYalms = 0.5f;

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
    /// vNav <c>PathfindAndMoveCloseTo</c> arrival tolerance (yalms) for positional rear/flank arcs.
    /// </summary>
    public const float PositionalArrivalToleranceYalms = 0.35f;

    // --- MovementArbiter cadence (yield-to-BMR + vNav churn protection) ---

    /// <summary>
    /// Danger horizon (seconds) at which Daedalus cedes the input pipeline to BMR. Tighter than
    /// <see cref="DefaultImminentWindowSeconds"/> (which gates destination *selection*): the arbiter only
    /// needs vNav idle by the time BMR starts steering, and 1.5s covers BMR's pathfind + reaction latency
    /// without freezing Daedalus movement for whole mechanic cycles.
    /// </summary>
    public const float BmrYieldWindowSeconds = 1.5f;

    /// <summary>
    /// Base cooldown (seconds) after BMR danger clears before Daedalus may issue movement again. Grabbing
    /// control the frame a zone expires is the second half of the tug-of-war — BMR often takes a few
    /// hundred ms to settle on / finish reaching its safe spot.
    /// </summary>
    public const float BmrRegrabCooldownSeconds = 0.75f;

    /// <summary>
    /// If danger re-appears within this many seconds of a granted path, that grab was a mid-mechanic
    /// mistake (staggered effects — we regrabbed in a calm gap and froze BMR's next dodge). The regrab
    /// cooldown doubles on each such interruption, up to <see cref="BmrRegrabCooldownMaxSeconds"/>.
    /// </summary>
    public const float BmrRegrabBackoffTriggerSeconds = 2f;

    /// <summary>Cap for the escalated regrab cooldown during multi-effect sequences.</summary>
    public const float BmrRegrabCooldownMaxSeconds = 3f;

    /// <summary>
    /// BMR counts as "steering" for this long after the last frame its AI had a nav target. The raw
    /// AI.IsNavigating signal flickers per-frame while BMR micro-adjusts follow distance (field log
    /// 2026-07-05: onset→clear cycles within 300ms), and each flicker edge caused a stop/grant against
    /// BMR's input injection — the residual stutter. With BMR AI on it re-steers well inside this window,
    /// so Daedalus movement stands down entirely; with BMR AI off the signal never fires and Daedalus
    /// moves freely.
    /// </summary>
    public const float BmrSteeringStickySeconds = 3f;

    /// <summary>
    /// Continuous calm (seconds, no danger) after which the escalated regrab cooldown resets to base —
    /// the mechanic sequence is over.
    /// </summary>
    public const float BmrRegrabResetCalmSeconds = 5f;

    /// <summary>
    /// Minimum interval (seconds) between vNav submissions. Kills per-frame re-path churn loops while
    /// staying below perceptible sluggishness (~2 legitimate re-paths per GCD max).
    /// </summary>
    public const float MinRepathIntervalSeconds = 0.30f;

    /// <summary>
    /// A Daedalus-owned path younger than this cannot be *replaced* by a new submission — long enough for
    /// vNav to make visible progress, short enough not to fight the GCD-budgeted arc logic. Telegraph
    /// aborts bypass this because <c>Stop()</c> is never gated.
    /// </summary>
    public const float PathCommitmentSeconds = 0.50f;

    /// <summary>
    /// Minimum destination change (yalms) for a new submission to replace a running path. Below this it's
    /// pure jitter from mob micro-movement; above the 0.35/0.5y arrival tolerances so legitimate
    /// corrections still pass.
    /// </summary>
    public const float MinDestinationDeltaYalms = 0.75f;

    /// <summary>
    /// How long after a grant the destination-delta rule keeps applying even once the path has completed.
    /// Short hops finish inside the repath interval, clearing path ownership — without this memory,
    /// max-melee maintenance machine-gunned near-identical destinations at ~3Hz while chasing a drifting
    /// pack (field log 2026-07-05: "granted ×14/×18/×22"). Real chases accumulate &gt;0.75y within the
    /// window and re-path immediately.
    /// </summary>
    public const float DestinationMemorySeconds = 1.0f;
}
