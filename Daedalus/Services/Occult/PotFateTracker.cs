using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin.Services;

namespace Daedalus.Services.Occult;

/// <summary>
/// Watches the Occult Crescent magic-pot FATEs — far and away the best currency in the zone.
/// There are TWO payouts and the second dwarfs the first (field 2026-07-31):
///   • completing the FATE itself: ~160 Silver + ~160 Gold Obols
///   • the hidden coffer the Magical Elixir leads you to, which comes in TIERS (field
///     2026-07-31, still being pinned down):
///         bronze — 100 Silver, 160 Gold, 1,000 gil, XI/XII materia
///         silver — not yet observed (presumably between the two)
///         gold   — 1,000 Silver, 1,600 Gold, 30,000 gil, XI/XII materia
///     The tiers scale ~10x, and the gil confirms it: 1,000 gil on bronze against 30,000 on
///     gold, which is what settled an earlier ambiguity over the gold chest's silver figure.
///     TIER LOOKS ZONE-BOUND (field 2026-07-31, 3 samples): both South Horn pots gave bronze,
///     the North Horn pot gave gold. If that holds, South Horn pots are worth a TENTH of a
///     North Horn one and the farm should never leave North Horn. Small sample — but the
///     hypothesis is clean and matches how field ops usually scale by zone.
/// So the tier matters enormously: a gold coffer is a whole silver shard AND a whole gold
/// shard in one go, a bronze barely a tenth of that,
/// while a trash mob pays 3-5 gold. That is why this is a tracker — being present for the
/// FATE, and not losing the pot on the escort, is worth more than any farming routine.
/// <para>
/// The pot FATEs run on a ~30 minute cycle and ALTERNATE between the two spawn points, so
/// each individual one comes round about every hour. Because the reward is gated by the timer
/// rather than by clear speed, the thing that actually costs you obols is not being there when
/// one pops — hence a tracker rather than a farming routine.
/// </para>
/// Everything shown is measured, not assumed: the countdown is seeded from
/// <see cref="ExpectedCycleSeconds"/> but re-derives from the real gap once two spawns of the
/// same FATE have been observed.
/// </summary>
public sealed class PotFateTracker
{
    /// <summary>
    /// "Cache Me if You Can" — the treasure-hunt status the Magical Elixir grants after a
    /// Gold-rank pot FATE ("Being guided to buried treasure"). While this is up you are
    /// hunting the coffer, which is the 1,000 Silver + 1,600 Gold payout — the single most
    /// valuable state in the zone, so the HUD calls it out loudly.
    /// </summary>
    public const uint TreasureHuntStatusId = 1531;

    /// <summary>Published cycle: a pot FATE every ~30 minutes, alternating between the two.</summary>
    public const double ExpectedCycleSeconds = 30 * 60;

    /// <summary>The magic-pot FATEs. Matched case-insensitively against live FATE names.</summary>
    public static readonly IReadOnlyList<string> PotFateNames =
    [
        "In a Pot of Bother",
        "Daylight Pottery",
    ];

    /// <summary>Lead time on the "about to pop" warning — enough to travel, not enough to idle.</summary>
    public const double ImminentWarningSeconds = 60;

    private readonly IFateTable? _fateTable;
    private readonly IClientState _clientState;
    private readonly IGameGui? _gameGui;
    private readonly IDataManager? _dataManager;

    private System.Numerics.Vector3? _activePosition;

    // Keyed by ZONE + name. South Horn and North Horn run their own pot FATEs and the names
    // can repeat across them, so a name-only key would have South's spawn resetting North's
    // timer — and the two are not interchangeable (tier looks zone-bound: North pays gold,
    // South bronze), so conflating them would point the farm at the wrong Horn.
    private readonly Dictionary<(ushort Zone, string Name), DateTime> _lastSeenUtc = new();
    private readonly Dictionary<(ushort Zone, string Name), double> _observedCycleSeconds = new();
    private readonly HashSet<(ushort Zone, string Name)> _activeNow = new();

    public PotFateTracker(IFateTable? fateTable, IClientState clientState,
        IGameGui? gameGui = null, IDataManager? dataManager = null)
    {
        _fateTable = fateTable;
        _clientState = clientState;
        _gameGui = gameGui;
        _dataManager = dataManager;
    }

    /// <summary>True when the next pot is due within the warning window (or overdue).</summary>
    public bool PotImminent => SecondsUntilNextPot() is { } s && s <= ImminentWarningSeconds;

    /// <summary>Whether a live pot FATE can be flagged on the map right now.</summary>
    public bool CanOpenMap => _activePosition is not null && _gameGui is not null && _dataManager is not null;

