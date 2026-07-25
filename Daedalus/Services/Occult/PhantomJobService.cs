using System;
using System.Collections.Generic;
using Dalamud.Plugin.Services;
using Daedalus.Data;
using Daedalus.Services.Consumables;

namespace Daedalus.Services.Occult;

/// <summary>A duty-bar slot as shown in Debug ▸ Occult. ActionId 0 = empty slot.</summary>
public readonly record struct PhantomSlot(uint ActionId, string Name);

/// <summary>A tracked occult consumable with its live inventory count.</summary>
public readonly record struct PhantomItemCount(uint ItemId, string Name, uint Count);

/// <summary>Zone progression read from OccultCrescentState (null when unavailable).</summary>
public sealed record OccultProgression
{
    public required int KnowledgeLevel { get; init; }
    public required int KnowledgeLevelCap { get; init; }
    public required uint KnowledgeExp { get; init; }
    public required uint KnowledgeExpNeeded { get; init; }
    public required uint Silver { get; init; }
    public required uint Gold { get; init; }

    /// <summary>
    /// Diagnostic: numeric AtkValues of the MKDInfo zone HUD addon (the panel that
    /// displays "Knowledge Level 18 ▶ 18"). Used to pin the knowledge-level source if
    /// the director-inherited GetCurrentLevel path reads 0. Remove once pinned.
    /// </summary>
    public required IReadOnlyList<string> MkdInfoValueRows { get; init; }
}

/// <summary>Point-in-time phantom detection state (Phase 1: read-only, nothing fires).</summary>
public sealed record PhantomStateSnapshot
{
    public required bool InOccultCrescent { get; init; }
    public required ushort TerritoryId { get; init; }
    public required PhantomJob ActiveJob { get; init; }
    public required byte Level { get; init; }
    public required uint LevelStatusId { get; init; }
    public required IReadOnlyList<PhantomSlot> DutySlots { get; init; }
    public required IReadOnlyList<PhantomItemCount> Items { get; init; }
    public required OccultProgression? Progression { get; init; }
}

/// <summary>
/// Occult Crescent phantom-job detection: territory gate, active job + level from
/// player status stacks, duty-bar slot reads, and consumable counts.
/// Phase 1 of docs/occult-phantom-plan.md — detection only, no action dispatch.
/// All native reads fail closed (empty slots / zero counts) so a bad read can never
/// look like a usable action.
/// </summary>
public sealed class PhantomJobService
{
    private const int DutySlotCount = 5;

    private readonly IClientState _clientState;
    private readonly IObjectTable _objectTable;
    private readonly IDataManager _dataManager;
    private readonly IInventoryProbe _inventoryProbe;
    private readonly IGameGui _gameGui;
    private readonly IPluginLog _log;

    private readonly Dictionary<uint, string> _actionNameCache = [];
    private readonly Dictionary<uint, string> _itemNameCache = [];
    private bool _slotReadFaulted;
    private bool _progressionReadFaulted;
    private bool _mkdInfoReadFaulted;

    public PhantomJobService(
        IClientState clientState,
        IObjectTable objectTable,
        IDataManager dataManager,
        IInventoryProbe inventoryProbe,
        IGameGui gameGui,
        IPluginLog log)
    {
        _clientState = clientState;
        _objectTable = objectTable;
        _dataManager = dataManager;
        _inventoryProbe = inventoryProbe;
        _gameGui = gameGui;
        _log = log;
    }

    /// <summary>True while the player is in an Occult Crescent territory.</summary>
    public bool IsInOccultCrescent => PhantomJobData.OccultTerritoryIds.Contains((ushort)_clientState.TerritoryType);

    /// <summary>
    /// Builds the current detection snapshot. Call from the framework/draw thread
    /// (reads LocalPlayer and native managers).
    /// </summary>
    public PhantomStateSnapshot GetSnapshot()
    {
        var territoryId = (ushort)_clientState.TerritoryType;
        var inZone = PhantomJobData.OccultTerritoryIds.Contains(territoryId);

        var (job, level) = ResolveActiveJobFromPlayer();

        return new PhantomStateSnapshot
        {
            InOccultCrescent = inZone,
            TerritoryId = territoryId,
            ActiveJob = job,
            Level = level,
            LevelStatusId = PhantomJobData.GetLevelStatusId(job),
            DutySlots = inZone ? ReadDutySlots() : Array.Empty<PhantomSlot>(),
            Items = inZone ? ReadItemCounts() : Array.Empty<PhantomItemCount>(),
            Progression = inZone ? ReadProgression() : null,
        };
    }

    private unsafe OccultProgression? ReadProgression()
    {
        try
        {
            var state = FFXIVClientStructs.FFXIV.Client.Game.InstanceContent.PublicContentOccultCrescent.GetState();
            if (state == null)
                return null;

            // The knowledge level is NOT in OccultCrescentState (full-dump field check
            // 2026-07-25: KnowledgeLevelSync only carries downsync). The director's
            // inherited ContentDirector level accessors carry the zone level.
            var levels = ReadDirectorLevels();

            return new OccultProgression
            {
                KnowledgeLevel = levels.Current,
                KnowledgeLevelCap = levels.Max,
                KnowledgeExp = state->CurrentKnowledge,
                KnowledgeExpNeeded = state->NeededKnowledge,
                Silver = _inventoryProbe.GetItemCount(PhantomJobData.SilverPieceItemId),
                Gold = _inventoryProbe.GetItemCount(PhantomJobData.GoldPieceItemId),
                MkdInfoValueRows = ReadMkdInfoValues(),
            };
        }
        catch (Exception ex)
        {
            if (!_progressionReadFaulted)
            {
                _progressionReadFaulted = true;
                _log.Warning(ex, "PhantomJobService: OccultCrescentState read failed");
            }

            return null;
        }
    }

