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
///         bronze — 100 Silver, 160 Gold, 1,000 gil, XI/XII materia (confirmed in BOTH Horns:
///                  identical amounts, only the currency differs — Pieces south, Obols north)
///         silver — 300 Silver, 480 Gold, 5,000 gil, XI/XII materia (exactly 3x bronze)
///         gold   — 1,000 Silver, 1,600 Gold, 30,000 gil, XI/XII materia
///     The tiers are exact multiples of bronze: silver 3x, gold 10x, consistently across
///     obols/pieces AND gil. That regularity is what settled an earlier ambiguity over the
///     gold chest's silver figure.
///     CURRENCY IS PER ZONE (field 2026-07-31, confirmed): South Horn pays Enlightenment
///     PIECES, North Horn pays OBOLS.
///     ⚠ UNDER SUSPICION (field 2026-08-01): a WORLD coffer spot produced silver on one visit
///     and bronze on another, observed directly. So for world coffers the tier is rolled PER
///     SPAWN and location predicts nothing. That does not automatically carry over to POT
///     coffers — they are a different mechanism (EventObj awarded by a FATE, not Treasure-sheet
///     objects found in the world) — but the per-spot claim below rests on a handful of samples
///     and the same "generalised from one observation" error has already been made twice in this
///     file. Treat it as unproven until the chest ledger has real counts.
///     TIER LOOKS PER SPOT, NOT PER ZONE. Each Horn has a northern-positioned pot FATE and a
///     southern-positioned one, and within NORTH HORN the southern spot (In a Pot of Bother,
///     11.0/25.8) produced bronze coffers twice while the northern spot (Daylight Pottery,
///     26.2/11.6) produced gold — the bronze drops paid obols, which is what places them in
///     North Horn rather than South. If that holds, the spot you run matters as much as the
///     zone: same currency, ~10x the payout. Needs more samples, and an earlier note in this
///     file wrongly read the same evidence as a zone difference.
///     They are therefore not competing — they fund different things:
///         Obols (North) -> phantom job soul shards, 1,000-1,600 each
///         Pieces (South) -> Arcanaut's armour upgrades, and those are far dearer:
///                           +1 costs 3 Aetherspun Silver (1,200 Pieces each) plus
///                           3 Aetherial Fixative (1,600 Pieces each) PER PIECE
///     +2 is NOT purely a currency problem: Aetherspun Gold is a CHEST DROP, not a purchase
///     (field 2026-07-31) — best odds from SILVER non-pot chests, ~16% for 1-3. So the +2
///     tier is RNG-gated on top of the Pieces cost, and the chest hunt matters as much as the
///     currency grind.
///     An earlier note here claimed South pots were worth a tenth of North ones and the farm
///     should stay north. That was wrong — it compared a bronze South chest against a gold
///     North one as though the numbers shared a unit. Which Horn to farm depends entirely on
///     whether you need shards or armour.
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

    /// <summary>
    /// Magic-pot FATEs per zone, matched case-insensitively against live FATE names. Each Horn
    /// runs TWO of them and they are NOT the same FATEs — the names below are confirmed North
    /// Horn (wiki + field), and South Horn's two are field-confirmed as well. All four are
    /// now known, so the tracker works in both Horns. South matters as much as North — its
    /// pots pay the PIECES that Arcanaut's armour upgrades need, the dearer project of the
    /// two.
    /// </summary>
    public static readonly IReadOnlyDictionary<ushort, IReadOnlyList<string>> PotFatesByZone =
        new Dictionary<ushort, IReadOnlyList<string>>
        {
            [Data.PhantomJobData.NorthHornTerritoryId] = new[] { "In a Pot of Bother", "Daylight Pottery" },
            // South Horn, both field-confirmed 2026-07-31: "Pleading Pots" at the southern
            // spot, "Persistent Pots" at the northern one. (Note "Persistent Pots" the FATE is
            // distinct from "Persistent Pot" the escortable NPC that appears in the enemy
            // census — near-identical names, different things.)
            [Data.PhantomJobData.SouthHornTerritoryId] = new[] { "Pleading Pots", "Persistent Pots" },
        };

    /// <summary>
    /// Where each pot FATE sits WITHIN its own Horn. Both Horns run a northern and a southern
    /// spot, and the coffer tier looks spot-bound rather than zone-bound — inside North Horn the
    /// northern spot (Daylight Pottery, 26.2/11.6) produced gold coffers while the southern one
    /// (In a Pot of Bother, 11.0/25.8) produced bronze. So which spot you travel to matters as
    /// much as which Horn you are in, and the HUD says which is which.
    /// <para>
    /// South Horn's pair is labelled from the same field notes: Persistent Pots is the northern
    /// spot, Pleading Pots the southern one.
    /// </para>
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> SpotLabels =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Daylight Pottery"] = "north pots",
            ["In a Pot of Bother"] = "south pots",
            ["Persistent Pots"] = "north pots",
            ["Pleading Pots"] = "south pots",
        };

    /// <summary>The in-Horn spot label for a pot FATE, or empty when it isn't one we know.</summary>
    public static string DescribeSpot(string fateName) =>
        !string.IsNullOrWhiteSpace(fateName) && SpotLabels.TryGetValue(fateName, out var label)
            ? label
            : string.Empty;

    /// <summary>FATE name with its spot label appended, e.g. "Daylight Pottery (north pots)".</summary>
    public static string NameWithSpot(string fateName)
    {
        var spot = DescribeSpot(fateName);
        return spot.Length == 0 ? fateName : $"{fateName} ({spot})";
    }

    /// <summary>The pot FATEs for the zone the player is standing in (empty when unknown).</summary>
    public IReadOnlyList<string> PotFateNames =>
        PotFatesByZone.TryGetValue((ushort)_clientState.TerritoryType, out var names)
            ? names
            : System.Array.Empty<string>();

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

    private readonly Config.PhantomConfig? _config;
    private readonly System.Action? _save;

    public PotFateTracker(IFateTable? fateTable, IClientState clientState,
        IGameGui? gameGui = null, IDataManager? dataManager = null,
        Config.PhantomConfig? config = null, System.Action? save = null)
    {
        _fateTable = fateTable;
        _clientState = clientState;
        _gameGui = gameGui;
        _dataManager = dataManager;
        _config = config;
        _save = save;

        LoadHistory();
    }

    /// <summary>
    /// Seed the in-memory history from config. Without this the countdown restarts from "never
    /// seen one" on every plugin reload, which is silent failure — the HUD simply shows nothing
    /// until a pot spawns, by which point you have already missed the travel window.
    /// </summary>
    private void LoadHistory()
    {
        if (_config?.PotFateHistory is not { Count: > 0 } history)
            return;

        foreach (var entry in history)
        {
            if (!TryParseHistoryKey(entry.Key, out var key))
                continue;

            _lastSeenUtc[key] = DateTimeOffset.FromUnixTimeSeconds(entry.Value.LastSeenUnixSeconds).UtcDateTime;
            if (entry.Value.CycleSeconds is { } cycle && cycle > 0)
                _observedCycleSeconds[key] = cycle;
        }
    }

    private void SaveHistory((ushort Zone, string Name) key)
    {
        if (_config is null)
            return;

        var stored = new Config.PotFateSighting
        {
            LastSeenUnixSeconds = new DateTimeOffset(DateTime.SpecifyKind(_lastSeenUtc[key], DateTimeKind.Utc))
                .ToUnixTimeSeconds(),
            CycleSeconds = _observedCycleSeconds.TryGetValue(key, out var cycle) ? cycle : null,
        };

        _config.PotFateHistory[HistoryKey(key)] = stored;
        _save?.Invoke();
    }

    /// <summary>
    /// Forget everything. Guarded so the no-op case costs nothing — <see cref="Update"/> runs
    /// every framework tick and must not rewrite the config file while out of the zone.
    /// </summary>
    internal void ClearHistory()
    {
        var hadMemory = _lastSeenUtc.Count > 0 || _observedCycleSeconds.Count > 0 || _activeNow.Count > 0;
        var hadStored = _config is { PotFateHistory.Count: > 0 };
        if (!hadMemory && !hadStored)
            return;

        _lastSeenUtc.Clear();
        _observedCycleSeconds.Clear();
        _activeNow.Clear();
        _activePosition = null;

        if (hadStored)
        {
            _config!.PotFateHistory.Clear();
            _save?.Invoke();
        }
    }

    private static string HistoryKey((ushort Zone, string Name) key) => $"{key.Zone}:{key.Name}";

    private static bool TryParseHistoryKey(string raw, out (ushort Zone, string Name) key)
    {
        key = default;
        var split = raw.IndexOf(':');
        if (split <= 0 || split == raw.Length - 1)
            return false;
        if (!ushort.TryParse(raw[..split], out var zone))
            return false;

        key = (zone, raw[(split + 1)..]);
        return true;
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
        var zone = (ushort)_clientState.TerritoryType;

        // Leaving the Horn drops the history. The zone is instanced, so a timestamp from the
        // instance you just left says nothing about the one you come back to — re-entering
        // needs a fresh spawn to restart the clock. Persistence only has to survive a plugin
        // reload WITHIN a visit, which is the case that was silently failing.
        if (!Data.PhantomJobData.OccultTerritoryIds.Contains(zone))
        {
            ClearHistory();
            return;
        }

        if (_fateTable is null)
            return;
        var now = UtcNow();
        var seen = new HashSet<(ushort, string)>();

        foreach (var fate in _fateTable)
        {
            var name = fate.Name.TextValue ?? string.Empty;
            if (!IsPotFate(zone, name))
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
            SaveHistory(key);
        }

        _activeNow.Clear();
        foreach (var k in seen)
            _activeNow.Add(k);

        if (seen.Count == 0)
            _activePosition = null;
    }

    /// <summary>Is this a pot FATE in the given zone?</summary>
    public static bool IsPotFate(ushort territoryId, string fateName) =>
        PotFatesByZone.TryGetValue(territoryId, out var names)
        && names.Any(n => string.Equals(n, fateName, StringComparison.OrdinalIgnoreCase));

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
