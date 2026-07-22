using System;
using System.Collections.Generic;
using System.Text;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using Daedalus.Models.Gear;
using LuminaItem = Lumina.Excel.Sheets.Item;
using LuminaMateria = Lumina.Excel.Sheets.Materia;

namespace Daedalus.Services.Gear;

/// <summary>
/// Reads the equipped set (items + melds) into an immutable <see cref="GearSnapshot"/>.
/// FRAMEWORK THREAD ONLY for <see cref="Refresh"/> (unsafe ClientStructs reads); consumers read
/// the published <see cref="Current"/> reference, which is swapped atomically — never a live
/// game pointer (the config-copy lesson). Container layout, belt-slot skip, and the two-hander
/// offhand rule follow the proven SealBreaker pattern (FarmController.GetAverageEquippedItemLevel).
/// </summary>
public sealed class GearSnapshotService
{
    /// <summary>Grade index (0-based) of grade XII materia in the Materia sheet Value column.</summary>
    private const int GradeXiiIndex = 11;

    private readonly IDataManager _dataManager;
    private readonly IObjectTable _objectTable;
    private readonly StatCapService _statCaps;
    private readonly IPluginLog _log;

    /// <summary>Last published snapshot — safe to read from any draw path.</summary>
    public GearSnapshot Current { get; private set; } = GearSnapshot.Empty;

    public GearSnapshotService(
        IDataManager dataManager,
        IObjectTable objectTable,
        StatCapService statCaps,
        IPluginLog log)
    {
        _dataManager = dataManager;
        _objectTable = objectTable;
        _statCaps = statCaps;
        _log = log;
    }

    /// <summary>Rebuild and publish the snapshot. Framework thread only. Returns the new snapshot.</summary>
    public unsafe GearSnapshot Refresh()
    {
        try
        {
            var manager = InventoryManager.Instance();
            if (manager == null)
                return Current;

            var container = manager->GetInventoryContainer(InventoryType.EquippedItems);
            if (container == null)
                return Current;

            var itemSheet = _dataManager.GetExcelSheet<LuminaItem>();
            var materiaSheet = _dataManager.GetExcelSheet<LuminaMateria>();
            if (itemSheet == null || materiaSheet == null)
                return Current;

            // Two-handed mainhand blocks the offhand slot — trust the sheet, skip the slot.
            var twoHanded = false;
            var mainhand = container->GetInventorySlot(0);
            if (mainhand != null && mainhand->ItemId != 0)
            {
                var mainRow = itemSheet.GetRowOrDefault(mainhand->ItemId);
                if (mainRow != null)
                    twoHanded = (mainRow.Value.EquipSlotCategory.ValueNullable?.OffHand ?? 0) != 0;
            }

            var pieces = new List<GearPiece>(12);
            foreach (GearSlotId slot in Enum.GetValues<GearSlotId>())
            {
                if (slot == GearSlotId.OffHand && twoHanded)
                    continue;

                var index = (int)slot;
                if (index >= container->Size)
                    continue;

                var invItem = container->GetInventorySlot(index);
                if (invItem == null || invItem->ItemId == 0)
                    continue;

                var row = itemSheet.GetRowOrDefault(invItem->ItemId);
                if (row == null)
                    continue;

                pieces.Add(BuildPiece(slot, invItem, row.Value, materiaSheet, twoHanded));
            }

            var player = _objectTable.LocalPlayer;
            byte gender = 0;
            uint jobId = 0;
            var level = 100;
            if (player != null)
            {
                gender = player.Customize[(int)Dalamud.Game.ClientState.Objects.Enums.CustomizeIndex.Gender];
                jobId = player.ClassJob.RowId;
                level = player.Level;
            }

            Current = new GearSnapshot(pieces, gender, jobId, DateTime.UtcNow, level);
            return Current;
        }
        catch (Exception ex)
        {
            // Zone-transition reads can hit half-initialized structs — keep the last snapshot.
            _log.Debug(ex, "[GearSnapshot] refresh failed; keeping previous snapshot.");
            return Current;
        }
    }

