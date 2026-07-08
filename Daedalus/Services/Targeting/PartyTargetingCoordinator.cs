using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using Daedalus.Config;
using Daedalus.Data;
using Daedalus.Services.Analytics;
using Daedalus.Services.Network;

namespace Daedalus.Services.Targeting;

/// <summary>
/// Enforces the party-wide target mode (Focus / Split / Kill Adds) set from the coordination window.
/// Per frame it forces the local toon's hard target for the active mode; a companion overlay
/// (<see cref="BuildTargetingOverlay"/>, applied by DutyConfigurationService) points the rotation at
/// that forced target. Role-gated so the Main Tank is NEVER moved off the boss — only DPS, and the
/// designated off-tank under Kill Adds, are ever retargeted.
/// </summary>
public sealed class PartyTargetingCoordinator
{
    private const float EnemyScanRangeY = 30f;
    private const float SplitRecomputeSeconds = 1.5f;

    private readonly CoordinationBus _bus;
    private readonly IObjectTable _objectTable;
    private readonly ITargetManager _targetManager;
    private readonly IDpsMeterService? _dpsMeter;

    private DateTime _lastSplitCompute = DateTime.MinValue;
    private ulong _splitAssignedId;

    public PartyTargetingCoordinator(
        CoordinationBus bus,
        IObjectTable objectTable,
        ITargetManager targetManager,
        IDpsMeterService? dpsMeter = null)
    {
        _bus = bus;
        _objectTable = objectTable;
        _targetManager = targetManager;
        _dpsMeter = dpsMeter;
    }

    /// <summary>Per-frame enforcement. No-op unless a mode is active and the local toon is eligible.</summary>
    public void Tick()
    {
        var mode = _bus.CurrentTargetMode;
        if (mode == PartyTargetMode.None)
        {
            _splitAssignedId = 0;
            return;
        }

        var player = _objectTable.LocalPlayer;
        if (player == null || !IsLocalEligible(mode, player))
            return;

        switch (mode)
        {
            case PartyTargetMode.Focus:
                var focus = ResolveEnemy(_bus.FocusTargetId);
                if (focus != null)
                    SetHardTarget(focus);
                break;

            case PartyTargetMode.KillAdds:
                var add = PickAdd(player);
                if (add != null)
                    SetHardTarget(add);
                break;

            case PartyTargetMode.Split:
                TickSplit(player);
                break;
        }
    }

    /// <summary>
    /// Targeting overlay for the local toon under the active mode, or null when no mode is active or
    /// the toon is exempt. Uniform across modes: follow the coordinator-forced hard target, with a
    /// graceful (non-strict) fallback so a dead forced target never strands the rotation.
    /// </summary>
    public Action<TargetingConfig>? BuildTargetingOverlay()
    {
        var mode = _bus.CurrentTargetMode;
        if (mode == PartyTargetMode.None)
            return null;

        var player = _objectTable.LocalPlayer;
        if (player == null || !IsLocalEligible(mode, player))
            return null;

        return cfg =>
        {
            cfg.EnemyStrategy = EnemyTargetingStrategy.CurrentTarget;
            cfg.StrictCurrentTargetStrategy = false;
        };
    }

    private bool IsLocalEligible(PartyTargetMode mode, IPlayerCharacter player)
        => IsEligible(player.ClassJob.RowId, mode, IsLocalOffTank());

    /// <summary>
    /// Role gate — the Main Tank invariant lives here (pure, so it can be unit-tested without a live
    /// player). A tank is eligible ONLY when it is the designated off-tank and the mode is Kill Adds;
    /// every other tank (the MT) is always exempt. Healers are always exempt; DPS are eligible in
    /// every active mode.
    /// </summary>
    public static bool IsEligible(uint jobId, PartyTargetMode mode, bool isDesignatedOffTank)
    {
        if (mode == PartyTargetMode.None)
            return false;

        if (JobRegistry.IsTank(jobId))
            return mode == PartyTargetMode.KillAdds && isDesignatedOffTank;

        if (JobRegistry.IsHealer(jobId))
            return false;

        // DPS — eligible in every active mode.
        return true;
    }

