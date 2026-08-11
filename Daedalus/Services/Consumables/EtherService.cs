using System;
using System.Collections.Generic;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;
using Daedalus.Data;
using Daedalus.Services.Action;

namespace Daedalus.Services.Consumables;

/// <summary>
/// Drinks ethers on a cascade — strongest first, stepping down the ladder as stock runs out —
/// and warns once the bag is thinning. Runs from the framework tick beside the Phoenix Down
/// safety net and uses the same item-execution route.
/// <para>
/// Ships dark: Consumables ▸ EnableEthers, default off, because it spends real consumables.
/// </para>
/// </summary>
public sealed class EtherService
{
    private const double CheckIntervalSeconds = 1.0;

    /// <summary>Don't repeat the running-low line more than this often, even across zones.</summary>
    private const double LowWarningCooldownSeconds = 600.0;

    private readonly IActionService _actionService;
    private readonly IInventoryProbe _inventory;
    private readonly Configuration _configuration;
    private readonly IChatGui? _chatGui;
    private readonly IPluginLog _log;

    private DateTime _lastCheck = DateTime.MinValue;
    private DateTime _lastUse = DateTime.MinValue;
    private DateTime _lastRefusal = DateTime.MinValue;
    private DateTime _lastLowWarning = DateTime.MinValue;

    /// <summary>Why the last decision did (not) fire — surfaced for the debug UI.</summary>
    public string LastState { get; private set; } = "idle";

    /// <summary>Total ethers seen at the last check, for the debug/HUD readout.</summary>
    public int LastKnownStock { get; private set; }

    public EtherService(
        IActionService actionService,
        IInventoryProbe inventory,
        Configuration configuration,
        IChatGui? chatGui,
        IPluginLog log)
    {
        _actionService = actionService;
        _inventory = inventory;
        _configuration = configuration;
        _chatGui = chatGui;
        _log = log;
    }

    /// <summary>Framework tick, throttled to a 1s decision cadence.</summary>
    public void Update(IPlayerCharacter? player)
    {
        if (player is null)
            return;

        var cfg = _configuration.Consumables;
        if (!cfg.EnableEthers)
        {
            LastState = "disabled";
            return;
        }

        var now = DateTime.UtcNow;
        if ((now - _lastCheck).TotalSeconds < CheckIntervalSeconds)
            return;
        _lastCheck = now;

        var stock = ReadStock();
        LastKnownStock = EtherPolicy.TotalHeld(stock);

        var situation = new EtherSituation(
            Enabled: true,
            Alive: !player.IsDead,
            UsesMp: player.MaxMp > 0,
            CurrentMp: player.CurrentMp,
            MaxMp: player.MaxMp,
            MpThreshold: cfg.EtherMpThreshold,
            Stock: stock,
            SecondsSinceOwnUse: (now - _lastUse).TotalSeconds,
            SecondsSinceRefusal: (now - _lastRefusal).TotalSeconds,
            IsCasting: player.IsCasting);

        var decision = EtherPolicy.Decide(in situation);
        LastState = decision.Reason;
        if (!decision.Fire)
            return;

        var choice = decision.Choice;
        if (_actionService.ExecuteItem(choice.ItemId, choice.Hq, player.GameObjectId))
        {
            _lastUse = now;
            LastState = $"drank {choice.DisplayName}";
            _log.Information(
                $"Ether: MP {situation.CurrentMp}/{situation.MaxMp} — {choice.DisplayName} " +
                $"(+{choice.RestoreAt(situation.MaxMp):N0}), {LastKnownStock - 1} left");
            WarnIfRunningLow(ReadStock(), now);
        }
        else
        {
            // The game said no — an item-blocked duty is the usual reason. Back off rather than
            // hammering it every second for the rest of the fight.
            _lastRefusal = now;
            LastState = "game refused the ether (items blocked here?)";
            _log.Warning($"Ether: {choice.DisplayName} refused by the game");
        }
    }

    /// <summary>Resets the zone-scoped warning state so a new field trip warns again.</summary>
    public void OnTerritoryChanged() => _lastRefusal = DateTime.MinValue;

    private Dictionary<uint, uint> ReadStock()
    {
        var stock = new Dictionary<uint, uint>(EtherItems.All.Count);
        foreach (var variant in EtherItems.All)
        {
            var id = variant.InventoryId;
            if (!stock.ContainsKey(id))
                stock[id] = _inventory.GetItemCount(id);
        }

        return stock;
    }

    private void WarnIfRunningLow(IReadOnlyDictionary<uint, uint> stock, DateTime now)
    {
        if (!_configuration.Consumables.WarnOnLowEthers)
            return;
        if (!EtherPolicy.IsRunningLow(stock))
            return;
        if ((now - _lastLowWarning).TotalSeconds < LowWarningCooldownSeconds)
            return;

        _lastLowWarning = now;
        var remaining = EtherPolicy.TotalHeld(stock);
        _chatGui?.Print(new XivChatEntry
        {
            Type = XivChatType.Echo,
            Message = new SeString().Append(
                $"[Daedalus] Running low on ethers — {remaining} left. Restock before the next run."),
        });
    }
}