    /// <summary>
    /// Opens the map on the live pot FATE and drops a flag there. Uses the FATE's own world
    /// position rather than hardcoded coordinates — the two Horns have their own pot FATEs and
    /// published coordinates for them disagree, so the live object is the only trustworthy
    /// source.
    /// </summary>
    public void OpenMapToActivePot()
    {
        if (_activePosition is not { } pos || _gameGui is null || _dataManager is null)
            return;

        try
        {
            var territory = (uint)_clientState.TerritoryType;
            var mapId = _dataManager.GetExcelSheet<Lumina.Excel.Sheets.TerritoryType>()
                ?.GetRowOrDefault(territory)?.Map.RowId ?? 0;
            if (mapId != 0)
                _gameGui.OpenMapWithMapLink(territory, mapId, pos);
        }
        catch
        {
            // Map link is a convenience — never let it take the HUD down.
        }
    }

    /// <summary>Testable clock.</summary>
    internal Func<DateTime> UtcNow { get; set; } = () => DateTime.UtcNow;

    /// <summary>A pot FATE is up right now (name), or null.</summary>
    public string? ActiveFate => _activeNow.Count > 0 ? _activeNow.First().Name : null;

    /// <summary>Framework tick — cheap; the fate table is a handful of entries.</summary>
    public void Update()
    {
        if (_fateTable is null || !Data.PhantomJobData.OccultTerritoryIds.Contains((ushort)_clientState.TerritoryType))
            return;

        var zone = (ushort)_clientState.TerritoryType;
        var now = UtcNow();
        var seen = new HashSet<(ushort, string)>();

        foreach (var fate in _fateTable)
        {
            var name = fate.Name.TextValue ?? string.Empty;
            if (!IsPotFate(name))
                continue;

            var key = (zone, name);
            seen.Add(key);
            _activePosition = fate.Position;
            if (_activeNow.Contains(key))
                continue; // already counted this spawn

            // Rising edge: a spawn we have not recorded. If we have seen this one before, the
            // gap between the two IS the cycle — measured beats the published figure.
            if (_lastSeenUtc.TryGetValue(key, out var previous))
            {
                var gap = (now - previous).TotalSeconds;
                if (gap > 60)
                    _observedCycleSeconds[key] = gap;
            }

            _lastSeenUtc[key] = now;
        }

        _activeNow.Clear();
        foreach (var k in seen)
            _activeNow.Add(k);

        if (seen.Count == 0)
            _activePosition = null;
    }

    public static bool IsPotFate(string fateName) =>
        PotFateNames.Any(n => string.Equals(n, fateName, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Seconds until this FATE is next expected, or null if it has never been seen (nothing to
    /// count from). Negative means overdue — the cycle is approximate and the instance's own
    /// timing shifts it.
    /// </summary>
    public double? SecondsUntilExpected(string fateName)
    {
        var key = ((ushort)_clientState.TerritoryType, fateName);
        if (!_lastSeenUtc.TryGetValue(key, out var last))
            return null;

        var cycle = _observedCycleSeconds.TryGetValue(key, out var measured)
            ? measured
            : ExpectedCycleSeconds * 2; // each individual FATE alternates, so ~1h apart
        return cycle - (UtcNow() - last).TotalSeconds;
    }

    /// <summary>Whether the cycle for this FATE came from observation rather than the default.</summary>
    public bool CycleIsMeasured(string fateName) =>
        _observedCycleSeconds.ContainsKey(((ushort)_clientState.TerritoryType, fateName));

    /// <summary>
    /// Seconds until the NEXT pot FATE of EITHER kind — the number that actually matters, since
    /// both pay the same and they alternate. Seeing one spawn dates the other: the alternating
    /// ~30 min cycle means the next pot is due half a cycle after the last one, whichever it
    /// was. Null until something has been seen.
    /// </summary>
    public double? SecondsUntilNextPot()
    {
        // Only THIS zone's pots — a South Horn sighting must not date a North Horn estimate.
        var zone = (ushort)_clientState.TerritoryType;
        DateTime? latest = null;
        foreach (var kv in _lastSeenUtc)
        {
            if (kv.Key.Zone == zone && (latest is null || kv.Value > latest))
                latest = kv.Value;
        }

        return latest is null ? null : ExpectedCycleSeconds - (UtcNow() - latest.Value).TotalSeconds;
    }

    /// <summary>Last time this FATE was seen up, or null.</summary>
    public DateTime? LastSeenUtc(string fateName) =>
        _lastSeenUtc.TryGetValue(((ushort)_clientState.TerritoryType, fateName), out var t) ? t : null;
}
