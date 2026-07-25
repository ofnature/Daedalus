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
    public required byte KnowledgeLevel { get; init; }
    public required uint KnowledgeExp { get; init; }
    public required uint KnowledgeExpNeeded { get; init; }
    public required uint Silver { get; init; }
    public required uint Gold { get; init; }

    /// <summary>
    /// Diagnostic: raw OccultCrescentState bytes in 16-byte rows (offset-labelled).
    /// The exp fields verify correct but the level byte (0x92, KnowledgeLevelSync) read 0
    /// and 0x88–0x9B held no 0x12 while the game showed Lv.18 — full dump to locate the
    /// real level byte and the support-job level array. Remove once pinned.
    /// </summary>
    public required IReadOnlyList<string> RawDumpRows { get; init; }
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
    private readonly IPluginLog _log;

    private readonly Dictionary<uint, string> _actionNameCache = [];
    private readonly Dictionary<uint, string> _itemNameCache = [];
    private bool _slotReadFaulted;
    private bool _progressionReadFaulted;

    public PhantomJobService(
        IClientState clientState,
        IObjectTable objectTable,
        IDataManager dataManager,
        IInventoryProbe inventoryProbe,
        IPluginLog log)
    {
        _clientState = clientState;
        _objectTable = objectTable;
        _dataManager = dataManager;
        _inventoryProbe = inventoryProbe;
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

            // Diagnostic dump (see RawDumpRows docs). Declared struct size is 0x9C; the
            // extra margin to 0xC0 is safe — State is embedded mid-object in the much
            // larger PublicContentOccultCrescent, so these reads stay in-allocation.
            var raw = (byte*)state;
            var rows = new List<string>(12);
            for (var rowStart = 0x00; rowStart < 0xC0; rowStart += 16)
            {
                var sb = new System.Text.StringBuilder(60);
                sb.Append($"0x{rowStart:X2}: ");
                for (var i = 0; i < 16; i++)
                    sb.Append(raw[rowStart + i].ToString("X2")).Append(i == 15 ? "" : " ");
                rows.Add(sb.ToString());
            }

            return new OccultProgression
            {
                KnowledgeLevel = state->KnowledgeLevelSync,
                KnowledgeExp = state->CurrentKnowledge,
                KnowledgeExpNeeded = state->NeededKnowledge,
                Silver = _inventoryProbe.GetItemCount(PhantomJobData.SilverPieceItemId),
                Gold = _inventoryProbe.GetItemCount(PhantomJobData.GoldPieceItemId),
                RawDumpRows = rows,
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
