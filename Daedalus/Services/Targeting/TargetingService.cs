using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Common.Component.BGCollision;
using Daedalus.Data;
using Daedalus.Rotation.ApolloCore.Helpers;

namespace Daedalus.Services.Targeting;

/// <summary>
/// Centralized targeting service with optimized filtering, caching, and multiple strategies.
/// </summary>
public sealed class TargetingService : ITargetingService
{
    private readonly IObjectTable _objectTable;
    private readonly IPartyList _partyList;
    private readonly ITargetManager _targetManager;
    private readonly Configuration _configuration;

    // Cache of enemy GameObjectIds in range — re-resolved via ObjectTable each use (no stale IBattleNpc refs).
    private readonly List<ulong> _cachedEnemyIds = new(32);
    private readonly Stopwatch _cacheTimer = new();
    private float _lastCacheRange;
    private bool _lastScanApplyLineOfSight = true;

    // Reusable work list for AoE target methods (all called from game thread)
    private readonly List<IBattleNpc> _aoeWorkList = new();

    // User-selected target sticky — GameObjectId only, re-resolved via ObjectTable.
    // Set exclusively from <see cref="ITargetManager.Target"/> (never from FindEnemy strategy picks)
    // so LowestHp/AoE candidate cycling cannot thrash the ID during multi-mob fights.
    private const int StickyTargetGraceMs = 400;
    // Max range for combat-death auto-retarget and live-hostile detection (matches tank engage pass).
    private const float CombatRetargetMaxRangeY = 25f;

    private ulong _userStickyTargetGameObjectId;
    private long _userStickyTargetLastValidMs;
    private readonly Stopwatch _stickyClock = new();

    // Tank job IDs: PLD=19, WAR=21, DRK=32, GNB=37
    private static readonly HashSet<uint> TankJobIds = [19, 21, 32, 37];

    public IGapCloserSafetyService GapCloserSafety { get; }

    public TargetingService(
        IObjectTable objectTable,
        IPartyList partyList,
        ITargetManager targetManager,
        Configuration configuration,
        IGapCloserSafetyService gapCloserSafety)
    {
        _objectTable = objectTable;
        _partyList = partyList;
        _targetManager = targetManager;
        _configuration = configuration;
        GapCloserSafety = gapCloserSafety;
        _cacheTimer.Start();
        _stickyClock.Start();
    }

    /// <summary>
    /// Returns true when damage targeting should be suppressed because the player has
    /// intentionally dropped their target and <see cref="Config.TargetingConfig.PauseWhenNoTarget"/> is on.
    /// A dead hard target during active combat with live hostiles nearby is NOT treated as
    /// an intentional drop — see combat retarget (Layer 1/2).
    /// </summary>
    public bool IsDamageTargetingPaused()
    {
        if (!_configuration.Targeting.PauseWhenNoTarget)
            return false;

        PrepareDamageTargeting(_objectTable.LocalPlayer);

        SyncUserStickyFromTargetManager();

        if (HasValidUserSelectedEnemy())
            return false;

        if (ResolveUserStickyTarget() != null)
            return false;

        var player = _objectTable.LocalPlayer;
        if (player != null && IsCombatRetargetScenario(player))
            return false;

        return true;
    }

    /// <inheritdoc />
    public IBattleNpc? GetUserEnemyTarget()
    {
        SyncUserStickyFromTargetManager();

        if (_targetManager.Target is IBattleNpc userEnemy)
        {
            var resolved = ResolveEnemyById(userEnemy.GameObjectId);
            if (resolved != null)
                return resolved;
        }

        return ResolveUserStickyTarget();
    }

    /// <summary>
    /// Finds an enemy target using the specified strategy.
    /// </summary>
    /// <param name="strategy">Targeting strategy to use.</param>
    /// <param name="maxRange">Maximum range in yalms.</param>
    /// <param name="player">Current player character.</param>
    /// <returns>Best target according to strategy, or null if none found.</returns>
    public IBattleNpc? FindEnemy(EnemyTargetingStrategy strategy, float maxRange, IPlayerCharacter player)
    {
        PrepareDamageTargeting(player);

        // Hard pause: player has no target and PauseWhenNoTarget is on. Covers gaze mechanics.
        if (IsDamageTargetingPaused())
            return null;

        // Try primary strategy
        var target = FindEnemyByStrategy(strategy, maxRange, player);

        // If TankAssist fails and fallback is enabled, try LowestHp
        if (target == null && strategy == EnemyTargetingStrategy.TankAssist && _configuration.Targeting.UseTankAssistFallback)
        {
            target = FindEnemyByStrategy(EnemyTargetingStrategy.LowestHp, maxRange, player);
        }

        // If CurrentTarget/FocusTarget fails, fall back to LowestHp — unless strict mode
        // is on, in which case an explicit-target strategy with no target stays empty
        // (prevents auto-retargeting when the player is trying to stop attacking)
        if (target == null && strategy is EnemyTargetingStrategy.CurrentTarget or EnemyTargetingStrategy.FocusTarget
            && (!_configuration.Targeting.StrictCurrentTargetStrategy
                || ShouldRelaxStrictOnCombatDeath(player)))
        {
            target = FindEnemyByStrategy(EnemyTargetingStrategy.LowestHp, maxRange, player);
        }

        return ApplyStickyFallback(target);
    }

