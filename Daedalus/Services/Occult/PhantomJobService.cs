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
    /// <summary>Current knowledge level, or 0 when unreadable.</summary>
    public required int KnowledgeLevel { get; init; }
    public required uint KnowledgeExp { get; init; }
    public required uint KnowledgeExpNeeded { get; init; }
    public required uint Silver { get; init; }
    public required uint Gold { get; init; }
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

    /// <summary>
    /// Level of every phantom job for this character (0 = not unlocked), from
    /// OccultCrescentState's per-job level array. Empty outside the zone or when
    /// the state is unavailable.
    /// </summary>
    public required IReadOnlyDictionary<PhantomJob, byte> JobLevels { get; init; }
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
            JobLevels = inZone ? ReadAllJobLevels() : new Dictionary<PhantomJob, byte>(),
        };
    }

    /// <summary>Per-job phantom levels from OccultCrescentState (0 = not unlocked).</summary>
    private unsafe IReadOnlyDictionary<PhantomJob, byte> ReadAllJobLevels()
    {
        var result = new Dictionary<PhantomJob, byte>();
        try
        {
            var state = FFXIVClientStructs.FFXIV.Client.Game.InstanceContent.PublicContentOccultCrescent.GetState();
            if (state == null)
                return result;

            var levels = state->SupportJobLevels;
            foreach (var entry in PhantomJobData.LevelStatuses)
            {
                var index = PhantomJobData.GetSupportJobRowIndex(entry.Key);
                if (index >= 0 && index < levels.Length)
                    result[entry.Key] = levels[index];
            }
        }
        catch (Exception ex)
        {
            if (!_progressionReadFaulted)
            {
                _progressionReadFaulted = true;
                _log.Warning(ex, "PhantomJobService: job level array read failed");
            }
        }

        return result;
    }

    private unsafe OccultProgression? ReadProgression()
    {
        try
        {
            var state = FFXIVClientStructs.FFXIV.Client.Game.InstanceContent.PublicContentOccultCrescent.GetState();
            if (state == null)
                return null;

            return new OccultProgression
            {
                KnowledgeLevel = ReadKnowledgeLevel(),
                KnowledgeExp = state->CurrentKnowledge,
                KnowledgeExpNeeded = state->NeededKnowledge,
                Silver = _inventoryProbe.GetItemCount(PhantomJobData.SilverPieceItemId),
                Gold = _inventoryProbe.GetItemCount(PhantomJobData.GoldPieceItemId),
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

    // MKDInfo AtkValue layout (field-pinned 2026-07-25, KL 18 Cannoneer Lv.3):
    // [5] = knowledge level, [10] = current support job row, [17] = support job level.
    private const int MkdInfoKnowledgeLevelIndex = 5;
    private const int MaxPlausibleKnowledgeLevel = 99;

    /// <summary>
    /// Current knowledge level. Primary source is the MKDInfo zone HUD's AtkValues —
    /// the exact numbers the game renders. Fallback is the director's inherited
    /// GetMaxLevel(): in the 2026-07-25 field check GetCurrentLevel() returned garbage
    /// while GetMaxLevel() returned the CURRENT level (18, zone cap was 20) — the pinned
    /// ClientStructs vtable slots look shifted, so it is fallback only, range-checked.
    /// </summary>
    private unsafe int ReadKnowledgeLevel()
    {
        try
        {
            var addonPtr = _gameGui.GetAddonByName("MKDInfo", 1);
            if (addonPtr.Address != nint.Zero)
            {
                var addon = (FFXIVClientStructs.FFXIV.Component.GUI.AtkUnitBase*)addonPtr.Address;
                if (addon->AtkValuesCount > MkdInfoKnowledgeLevelIndex)
                {
                    var value = addon->AtkValues[MkdInfoKnowledgeLevelIndex];
                    var level = value.Type switch
                    {
                        FFXIVClientStructs.FFXIV.Component.GUI.AtkValueType.Int => value.Int,
                        FFXIVClientStructs.FFXIV.Component.GUI.AtkValueType.UInt => (int)value.UInt,
                        _ => 0,
                    };
                    if (level is > 0 and <= MaxPlausibleKnowledgeLevel)
                        return level;
                }
            }

            var director = FFXIVClientStructs.FFXIV.Client.Game.InstanceContent.PublicContentOccultCrescent.GetInstance();
            if (director != null)
            {
                var level = (int)director->GetMaxLevel();
                if (level is > 0 and <= MaxPlausibleKnowledgeLevel)
                    return level;
            }

            return 0;
        }
        catch (Exception ex)
        {
            if (!_mkdInfoReadFaulted)
            {
                _mkdInfoReadFaulted = true;
                _log.Warning(ex, "PhantomJobService: knowledge level read failed");
            }

            return 0;
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
