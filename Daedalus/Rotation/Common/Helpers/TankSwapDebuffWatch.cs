using System;
using System.Collections.Generic;
using Dalamud.Plugin.Services;

namespace Daedalus.Rotation.Common.Helpers;

/// <summary>
/// Watchlist for the auto-tank-swap stack trigger: which status ids count as "buster stacks" on
/// the watched tank. The default set is built once from the Lumina Status sheet — detrimental
/// (<c>StatusCategory == 2</c>), stackable (<c>MaxStacks >= 2</c>), and a damage-taken-increase
/// description ("...damage taken is increased", the Vulnerability Up family). NOTE: the sheet has
/// no mechanical effect data (that is server-side), so the description text is the only queryable
/// signal — English client wording. Fight-specific stack debuffs with other wording go through
/// <see cref="RegisterContentOverride"/> (per-territory, 0 = everywhere).
/// Fail-open: before <see cref="Initialize"/> (or if the sheet probe fails) every stacked status
/// matches — the v1 behavior, and what unit tests without game data get.
/// </summary>
public static class TankSwapDebuffWatch
{
    private static readonly object Gate = new();
    private static HashSet<uint>? _defaults;
    private static readonly Dictionary<ushort, HashSet<uint>> ContentOverrides = new();
    private static Func<ushort>? _territorySource;

    /// <summary>The English description phrase shared by the damage-taken-up debuff family.</summary>
    internal const string DamageTakenPhrase = "damage taken is increased";

    /// <summary>Builds the default watchlist from the Status sheet. Call once at plugin start.</summary>
    public static void Initialize(IDataManager dataManager, Func<ushort> territorySource, IPluginLog? log = null)
    {
        _territorySource = territorySource;
        try
        {
            var sheet = dataManager.GetExcelSheet<Lumina.Excel.Sheets.Status>();
            if (sheet == null)
                return;

            var defaults = new HashSet<uint>();
            foreach (var row in sheet)
            {
                if (MatchesDamageTakenDebuff(row.StatusCategory, row.MaxStacks, row.Description.ExtractText()))
                    defaults.Add(row.RowId);
            }

            lock (Gate)
                _defaults = defaults;

            log?.Info("[TankSwap] debuff watchlist: {0} damage-taken-up stack debuffs from the Status sheet", defaults.Count);
        }
        catch (Exception ex)
        {
            // Sheet probe failed — stay fail-open (watch everything), matching v1.
            log?.Warning(ex, "[TankSwap] Status sheet probe failed — stack trigger watches all stacked statuses");
        }
    }

    /// <summary>Plugin unload: statics must not survive a reload.</summary>
    public static void Shutdown()
    {
        lock (Gate)
        {
            _defaults = null;
            ContentOverrides.Clear();
            _territorySource = null;
        }
    }

    /// <summary>
    /// The sheet filter, kept pure for tests: detrimental + stackable + damage-taken-up wording.
    /// </summary>
    internal static bool MatchesDamageTakenDebuff(byte statusCategory, byte maxStacks, string? description)
    {
        const byte detrimental = 2;
        if (statusCategory != detrimental)
            return false;

        if (maxStacks < 2)
            return false;

        return description != null
               && description.Contains(DamageTakenPhrase, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Per-content override hook: additional status ids that count as buster stacks in a specific
    /// territory (pass 0 to watch the id everywhere). For fight-specific stack debuffs whose
    /// descriptions don't carry the damage-taken wording.
    /// </summary>
    public static void RegisterContentOverride(ushort territoryId, params uint[] statusIds)
    {
        lock (Gate)
        {
            if (!ContentOverrides.TryGetValue(territoryId, out var set))
                ContentOverrides[territoryId] = set = new HashSet<uint>();
            foreach (var id in statusIds)
                set.Add(id);
        }
    }

    /// <summary>
    /// True when the status id should feed the auto-swap stack trigger. Uninitialized = watch
    /// everything (fail-open to the pre-watchlist behavior).
    /// </summary>
    public static bool IsWatched(uint statusId)
    {
        lock (Gate)
        {
            if (_defaults == null)
                return true;

            return _defaults.Contains(statusId) || IsOverriddenLocked(statusId);
        }
    }

    private static bool IsOverriddenLocked(uint statusId)
    {
        if (ContentOverrides.TryGetValue(0, out var global) && global.Contains(statusId))
            return true;

        var territory = _territorySource?.Invoke() ?? 0;
        return territory != 0
               && ContentOverrides.TryGetValue(territory, out var scoped)
               && scoped.Contains(statusId);
    }

    /// <summary>Test hook: seed the default set without game data.</summary>
    internal static void InitializeForTest(IEnumerable<uint>? defaults, Func<ushort>? territorySource = null)
    {
        lock (Gate)
        {
            _defaults = defaults == null ? null : new HashSet<uint>(defaults);
            ContentOverrides.Clear();
            _territorySource = territorySource;
        }
    }
}