    private (PhantomJob Job, byte Level) ResolveActiveJobFromPlayer()
    {
        var player = _objectTable.LocalPlayer;
        if (player == null)
            return (PhantomJob.None, 0);

        var statuses = new List<(uint StatusId, byte Stacks)>();
        foreach (var status in player.StatusList)
        {
            if (status == null || status.StatusId == 0)
                continue;

            // Param carries the stack count for stackable statuses (Soteria/tank-swap precedent).
            statuses.Add((status.StatusId, (byte)status.Param));
        }

        return PhantomJobData.ResolveActiveJob(statuses);
    }

    private unsafe IReadOnlyList<PhantomSlot> ReadDutySlots()
    {
        var slots = new PhantomSlot[DutySlotCount];
        try
        {
            for (ushort i = 0; i < DutySlotCount; i++)
            {
                var actionId = FFXIVClientStructs.FFXIV.Client.Game.DutyActionManager.GetDutyActionId(i);
                slots[i] = new PhantomSlot(actionId, actionId == 0 ? "—" : GetActionName(actionId));
            }

            _slotReadFaulted = false;
            return slots;
        }
        catch (Exception ex)
        {
            // Fail closed: no slots means later phases never consider an action usable.
            if (!_slotReadFaulted)
            {
                _slotReadFaulted = true;
                _log.Warning(ex, "PhantomJobService: duty action slot read failed");
            }

            return Array.Empty<PhantomSlot>();
        }
    }

    private IReadOnlyList<PhantomItemCount> ReadItemCounts()
    {
        var items = new PhantomItemCount[PhantomJobData.ConsumableItemIds.Count];
        for (var i = 0; i < items.Length; i++)
        {
            var itemId = PhantomJobData.ConsumableItemIds[i];
            items[i] = new PhantomItemCount(itemId, GetItemName(itemId), _inventoryProbe.GetItemCount(itemId));
        }

        return items;
    }

    private unsafe (int Current, int Max) ReadDirectorLevels()
    {
        try
        {
            var director = FFXIVClientStructs.FFXIV.Client.Game.InstanceContent.PublicContentOccultCrescent.GetInstance();
            if (director == null)
                return (0, 0);

            return ((int)director->GetCurrentLevel(), (int)director->GetMaxLevel());
        }
        catch (Exception ex)
        {
            if (!_progressionReadFaulted)
            {
                _progressionReadFaulted = true;
                _log.Warning(ex, "PhantomJobService: director level read failed");
            }

            return (0, 0);
        }
    }

    private unsafe IReadOnlyList<string> ReadMkdInfoValues()
    {
        try
        {
            var addonPtr = _gameGui.GetAddonByName("MKDInfo", 1);
            if (addonPtr.Address == nint.Zero)
                return ["MKDInfo addon not present"];

            var addon = (FFXIVClientStructs.FFXIV.Component.GUI.AtkUnitBase*)addonPtr.Address;
            var count = Math.Min((int)addon->AtkValuesCount, 60);
            if (count == 0)
                return ["MKDInfo has no AtkValues"];

            var rows = new List<string>(8);
            var sb = new System.Text.StringBuilder(96);
            var pairsOnLine = 0;
            for (var i = 0; i < count; i++)
            {
                var value = addon->AtkValues[i];
                var text = value.Type switch
                {
                    FFXIVClientStructs.FFXIV.Component.GUI.AtkValueType.Int => value.Int.ToString(),
                    FFXIVClientStructs.FFXIV.Component.GUI.AtkValueType.UInt => value.UInt.ToString(),
                    FFXIVClientStructs.FFXIV.Component.GUI.AtkValueType.Bool => value.Byte.ToString(),
                    _ => null,
                };
                if (text == null)
                    continue;

                sb.Append($"{i}:{text}  ");
                if (++pairsOnLine == 8)
                {
                    rows.Add(sb.ToString());
                    sb.Clear();
                    pairsOnLine = 0;
                }
            }

            if (sb.Length > 0)
                rows.Add(sb.ToString());

            return rows.Count == 0 ? ["MKDInfo has no numeric AtkValues"] : rows;
        }
        catch (Exception ex)
        {
            if (!_mkdInfoReadFaulted)
            {
                _mkdInfoReadFaulted = true;
                _log.Warning(ex, "PhantomJobService: MKDInfo AtkValue read failed");
            }

            return ["MKDInfo read failed"];
        }
    }

    private string GetActionName(uint actionId)
    {
        if (_actionNameCache.TryGetValue(actionId, out var cached))
            return cached;

        var name = _dataManager.GetExcelSheet<Lumina.Excel.Sheets.Action>()
            ?.GetRowOrDefault(actionId)?.Name.ExtractText();
        var resolved = string.IsNullOrEmpty(name) ? $"Action#{actionId}" : name;
        _actionNameCache[actionId] = resolved;
        return resolved;
    }

    private string GetItemName(uint itemId)
    {
        if (_itemNameCache.TryGetValue(itemId, out var cached))
            return cached;

        var name = _dataManager.GetExcelSheet<Lumina.Excel.Sheets.Item>()
            ?.GetRowOrDefault(itemId)?.Name.ExtractText();
        var resolved = string.IsNullOrEmpty(name) ? $"Item#{itemId}" : name;
        _itemNameCache[itemId] = resolved;
        return resolved;
    }
}
