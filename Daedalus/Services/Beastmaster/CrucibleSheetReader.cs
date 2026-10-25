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
/// <item>XBMContent: col 0 = ContentFinderCondition; col 1 = roster cap (10/12/14/12/15, as the boards say); cols 4-36 =
/// the 33 score bonus values (XBMScoreBonus order).</item>
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
                battlesByBoard[board.RowId] = board.Select(r => (int)r.ReadUInt16Column(0)).ToList();

            // The route: each board's spaces (XBMContentStageEvent), and what its random spaces can be
            // (XBMContentRandomStageEvent → XBMRandomStageEvent). Layout in CrucibleRouteBuilder.
            var stageRows = new Dictionary<uint, List<CrucibleStageRow>>();
            foreach (var board in excel.GetSubrowSheet<RawSubrow>(name: "XBMContentStageEvent"))
                stageRows[board.RowId] = board.Select(r => new CrucibleStageRow(r.ReadInt8Column(0), r.ReadUInt8Column(1), r.ReadInt8Column(2))).ToList();

            var randomOptions = new Dictionary<uint, IReadOnlyList<(byte Type, int Index)>>();
            foreach (var pool in excel.GetSubrowSheet<RawSubrow>(name: "XBMRandomStageEvent"))
                randomOptions[pool.RowId] = pool.Select(r => (r.ReadUInt8Column(0), (int)r.ReadInt8Column(1))).ToList();

            var randomByBoard = new Dictionary<uint, List<IReadOnlyList<(byte Type, int Index)>>>();
            foreach (var board in excel.GetSubrowSheet<RawSubrow>(name: "XBMContentRandomStageEvent"))
                randomByBoard[board.RowId] = board
                    .Select(r => randomOptions.GetValueOrDefault(r.ReadUInt16Column(0)) ?? (IReadOnlyList<(byte, int)>)[])
                    .ToList();

            var boards = new List<CrucibleBoard>();
            foreach (var row in excel.GetSheet<RawRow>(name: "XBMContent"))
            {
                var cfcId = row.ReadUInt16Column(0);
                if (cfcId == 0 || cfcs.GetRowOrDefault(cfcId) is not { } cfc)
                    continue;
                var bonuses = Enumerable.Range(4, 33).Select(c => (int)row.ReadUInt16Column(c)).ToList();
                var rawBattles = battlesByBoard.GetValueOrDefault(row.RowId) ?? [];
                var route = CrucibleBoardLayout.Place(
                    CrucibleRouteBuilder.Build(
                        stageRows.GetValueOrDefault(row.RowId) ?? [], rawBattles,
                        randomByBoard.GetValueOrDefault(row.RowId) ?? []),
                    RoutePoints(data, cfc.TerritoryType.ValueNullable?.Bg.ExtractText(), log));
                boards.Add(new CrucibleBoard(
                    (int)row.RowId, cfcId, cfc.TerritoryType.RowId, cfc.Name.ExtractText(), cfc.ClassJobLevelRequired,
                    rawBattles.Where(b => b != 0).ToList(), bonuses, route, row.ReadUInt8Column(1), row.ReadUInt8Column(3)));
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

            log($"Crucible: read {boards.Count} board(s) ({string.Join("/", boards.Select(b => b.Route.Count))} spaces, "
                + $"{string.Join("/", boards.Select(b => b.Route.Count(s => s.At != null)))} placed) and {enemies.Count} enemies from the game data.");
            var items = excel.GetSheet<RawRow>(name: "XBMItem").ToList();
            var beastGear = items.Where(r => r.ReadUInt8Column(0) == 1).Select(r => (int)r.RowId).ToList();
            var itemNames = items.ToDictionary(r => (int)r.RowId, r => Text(r, 11));
            return new CrucibleIndex(boards, enemies, beastGear, itemNames);
        }
        catch (Exception ex)
        {
            log($"Crucible: could not read the game data ({ex.Message}); the Crucible window will be empty.");
            return CrucibleIndex.Empty;
        }
    }

    /// <summary>
    /// The board's space positions: the player pop points on the <c>LVD_ROUTE_*</c> layers of its territory's
    /// <c>planmap.lgb</c> (e.g. bg/ffxiv/fst_f1/xbm/f1x1/level/planmap.lgb). Empty when unreadable — the board is then
    /// just not walked.
    /// </summary>
    private static IReadOnlyList<System.Numerics.Vector3> RoutePoints(IDataManager data, string? bg, Action<string> log)
    {
        try
        {
            var level = bg?.IndexOf("/level/", StringComparison.Ordinal) ?? -1;
            if (bg == null || level < 0)
                return [];

            var lgb = data.GetFile<Lumina.Data.Files.LgbFile>($"bg/{bg[..level]}/level/planmap.lgb");
            if (lgb == null)
                return [];

            return lgb.Layers
                .Where(l => l.Name.StartsWith("LVD_ROUTE", StringComparison.Ordinal))
                .SelectMany(l => l.InstanceObjects)
                .Where(o => o.Object is Lumina.Data.Parsing.Layer.LayerCommon.PopRangeInstanceObject)
                .Select(o => new System.Numerics.Vector3(o.Transform.Translation.X, o.Transform.Translation.Y, o.Transform.Translation.Z))
                .ToList();
        }
        catch (Exception ex)
        {
            log($"Crucible: could not read the board layout for {bg} ({ex.Message}); that board will not be walked.");
            return [];
        }
    }
}
