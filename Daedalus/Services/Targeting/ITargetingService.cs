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
    /// Finds the nearest in-range enemy that is part of the pull but is NOT currently focused on the
    /// player (no aggro on us yet). Used by tanks to tag stray adds with a ranged GCD during wall-to-wall
    /// movement so nothing is left behind. Returns null when every nearby enemy is already on the player.
    /// </summary>
    IBattleNpc? FindEnemyNotTargetingPlayer(float maxRange, IPlayerCharacter player);

    /// <summary>
    /// Finds the nearest valid hostile in range that is not already focused on the player, INCLUDING
    /// not-yet-aggroed idle mobs. Used by tanks to ranged-pull/gather the next pack while moving toward it
    /// (wall-to-wall). Broader than <see cref="FindEnemyNotTargetingPlayer"/>, which requires the mob to
    /// already be engaged.
    /// </summary>
    IBattleNpc? FindNearestTaggableEnemy(float maxRange, IPlayerCharacter player);

    /// <summary>
    /// Finds the nearest valid enemy the game has flagged with a nameplate icon — quest kill targets,
    /// levequest mobs, treasure-hunt marks. The authoritative "this mob counts for my objective"
    /// signal; ambient mobs are never flagged. Used by the Questionable kill-step bridge so quest
    /// automation only pulls the mobs the quest actually wants.
    /// </summary>
    IBattleNpc? FindNearestQuestFlaggedEnemy(float maxRange, IPlayerCharacter player);

    /// <summary>
    /// Finds the nearest valid enemy that has aggro on the local player — from the game's enmity
    /// ("hater") list plus mobs hard-targeting the player. Unlike the engaged/hostile scan this
    /// never matches mobs fighting someone else. Used by automation aggro cleanup so the pull
    /// chasing the player gets killed, in nearest-first order, without touching anyone else's mobs.
    /// </summary>
    IBattleNpc? FindNearestAggroedEnemy(float maxRange, IPlayerCharacter player);

    /// <summary>
    /// Finds the nearest valid enemy whose BNpcName row id is in <paramref name="nameIds"/> —
    /// farm-mode mob selection (NameId is language-independent and stable across levels).
    /// When <paramref name="anchor"/> is set, only enemies within <paramref name="anchorRadiusYalms"/>
    /// of it qualify, leashing the farm to its spot so it never chases spawns across the zone.
    /// </summary>
    IBattleNpc? FindNearestEnemyByNameIds(
        System.Collections.Generic.IReadOnlyCollection<uint> nameIds,
        float maxRange,
        IPlayerCharacter player,
        System.Numerics.Vector3? anchor = null,
        float anchorRadiusYalms = 0f);

    /// <summary>
    /// Counts the number of valid enemies within the specified radius of the player.
    /// </summary>
    int CountEnemiesInRange(float radius, IPlayerCharacter player);

    /// <summary>
    /// Total current HP of engaged/hostile enemies within radius — feeds pack time-to-kill
    /// estimates (e.g. holding the MCH Queen when the pack will die before she ramps).
    /// </summary>
    long SumEnemyCurrentHpInRange(float radius, IPlayerCharacter player);

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
    /// Counts engaged enemies within <paramref name="scanRadius"/> that the player has line of sight to,
    /// and (subset) that are also in the player's front hemisphere. Debug aid for facing/LoS rejections.
    /// </summary>
    (int inLineOfSight, int facing) CountEngagedLineOfSightAndFacing(float scanRadius, IPlayerCharacter player);

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
    /// Make the given enemy the game's hard target so auto-face turns toward it. Face-recovery hook for
    /// GCDs refused on facing; no-op if already the hard target or the id isn't a live enemy.
    /// </summary>
    void EnsureHardTarget(ulong enemyGameObjectId);

    /// <summary>Current game hard-target GameObjectId, or 0 if none. Used for stuck diagnostics.</summary>
    ulong GetGameHardTargetId();

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
    /// The player's hard target as a live hostile BattleNpc WITHOUT the attackability probe —
    /// for MOVEMENT decisions only. <see cref="GetUserEnemyTarget"/> runs CanUseActionOnTarget,
    /// which false-negatives while the mob is still out of range; movement gated on it never
    /// walks in (melee parked outside range). Never use this to dispatch actions.
    /// </summary>
    IBattleNpc? GetRawEnemyHardTarget();

    /// <summary>
    /// Safety helper for gap closers. Exposed here so rotations can access it via the
    /// existing <c>context.TargetingService</c> field without plumbing a new dependency
    /// through every rotation context.
    /// </summary>
    IGapCloserSafetyService GapCloserSafety { get; }
}
