using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Dalamud.Plugin.Services;

namespace Daedalus.Services.Debug;

/// <summary>One action as read off the game's own sheets, ready to be transcribed into a catalog.</summary>
/// <param name="ActionId">Row id.</param>
/// <param name="Name">Display name.</param>
/// <param name="Level">`ClassJobLevel` — the unlock level.</param>
/// <param name="CastSeconds">Cast time; 0 for instants.</param>
/// <param name="RecastSeconds">Recast (the GCD for weaponskills/spells).</param>
/// <param name="Range">Ability range in yalms; 0 = self.</param>
/// <param name="EffectRange">AoE radius in yalms; 0 = single target.</param>
/// <param name="Category">ActionCategory name — Ability / Weaponskill / Spell.</param>
/// <param name="IsRoleAction">True for shared role actions.</param>
/// <param name="TargetsArea">Ground-placed.</param>
/// <param name="Description">ActionTransient tooltip — where potencies actually live.</param>
public readonly record struct JobActionRow(
    uint ActionId,
    string Name,
    int Level,
    float CastSeconds,
    float RecastSeconds,
    int Range,
    int EffectRange,
    string Category,
    bool IsRoleAction,
    bool TargetsArea,
    string Description);

/// <summary>
/// Dumps every action belonging to a ClassJob straight from the client's own Excel sheets.
///
/// <para>
/// Built for a job whose data has not reached XIVAPI yet — Beastmaster on 7.56 being the case in
/// hand. The client has the sheets the moment the patch installs, so reading them in-process
/// beats waiting on a third-party mirror, and it is the same source the catalogs were always
/// meant to come from. Output goes to <c>/xllog</c> in a shape that can be transcribed into a
/// catalog without re-typing numbers.
/// </para>
///
/// <para>
/// Potencies are deliberately NOT parsed out of the description — they are left in the tooltip
/// text verbatim, because reading them off the sheet is the rule and paraphrasing them is how a
/// wrong number gets laundered into a catalog.
/// </para>
/// </summary>
public sealed class JobActionDumpService
{
    private readonly IDataManager _dataManager;

    public JobActionDumpService(IDataManager dataManager) => _dataManager = dataManager;

    /// <summary>
    /// Every non-PvP action whose <c>ClassJob</c> is <paramref name="jobId"/>, ordered by level
    /// then id. Empty when the sheet is unavailable or the job has no rows yet.
    /// </summary>
    public IReadOnlyList<JobActionRow> Collect(uint jobId)
    {
        var sheet = _dataManager.GetExcelSheet<Lumina.Excel.Sheets.Action>();
        if (sheet is null)
            return [];

        var transient = _dataManager.GetExcelSheet<Lumina.Excel.Sheets.ActionTransient>();
        var rows = new List<JobActionRow>();

        foreach (var row in sheet)
        {
            // RowId only — never .Value on a RowRef here. An invalid reference THROWS, and a
            // sheet mid-patch is exactly where invalid references live.
            if (row.ClassJob.RowId != jobId)
                continue;
            if (row.RowId == 0 || row.IsPvP)
                continue;

            var name = SafeName(row);
            if (string.IsNullOrWhiteSpace(name))
                continue;

            rows.Add(new JobActionRow(
                row.RowId,
                name,
                row.ClassJobLevel,
                row.Cast100ms / 10f,
                row.Recast100ms / 10f,
                row.Range,
                row.EffectRange,
                SafeCategory(row),
                row.IsRoleAction,
                row.TargetArea,
                SafeDescription(transient, row.RowId)));
        }

        return rows
            .OrderBy(r => r.Level)
            .ThenBy(r => r.ActionId)
            .ToList();
    }

    /// <summary>
    /// Paste-ready dump. Pure over the collected rows so the formatting is testable without a
    /// game attached.
    /// </summary>
    public static string Format(uint jobId, string jobName, IReadOnlyList<JobActionRow> rows)
    {
        if (rows.Count == 0)
        {
            return $"No actions found for ClassJob {jobId} ({jobName}).\n"
                 + "Either the job is not in this client's sheets yet, or its actions are keyed by "
                 + "ClassJobCategory rather than ClassJob — check a known action's row before "
                 + "concluding the data is absent.";
        }

        var sb = new StringBuilder();
        sb.AppendLine($"ClassJob {jobId} ({jobName}) — {rows.Count} action(s), from this client's sheets");
        sb.AppendLine();
        sb.AppendLine("| id | name | lv | cast | recast | range | aoe | category | role | ground |");
        sb.AppendLine("| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |");
        foreach (var r in rows)
        {
            sb.AppendLine($"| {r.ActionId} | {r.Name} | {r.Level} | {r.CastSeconds:0.#}s | "
                        + $"{r.RecastSeconds:0.#}s | {r.Range} | {r.EffectRange} | {r.Category} | "
                        + $"{(r.IsRoleAction ? "yes" : "")} | {(r.TargetsArea ? "yes" : "")} |");
        }

        sb.AppendLine();
        sb.AppendLine("Tooltips (potencies live here — transcribe, do not paraphrase):");
        foreach (var r in rows)
        {
            if (string.IsNullOrWhiteSpace(r.Description))
                continue;
            sb.AppendLine();
            sb.AppendLine($"{r.ActionId} {r.Name}:");
            sb.AppendLine("  " + r.Description.Replace("\n", "\n  "));
        }

        return sb.ToString();
    }

    private static string SafeName(Lumina.Excel.Sheets.Action row)
    {
        try { return row.Name.ToString(); }
        catch { return string.Empty; }
    }

    private static string SafeCategory(Lumina.Excel.Sheets.Action row)
    {
        try { return row.ActionCategory.ValueNullable?.Name.ToString() ?? string.Empty; }
        catch { return string.Empty; }
    }

    private static string SafeDescription(
        Lumina.Excel.ExcelSheet<Lumina.Excel.Sheets.ActionTransient>? transient, uint id)
    {
        if (transient is null)
            return string.Empty;

        try { return transient.GetRowOrDefault(id)?.Description.ToString() ?? string.Empty; }
        catch { return string.Empty; }
    }
}