    /// <summary>
    /// Finds an enemy that needs DoT applied or refreshed. Respects the configured
    /// <see cref="EnemyTargetingStrategy"/> — on explicit strategies (CurrentTarget,
    /// FocusTarget) the DoT will never spill onto enemies the player did not pick.
    /// On aggregate strategies (LowestHp, HighestHp, Nearest, TankAssist) the DoT
    /// still goes to the strategy's chosen enemy, but only if that enemy actually
    /// needs the DoT applied or refreshed.
    /// </summary>
    /// <param name="dotStatusId">Status ID to check for (Aero/Dia variant).</param>
    /// <param name="refreshThreshold">Seconds remaining before DoT should be refreshed.</param>
    /// <param name="maxRange">Maximum range in yalms.</param>
    /// <param name="player">Current player character.</param>
    /// <returns>Enemy needing DoT under the current strategy, or null.</returns>
    public IBattleNpc? FindEnemyNeedingDot(
        uint dotStatusId,
        float refreshThreshold,
        float maxRange,
        IPlayerCharacter player)
    {
        // Hard pause: player has no target — don't DoT anything.
        if (IsDamageTargetingPaused())
            return null;

        var strategy = _configuration.Targeting.EnemyStrategy;

        // Explicit-target strategies: only consider the player's selected target/focus,
        // never spread DoT to unrelated enemies. This is the "smart DoT" safety fix —
        // hitting an add that isn't supposed to take damage (reflect, vulnerability down,
        // damage debuff) breaks fights, so honor player intent here.
        if (strategy is EnemyTargetingStrategy.CurrentTarget or EnemyTargetingStrategy.FocusTarget)
        {
            var explicitTarget = strategy == EnemyTargetingStrategy.CurrentTarget
                ? _targetManager.Target as IBattleNpc
                : _targetManager.FocusTarget as IBattleNpc;

            if (explicitTarget == null || !IsStillValid(explicitTarget))
                return null;

            if (!DistanceHelper.IsInRange(player.Position, explicitTarget.Position, maxRange + explicitTarget.HitboxRadius + player.HitboxRadius))
                return null;

            return GetDotDuration(explicitTarget, dotStatusId) < refreshThreshold ? explicitTarget : null;
        }

        // Aggregate strategies: pick the strategy's best enemy, but only DoT it if it
        // actually needs the DoT. This prevents the old behavior of scanning every
        // in-combat enemy and targeting whichever had the lowest DoT duration.
        IBattleNpc? strategyTarget = strategy switch
        {
            EnemyTargetingStrategy.TankAssist => FindEnemyByStrategy(strategy, maxRange, player),
            EnemyTargetingStrategy.Nearest => FindEnemyByStrategy(strategy, maxRange, player),
            EnemyTargetingStrategy.HighestHp => FindEnemyByStrategy(strategy, maxRange, player),
            _ => FindEnemyByStrategy(EnemyTargetingStrategy.LowestHp, maxRange, player)
        };

        if (strategyTarget == null)
            return null;

        return GetDotDuration(strategyTarget, dotStatusId) < refreshThreshold ? strategyTarget : null;
    }

    /// <inheritdoc/>
    public float GetBestStatusRemainingOnAnyEnemy(uint[] statusIds, float maxRange, IPlayerCharacter player)
    {
        if (statusIds.Length == 0)
            return 0f;

        var best = 0f;
        foreach (var enemy in GetValidEnemies(maxRange, player))
        {
            foreach (var statusId in statusIds)
            {
                var remaining = GetDotDuration(enemy, statusId);
                if (remaining > best)
                    best = remaining;
            }
        }

        return best;
    }

    /// <inheritdoc/>
    public float GetBestStatusRemainingFromSourceOnAnyEnemy(
        uint[] statusIds, uint sourceId, float maxRange, IPlayerCharacter player)
    {
        if (statusIds.Length == 0)
            return 0f;

        var best = 0f;
        foreach (var enemy in GetValidEnemies(maxRange, player))
        {
            foreach (var statusId in statusIds)
            {
                var remaining = Rotation.Common.Helpers.BaseStatusHelper.GetStatusRemainingFromSource(
                    enemy, statusId, sourceId);
                if (remaining > best)
                    best = remaining;
            }
        }

        return best;
    }

    /// <summary>
    /// Counts the number of valid enemies within the specified radius of the player.
    /// Used for AoE damage decisions (e.g., Holy when 3+ enemies).
    /// </summary>
    /// <inheritdoc />
    public int CountNearbyEnemiesInRange(float radius, IPlayerCharacter player)
    {
        var count = 0;
        foreach (var enemy in GetValidEnemies(radius, player))
        {
            if (IsEngagedOrHostile(enemy, player))
                count++;
        }
        return count;
    }

    /// <inheritdoc />
    public IBattleNpc? FindNearbyEnemy(float maxRange, IPlayerCharacter player)
    {
        IBattleNpc? nearest = null;
        var nearestDist = float.MaxValue;
        foreach (var enemy in GetValidEnemies(maxRange, player))
        {
            if (!IsEngagedOrHostile(enemy, player))
                continue;
            var dist = Vector3.DistanceSquared(player.Position, enemy.Position);
            if (dist < nearestDist)
            {
                nearest = enemy;
                nearestDist = dist;
            }
        }
        return nearest;
    }

    private bool IsEngagedOrHostile(IBattleNpc enemy, IPlayerCharacter player)
    {
        if ((enemy.StatusFlags & StatusFlags.InCombat) != 0)
            return true;
        if (enemy.TargetObjectId == player.GameObjectId)
            return true;
        if ((enemy.StatusFlags & StatusFlags.Hostile) != 0 && enemy.TargetObjectId != 0)
            return true;
        if ((enemy.StatusFlags & StatusFlags.Hostile) != 0 && enemy.CurrentHp < enemy.MaxHp)
            return true;
        // Last resort: player is in combat and the mob is hostile — likely part of the pull
        // even if the game hasn't set InCombat/target flags yet (common in trusts).
        if ((player.StatusFlags & StatusFlags.InCombat) != 0
            && (enemy.StatusFlags & StatusFlags.Hostile) != 0)
            return true;
        return false;
    }

    /// <param name="radius">Radius in yalms to check.</param>
    /// <param name="player">Current player character.</param>
    /// <returns>Number of valid enemies within radius.</returns>
    public int CountEnemiesInRange(float radius, IPlayerCharacter player)
    {
        SyncUserStickyFromTargetManager();

        // Hard pause: no target → report 0 enemies so AoE thresholds can't trigger.
        if (IsDamageTargetingPaused())
            return 0;

        var playerEffectivelyInCombat = IsPlayerEffectivelyInCombat(player);
        var count = 0;
        var engagedTargetId = GetEngagedTargetIdForEnemyCount();
        foreach (var enemy in GetValidEnemies(radius, player))
        {
            if (playerEffectivelyInCombat)
            {
                if (!ShouldIncludeEnemyForTargeting(enemy, engagedTargetId, player))
                    continue;
            }
            else if ((enemy.StatusFlags & StatusFlags.InCombat) == 0
                     && enemy.GameObjectId != engagedTargetId)
            {
                continue;
            }

            count++;
        }

        return count;
    }