    private bool IsLocalOffTank() =>
        _bus.OffTankSenderId.Length > 0
        && string.Equals(_bus.OffTankSenderId, _bus.LocalSenderId, StringComparison.Ordinal);

    private void TickSplit(IPlayerCharacter player)
    {
        var now = DateTime.UtcNow;
        if ((now - _lastSplitCompute).TotalSeconds >= SplitRecomputeSeconds)
        {
            _lastSplitCompute = now;
            RecomputeSplit(player, now);
        }

        if (_splitAssignedId != 0)
        {
            var enemy = ResolveEnemy(_splitAssignedId);
            if (enemy != null)
                SetHardTarget(enemy);
        }
    }

    private void RecomputeSplit(IPlayerCharacter player, DateTime now)
    {
        var enemies = EnumerateEnemies(player.Position)
            .Select(n => new SplitEnemy(n.GameObjectId, n.CurrentHp, n.Position))
            .ToList();

        var toons = BuildSplitToons(now);
        var assignment = SplitTargetAssigner.Assign(enemies, toons);
        _splitAssignedId = assignment.TryGetValue(_bus.LocalSenderId, out var id) ? id : 0;
    }

    private List<SplitToon> BuildSplitToons(DateTime now)
    {
        var dpsByName = BuildDpsLookup();
        var toons = new List<SplitToon>();
        foreach (var peer in _bus.Roster)
        {
            if (peer.IsStale(now) || peer.Role != "DPS" || !peer.InCombat)
                continue;

            var dps = dpsByName.TryGetValue(peer.CharacterName, out var d) && d > 0f ? d : 1f;
            toons.Add(new SplitToon(peer.SenderId, dps, peer.Position, JobRegistry.IsMeleeDps(peer.JobId)));
        }

        return toons;
    }

    private Dictionary<string, float> BuildDpsLookup()
    {
        var map = new Dictionary<string, float>(StringComparer.Ordinal);
        var encounter = _dpsMeter?.Current;
        if (encounter == null)
            return map;

        foreach (var combatant in encounter.GetRanked())
            map[combatant.Name] = encounter.GetDps(combatant);

        return map;
    }

    /// <summary>Nearest non-boss hostile (boss = the highest-max-HP enemy). Null when only the boss remains.</summary>
    private IBattleNpc? PickAdd(IPlayerCharacter player)
    {
        var enemies = EnumerateEnemies(player.Position).ToList();
        if (enemies.Count == 0)
            return null;

        IBattleNpc? boss = null;
        uint bossMaxHp = 0;
        foreach (var e in enemies)
        {
            if (e.MaxHp > bossMaxHp) { bossMaxHp = e.MaxHp; boss = e; }
        }

        IBattleNpc? nearestAdd = null;
        var nearest = float.MaxValue;
        foreach (var e in enemies)
        {
            if (enemies.Count > 1 && ReferenceEquals(e, boss))
                continue;

            var dist = Vector3.DistanceSquared(player.Position, e.Position);
            if (dist < nearest)
            {
                nearest = dist;
                nearestAdd = e;
            }
        }

        return nearestAdd;
    }

    private IBattleNpc? ResolveEnemy(ulong gameObjectId)
    {
        if (gameObjectId == 0)
            return null;

        return _objectTable.SearchById(gameObjectId) is IBattleNpc npc && npc.CurrentHp > 0 ? npc : null;
    }

    private IEnumerable<IBattleNpc> EnumerateEnemies(Vector3 center)
    {
        foreach (var obj in _objectTable)
        {
            if (obj is not IBattleNpc npc)
                continue;
            if ((byte)npc.BattleNpcKind != Daedalus.Compat.BattleNpcKinds.Combatant && npc.SubKind != 0)
                continue;
            if (npc.CurrentHp == 0 || !npc.IsTargetable)
                continue;
            if (!EnemyAttackability.IsPlayerAttackable(npc))
                continue;
            if (Vector3.Distance(center, npc.Position) > EnemyScanRangeY)
                continue;

            yield return npc;
        }
    }

    private void SetHardTarget(IBattleNpc enemy)
    {
        if (_targetManager.Target?.GameObjectId != enemy.GameObjectId)
            _targetManager.Target = enemy;
    }
}
