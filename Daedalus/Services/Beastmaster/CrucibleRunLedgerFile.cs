using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Daedalus.Data;

namespace Daedalus.Services.Beastmaster;

/// <summary>
/// Keeps <see cref="CrucibleRunLedger"/> in <c>crucible-runs.jsonl</c> in the plugin's config folder, one line a run,
/// and remembers the last enemy fought so a run that ended early says where.
/// </summary>
public sealed class CrucibleRunLedgerFile
{
    public const string FileName = "crucible-runs.jsonl";

    private readonly string _path;
    private readonly Action<string> _log;
    private int _boardIndex;

    public CrucibleRunLedgerFile(string configDirectory, Action<string> log)
    {
        _path = Path.Combine(configDirectory, FileName);
        _log = log;
    }

    /// <summary>The last enemy fought on the board being run ("" before the first).</summary>
    public string LastEnemy { get; private set; } = "";

    /// <summary>Framework thread, every frame.</summary>
    public void Update(CrucibleSnapshot now)
    {
        if (now.Board is not { } board)
            return; // the result screen can outlast the board: keep what was seen
        if (board.Index != _boardIndex)
        {
            _boardIndex = board.Index;
            LastEnemy = "";
        }

        if (now.Enemy is { } enemy)
            LastEnemy = enemy.Name;
    }

    /// <summary>Writes the run down and says how runs made this way have gone.</summary>
    public void Record(CrucibleLedgerEntry entry)
    {
        try
        {
            File.AppendAllText(_path, CrucibleRunLedger.ToLine(entry) + Environment.NewLine);
            var all = File.ReadLines(_path).Select(CrucibleRunLedger.Parse).OfType<CrucibleLedgerEntry>().ToList();
            var squad = CrucibleBonusRules.BeastSquadFor(entry.Roster.Count) is { } tier ? $", {tier}" : "";
            _log($"Crucible ledger: {entry.Board}, {CrucibleLoopRules.DegreeName(entry.Degree)}{squad} — "
                 + CrucibleRunLedger.Summarize(all, entry).Describe() + ".");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log($"Crucible ledger: could not write {FileName} ({ex.Message}).");
        }
    }

    public IReadOnlyList<CrucibleLedgerEntry> ReadAll()
    {
        try
        {
            return File.Exists(_path)
                ? File.ReadLines(_path).Select(CrucibleRunLedger.Parse).OfType<CrucibleLedgerEntry>().ToList()
                : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