    /// <inheritdoc />
    public int CountEnemiesInRangeOfTarget(float radius, IBattleNpc target, IPlayerCharacter player)
    {
        SyncUserStickyFromTargetManager();

        if (IsDamageTargetingPaused())
            return 0;

        var anchor = ResolveEnemyById(target.GameObjectId);
        if (anchor == null)
            return 0;

        var playerPos = player.Position;
        var centerPos = anchor.Position;
        var scanRange = radius
                        + Vector3.Distance(playerPos, centerPos)
                        + anchor.HitboxRadius
                        + player.HitboxRadius;

        var playerEffectivelyInCombat = IsPlayerEffectivelyInCombat(player);
        var count = 0;
        var engagedTargetId = GetEngagedTargetIdForEnemyCount();

        // Pack counting: anchor is already acquired — skip LoS on scan and near-point filters.
        foreach (var enemy in GetValidEnemies(scanRange, player, applyLineOfSightFilter: false))
        {
            if (!PassesEnemyNearPointFilters(enemy, centerPos, anchor, radius, playerPos))
                continue;

            if (playerEffectivelyInCombat)
            {
                if (!ShouldIncludeEnemyForTargeting(enemy, engagedTargetId, player))
                    continue;
            }
            else if ((enemy.StatusFlags & StatusFlags.InCombat) == 0
                     && enemy.GameObjectId != engagedTargetId)
            {
                continue;
            }

            count++;
        }

        return count;
    }

    /// <summary>
    /// Target ID used for the "in combat or engaged" exception when counting nearby enemies.
    /// Includes sticky combat target when <see cref="ITargetManager.Target"/> is briefly null.
    /// </summary>
    private ulong GetEngagedTargetIdForEnemyCount()
    {
        if (_targetManager.Target is IBattleNpc userTarget)
            return userTarget.GameObjectId;

        if (_userStickyTargetGameObjectId != 0 && ResolveEnemyById(_userStickyTargetGameObjectId) != null)
            return _userStickyTargetGameObjectId;

        return 0;
    }

    /// <summary>
    /// Finds the enemy that has the most other enemies within the specified radius.
    /// Used for targeted AoE spells like Glare IV and Afflatus Misery.
    /// </summary>
    /// <param name="aoeRadius">Radius around the target to count enemies.</param>
    /// <param name="maxRange">Maximum range from player to target.</param>
    /// <param name="player">Current player character.</param>
    /// <returns>Best AoE target and count of enemies that will be hit (including target).</returns>
    public (IBattleNpc? target, int hitCount) FindBestAoETarget(float aoeRadius, float maxRange, IPlayerCharacter player)
    {
        // Hard pause: no target.
        if (IsDamageTargetingPaused())
            return (null, 0);

        IBattleNpc? bestTarget = null;
        int bestHitCount = 0;

        // Collect all valid enemies in reusable work list
        _aoeWorkList.Clear();
        foreach (var enemy in GetValidEnemies(maxRange, player))
        {
            _aoeWorkList.Add(enemy);
        }

        if (_aoeWorkList.Count == 0)
            return (null, 0);

        // Early exit: if only 1 enemy, no need for O(n²) calculation
        if (_aoeWorkList.Count == 1)
            return (_aoeWorkList[0], 1);

        // For each potential target, count how many enemies would be hit
        var aoeRadiusSquared = aoeRadius * aoeRadius;
        foreach (var potentialTarget in _aoeWorkList)
        {
            int hitCount = 1; // Always hits the target itself

            // Count other enemies within AoE radius of this target
            foreach (var other in _aoeWorkList)
            {
                if (other.EntityId == potentialTarget.EntityId)
                    continue;

                var distSquared = Vector3.DistanceSquared(potentialTarget.Position, other.Position);
                if (distSquared <= aoeRadiusSquared)
                {
                    hitCount++;
                }
            }

            if (hitCount > bestHitCount)
            {
                bestHitCount = hitCount;
                bestTarget = potentialTarget;
            }
        }

        return (bestTarget, bestHitCount);
    }

    /// <inheritdoc />
    public IBattleNpc? FindEnemyForAction(EnemyTargetingStrategy strategy, uint actionId, IPlayerCharacter player)
    {
        PrepareDamageTargeting(player);

        // Hard pause: no target → no damage targeting at all.
        if (IsDamageTargetingPaused())
            return null;

        var target = FindEnemyByActionStrategy(strategy, actionId, player);

        if (target == null && strategy == EnemyTargetingStrategy.TankAssist && _configuration.Targeting.UseTankAssistFallback)
            target = FindEnemyByActionStrategy(EnemyTargetingStrategy.LowestHp, actionId, player);

        // Fall back from explicit-target strategies to LowestHp only when strict mode
        // is off. Strict mode keeps explicit-target intent as a hard stop — important
        // for players who use CurrentTarget to manually control every engagement.
        if (target == null && strategy is EnemyTargetingStrategy.CurrentTarget or EnemyTargetingStrategy.FocusTarget
            && (!_configuration.Targeting.StrictCurrentTargetStrategy
                || ShouldRelaxStrictOnCombatDeath(player)))
            target = FindEnemyByActionStrategy(EnemyTargetingStrategy.LowestHp, actionId, player);

        if (target == null)
            target = TryRecoverActionTargetFromStickyGrace(actionId, player, strategy)
                     ?? TryRecoverStickyOnTransientActionRangeFail(actionId, player)
                     ?? TryRecoverUserStickyForAggregateStrategy(strategy, actionId, player);

        return ApplyStickyFallback(target);
    }

    /// <summary>
    /// Invalidates the enemy cache. Call when targets may have changed significantly.
    /// </summary>
    public void InvalidateCache()
    {
        _cachedEnemyIds.Clear();
        _cacheTimer.Restart();
        _userStickyTargetGameObjectId = 0;
    }

    #region Combat death retarget (Layers 1–3)

