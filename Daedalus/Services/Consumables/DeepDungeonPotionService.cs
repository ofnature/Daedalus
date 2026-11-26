using System;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using Daedalus.Data;
using Daedalus.Rotation.Base;
using Daedalus.Services.Action;
using Daedalus.Services.Debug;

namespace Daedalus.Services.Consumables;

/// <summary>
/// Drinks the deep dungeon's regen potion on low HP (<see cref="DeepDungeonPotionPolicy"/>). Runs from the framework
/// tick beside the ether and Phoenix Down services. Off by default: Consumables ▸ EnableDeepDungeonPotions.
/// </summary>
public sealed class DeepDungeonPotionService
{
    private const double CheckIntervalSeconds = 0.25;

    private readonly IActionService _actionService;
    private readonly IInventoryProbe _inventory;
    private readonly Configuration _configuration;
    private readonly IPluginLog _log;

    private DateTime _lastCheck = DateTime.MinValue;
    private DateTime _lastAttempt = DateTime.MinValue;
    private int _unconfirmedAttempts;
    private string? _heldFor;

    public DeepDungeonPotionService(
        IActionService actionService,
        IInventoryProbe inventory,
        Configuration configuration,
        IPluginLog log)
    {
        _actionService = actionService;
        _inventory = inventory;
        _configuration = configuration;
        _log = log;
    }

    public void Update(IPlayerCharacter? player)
    {
        var cfg = _configuration.Consumables;
        if (player is null || !cfg.EnableDeepDungeonPotions)
            return;

        var now = DateTime.UtcNow;
        if ((now - _lastCheck).TotalSeconds < CheckIntervalSeconds)
            return;
        _lastCheck = now;

        var dungeonId = ReadDungeonId();
        if (dungeonId == 0)
            return;

        var regenerating = false;
        var itemPenalty = false;
        var raiseImmune = false;
        foreach (var status in player.StatusList)
        {
            regenerating |= DeepDungeonPotions.RegenStatusIds.Contains(status.StatusId);
            itemPenalty |= status.StatusId == DeepDungeonPotions.ItemPenaltyStatusId;
            raiseImmune |= FFXIVConstants.PostReviveInvulnerabilityStatusIds.Contains(status.StatusId);
        }

        // The regen is the only proof a press worked: UseAction returns false for items that do go off.
        if (regenerating)
            _unconfirmedAttempts = 0;

        var held = DeepDungeonPotions.ForDungeon(dungeonId) is { } own ? _inventory.GetItemCount(own.ItemId) : 0u;
        var situation = new DeepDungeonPotionSituation(
            Enabled: true,
            dungeonId,
            Alive: !player.IsDead,
            InCombat: (player.StatusFlags & StatusFlags.InCombat) != 0,
            player.CurrentHp,
            player.MaxHp,
            cfg.DeepDungeonPotionHpThreshold,
            regenerating,
            itemPenalty,
            raiseImmune,
            held,
            player.IsCasting,
            SecondsSinceAttempt: (now - _lastAttempt).TotalSeconds,
            _unconfirmedAttempts);

        var decision = DeepDungeonPotionPolicy.Decide(in situation);
        if (!decision.Fire)
        {
            // why a wanted potion is not drunk, once per reason; the two waits that pass by themselves get no line
            if (!decision.Wanted)
            {
                _heldFor = null;
            }
            else if (decision.Reason is not (DeepDungeonPotionPolicy.JustPressed or DeepDungeonPotionPolicy.MidCast)
                     && decision.Reason != _heldFor)
            {
                _heldFor = decision.Reason;
                Note($"{decision.Potion.Name} held -- {decision.Reason}");
            }

            return;
        }

        _heldFor = null;
        _lastAttempt = now;
        _unconfirmedAttempts++;
        _actionService.ExecuteItem(decision.Potion.ItemId, preferHq: false, player.GameObjectId);
        var line = $"HP {(float)player.CurrentHp / player.MaxHp:P0} -- {decision.Potion.Name} ({held} held)";
        Note(line);
        _log.Information($"DeepDungeonPotion: {line}");
    }

    private static void Note(string message)
        => RotationServices.DebugLog?.Log(DebugLogCategory.Action, DebugLogSeverity.Info, $"[Potion] {message}");

    private static unsafe byte ReadDungeonId()
    {
        var framework = EventFramework.Instance();
        if (framework == null)
            return 0;
        var dungeon = framework->GetInstanceContentDeepDungeon();
        return dungeon == null ? (byte)0 : dungeon->DeepDungeonId;
    }
}
