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

    /// <summary>
    /// Per-grade minimum BASE ITEM LEVEL for melding (grade index → ilvl), read from the sheets:
    /// each materia item's own LevelItem IS its melding requirement (field-confirmed 2026-07-23:
    /// Heavens' Eye XI shows "Base Item: Item Level 690" and its item row is ilvl 690). Cached
    /// per session; combat substat materia share one requirement table.
    /// </summary>
    private int[]? _gradeMinIlvl;
    private short[]? _gradeValues;

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

            // Live PlayerState attributes (food-inclusive) for display + GCD tiers. Field
            // validation 2026-07-22: gear+floor matched the Character window exactly on Det/DH
            // but drifted on Crit/Ten by a small flat amount — food, invisible to gear math.
            var liveStats = new Dictionary<uint, int>();
            foreach (var statId in GearStatIds.MeldableSubstats)
            {
                var value = SafeGameAccess.GetPlayerAttribute((int)statId, null);
                if (value > 0)
                    liveStats[statId] = value;
            }

            // Mains too (field report 2026-07-22: ~500 STR/VIT drift = naked base × job modifier
            // + clan + traits — not derivable from gear, so display the live value).
            foreach (var statId in GearStatIds.MainStats)
            {
                var value = SafeGameAccess.GetPlayerAttribute((int)statId, null);
                if (value > 0)
                    liveStats[statId] = value;
            }

            Current = new GearSnapshot(pieces, gender, jobId, DateTime.UtcNow, level,
                liveStats.Count > 0 ? liveStats : null);
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

        // HQ bonus (field find 2026-07-22: an HQ crafted-then-augmented ring carried +21 Crit /
        // +15 Ten the sheet-base read missed — the exact drift seen vs the Character window).
        // HQ deltas live in the parallel BaseParamSpecial/BaseParamValueSpecial columns.
        if ((invItem->Flags & InventoryItem.ItemFlags.HighQuality) != 0)
        {
            for (var i = 0; i < row.BaseParamSpecial.Count; i++)
            {
                var paramId = row.BaseParamSpecial[i].RowId;
                var value = (int)row.BaseParamValueSpecial[i];
                if (paramId == 0 || value == 0)
                    continue;
                baseStats.TryGetValue(paramId, out var existing);
                baseStats[paramId] = existing + value;
            }
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

        var (sweepGrade, sweepValue) = BestMeldGradeFor(ilvl, materiaSheet);

        return new GearPiece(
            Slot: slot,
            ItemId: invItem->ItemId,
            Name: row.Name.ExtractText(),
            Ilvl: ilvl,
            BaseStats: baseStats,
            Melds: melds,
            GuaranteedSockets: guaranteed,
            AdvancedMeldingPermitted: advanced,
            Caps: _statCaps.CapsFor(ilvl, slot, twoHanded && slot == GearSlotId.MainHand),
            SweepMeldGrade: sweepGrade,
            SweepMeldValue: sweepValue);
    }

    /// <summary>Highest materia grade the piece's ilvl can hold, and its substat value.</summary>
    private (int Grade, int Value) BestMeldGradeFor(int ilvl, Lumina.Excel.ExcelSheet<LuminaMateria> materiaSheet)
    {
        if (_gradeMinIlvl == null || _gradeValues == null)
        {
            // One requirement/value table serves all combat substats — take the Crit row.
            foreach (var materiaRow in materiaSheet)
            {
                if (materiaRow.BaseParam.RowId != GearStatIds.CriticalHit)
                    continue;

                var count = Math.Min(materiaRow.Item.Count, materiaRow.Value.Count);
                var minIlvl = new int[count];
                var values = new short[count];
                for (var g = 0; g < count; g++)
                {
                    var itemRef = materiaRow.Item[g];
                    minIlvl[g] = itemRef.RowId != 0 ? (int)(itemRef.ValueNullable?.LevelItem.RowId ?? 0) : int.MaxValue;
                    values[g] = materiaRow.Value[g];
                }

                _gradeMinIlvl = minIlvl;
                _gradeValues = values;
                break;
            }

            if (_gradeMinIlvl == null || _gradeValues == null)
                return (GradeXiiIndex + 1, 54); // sheet unavailable — fail open to the XII model
        }

        for (var g = Math.Min(GradeXiiIndex, _gradeMinIlvl.Length - 1); g >= 0; g--)
        {
            if (_gradeValues[g] > 0 && _gradeMinIlvl[g] <= ilvl)
                return (g + 1, _gradeValues[g]);
        }

        return (1, _gradeValues.Length > 0 ? Math.Max((int)_gradeValues[0], 1) : 1);
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