    /// <summary>
    /// Layer 1 entry: auto-retarget the game hard target when the current one died mid-combat.
    /// Idempotent — no-op when a valid hard target already exists.
    /// </summary>
    private void PrepareDamageTargeting(IPlayerCharacter? player)
    {
        if (player == null)
            return;

        TryAutoRetargetOnCombatDeath(player);
    }

    private bool IsCombatRetargetScenario(IPlayerCharacter player) =>
        IsPlayerEffectivelyInCombat(player)
        && IsHardTargetInvalid()
        && HasLiveHostilesNearby(player);

    private static bool IsPlayerInCombat(IPlayerCharacter player) =>
        (player.StatusFlags & StatusFlags.InCombat) != 0;

    /// <summary>
    /// True when the player's hard target is missing, not an enemy, or dead.
    /// </summary>
    private bool IsHardTargetInvalid()
    {
        if (_targetManager.Target is not IBattleNpc raw)
            return true;

        return ResolveEnemyById(raw.GameObjectId) == null;
    }

    /// <summary>
    /// True when at least one valid in-combat hostile is within retarget scan range.
    /// </summary>
    private bool HasLiveHostilesNearby(IPlayerCharacter player)
    {
        var engagedTargetId = GetEngagedTargetIdForEnemyCount();
        foreach (var enemy in GetValidEnemies(CombatRetargetMaxRangeY, player))
        {
            if (ShouldIncludeEnemyForTargeting(enemy, engagedTargetId, player))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Layer 1: RSR-style game target write when hard target died and live hostiles remain.
    /// </summary>
    private void TryAutoRetargetOnCombatDeath(IPlayerCharacter player)
    {
        if (!IsCombatRetargetScenario(player))
            return;

        if (HasValidUserSelectedEnemy())
            return;

        var pickStrategy = CombatRetargetPolicy.ResolveAutoRetargetStrategy(
            _configuration.Targeting.EnemyStrategy);

        var pick = FindEnemyByStrategy(pickStrategy, CombatRetargetMaxRangeY, player);
        if (pick == null)
            return;

        SetGameHardTarget(pick);
    }

    /// <summary>
    /// Writes the client's hard target and syncs user sticky (feeds gap closer Rule 1).
    /// </summary>
    private void SetGameHardTarget(IBattleNpc enemy)
    {
        _targetManager.Target = enemy;
        SetUserStickyTarget(enemy.GameObjectId);
    }

    /// <summary>
    /// Layer 3: bypass StrictCurrentTargetStrategy for explicit-strategy → aggregate fallback.
    /// </summary>
    private bool ShouldRelaxStrictOnCombatDeath(IPlayerCharacter player) =>
        CombatRetargetPolicy.ShouldRelaxStrictOnCombatDeath(
            _configuration.Targeting.StrictCurrentTargetStrategy,
            _configuration.Targeting.EnemyStrategy,
            IsCombatRetargetScenario(player));

    #endregion

    private bool HasValidUserSelectedEnemy()
    {
        return _targetManager.Target is IBattleNpc enemy
               && ResolveEnemyById(enemy.GameObjectId) != null;
    }

    /// <summary>
    /// Captures the player's hard target into the user sticky slot when it resolves to a valid enemy.
    /// </summary>
    private void SyncUserStickyFromTargetManager()
    {
        if (_targetManager.Target is not IBattleNpc raw)
            return;

        var resolved = ResolveEnemyById(raw.GameObjectId);
        if (resolved != null)
            SetUserStickyTarget(resolved.GameObjectId);
    }

    private void SetUserStickyTarget(ulong gameObjectId)
    {
        _userStickyTargetGameObjectId = gameObjectId;
        _userStickyTargetLastValidMs = _stickyClock.ElapsedMilliseconds;
    }

    private void TouchUserStickyGrace()
    {
        if (_userStickyTargetGameObjectId != 0)
            _userStickyTargetLastValidMs = _stickyClock.ElapsedMilliseconds;
    }

    private IBattleNpc? ResolveUserStickyTarget()
    {
        if (_userStickyTargetGameObjectId == 0)
            return null;

        if (_stickyClock.ElapsedMilliseconds - _userStickyTargetLastValidMs > StickyTargetGraceMs)
        {
            _userStickyTargetGameObjectId = 0;
            return null;
        }

        return ResolveEnemyById(_userStickyTargetGameObjectId);
    }

    private IBattleNpc? ResolveEnemyById(ulong gameObjectId)
    {
        if (gameObjectId == 0)
            return null;

        var obj = _objectTable.SearchById(gameObjectId);
        if (obj is not IBattleNpc enemy || !IsStillValid(enemy))
            return null;

        return enemy;
    }

    /// <summary>
    /// Returns the strategy-resolved target when present; otherwise the user sticky target.
    /// Does not write strategy picks into the user sticky slot (prevents LowestHp thrashing).
    /// </summary>
    private IBattleNpc? ApplyStickyFallback(IBattleNpc? resolved)
    {
        SyncUserStickyFromTargetManager();

        if (resolved != null)
            return resolved;

        var sticky = ResolveUserStickyTarget();
        if (sticky != null)
            TouchUserStickyGrace();
        return sticky;
    }

    /// <summary>
    /// When <see cref="IsActionInRange"/> fails transiently on the player's explicit target but that
    /// target is still alive and matches the sticky combat ID within the grace window, keep it.
    /// </summary>
    private IBattleNpc? TryRecoverActionTargetFromStickyGrace(
        uint actionId,
        IPlayerCharacter player,
        EnemyTargetingStrategy strategy)
    {
        var explicitEnemy = strategy switch
        {
            EnemyTargetingStrategy.CurrentTarget => ResolveEnemyById(_targetManager.Target?.GameObjectId ?? 0),
            EnemyTargetingStrategy.FocusTarget => ResolveEnemyById(_targetManager.FocusTarget?.GameObjectId ?? 0),
            _ => null,
        };

        if (explicitEnemy == null)
            return null;

        if (IsActionInRange(actionId, player, explicitEnemy))
            return explicitEnemy;

        return TryStickyActionRangeGrace(explicitEnemy) ? explicitEnemy : null;
    }

    /// <summary>
    /// When aggregate action strategies find no in-range enemy but the sticky combat target is still
    /// valid, keep attacking through transient <see cref="IsActionInRange"/> failures.
    /// </summary>
    private IBattleNpc? TryRecoverStickyOnTransientActionRangeFail(uint actionId, IPlayerCharacter player)
    {
        var sticky = ResolveUserStickyTarget();
        if (sticky == null)
            return null;

        if (IsActionInRange(actionId, player, sticky))
            return null;

        TouchUserStickyGrace();
        return sticky;
    }

    /// <summary>
    /// For aggregate strategies (LowestHp, etc.): when no candidate is returned but the user's
    /// hard target is in action range, prefer that target so multi-mob scans cannot drop to null.
    /// </summary>
    private IBattleNpc? TryRecoverUserStickyForAggregateStrategy(
        EnemyTargetingStrategy strategy,
        uint actionId,
        IPlayerCharacter player)
    {
        if (strategy is EnemyTargetingStrategy.CurrentTarget or EnemyTargetingStrategy.FocusTarget)
            return null;

        var sticky = ResolveUserStickyTarget();
        if (sticky == null || !IsActionInRange(actionId, player, sticky))
            return null;

        TouchUserStickyGrace();
        return sticky;
    }

    /// <summary>
    /// True when <paramref name="enemy"/> is the user sticky target and still within the grace window.
    /// Refreshes the grace timestamp so intermittent range-check failures do not outlive the window.
    /// </summary>
    private bool TryStickyActionRangeGrace(IBattleNpc enemy)
    {
        if (_userStickyTargetGameObjectId == 0 || enemy.GameObjectId != _userStickyTargetGameObjectId)
            return false;

        if (_stickyClock.ElapsedMilliseconds - _userStickyTargetLastValidMs > StickyTargetGraceMs)
            return false;

        if (ResolveEnemyById(enemy.GameObjectId) == null)
            return false;

        TouchUserStickyGrace();
        return true;
    }

    private IBattleNpc? FindEnemyByActionStrategy(EnemyTargetingStrategy strategy, uint actionId, IPlayerCharacter player)
    {
        return strategy switch
        {
            EnemyTargetingStrategy.LowestHp => FindLowestHpEnemyForAction(actionId, player),
            EnemyTargetingStrategy.HighestHp => FindHighestHpEnemyForAction(actionId, player),
            EnemyTargetingStrategy.Nearest => FindNearestEnemyForAction(actionId, player),
            EnemyTargetingStrategy.TankAssist => FindTankTargetForAction(actionId, player),
            EnemyTargetingStrategy.CurrentTarget => FindCurrentTargetForAction(actionId, player),
            EnemyTargetingStrategy.FocusTarget => FindFocusTargetForAction(actionId, player),
            _ => FindLowestHpEnemyForAction(actionId, player)
        };
    }

    private IBattleNpc? FindLowestHpEnemyForAction(uint actionId, IPlayerCharacter player)
    {
        IBattleNpc? best = null;
        uint lowestHp = uint.MaxValue;
        foreach (var candidate in GetActionCandidates(player))
        {
            if (!IsActionInRange(actionId, player, candidate)) continue;
            if (candidate.CurrentHp < lowestHp)
            {
                lowestHp = candidate.CurrentHp;
                best = candidate;
            }
        }
        return best;
    }

    private IBattleNpc? FindHighestHpEnemyForAction(uint actionId, IPlayerCharacter player)
    {
        IBattleNpc? best = null;
        uint highestHp = 0;
        foreach (var candidate in GetActionCandidates(player))
        {
            if (!IsActionInRange(actionId, player, candidate)) continue;
            if (candidate.CurrentHp > highestHp)
            {
                highestHp = candidate.CurrentHp;
                best = candidate;
            }
        }
        return best;
    }

    private IBattleNpc? FindNearestEnemyForAction(uint actionId, IPlayerCharacter player)
    {
        IBattleNpc? best = null;
        float nearestDist = float.MaxValue;
        var playerPos = player.Position;
        foreach (var candidate in GetActionCandidates(player))
        {
            if (!IsActionInRange(actionId, player, candidate)) continue;
            var dist = Vector3.DistanceSquared(playerPos, candidate.Position);
            if (dist < nearestDist)
            {
                nearestDist = dist;
                best = candidate;
            }
        }
        return best;
    }

    private IBattleNpc? FindTankTargetForAction(uint actionId, IPlayerCharacter player)
    {
        foreach (var member in _partyList)
        {
            if (member.GameObject is not IBattleChara chara) continue;
            if (!TankJobIds.Contains(chara.ClassJob.RowId)) continue;
            var targetId = chara.TargetObjectId;
            if (targetId is 0 or 0xE0000000) continue;
            var target = _objectTable.SearchById(targetId);
            if (target is IBattleNpc enemy && IsStillValid(enemy) && IsActionInRange(actionId, player, enemy))
                return enemy;
        }
        return null;
    }

    private IBattleNpc? FindCurrentTargetForAction(uint actionId, IPlayerCharacter player)
    {
        var resolved = ResolveEnemyById(_targetManager.Target?.GameObjectId ?? 0);
        if (resolved == null)
            return null;

        if (IsActionInRange(actionId, player, resolved))
            return resolved;

        return TryStickyActionRangeGrace(resolved) ? resolved : null;
    }

    private IBattleNpc? FindFocusTargetForAction(uint actionId, IPlayerCharacter player)
    {
        var resolved = ResolveEnemyById(_targetManager.FocusTarget?.GameObjectId ?? 0);
        if (resolved == null)
            return null;

        if (IsActionInRange(actionId, player, resolved))
            return resolved;

        return TryStickyActionRangeGrace(resolved) ? resolved : null;
    }

    /// <summary>
    /// Iterates all nearby battle NPCs as candidates for action-based range checks.
    /// Uses a generous 15y pre-filter (safe for any 3y melee action including large boss hitboxes).
    /// </summary>
    private IEnumerable<IBattleNpc> GetActionCandidates(IPlayerCharacter player)
    {
        foreach (var obj in _objectTable)
        {
            if (obj.ObjectKind != ObjectKind.BattleNpc) continue;
            if (!obj.IsTargetable) continue;
            if (obj.IsDead) continue;
            if (obj.YalmDistanceX > 15) continue;
            if (obj is not IBattleNpc npc) continue;
            if ((byte)npc.BattleNpcKind != Daedalus.Compat.BattleNpcKinds.Combatant && npc.SubKind != 0) continue;
            if (_configuration.Targeting.EnableInvulnerabilityFiltering &&
                HasInvulnerabilityStatus(npc))
                continue;
            yield return npc;
        }
    }

    /// <summary>
    /// Uses the game's native GetActionInRangeOrLoS to check if an action can reach a target.
    /// Returns true for result 0 (in range + LoS) or 565 (in range but facing wrong way).
    /// </summary>
    private static unsafe bool IsActionInRange(uint actionId, IGameObject player, IGameObject target)
    {
        try
        {
            var playerStruct = (FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)player.Address;
            var targetStruct = (FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)target.Address;
            if (playerStruct == null || targetStruct == null) return false;
            var result = ActionManager.GetActionInRangeOrLoS(actionId, playerStruct, targetStruct);
            return result is 0 or 565; // 0=in range+LoS, 565=in range but facing wrong way
        }
        catch
        {
            return false;
        }
    }

    private IBattleNpc? FindEnemyByStrategy(EnemyTargetingStrategy strategy, float maxRange, IPlayerCharacter player)
    {
        return strategy switch
        {
            EnemyTargetingStrategy.LowestHp => FindLowestHpEnemy(maxRange, player),
            EnemyTargetingStrategy.HighestHp => FindHighestHpEnemy(maxRange, player),
            EnemyTargetingStrategy.Nearest => FindNearestEnemy(maxRange, player),
            EnemyTargetingStrategy.TankAssist => FindTankTarget(maxRange, player),
            EnemyTargetingStrategy.CurrentTarget => FindCurrentTarget(maxRange, player),
            EnemyTargetingStrategy.FocusTarget => FindFocusTarget(maxRange, player),
            _ => FindLowestHpEnemy(maxRange, player)
        };
    }

    private IBattleNpc? FindLowestHpEnemy(float maxRange, IPlayerCharacter player)
    {
        IBattleNpc? best = null;
        uint lowestHp = uint.MaxValue;
        var currentTargetId = _targetManager.Target is IBattleNpc ? _targetManager.Target.GameObjectId : 0UL;

        foreach (var enemy in GetValidEnemies(maxRange, player))
        {
            if (!ShouldIncludeEnemyForTargeting(enemy, currentTargetId, player))
                continue;

            if (enemy.CurrentHp < lowestHp)
            {
                lowestHp = enemy.CurrentHp;
                best = enemy;
            }
        }

        return best;
    }

    private IBattleNpc? FindHighestHpEnemy(float maxRange, IPlayerCharacter player)
    {
        IBattleNpc? best = null;
        uint highestHp = 0;
        var currentTargetId = _targetManager.Target is IBattleNpc ? _targetManager.Target.GameObjectId : 0UL;

        foreach (var enemy in GetValidEnemies(maxRange, player))
        {
            if (!ShouldIncludeEnemyForTargeting(enemy, currentTargetId, player))
                continue;

            if (enemy.CurrentHp > highestHp)
            {
                highestHp = enemy.CurrentHp;
                best = enemy;
            }
        }

        return best;
    }

    private IBattleNpc? FindNearestEnemy(float maxRange, IPlayerCharacter player)
    {
        IBattleNpc? best = null;
        float nearestDist = float.MaxValue;
        var playerPos = player.Position;
        var currentTargetId = _targetManager.Target is IBattleNpc ? _targetManager.Target.GameObjectId : 0UL;

        foreach (var enemy in GetValidEnemies(maxRange, player))
        {
            if (!ShouldIncludeEnemyForTargeting(enemy, currentTargetId, player))
                continue;

            var dist = Vector3.DistanceSquared(playerPos, enemy.Position);
            if (dist < nearestDist)
            {
                nearestDist = dist;
                best = enemy;
            }
        }

        return best;
    }

    private IBattleNpc? FindTankTarget(float maxRange, IPlayerCharacter player)
    {
        // Find tank in party and get their target
        foreach (var member in _partyList)
        {
            if (member.GameObject is not IBattleChara chara)
                continue;

            // Check if this party member is a tank
            if (!TankJobIds.Contains(chara.ClassJob.RowId))
                continue;

            // Get what the tank is targeting
            var targetId = chara.TargetObjectId;
            if (targetId is 0 or 0xE0000000)
                continue;

            var target = _objectTable.SearchById(targetId);
            if (target is IBattleNpc enemy && IsValidEnemy(enemy, maxRange, player))
                return enemy;
        }

        return null;
    }

    private IBattleNpc? FindCurrentTarget(float maxRange, IPlayerCharacter player)
    {
        var resolved = ResolveEnemyById(_targetManager.Target?.GameObjectId ?? 0);
        if (resolved != null && IsValidEnemy(resolved, maxRange, player))
            return resolved;

        return null;
    }

    private IBattleNpc? FindFocusTarget(float maxRange, IPlayerCharacter player)
    {
        var resolved = ResolveEnemyById(_targetManager.FocusTarget?.GameObjectId ?? 0);
        if (resolved != null && IsValidEnemy(resolved, maxRange, player))
            return resolved;

        return null;
    }

    /// <summary>
    /// Gets valid enemies in range, using an ID cache when available.
    /// Each entry is re-resolved through <see cref="IObjectTable.SearchById"/> every iteration.
    /// </summary>
    private IEnumerable<IBattleNpc> GetValidEnemies(
        float maxRange,
        IPlayerCharacter player,
        bool applyLineOfSightFilter = true)
    {
        var cacheAge = _cacheTimer.ElapsedMilliseconds;
        if (_cachedEnemyIds.Count > 0 &&
            cacheAge < _configuration.Targeting.TargetCacheTtlMs &&
            Math.Abs(_lastCacheRange - maxRange) < 0.1f &&
            _lastScanApplyLineOfSight == applyLineOfSightFilter)
        {
            var yieldedFromCache = false;
            for (var i = _cachedEnemyIds.Count - 1; i >= 0; i--)
            {
                var enemy = TryResolveValidEnemyInRange(_cachedEnemyIds[i], maxRange, player, applyLineOfSightFilter);
                if (enemy == null)
                    _cachedEnemyIds.RemoveAt(i);
                else
                {
                    yieldedFromCache = true;
                    yield return enemy;
                }
            }

            if (yieldedFromCache)
                yield break;
        }

        _cachedEnemyIds.Clear();
        _lastCacheRange = maxRange;
        _lastScanApplyLineOfSight = applyLineOfSightFilter;
        _cacheTimer.Restart();

        var playerPos = player.Position;
        var maxRangeYalms = (byte)Math.Ceiling(maxRange);

        foreach (var obj in _objectTable)
        {
            if (obj.ObjectKind != ObjectKind.BattleNpc)
                continue;

            if (!obj.IsTargetable || obj.IsDead)
                continue;

            if (obj.YalmDistanceX > maxRangeYalms + (int)Math.Ceiling(obj.HitboxRadius))
                continue;

            if (obj is not IBattleNpc npc)
                continue;

            if (!PassesEnemyInRangeFilters(npc, maxRange, player, playerPos, applyLineOfSightFilter))
                continue;

            _cachedEnemyIds.Add(npc.GameObjectId);
            yield return npc;
        }
    }

    /// <summary>
    /// Re-resolves a cached enemy ID and applies the same range/LoS/invuln filters as a fresh scan.
    /// </summary>
    private IBattleNpc? TryResolveValidEnemyInRange(
        ulong gameObjectId,
        float maxRange,
        IPlayerCharacter player,
        bool applyLineOfSightFilter = true)
    {
        var obj = _objectTable.SearchById(gameObjectId);
        if (obj is not IBattleNpc npc || !IsStillValid(npc))
            return null;

        return PassesEnemyInRangeFilters(npc, maxRange, player, player.Position, applyLineOfSightFilter)
            ? npc
            : null;
    }

    private bool PassesEnemyInRangeFilters(
        IBattleNpc npc,
        float maxRange,
        IPlayerCharacter player,
        Vector3 playerPos,
        bool applyLineOfSightFilter = true)
    {
        if ((byte)npc.BattleNpcKind != Daedalus.Compat.BattleNpcKinds.Combatant && npc.SubKind != 0)
            return false;

        var effectiveRange = maxRange + npc.HitboxRadius + player.HitboxRadius;
        if (Vector3.DistanceSquared(playerPos, npc.Position) > effectiveRange * effectiveRange)
            return false;

        if (applyLineOfSightFilter
            && _configuration.Targeting.EnableLineOfSightFiltering
            && !HasLineOfSight(playerPos, npc.Position))
            return false;

        if (_configuration.Targeting.EnableInvulnerabilityFiltering &&
            HasInvulnerabilityStatus(npc))
            return false;

        if (!EnemyAttackability.IsPlayerAttackable(npc))
            return false;

        return true;
    }

    /// <summary>
    /// Kind, LoS, invuln, and anchor-centered distance — used for targeted AoE pack counting.
    /// </summary>
    private bool PassesEnemyNearPointFilters(
        IBattleNpc npc,
        Vector3 centerPos,
        IBattleNpc anchor,
        float radius,
        Vector3 playerPos)
    {
        if ((byte)npc.BattleNpcKind != Daedalus.Compat.BattleNpcKinds.Combatant && npc.SubKind != 0)
            return false;

        var effectiveRange = radius + npc.HitboxRadius + anchor.HitboxRadius;
        if (Vector3.DistanceSquared(centerPos, npc.Position) > effectiveRange * effectiveRange)
            return false;

        // No LoS check — used only for AoE pack counting around an already-acquired anchor.

        if (_configuration.Targeting.EnableInvulnerabilityFiltering &&
            HasInvulnerabilityStatus(npc))
            return false;

        if (!EnemyAttackability.IsPlayerAttackable(npc))
            return false;

        return true;
    }

    private static bool IsValidEnemy(IBattleNpc enemy, float maxRange, IPlayerCharacter player)
    {
        if (!enemy.IsTargetable || enemy.IsDead)
            return false;

        if ((byte)enemy.BattleNpcKind != Daedalus.Compat.BattleNpcKinds.Combatant && enemy.SubKind != 0)
            return false;

        if (!EnemyAttackability.IsPlayerAttackable(enemy))
            return false;

        return DistanceHelper.IsInRange(player.Position, enemy.Position, maxRange + enemy.HitboxRadius + player.HitboxRadius);
    }

    private bool IsPlayerEffectivelyInCombat(IPlayerCharacter player) =>
        EnemyEngagementPolicy.IsPlayerEffectivelyInCombat(player, _configuration, _partyList, _objectTable);

    private bool ShouldRelaxEnemyInCombatRequirement(IPlayerCharacter player) =>
        EnemyEngagementPolicy.ShouldRelaxEnemyInCombatRequirement(
            _configuration, player, _partyList, _objectTable);

    private bool ShouldIncludeEnemyForTargeting(IBattleNpc enemy, ulong currentTargetId, IPlayerCharacter player) =>
        EnemyEngagementPolicy.ShouldIncludeEnemyForTargeting(
            enemy,
            currentTargetId,
            IsPlayerEffectivelyInCombat(player),
            ShouldRelaxEnemyInCombatRequirement(player));

    private static bool IsStillValid(IBattleNpc enemy)
    {
        return enemy.IsTargetable && !enemy.IsDead;
    }

    /// <summary>
    /// Eye height above <paramref name="playerPos"/> for LoS ray origin (avoids floor / self-hitbox).
    /// </summary>
    private const float LineOfSightPlayerEyeOffsetY = 1.8f;

    /// <summary>
    /// Target point height above enemy root for LoS ray end (avoids floor and feet colliders on large mobs).
    /// </summary>
    private const float LineOfSightEnemyChestOffsetY = 1.5f;

    /// <summary>
    /// Stop the ray short of the target point so the target's own collision mesh is not counted as a block.
    /// </summary>
    private const float LineOfSightRayStopShortYalms = 0.75f;

    /// <summary>
    /// Checks line of sight from the player's eye height to chest height on the enemy using a BGCollision raycast.
    /// Returns false if geometry blocks the path.
    /// </summary>
    private static unsafe bool HasLineOfSight(Vector3 playerPos, Vector3 enemyPos)
    {
        try
        {
            var eyePos = playerPos with { Y = playerPos.Y + LineOfSightPlayerEyeOffsetY };
            var targetPos = enemyPos with { Y = enemyPos.Y + LineOfSightEnemyChestOffsetY };
            var direction = targetPos - eyePos;
            var distance = direction.Length();
            if (distance < 0.01f)
                return true;

            direction /= distance;
            var castDistance = Math.Max(0f, distance - LineOfSightRayStopShortYalms);
            return !BGCollisionModule.RaycastMaterialFilter(eyePos, direction, out _, castDistance);
        }
        catch
        {
            // BGCollision unavailable (loading screen, etc.) — assume LoS is fine
            return true;
        }
    }

    private static float GetDotDuration(IBattleChara target, uint statusId)
    {
        foreach (var status in target.StatusList)
        {
            if (status.StatusId == statusId)
                return status.RemainingTime;
        }
        return 0f;
    }

    /// <summary>
    /// Checks whether an enemy has a known invulnerability status effect.
    /// Used to skip immune targets during auto-targeting (boss phase transitions,
    /// invulnerable adds, untouchable objects like ARR crystals).
    /// </summary>
    private static bool HasInvulnerabilityStatus(IBattleNpc npc)
    {
        if (npc.StatusList == null)
            return false;

        foreach (var status in npc.StatusList)
        {
            if (FFXIVConstants.EnemyInvulnerabilityStatusIds.Contains(status.StatusId))
                return true;
        }

        return false;
    }

    // ── Cone / Line AoE Targeting ──

    /// <inheritdoc />
    public (IBattleNpc? target, int hitCount, float optimalAngle) FindBestConeAoETarget(
        float coneHalfAngle, float radius, float maxRange, IPlayerCharacter player)
    {
        if (IsDamageTargetingPaused())
            return (null, 0, 0f);

        // Use the ability's effect range for candidate filtering, not the rotation's targeting range
        var candidateRange = MathF.Max(radius, maxRange);
        _aoeWorkList.Clear();
        foreach (var e in GetValidEnemies(candidateRange, player))
            _aoeWorkList.Add(e);

        if (_aoeWorkList.Count == 0) return (null, 0, 0f);
        if (_aoeWorkList.Count == 1)
        {
            var dx = _aoeWorkList[0].Position.X - player.Position.X;
            var dz = _aoeWorkList[0].Position.Z - player.Position.Z;
            return (_aoeWorkList[0], 1, MathF.Atan2(dx, dz));
        }

        int bestCount = 0;
        float bestAngle = 0f;
        IBattleNpc? bestTarget = null;
        var playerPos = player.Position;

        // For each enemy as potential target: aim at them and count how many others
        // the cone/line would clip. We can only face enemies we target (game auto-faces).
        foreach (var candidate in _aoeWorkList)
        {
            var dx = candidate.Position.X - playerPos.X;
            var dz = candidate.Position.Z - playerPos.Z;
            var aimAngle = MathF.Atan2(dx, dz);

            var count = 0;
            foreach (var e in _aoeWorkList)
            {
                var edx = e.Position.X - playerPos.X;
                var edz = e.Position.Z - playerPos.Z;
                var dist = MathF.Sqrt(edx * edx + edz * edz);
                if (dist - e.HitboxRadius > radius) continue;

                var angleToE = MathF.Atan2(edx, edz);
                var diff = NormalizeAngle(angleToE - aimAngle);
                if (MathF.Abs(diff) <= coneHalfAngle)
                    count++;
            }

            if (count > bestCount)
            {
                bestCount = count;
                bestAngle = aimAngle;
                bestTarget = candidate;
            }
        }

        return (bestTarget, bestCount, bestAngle);
    }

    /// <inheritdoc />
    public (IBattleNpc? target, int hitCount, float optimalAngle) FindBestLineAoETarget(
        float lineWidth, float length, float maxRange, IPlayerCharacter player)
    {
        if (IsDamageTargetingPaused())
            return (null, 0, 0f);

        // Use the ability's effect range for candidate filtering, not the rotation's targeting range
        var candidateRange = MathF.Max(length, maxRange);
        _aoeWorkList.Clear();
        foreach (var e in GetValidEnemies(candidateRange, player))
            _aoeWorkList.Add(e);

        if (_aoeWorkList.Count == 0) return (null, 0, 0f);
        if (_aoeWorkList.Count == 1)
        {
            var dx = _aoeWorkList[0].Position.X - player.Position.X;
            var dz = _aoeWorkList[0].Position.Z - player.Position.Z;
            return (_aoeWorkList[0], 1, MathF.Atan2(dx, dz));
        }

        int bestCount = 0;
        float bestAngle = 0f;
        IBattleNpc? bestTarget = null;
        var playerPos = player.Position;
        var halfWidth = lineWidth * 0.5f;

        // For each enemy as potential target: aim at them and count how many others
        // the line would clip. We can only face enemies we target.
        foreach (var candidate in _aoeWorkList)
        {
            var dx = candidate.Position.X - playerPos.X;
            var dz = candidate.Position.Z - playerPos.Z;
            var aimAngle = MathF.Atan2(dx, dz);
            var sinH = MathF.Sin(aimAngle);
            var cosH = MathF.Cos(aimAngle);

            var count = 0;
            foreach (var e in _aoeWorkList)
            {
                var edx = e.Position.X - playerPos.X;
                var edz = e.Position.Z - playerPos.Z;

                var forward = edx * sinH + edz * cosH;
                var lateral = edx * cosH - edz * sinH;

                if (forward >= -e.HitboxRadius
                    && forward <= length + e.HitboxRadius
                    && MathF.Abs(lateral) <= halfWidth + e.HitboxRadius)
                    count++;
            }

            if (count > bestCount)
            {
                bestCount = count;
                bestAngle = aimAngle;
                bestTarget = candidate;
            }
        }

        return (bestTarget, bestCount, bestAngle);
    }

    private static float NormalizeAngle(float angle)
    {
        while (angle > MathF.PI) angle -= 2f * MathF.PI;
        while (angle < -MathF.PI) angle += 2f * MathF.PI;
        return angle;
    }
}
