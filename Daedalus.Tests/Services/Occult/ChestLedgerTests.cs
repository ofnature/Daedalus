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

    // ── A spot that produces more than one tier ──

    /// <summary>
    /// Field-observed: the same location reporting different qualities. Overwriting the tier
    /// hid that entirely — the distribution is the whole point.
    /// </summary>
    [Fact]
    public void Record_KeepsEveryTierSeenAtASpot()
    {
        var ledger = new List<ChestLedgerEntry>();
        ChestLedger.Record(ledger, Zone, new Vector3(10, 0, 10), TreasureTier.Bronze, T0);
        ChestLedger.Record(ledger, Zone, new Vector3(10, 0, 10), TreasureTier.Silver, T0.AddMinutes(5));
        ChestLedger.Record(ledger, Zone, new Vector3(10, 0, 10), TreasureTier.Bronze, T0.AddMinutes(10));

        var entry = Assert.Single(ledger);
        Assert.Equal(2, entry.TierCounts["Bronze"]);
        Assert.Equal(1, entry.TierCounts["Silver"]);
        Assert.True(ChestLedger.IsMixedTier(entry));
    }

    [Fact]
    public void Record_TierFieldTracksTheMostObservedTier()
    {
        var ledger = new List<ChestLedgerEntry>();
        ChestLedger.Record(ledger, Zone, new Vector3(10, 0, 10), TreasureTier.Silver, T0);
        ChestLedger.Record(ledger, Zone, new Vector3(10, 0, 10), TreasureTier.Bronze, T0.AddMinutes(5));
        ChestLedger.Record(ledger, Zone, new Vector3(10, 0, 10), TreasureTier.Bronze, T0.AddMinutes(10));

        Assert.Equal("Bronze", ledger[0].Tier);
    }

    /// <summary>
    /// A tier observation belongs to a SIGHTING. Counting it on the open event too made every
    /// looted spot read "Bronze: 2" off one chest while an unlooted one read "Bronze: 1" —
    /// a distribution skewed by exactly the open count (caught in field data 2026-08-01).
    /// </summary>
    /// <summary>
    /// World chests are rolled per player on instance entry, so a chest cannot change tier while
    /// you stand in the instance that spawned it. The caller counts a spot ONCE per visit and
    /// passes countTier:false for every later sighting — otherwise one roll masquerades as a
    /// dozen independent samples and every spot looks rock-solid consistent.
    /// </summary>
    [Fact]
    public void Record_CountsOneTierObservationPerVisit()
    {
        var ledger = new List<ChestLedgerEntry>();
        ChestLedger.Record(ledger, Zone, new Vector3(10, 0, 10), TreasureTier.Bronze, T0, countTier: true);
        ChestLedger.Record(ledger, Zone, new Vector3(10, 0, 10), TreasureTier.Bronze, T0.AddSeconds(14),
            opened: true, countTier: false);
        ChestLedger.Record(ledger, Zone, new Vector3(10, 0, 10), TreasureTier.Bronze, T0.AddMinutes(5),
            countTier: false);

        var entry = Assert.Single(ledger);
        Assert.Equal(1, entry.TimesOpened);
        Assert.Equal(1, entry.TierCounts["Bronze"]);
    }

    /// <summary>A later visit re-rolls the chest, so that observation does count.</summary>
    [Fact]
    public void Record_ASecondVisitAddsAnObservation()
    {
        var ledger = new List<ChestLedgerEntry>();
        ChestLedger.Record(ledger, Zone, new Vector3(10, 0, 10), TreasureTier.Bronze, T0, countTier: true);
        ChestLedger.Record(ledger, Zone, new Vector3(10, 0, 10), TreasureTier.Silver, T0.AddHours(1), countTier: true);

        var entry = Assert.Single(ledger);
        Assert.Equal(1, entry.TierCounts["Bronze"]);
        Assert.Equal(1, entry.TierCounts["Silver"]);
        Assert.True(ChestLedger.IsMixedTier(entry));
    }

    // ── Source and hunt tagging ──

    [Fact]
    public void Record_TagsTheCofferSource()
    {
        var ledger = new List<ChestLedgerEntry>();
        ChestLedger.Record(ledger, Zone, new Vector3(10, 0, 10), TreasureTier.Gold, T0, source: "EventObj");

        Assert.Equal("EventObj", ledger[0].Source);
    }

    /// <summary>
    /// The discriminator the pot-coffer question turns on — a hunt coffer is otherwise identical
    /// to a world coffer. Sticky, because a spot that has ever produced one stays a candidate.
    /// </summary>
    [Fact]
    public void Record_HuntTagIsStickyOnceSet()
    {
        var ledger = new List<ChestLedgerEntry>();
        ChestLedger.Record(ledger, Zone, new Vector3(10, 0, 10), TreasureTier.Gold, T0,
            duringTreasureHunt: true, source: "EventObj");
        ChestLedger.Record(ledger, Zone, new Vector3(10, 0, 10), TreasureTier.Gold, T0.AddHours(1),
            duringTreasureHunt: false, countTier: true);

        Assert.True(ledger[0].FoundDuringTreasureHunt);
    }

    /// <summary>
    /// The trap: the hunt flag lands on ANY coffer seen while the hunt is up, so an ordinary
    /// per-player chest walked past mid-hunt looks like a candidate. Both halves are required, or
    /// the location predictor gets seeded with spawns that can never be the answer.
    /// </summary>
    [Fact]
    public void IsPotHuntCandidate_RejectsANormalChestFoundMidHunt()
    {
        var normalChestDuringHunt = new ChestLedgerEntry
        {
            Zone = Zone,
            Source = ChestLedger.SourceTreasure,
            FoundDuringTreasureHunt = true,
        };

        Assert.False(ChestLedger.IsPotHuntCandidate(normalChestDuringHunt));
    }

    [Fact]
    public void IsPotHuntCandidate_AcceptsAPotCofferFoundDuringAHunt()
    {
        var potCoffer = new ChestLedgerEntry
        {
            Zone = Zone,
            Source = ChestLedger.SourceEventObj,
            FoundDuringTreasureHunt = true,
        };

        Assert.True(ChestLedger.IsPotHuntCandidate(potCoffer));
    }

    [Fact]
    public void IsPotHuntCandidate_RejectsAPotCofferSeenOutsideAHunt()
    {
        var potCoffer = new ChestLedgerEntry
        {
            Zone = Zone,
            Source = ChestLedger.SourceEventObj,
            FoundDuringTreasureHunt = false,
        };

        Assert.False(ChestLedger.IsPotHuntCandidate(potCoffer));
        Assert.False(ChestLedger.IsPotHuntCandidate(null));
    }

    /// <summary>
    /// Carrot chests are expected to be EventObj as well, so kind and hunt flag alone will not
    /// tell them apart from a pot coffer. The BaseId is recorded now so that separation is
    /// possible later from data already gathered.
    /// </summary>
    [Fact]
    public void Record_KeepsTheBaseIdSoCofferTypesStaySeparable()
    {
        var ledger = new List<ChestLedgerEntry>();
        ChestLedger.Record(ledger, Zone, new Vector3(10, 0, 10), TreasureTier.Gold, T0,
            duringTreasureHunt: true, source: ChestLedger.SourceEventObj, baseId: 2014741);

        Assert.Equal(2014741u, ledger[0].BaseId);
    }

    [Fact]
    public void Merge_FillsInABaseIdTheOtherToonCaptured()
    {
        var mine = new List<ChestLedgerEntry>();
        ChestLedger.Record(mine, Zone, new Vector3(10, 0, 10), TreasureTier.Gold, T0);

        var theirs = new List<ChestLedgerEntry>();
        ChestLedger.Record(theirs, Zone, new Vector3(10, 0, 10), TreasureTier.Gold, T0,
            source: ChestLedger.SourceEventObj, baseId: 2014741);

        ChestLedger.Merge(mine, theirs);

        Assert.Equal(2014741u, mine[0].BaseId);
    }

    /// <summary>
    /// Carrot spots share the ledger's plumbing but are not chests — and must never be mistaken
    /// for pot-hunt candidates, even when logged mid-hunt.
    /// </summary>
    [Fact]
    public void IsPotHuntCandidate_RejectsACarrotSpot()
    {
        var carrot = new ChestLedgerEntry
        {
            Zone = Zone,
            Source = ChestLedger.SourceCarrot,
            FoundDuringTreasureHunt = true,
            BaseId = 2010139,
        };

        Assert.False(ChestLedger.IsPotHuntCandidate(carrot));
    }

    [Fact]
    public void Record_StoresCarrotSpotsWithoutATier()
    {
        var ledger = new List<ChestLedgerEntry>();
        ChestLedger.Record(ledger, Zone, new Vector3(10, 0, 10), TreasureTier.Unknown, T0,
            countTier: false, source: ChestLedger.SourceCarrot, baseId: 2010139);

        var entry = Assert.Single(ledger);
        Assert.Equal(ChestLedger.SourceCarrot, entry.Source);
        Assert.Equal(2010139u, entry.BaseId);
        Assert.Empty(entry.TierCounts);
    }

    [Fact]
    public void Merge_CarriesTheHuntTagAndSourceAcrossToons()
    {
        var mine = new List<ChestLedgerEntry>();
        ChestLedger.Record(mine, Zone, new Vector3(10, 0, 10), TreasureTier.Gold, T0);

        var theirs = new List<ChestLedgerEntry>();
        ChestLedger.Record(theirs, Zone, new Vector3(10, 0, 10), TreasureTier.Gold, T0,
            duringTreasureHunt: true, source: "EventObj");

        ChestLedger.Merge(mine, theirs);

        Assert.True(mine[0].FoundDuringTreasureHunt);
        Assert.Equal("EventObj", mine[0].Source);
    }

    /// <summary>Entries written before TierCounts existed must still join the distribution.</summary>
    [Fact]
    public void BackfillTierCounts_FoldsAnOldScalarTierIn()
    {
        var old = new ChestLedgerEntry { Zone = Zone, Tier = "Gold", TimesSeen = 3 };

        ChestLedger.BackfillTierCounts(old);

        Assert.Equal(3, old.TierCounts["Gold"]);
    }

    [Fact]
    public void BackfillTierCounts_LeavesUnknownAndExistingCountsAlone()
    {
        var unknown = new ChestLedgerEntry { Zone = Zone, Tier = "Unknown", TimesSeen = 2 };
        ChestLedger.BackfillTierCounts(unknown);
        Assert.Empty(unknown.TierCounts);

        var already = new ChestLedgerEntry { Zone = Zone, Tier = "Bronze", TimesSeen = 5 };
        already.TierCounts["Silver"] = 1;
        ChestLedger.BackfillTierCounts(already);
        Assert.Single(already.TierCounts);
    }

    [Fact]
    public void IsMixedTier_IsFalseForAConsistentSpot()
    {
        var ledger = new List<ChestLedgerEntry>();
        ChestLedger.Record(ledger, Zone, new Vector3(10, 0, 10), TreasureTier.Gold, T0);
        ChestLedger.Record(ledger, Zone, new Vector3(10, 0, 10), TreasureTier.Gold, T0.AddMinutes(5));

        Assert.False(ChestLedger.IsMixedTier(ledger[0]));
    }

    /// <summary>Two toons disagreeing about a spot is evidence, not a conflict to resolve.</summary>
    [Fact]
    public void Merge_SumsTierDistributionsAcrossToons()
    {
        var mine = new List<ChestLedgerEntry>();
        ChestLedger.Record(mine, Zone, new Vector3(10, 0, 10), TreasureTier.Bronze, T0);

        var theirs = new List<ChestLedgerEntry>();
        ChestLedger.Record(theirs, Zone, new Vector3(10, 0, 10), TreasureTier.Gold, T0);

        ChestLedger.Merge(mine, theirs);

        var entry = Assert.Single(mine);
        Assert.Equal(1, entry.TierCounts["Bronze"]);
        Assert.Equal(1, entry.TierCounts["Gold"]);
        Assert.True(ChestLedger.IsMixedTier(entry));
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

    // ── Merging two toons' ledgers ──

    private static ChestLedgerEntry Entry(float x, string tier, int seen, int opened, long first, long last) =>
        new() { Zone = Zone, X = x, Y = 0, Z = 0, Tier = tier, TimesSeen = seen, TimesOpened = opened,
                FirstSeenUnixSeconds = first, LastSeenUnixSeconds = last };

    [Fact]
    public void Merge_SumsCountsForTheSameSpot()
    {
        var mine = new List<ChestLedgerEntry> { Entry(10, "Bronze", 3, 1, 100, 200) };
        var theirs = new List<ChestLedgerEntry> { Entry(11, "Bronze", 2, 2, 150, 300) };

        var added = ChestLedger.Merge(mine, theirs);

        Assert.Equal(0, added);
        Assert.Single(mine);
        Assert.Equal(5, mine[0].TimesSeen);
        Assert.Equal(3, mine[0].TimesOpened);
    }

    [Fact]
    public void Merge_KeepsEarliestFirstSeenAndLatestLastSeen()
    {
        var mine = new List<ChestLedgerEntry> { Entry(10, "Bronze", 1, 0, 500, 600) };
        var theirs = new List<ChestLedgerEntry> { Entry(10, "Bronze", 1, 0, 100, 900) };

        ChestLedger.Merge(mine, theirs);

        Assert.Equal(100, mine[0].FirstSeenUnixSeconds);
        Assert.Equal(900, mine[0].LastSeenUnixSeconds);
    }

    [Fact]
    public void Merge_AddsSpotsTheOtherToonFoundFirst()
    {
        var mine = new List<ChestLedgerEntry> { Entry(10, "Bronze", 1, 0, 100, 100) };
        var theirs = new List<ChestLedgerEntry> { Entry(500, "Gold", 1, 1, 100, 100) };

        var added = ChestLedger.Merge(mine, theirs);

        Assert.Equal(1, added);
        Assert.Equal(2, mine.Count);
    }

    /// <summary>One toon may never have identified the model. A known tier must win.</summary>
    [Fact]
    public void Merge_LetsAKnownTierFillAnUnknownOne()
    {
        var mine = new List<ChestLedgerEntry> { Entry(10, "Unknown", 1, 0, 100, 100) };
        var theirs = new List<ChestLedgerEntry> { Entry(10, "Gold", 1, 1, 100, 100) };

        ChestLedger.Merge(mine, theirs);

        Assert.Equal("Gold", mine[0].Tier);
    }

    [Fact]
    public void Merge_UnknownNeverDowngradesAKnownTier()
    {
        var mine = new List<ChestLedgerEntry> { Entry(10, "Gold", 1, 1, 100, 100) };
        var theirs = new List<ChestLedgerEntry> { Entry(10, "Unknown", 1, 0, 100, 100) };

        ChestLedger.Merge(mine, theirs);

        Assert.Equal("Gold", mine[0].Tier);
    }

    [Fact]
    public void Merge_IsSafeOnNulls()
    {
        Assert.Equal(0, ChestLedger.Merge(null!, new List<ChestLedgerEntry>()));
        Assert.Equal(0, ChestLedger.Merge(new List<ChestLedgerEntry>(), null));
    }

    [Fact]
    public void SanitizeForFileName_MakesACharacterNameSafe()
    {
        Assert.Equal("Aria_Ishere", ChestLedger.SanitizeForFileName("Aria Ishere"));
        Assert.DoesNotContain('/', ChestLedger.SanitizeForFileName("A/B"));
    }

    // ── Pickup detection ──

    [Fact]
    public void Record_CountsAnOpenSeparatelyFromASighting()
    {
        var ledger = new List<ChestLedgerEntry>();
        ChestLedger.Record(ledger, Zone, new Vector3(10, 0, 10), TreasureTier.Gold, T0);
        Assert.Equal(0, ledger[0].TimesOpened);

        ChestLedger.Record(ledger, Zone, new Vector3(10, 0, 10), TreasureTier.Gold, T0.AddSeconds(5), opened: true);

        Assert.Equal(1, ledger[0].TimesOpened);
        Assert.Equal(1, ledger[0].TimesSeen); // still the same visit
    }

    [Fact]
    public void Record_FirstSightingCanItselfBeAnOpen()
    {
        var ledger = new List<ChestLedgerEntry>();
        ChestLedger.Record(ledger, Zone, new Vector3(10, 0, 10), TreasureTier.Silver, T0, opened: true);

        Assert.Equal(1, ledger[0].TimesOpened);
    }

    /// <summary>
    /// A coffer reads Opened for as long as it lingers, so counting the state rather than the
    /// transition would count one pickup once per frame.
    /// </summary>
    [Theory]
    [InlineData(false, true, true)]   // just opened
    [InlineData(false, false, false)] // still shut
    [InlineData(true, true, false)]   // already counted, still sitting there
    [InlineData(true, false, false)]  // flags went backwards; not an open
    public void BecameOpened_OnlyFiresOnTheTransition(bool previously, bool now, bool expected)
    {
        Assert.Equal(expected, ChestLedger.BecameOpened(previously, now));
    }

    [Theory]
    [InlineData(0f, true)]
    [InlineData(6f, true)]
    [InlineData(6.1f, false)]
    [InlineData(40f, false)]
    public void DespawnedIntoPickup_OnlyCountsAVanishNextToYou(float distance, bool expected)
    {
        Assert.Equal(expected, ChestLedger.DespawnedIntoPickup(distance));
    }

    // ── Name-based tiering (pot coffers are EventObj and name themselves) ──

    [Theory]
    [InlineData("Gold Coffer", TreasureTier.Gold)]
    [InlineData("Silver Coffer", TreasureTier.Silver)]
    [InlineData("Bronze Coffer", TreasureTier.Bronze)]
    [InlineData("gold coffer", TreasureTier.Gold)]
    [InlineData("Treasure Coffer", TreasureTier.Unknown)]
    [InlineData("", TreasureTier.Unknown)]
    [InlineData(null, TreasureTier.Unknown)]
    public void TierFromCofferName_ReadsTheTierOffTheName(string? name, TreasureTier expected)
    {
        Assert.Equal(expected, ChestLedger.TierFromCofferName(name));
    }

    [Theory]
    [InlineData("Gold Coffer", true)]
    [InlineData("Bronze Coffer", true)]
    [InlineData("Destination", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsCofferName_SeparatesCoffersFromOtherEventObjects(string? name, bool expected)
    {
        Assert.Equal(expected, ChestLedger.IsCofferName(name));
    }
}
