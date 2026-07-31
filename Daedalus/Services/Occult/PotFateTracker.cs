using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin.Services;

namespace Daedalus.Services.Occult;

/// <summary>
/// Watches the Occult Crescent magic-pot FATEs — the best currency in the zone by a wide
/// margin (a pot pays ~160 Silver AND ~160 Gold Obols, versus 3-5 gold for a trash kill, so
/// one pot is worth 30-50 mobs).
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
    /// <summary>Published cycle: a pot FATE every ~30 minutes, alternating between the two.</summary>
    public const double ExpectedCycleSeconds = 30 * 60;

    /// <summary>The magic-pot FATEs. Matched case-insensitively against live FATE names.</summary>
    public static readonly IReadOnlyList<string> PotFateNames =
    [
        "In a Pot of Bother",
        "Daylight Pottery",
    ];

    private readonly IFateTable? _fateTable;
    private readonly IClientState _clientState;

    private readonly Dictionary<string, DateTime> _lastSeenUtc = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, double> _observedCycleSeconds = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _activeNow = new(StringComparer.OrdinalIgnoreCase);

    public PotFateTracker(IFateTable? fateTable, IClientState clientState)
    {
        _fateTable = fateTable;
        _clientState = clientState;
    }

    /// <summary>Testable clock.</summary>
    internal Func<DateTime> UtcNow { get; set; } = () => DateTime.UtcNow;

    /// <summary>A pot FATE is up right now (name), or null.</summary>
    public string? ActiveFate => _activeNow.Count > 0 ? _activeNow.First() : null;

    /// <summary>Framework tick — cheap; the fate table is a handful of entries.</summary>
    public void Update()
    {
        if (_fateTable is null || !Data.PhantomJobData.OccultTerritoryIds.Contains((ushort)_clientState.TerritoryType))
            return;

        var now = UtcNow();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var fate in _fateTable)
        {
            var name = fate.Name.TextValue ?? string.Empty;
            if (!IsPotFate(name))
                continue;

            seen.Add(name);
            if (_activeNow.Contains(name))
                continue; // already counted this spawn

            // Rising edge: a spawn we have not recorded. If we have seen this one before, the
            // gap between the two IS the cycle — measured beats the published figure.
            if (_lastSeenUtc.TryGetValue(name, out var previous))
            {
                var gap = (now - previous).TotalSeconds;
                if (gap > 60)
                    _observedCycleSeconds[name] = gap;
            }

            _lastSeenUtc[name] = now;
        }

        _activeNow.Clear();
        foreach (var name in seen)
            _activeNow.Add(name);
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
        if (!_lastSeenUtc.TryGetValue(fateName, out var last))
            return null;

        var cycle = _observedCycleSeconds.TryGetValue(fateName, out var measured)
            ? measured
            : ExpectedCycleSeconds * 2; // each individual FATE alternates, so ~1h apart
        return cycle - (UtcNow() - last).TotalSeconds;
    }

    /// <summary>Whether the cycle for this FATE came from observation rather than the default.</summary>
    public bool CycleIsMeasured(string fateName) => _observedCycleSeconds.ContainsKey(fateName);

    /// <summary>Last time this FATE was seen up, or null.</summary>
    public DateTime? LastSeenUtc(string fateName) =>
        _lastSeenUtc.TryGetValue(fateName, out var t) ? t : null;
}
