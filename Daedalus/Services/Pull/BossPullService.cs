using System;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;

namespace Daedalus.Services.Pull;

/// <summary>
/// Targets the boss the boss engine says nobody else will pull, so the rotation opens on it (see
/// <see cref="BossPullPolicy"/>). This only gathers the facts and sets the target.
/// </summary>
public sealed class BossPullService
{
    private readonly IObjectTable _objectTable;
    private readonly ITargetManager _targetManager;
    private readonly Func<bool> _enabled;
    private readonly Func<ulong> _pullTargetId;
    private readonly Func<bool> _mounted;
    private readonly IPluginLog? _log;

    private DateTime _lastSet = DateTime.MinValue;

    public BossPullService(IObjectTable objectTable, ITargetManager targetManager, Func<bool> enabled, Func<ulong> pullTargetId,
        Func<bool> mounted, IPluginLog? log = null)
    {
        _objectTable = objectTable;
        _targetManager = targetManager;
        _enabled = enabled;
        _pullTargetId = pullTargetId;
        _mounted = mounted;
        _log = log;
    }

    public void Update(IPlayerCharacter? player)
    {
        if (player == null)
            return;

        var enabled = _enabled();
        var id = enabled ? _pullTargetId() : 0ul;
        var boss = id != 0 ? _objectTable.SearchById(id) : null;
        var current = _targetManager.Target;
        var now = DateTime.UtcNow;

        var situation = new BossPullSituation(
            Enabled: enabled,
            PullTargetId: id,
            PullTargetFound: boss != null,
            SelfInCombat: (player.StatusFlags & StatusFlags.InCombat) != 0,
            Mounted: _mounted(),
            CurrentTargetId: current?.GameObjectId ?? 0,
            CurrentTargetIsLiveEnemy: current is IBattleNpc { IsDead: false } npc && (byte)npc.BattleNpcKind == Daedalus.Compat.BattleNpcKinds.Combatant,
            SecondsSinceLastSet: (now - _lastSet).TotalSeconds);

        if (!BossPullPolicy.ShouldTarget(situation) || boss == null)
            return;

        _targetManager.Target = boss;
        _lastSet = now;
        _log?.Info($"[BossPull] targeting {boss.Name} ({id:X}): nobody else will pull it");
    }
}