    private unsafe GearPiece BuildPiece(
        GearSlotId slot,
        InventoryItem* invItem,
        LuminaItem row,
        Lumina.Excel.ExcelSheet<LuminaMateria> materiaSheet,
        bool twoHanded)
    {
        var ilvl = (int)row.LevelItem.RowId;

        // Base stats from the Item sheet's parallel BaseParam/BaseParamValue arrays.
        var baseStats = new Dictionary<uint, int>();
        for (var i = 0; i < row.BaseParam.Count; i++)
        {
            var paramId = row.BaseParam[i].RowId;
            var value = (int)row.BaseParamValue[i];
            if (paramId == 0 || value == 0)
                continue;
            baseStats.TryGetValue(paramId, out var existing);
            baseStats[paramId] = existing + value;
        }

        // Melds in socket order. Sockets past the sweepable count are the fixed XI overmelds.
        var guaranteed = row.MateriaSlotCount;
        var advanced = row.IsAdvancedMeldingPermitted;
        var sweepable = advanced ? guaranteed + 1 : guaranteed;
        var melds = new List<MateriaMeld>(5);
        for (byte socket = 0; socket < 5; socket++)
        {
            var materiaId = invItem->GetMateriaId(socket);
            if (materiaId == 0)
                continue;

            var materiaRow = materiaSheet.GetRowOrDefault(materiaId);
            if (materiaRow == null)
                continue;

            var gradeIndex = invItem->GetMateriaGrade(socket);
            var statId = materiaRow.Value.BaseParam.RowId;
            var value = gradeIndex < materiaRow.Value.Value.Count
                ? (int)materiaRow.Value.Value[gradeIndex]
                : 0;

            melds.Add(new MateriaMeld(
                StatId: statId,
                Value: value,
                Grade: gradeIndex + 1,
                IsFixedOvermeld: melds.Count >= sweepable || (advanced && gradeIndex < GradeXiiIndex && melds.Count >= guaranteed)));
        }

        return new GearPiece(
            Slot: slot,
            ItemId: invItem->ItemId,
            Name: row.Name.ExtractText(),
            Ilvl: ilvl,
            BaseStats: baseStats,
            Melds: melds,
            GuaranteedSockets: guaranteed,
            AdvancedMeldingPermitted: advanced,
            Caps: _statCaps.CapsFor(ilvl, slot, twoHanded && slot == GearSlotId.MainHand));
    }

    /// <summary>Full text dump for /daedalus dumpgear — the phase-1 field validation tool.</summary>
    public string Describe(GearSnapshot snapshot)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"GearSnapshot @ {snapshot.CapturedUtc:HH:mm:ss}Z — job {snapshot.JobId}, gender {(snapshot.GenderId == 0 ? "M" : "F")}, {snapshot.Pieces.Count} pieces");
        foreach (var piece in snapshot.Pieces)
        {
            sb.AppendLine($"[{piece.Slot}] {piece.Name} (i{piece.Ilvl}) sockets {piece.GuaranteedSockets}{(piece.AdvancedMeldingPermitted ? "+adv" : "")} sweepable {piece.SweepableSockets}");
            sb.Append("  base: ");
            foreach (var (statId, value) in piece.BaseStats)
                sb.Append($"{GearStatIds.Name(statId)} {value}  ");
            sb.AppendLine();
            for (var i = 0; i < piece.Melds.Count; i++)
            {
                var meld = piece.Melds[i];
                var waste = piece.OvercapWaste(meld.StatId);
                sb.AppendLine($"  socket {i + 1}: +{meld.Value} {GearStatIds.Name(meld.StatId)} (G{meld.Grade}{(meld.IsFixedOvermeld ? ", fixed XI" : "")}{(waste > 0 ? $", OVERCAP {waste} wasted on piece" : "")})");
            }
            sb.Append("  caps: ");
            foreach (var (statId, cap) in piece.Caps)
                sb.Append($"{GearStatIds.Name(statId)} {cap}  ");
            sb.AppendLine();
        }

        var aggregate = GearStatAggregator.Aggregate(snapshot);
        sb.Append("TOTALS: ");
        foreach (var (statId, total) in aggregate.Totals)
            sb.Append($"{GearStatIds.Name(statId)} {total}  ");
        sb.AppendLine();
        foreach (var overcap in aggregate.Overcaps)
            sb.AppendLine($"OVERCAP: {overcap.Slot} {GearStatIds.Name(overcap.StatId)} +{overcap.WastedPoints} wasted");
        return sb.ToString();
    }
}
