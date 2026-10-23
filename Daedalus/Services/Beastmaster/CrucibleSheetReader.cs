using System;
using System.Collections.Generic;
using System.Linq;
using Daedalus.Data;
using Dalamud.Plugin.Services;
using Lumina.Excel;

namespace Daedalus.Services.Beastmaster;

/// <summary>
/// Reads the Crucible from the game's own XBM sheets (7.56+). Lumina has no typed rows for them, so the columns are read
/// by index — the layout below was taken from the 2026.10.16 client and agrees with EXDSchema's XBMBattleDetail /
/// XBMBattleDetailAction (Name → BNpcName, two moves → XBMBattleDetailAction, Element → XBMElement).
/// <list type="bullet">
/// <item>XBMContent: col 0 = ContentFinderCondition.</item>
/// <item>XBMContentBattle (subrows, one per board): col 0 = battle id; subrow 0 is the final battle.</item>
/// <item>XBMBattleDetail (subrows, one per battle): 0 BNpcName, 1-5 ratings, 6-7 moves, 8 element, 9 BNpcResist.</item>
/// <item>XBMBattleDetailAction: 0 Action, 1 XBMActionTarget, 2 XBMActionEffectType, 3 Status.</item>
/// </list>
/// Any failure yields an empty index — the window then says it has no data, and nothing else depends on it.
/// </summary>
public static class CrucibleSheetReader
{
    public static CrucibleIndex Read(IDataManager data, Action<string> log)
    {
        try
        {
            var excel = data.Excel;
            var actions = data.GetExcelSheet<Lumina.Excel.Sheets.Action>();
            var statuses = data.GetExcelSheet<Lumina.Excel.Sheets.Status>();
            var names = data.GetExcelSheet<Lumina.Excel.Sheets.BNpcName>();
            var cfcs = data.GetExcelSheet<Lumina.Excel.Sheets.ContentFinderCondition>();

            string Text(RawRow r, int col) => r.ReadStringColumn(col).ExtractText().Trim();
            var targets = excel.GetSheet<RawRow>(name: "XBMActionTarget").ToDictionary(r => r.RowId, r => Text(r, 0));
            var shapes = excel.GetSheet<RawRow>(name: "XBMActionEffectType").ToDictionary(r => r.RowId, r => Text(r, 0));
            var moveRows = excel.GetSheet<RawRow>(name: "XBMBattleDetailAction").ToDictionary(r => r.RowId);

            CrucibleMove? Move(ushort id)
            {
                if (id == 0 || !moveRows.TryGetValue(id, out var m))
                    return null;
                var actionId = m.ReadUInt32Column(0);
                var statusId = m.ReadUInt32Column(3);
                return new CrucibleMove(
                    actionId,
                    actions.GetRowOrDefault(actionId)?.Name.ExtractText() ?? $"#{actionId}",
                    targets.GetValueOrDefault(m.ReadUInt8Column(1)) ?? "",
                    shapes.GetValueOrDefault(m.ReadUInt8Column(2)) ?? "",
                    statusId == 0 ? "" : statuses.GetRowOrDefault(statusId)?.Name.ExtractText() ?? "");
            }

            var battlesByBoard = new Dictionary<uint, List<int>>();
            foreach (var board in excel.GetSubrowSheet<RawSubrow>(name: "XBMContentBattle"))
                battlesByBoard[board.RowId] = board.Select(r => (int)r.ReadUInt16Column(0)).Where(b => b != 0).ToList();

            var boards = new List<CrucibleBoard>();
            foreach (var row in excel.GetSheet<RawRow>(name: "XBMContent"))
            {
                var cfcId = row.ReadUInt16Column(0);
                if (cfcId == 0 || cfcs.GetRowOrDefault(cfcId) is not { } cfc)
                    continue;
                boards.Add(new CrucibleBoard(
                    (int)row.RowId, cfcId, cfc.TerritoryType.RowId, cfc.Name.ExtractText(), cfc.ClassJobLevelRequired,
                    battlesByBoard.GetValueOrDefault(row.RowId) ?? []));
            }

            var finals = boards.Select(b => b.FinalBattle).Where(b => b != 0).ToHashSet();
            var enemies = new List<CrucibleEnemy>();
            foreach (var battle in excel.GetSubrowSheet<RawSubrow>(name: "XBMBattleDetail"))
            {
                foreach (var row in battle)
                {
                    var nameId = row.ReadUInt32Column(0);
                    if (nameId == 0)
                        continue;
                    var moves = new[] { Move(row.ReadUInt16Column(6)), Move(row.ReadUInt16Column(7)) }
                        .Where(m => m != null).Select(m => m!).ToList();
                    var element = row.ReadUInt8Column(8);
                    enemies.Add(new CrucibleEnemy(
                        nameId,
                        names.GetRowOrDefault(nameId)?.Singular.ExtractText() ?? $"#{nameId}",
                        (int)row.RowId,
                        finals.Contains((int)row.RowId),
                        element <= (byte)CrucibleElement.Slashing ? (CrucibleElement)element : CrucibleElement.None,
                        [row.ReadUInt8Column(1), row.ReadUInt8Column(2), row.ReadUInt8Column(3), row.ReadUInt8Column(4), row.ReadUInt8Column(5)],
                        moves));
                }
            }

            log($"Crucible: read {boards.Count} board(s) and {enemies.Count} enemies from the game data.");
            return new CrucibleIndex(boards, enemies);
        }
        catch (Exception ex)
        {
            log($"Crucible: could not read the game data ({ex.Message}); the Crucible window will be empty.");
            return CrucibleIndex.Empty;
        }
    }
}
