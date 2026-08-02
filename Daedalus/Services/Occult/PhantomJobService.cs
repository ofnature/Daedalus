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

    /// <summary>
    /// Names of the Critical Encounters that are not Inactive right now (registering, warming
    /// up, or in progress). Empty outside the zone.
    /// </summary>
    public required IReadOnlyList<string> ActiveCriticalEncounters { get; init; }
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
    private bool _criticalEncounterReadFaulted;

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

    /// <summary>True while the player is in a Variant/Criterion dungeon territory.</summary>
    public bool IsInVariantDungeon => VariantActionData.VariantTerritoryIds.Contains((ushort)_clientState.TerritoryType);

    /// <summary>
    /// Player status check (Set statuses, DoT/buff gates). Same scan the tank-swap and
    /// Soteria readers use; false when the player is unavailable.
    /// </summary>
    public bool PlayerHasStatus(uint statusId)
    {
        var player = _objectTable.LocalPlayer;
        if (player?.StatusList == null)
            return false;

        foreach (var status in player.StatusList)
        {
            if (status != null && status.StatusId == statusId)
                return true;
        }

        return false;
    }

    /// <summary>Phantom layer's CURRENT state, rewritten every frame — Debug tab readout.</summary>
    public string LayerLastEvent { get; set; } = "—";

    /// <summary>
    /// What the phantom raise is doing, or why it is not. A raise that silently never happens is
    /// the single thing this layer has been hardest to diagnose — level, duty-bar slot, range,
    /// the healer deferral and the GCD pre-empt each produced the same visible nothing.
    /// </summary>
    public string RaiseState { get; set; } = "—";

    /// <summary>Last action the phantom layer actually fired (sticky, timestamped).</summary>
    public string LayerLastDispatch { get; set; } = "none yet";

    /// <summary>Variant layer's CURRENT state, rewritten every frame — Debug tab readout.</summary>
    public string VariantLastEvent { get; set; } = "—";

    /// <summary>Last action the variant layer actually fired (sticky, timestamped).</summary>
    public string VariantLastDispatch { get; set; } = "none yet";

    /// <summary>Active phantom job + level from the player's status stacks (combat-path read).</summary>
    public (PhantomJob Job, byte Level) GetActiveJob() => ResolveActiveJobFromPlayer();

    /// <summary>Whether an action currently sits on one of the 5 duty-bar slots. Fail closed.</summary>
    public unsafe bool IsSlotted(uint actionId)
    {
        try
        {
            for (ushort i = 0; i < DutySlotCount; i++)
            {
                if (FFXIVClientStructs.FFXIV.Client.Game.DutyActionManager.GetDutyActionId(i) == actionId)
                    return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Inventory count for a phantom consumable (Occult Potion / Elixir / Coffer).</summary>
    public uint GetItemCount(uint itemId) => _inventoryProbe.GetItemCount(itemId);

    /// <summary>
    /// Raw duty-bar slot action IDs (5 entries, 0 = empty; empty array on read failure).
    /// Callers that need morph-aware matching (Oracle cards, Dancer steps, Geomancer
    /// weather variants) compare these through GetAdjustedActionId.
    /// </summary>
    public unsafe uint[] GetDutySlotIds()
    {
        try
        {
            var slots = new uint[DutySlotCount];
            for (ushort i = 0; i < DutySlotCount; i++)
                slots[i] = FFXIVClientStructs.FFXIV.Client.Game.DutyActionManager.GetDutyActionId(i);
            return slots;
        }
        catch
        {
            return Array.Empty<uint>();
        }
    }

    private readonly Dictionary<uint, Daedalus.Models.Action.ActionDefinition> _definitionCache = [];

    /// <summary>Phantom layer's convenience overload.</summary>
    public Daedalus.Models.Action.ActionDefinition GetActionDefinition(PhantomActionDef def)
        => GetOrBuildDefinition(def.ActionId, def.Name);

    /// <summary>
    /// Builds (and caches) an ActionDefinition for a duty action from the Lumina Action
    /// sheet — cast time, recast, range and GCD/oGCD category come from game data so the
    /// scheduler dispatches through the right path without hand-maintained numbers.
    /// </summary>
    public Daedalus.Models.Action.ActionDefinition GetOrBuildDefinition(uint defActionId, string defName)
    {
        var def = (ActionId: defActionId, Name: defName);
        if (_definitionCache.TryGetValue(def.ActionId, out var cached))
            return cached;

        float castTime = 0f, recastTime = 2.5f, range = 0f, radius = 0f;
        var isGcd = false;
        var row = _dataManager.GetExcelSheet<Lumina.Excel.Sheets.Action>()?.GetRowOrDefault(def.ActionId);
        if (row is { } action)
        {
            castTime = action.Cast100ms / 10f;
            recastTime = action.Recast100ms / 10f;
            range = action.Range < 0 ? 3f : action.Range;
            radius = action.EffectRange;
            // ActionCategory rows: 2 = Spell, 3 = Weaponskill (GCDs); 4 = Ability (oGCD).
            isGcd = action.ActionCategory.RowId is 2 or 3;
        }

        var built = new Daedalus.Models.Action.ActionDefinition
        {
            ActionId = def.ActionId,
            Name = def.Name,
            MinLevel = 1, // phantom actions gate on phantom level, not job level
            Category = isGcd ? Daedalus.Models.Action.ActionCategory.GCD : Daedalus.Models.Action.ActionCategory.oGCD,
            TargetType = Daedalus.Models.Action.ActionTargetType.Self,
            CastTime = castTime,
            RecastTime = recastTime,
            Range = range,
            Radius = radius,
        };
        _definitionCache[def.ActionId] = built;
        return built;
    }

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
            ActiveCriticalEncounters = inZone ? ReadActiveCriticalEncounters() : [],
        };
    }

    /// <summary>
    /// Live Critical Encounters that are not Inactive. Read from the Occult director's dynamic
    /// event container — the same source BOCCHI uses; CEs are dynamic events, not FATEs, so the
    /// FATE table never sees them.
    /// </summary>
    private unsafe IReadOnlyList<string> ReadActiveCriticalEncounters()
    {
        var result = new List<string>();
        try
        {
            var director = FFXIVClientStructs.FFXIV.Client.Game.InstanceContent.PublicContentOccultCrescent.GetInstance();
            if (director == null)
                return result;

            foreach (var dynamicEvent in director->DynamicEventContainer.Events)
            {
                if (dynamicEvent.State == FFXIVClientStructs.FFXIV.Client.Game.InstanceContent.DynamicEventState.Inactive)
                    continue;

                var name = dynamicEvent.Name.ToString();
                if (!string.IsNullOrWhiteSpace(name))
                    result.Add(name);
            }
        }
        catch (Exception ex)
        {
            if (!_criticalEncounterReadFaulted)
            {
                _criticalEncounterReadFaulted = true;
                _log.Warning(ex, "PhantomJobService: critical encounter read failed");
            }
        }

        return result;
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

            // Zone-aware currency: North Horn mints Obols, South Horn Pieces.
            var (silverId, goldId) = PhantomJobData.CurrencyItemIds((ushort)_clientState.TerritoryType);
            return new OccultProgression
            {
                KnowledgeLevel = ReadKnowledgeLevel(),
                KnowledgeExp = state->CurrentKnowledge,
                KnowledgeExpNeeded = state->NeededKnowledge,
                Silver = _inventoryProbe.GetItemCount(silverId),
                Gold = _inventoryProbe.GetItemCount(goldId),
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

    /// <summary>Public Lumina action-name lookup (Debug Duty tab slot display).</summary>
    public string ResolveActionName(uint actionId) => GetActionName(actionId);

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
