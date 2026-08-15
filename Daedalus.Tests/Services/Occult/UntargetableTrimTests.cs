using System;
using Daedalus.Services.Occult;
using Xunit;

namespace Daedalus.Tests.Services.Occult;

/// <summary>
/// Trimming the untargetable mechanics — the Pages, Spheres, Beacons and Plumes that Occult Libra
/// can never reach, so they can never carry an element and only drag the coverage figures down.
/// </summary>
public sealed class UntargetableTrimTests
{
    private static OccultWeaknessEntry Entry(
        bool everTargetable = false,
        OccultElement elements = OccultElement.None,
        int sightings = 100,
        DateTime? lastSeen = null) => new()
        {
            NameId = 12345,
            Name = "Radiant Beacon",
            TerritoryId = 1252,
            MaxHp = 8_700_000,
            EverTargetable = everTargetable,
            Elements = elements,
            Sightings = sightings,
            LastSeenUtc = (lastSeen ?? new DateTime(2026, 8, 14, 0, 0, 0, DateTimeKind.Utc)).ToString("O"),
        };

    [Fact]
    public void ProvenUnreachableMechanic_IsTrimmed()
    {
        var e = Entry();
        Assert.True(ElementalWeaknessLog.IsMechanicObject(e));
        Assert.False(ElementalWeaknessLog.IsWorthKeeping(e));
    }

    /// <summary>
    /// The guard that matters. 129 of 273 rows carried 20+ sightings while only 30 had been seen
    /// since the flag shipped, so judging on sightings alone would delete real enemies whose
    /// EverTargetable was false purely because nothing ever wrote it.
    /// </summary>
    [Fact]
    public void NeverSeenSinceTheFlagExisted_IsUnknown_NotAVerdict()
    {
        var stale = Entry(sightings: 5000, lastSeen: new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.False(ElementalWeaknessLog.WasTargetabilityActuallyRecorded(stale));
        Assert.False(ElementalWeaknessLog.IsMechanicObject(stale));
        Assert.True(ElementalWeaknessLog.IsWorthKeeping(stale));
    }

    [Fact]
    public void UnparseableTimestamp_IsTreatedAsUnknown()
    {
        var e = Entry();
        e.LastSeenUtc = "";
        Assert.False(ElementalWeaknessLog.WasTargetabilityActuallyRecorded(e));
        Assert.True(ElementalWeaknessLog.IsWorthKeeping(e));
    }

    /// <summary>A glimpse is not evidence — the sighting floor still applies.</summary>
    [Fact]
    public void TooFewSightings_IsNotAVerdict()
    {
        var e = Entry(sightings: ElementalWeaknessLog.MinSightingsForTargetabilityVerdict - 1);
        Assert.False(ElementalWeaknessLog.IsMechanicObject(e));
        Assert.True(ElementalWeaknessLog.IsWorthKeeping(e));
    }

    /// <summary>
    /// A learned element is the expensive part of this table — it only appears when Libra reveals
    /// it — so it always wins, and its presence also proves the thing was reachable.
    /// </summary>
    [Fact]
    public void KnownElement_IsNeverTrimmed()
    {
        var e = Entry(elements: OccultElement.Lightning, sightings: 5000);
        Assert.False(ElementalWeaknessLog.IsMechanicObject(e));
        Assert.True(ElementalWeaknessLog.IsWorthKeeping(e));
    }

    /// <summary>
    /// Self-correcting: one targetable sighting is enough to keep it forever. This is what lets
    /// the trim be safe on a disputed row — if it really is an add, the next run restores it.
    /// </summary>
    [Fact]
    public void OneTargetableSighting_KeepsItForever()
    {
        var e = Entry(everTargetable: true, sightings: 5000);
        Assert.False(ElementalWeaknessLog.IsMechanicObject(e));
        Assert.True(ElementalWeaknessLog.IsWorthKeeping(e));
    }

    /// <summary>The cutoff is the version that introduced the flag, not an arbitrary date.</summary>
    [Fact]
    public void TrackingCutoff_IsTheVersionThatAddedTheFlag()
        => Assert.Equal(
            new DateTime(2026, 8, 10, 0, 0, 0, DateTimeKind.Utc),
            ElementalWeaknessLog.TargetabilityTrackedSinceUtc);
}
