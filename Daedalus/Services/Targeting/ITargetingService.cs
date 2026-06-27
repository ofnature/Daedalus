using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;

namespace Daedalus.Services.Targeting;

/// <summary>
/// Interface for targeting services with multiple strategies.
/// </summary>
public interface ITargetingService
{
    /// <summary>
    /// Finds an enemy target using the specified strategy.
    /// </summary>
    IBattleNpc? FindEnemy(EnemyTargetingStrategy strategy, float maxRange, IPlayerCharacter player);

    /// <summary>
    /// Finds an enemy that needs DoT applied or refreshed.
    /// </summary>
    IBattleNpc? FindEnemyNeedingDot(uint dotStatusId, float refreshThreshold, float maxRange, IPlayerCharacter player);

    /// <summary>
    /// Longest remaining time for any of <paramref name="statusIds"/> on in-range enemies.
    /// </summary>
    float GetBestStatusRemainingOnAnyEnemy(uint[] statusIds, float maxRange, IPlayerCharacter player);

    /// <summary>
    /// Longest remaining time for any of <paramref name="statusIds"/> from <paramref name="sourceId"/> on in-range enemies.
    /// </summary>
    float GetBestStatusRemainingFromSourceOnAnyEnemy(uint[] statusIds, uint sourceId, float maxRange, IPlayerCharacter player);

    /// <summary>
    /// Finds the nearest valid enemy within range, bypassing <c>PauseWhenNoTarget</c>.
    /// Used as an AoE fallback when the player has no hard target but enemies are nearby.
    /// </summary>
    IBattleNpc? FindNearbyEnemy(float maxRange, IPlayerCharacter player);

    /// <summary>
    /// Counts the number of valid enemies within the specified radius of the player.
    /// </summary>
    int CountEnemiesInRange(float radius, IPlayerCharacter player);

    /// <summary>
    /// Counts valid in-combat enemies within range, bypassing <c>PauseWhenNoTarget</c>.
    /// Paired with <see cref="FindNearbyEnemy"/> for AoE fallback.
    /// </summary>
    int CountNearbyEnemiesInRange(float radius, IPlayerCharacter player);

    /// <summary>
    /// Counts engaged/hostile enemies within <paramref name="scanRadius"/> of the player.
    /// Used for pull-size decisions (positionals, pack awareness) — not self-centered AoE hit count.
    /// </summary>
    int CountEngagedEnemies(float scanRadius, IPlayerCharacter player);

    /// <summary>
    /// Returns engaged pull size and enemies within <paramref name="aoeRadiusYalms"/> of the player.
    /// </summary>
    EnemyPackCounts CountEnemyPack(float aoeRadiusYalms, IPlayerCharacter player);

    /// <summary>
    /// Counts valid enemies within <paramref name="radius"/> of <paramref name="target"/>'s position.
    /// Used for targeted AoE (e.g. Impact's circle on the target) while the player stands at cast range.
    /// Candidates are gathered within range of the player, then filtered by distance to the anchor target.
    /// </summary>
    int CountEnemiesInRangeOfTarget(float radius, IBattleNpc target, IPlayerCharacter player);

    /// <summary>
    /// Finds the enemy that has the most other enemies within the specified radius.
    /// </summary>
    (IBattleNpc? target, int hitCount) FindBestAoETarget(float aoeRadius, float maxRange, IPlayerCharacter player);

    /// <summary>
    /// Finds an enemy target using the game's native action range check (GetActionInRangeOrLoS).
    /// More accurate than distance-based range checks because it uses the exact same logic the game uses,
    /// including both player and enemy hitbox radii.
    /// </summary>
    IBattleNpc? FindEnemyForAction(EnemyTargetingStrategy strategy, uint actionId, IPlayerCharacter player);

    /// <summary>
    /// Finds the optimal facing angle for a cone AoE to hit the most enemies.
    /// </summary>
    (IBattleNpc? target, int hitCount, float optimalAngle) FindBestConeAoETarget(
        float coneHalfAngle, float radius, float maxRange, IPlayerCharacter player);

    /// <summary>
    /// Finds the optimal facing angle for a line/rect AoE to hit the most enemies.
    /// </summary>
    (IBattleNpc? target, int hitCount, float optimalAngle) FindBestLineAoETarget(
        float lineWidth, float length, float maxRange, IPlayerCharacter player);

    /// <summary>
    /// Invalidates the enemy cache.
    /// </summary>
    void InvalidateCache();

    /// <summary>
    /// Returns true when damage targeting should be paused because the player has
    /// intentionally dropped their target and <see cref="Config.TargetingConfig.PauseWhenNoTarget"/> is ON.
    /// Returns false during active combat when the hard target is dead or missing but live
    /// hostiles remain — Daedalus auto-retargets the game hard target in that case.
    /// Damage modules can check this to set a clear "Paused (no target)" debug state
    /// before any target acquisition is attempted.
    /// </summary>
    bool IsDamageTargetingPaused();

    /// <summary>
    /// Returns the player's currently selected target, if any, as an <see cref="IBattleNpc"/>.
    /// Used by gap closer safety and explicit-target checks. Returns null when the player
    /// has no target or has a non-enemy target.
    /// </summary>
    IBattleNpc? GetUserEnemyTarget();

    /// <summary>
    /// Safety helper for gap closers. Exposed here so rotations can access it via the
    /// existing <c>context.TargetingService</c> field without plumbing a new dependency
    /// through every rotation context.
    /// </summary>
    IGapCloserSafetyService GapCloserSafety { get; }
}
