using System;
using System.Collections.Generic;
using System.Numerics;
using Daedalus.Config;
using Daedalus.Services.Drawing;
using Daedalus.Services.Occult;
using Xunit;

namespace Daedalus.Tests.Services.Occult;

/// <summary>
/// The chest ledger collects evidence rather than predicting from it — so the merge rules are
/// the whole feature. Getting them wrong means either a ledger full of duplicates or one that
/// quietly forgets a tier it had already identified.
/// </summary>
public sealed class ChestLedgerTests
{
    private const ushort Zone = 1252;
    private static readonly DateTime T0 = new(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Record_AddsAnUnseenSpot()
    {
        var ledger = new List<ChestLedgerEntry>();

        Assert.True(ChestLedger.Record(ledger, Zone, new Vector3(10, 0, 10), TreasureTier.Bronze, T0));

        var entry = Assert.Single(ledger);
        Assert.Equal("Bronze", entry.Tier);
        Assert.Equal(1, entry.TimesSeen);
        Assert.Equal(Zone, entry.Zone);
    }

    /// <summary>A chest sits there for minutes and Update runs every frame — no duplicates.</summary>
    [Fact]
    public void Record_MergesSightingsAtTheSameSpot()
    {
        var ledger = new List<ChestLedgerEntry>();
        ChestLedger.Record(ledger, Zone, new Vector3(10, 0, 10), TreasureTier.Bronze, T0);
        ChestLedger.Record(ledger, Zone, new Vector3(11, 0, 10), TreasureTier.Bronze, T0.AddSeconds(1));

        Assert.Single(ledger);
    }

    /// <summary>Re-counting every frame would make TimesSeen a frame counter, not a sample count.</summary>
    [Fact]
    public void Record_DoesNotRecountWithinTheSameMinute()
    {
        var ledger = new List<ChestLedgerEntry>();
        ChestLedger.Record(ledger, Zone, new Vector3(10, 0, 10), TreasureTier.Bronze, T0);
        ChestLedger.Record(ledger, Zone, new Vector3(10, 0, 10), TreasureTier.Bronze, T0.AddSeconds(30));

        Assert.Equal(1, ledger[0].TimesSeen);
    }

    [Fact]
    public void Record_CountsAGenuinelyLaterSighting()
    {
        var ledger = new List<ChestLedgerEntry>();
        ChestLedger.Record(ledger, Zone, new Vector3(10, 0, 10), TreasureTier.Bronze, T0);
        ChestLedger.Record(ledger, Zone, new Vector3(10, 0, 10), TreasureTier.Bronze, T0.AddMinutes(5));

        Assert.Equal(2, ledger[0].TimesSeen);
    }

    [Fact]
    public void Record_KeepsSpotsInDifferentZonesApart()
    {
        var ledger = new List<ChestLedgerEntry>();
        ChestLedger.Record(ledger, Zone, new Vector3(10, 0, 10), TreasureTier.Bronze, T0);
        ChestLedger.Record(ledger, 1278, new Vector3(10, 0, 10), TreasureTier.Bronze, T0);

        Assert.Equal(2, ledger.Count);
    }

    [Fact]
    public void Record_KeepsDistantSpotsApart()
    {
        var ledger = new List<ChestLedgerEntry>();
        ChestLedger.Record(ledger, Zone, new Vector3(10, 0, 10), TreasureTier.Bronze, T0);
        ChestLedger.Record(ledger, Zone, new Vector3(40, 0, 10), TreasureTier.Bronze, T0);

        Assert.Equal(2, ledger.Count);
    }

    /// <summary>
    /// An unrecognised model reads as Unknown. It must never overwrite a tier we already
    /// identified, or one bad read erases good evidence.
    /// </summary>
    [Fact]
    public void Record_UnknownNeverOverwritesAKnownTier()
    {
        var ledger = new List<ChestLedgerEntry>();
        ChestLedger.Record(ledger, Zone, new Vector3(10, 0, 10), TreasureTier.Gold, T0);
        ChestLedger.Record(ledger, Zone, new Vector3(10, 0, 10), TreasureTier.Unknown, T0.AddMinutes(5));

        Assert.Equal("Gold", ledger[0].Tier);
    }

    [Fact]
    public void Record_UpgradesAnUnknownSpotOnceIdentified()
    {
        var ledger = new List<ChestLedgerEntry>();
        ChestLedger.Record(ledger, Zone, new Vector3(10, 0, 10), TreasureTier.Unknown, T0);
        ChestLedger.Record(ledger, Zone, new Vector3(10, 0, 10), TreasureTier.Silver, T0.AddMinutes(5));

        Assert.Equal("Silver", ledger[0].Tier);
    }

    [Fact]
    public void Record_StopsAtTheEntryCap()
    {
        var ledger = new List<ChestLedgerEntry>();
        for (var i = 0; i < ChestLedger.MaxEntries; i++)
            ChestLedger.Record(ledger, Zone, new Vector3(i * 10, 0, 0), TreasureTier.Bronze, T0);

        Assert.False(ChestLedger.Record(ledger, Zone, new Vector3(99999, 0, 0), TreasureTier.Gold, T0));
        Assert.Equal(ChestLedger.MaxEntries, ledger.Count);
    }

    [Fact]
    public void Record_IsSafeOnANullLedger()
    {
        Assert.False(ChestLedger.Record(null!, Zone, Vector3.Zero, TreasureTier.Bronze, T0));
    }
}
