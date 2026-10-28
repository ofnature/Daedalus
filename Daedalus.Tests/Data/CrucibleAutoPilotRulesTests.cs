using System;
using Daedalus.Data;
using Xunit;

namespace Daedalus.Tests.Data;

/// <summary>The Crucible autopilot's decisions: loot order, which prompts it answers, campsite pick, battle start.</summary>
public class CrucibleAutoPilotRulesTests
{
    [Theory]
    [InlineData(true, false, false, CrucibleLootStep.TakeItem)]
    [InlineData(true, true, false, CrucibleLootStep.TakeTokens)]
    [InlineData(false, false, false, CrucibleLootStep.TakeTokens)]
    [InlineData(false, false, true, CrucibleLootStep.Leave)]
    [InlineData(true, true, true, CrucibleLootStep.Leave)]
    public void LootIsItemThenTokensThenLeave(bool takeItems, bool itemTried, bool tokensTried, CrucibleLootStep expected)
        => Assert.Equal(expected, CrucibleAutoPilotRules.NextLootStep(takeItems, itemTried, tokensTried));

    /// <summary>Only the prompt its own click raised: the recorded wording, nothing else.</summary>
    [Theory]
    [InlineData("Take 203 territory tokens?", "Take ", true)]
    [InlineData("Some loot remains unclaimed. Leave it behind?", "Some loot remains unclaimed.", true)]
    [InlineData("Conclude purchasing and leave the shop? / You cannot return to the shop after leaving.", "Conclude purchasing and leave", true)]
    [InlineData("Rest and recover 45% of HP for you and your: / ・ Damselfly / Proceed?", "Rest ", true)]
    [InlineData("Rest alone while your familiars keep watch and recover 90% of HP?", "Rest ", true)] // field 2026-10-25: was left unanswered
    [InlineData("Take 203 territory tokens?", null, false)]                        // nothing expected
    [InlineData("Forfeit the board?", "Take ", false)]                             // a different prompt
    [InlineData("Discard changes and return to board selection?", "Discard", false)] // not a known prompt
    public void OnlyItsOwnPromptsAreConfirmed(string prompt, string? expected, bool confirm)
        => Assert.Equal(confirm, CrucibleAutoPilotRules.ShouldConfirm(prompt, expected));

    // --- spoils: take beast gear ---

    private static bool IsGear(int id) => id is 2 or 37; // Ring of Sacrifice, Astral Wristlet

    /// <summary>Every piece of gear is taken: still gear on offer after a take means another take.</summary>
    [Fact]
    public void MoreGearOnOfferIsTakenToo()
        => Assert.False(CrucibleAutoPilotRules.LootGearDone(gearOffered: true, takes: 1, lastTakeRefused: false));

    [Fact]
    public void NoGearLeftEndsTheGear()
        => Assert.True(CrucibleAutoPilotRules.LootGearDone(gearOffered: false, takes: 2, lastTakeRefused: false));

    /// <summary>A take that added nothing to the gear held was refused (e.g. full gear inventory): stop.</summary>
    [Fact]
    public void ARefusedTakeEndsTheGear()
        => Assert.True(CrucibleAutoPilotRules.LootGearDone(gearOffered: true, takes: 1, lastTakeRefused: true));

    [Fact]
    public void TheGearTakesAreCapped()
        => Assert.True(CrucibleAutoPilotRules.LootGearDone(gearOffered: true, takes: CrucibleAutoPilotRules.MaxGearTakes, lastTakeRefused: false));

    /// <summary>
    /// Recording 2026-10-25: a taken piece STAYS listed at its row. The next pick skips the rows already taken —
    /// otherwise it would "take" the same piece again and stop there.
    /// </summary>
    [Fact]
    public void ATakenRowIsSkipped()
        => Assert.Equal(2, CrucibleAutoPilotRules.PickGear([2, 78, 37], IsGear, new System.Collections.Generic.HashSet<int> { 0 }));

    [Fact]
    public void AllGearTakenLeavesNothingToPick()
        => Assert.Equal(-1, CrucibleAutoPilotRules.PickGear([2, 78, 37], IsGear, new System.Collections.Generic.HashSet<int> { 0, 2 }));

    [Fact]
    public void HeldGearFlagsAreFiveApartFrom78()
    {
        Assert.Equal(78, CrucibleAutoPilotRules.HeldGearFlagIndex(0));
        Assert.Equal(83, CrucibleAutoPilotRules.HeldGearFlagIndex(1));
    }

    /// <summary>The first gear row, wherever it sits — a potion first in the list is passed over.</summary>
    [Fact]
    public void TheGearRowIsTakenNotTheFirstRow()
        => Assert.Equal(1, CrucibleAutoPilotRules.PickGear([78, 37], IsGear));

    [Fact]
    public void NoGearOfferedMeansNoItem()
        => Assert.Equal(-1, CrucibleAutoPilotRules.PickGear([78, 129], IsGear));

    [Fact]
    public void AnEmptyRowIsNotAnOffer()
        => Assert.Equal(1, CrucibleAutoPilotRules.PickGear([0, 2], IsGear));

    /// <summary>
    /// The recorded offers: 77 (G2 beast potion) at 9, 10 (twisted headband) at 14 — the player's [4 | 1] took the
    /// headband. Field 2026-10-25: reading the HELD gear at 78+ as offers took a potion over the gear.
    /// </summary>
    [Fact]
    public void OffersAreTheIdsAt9And14()
    {
        Assert.Equal(9, CrucibleAutoPilotRules.LootItemIdIndex(0));
        Assert.Equal(14, CrucibleAutoPilotRules.LootItemIdIndex(1));
        Assert.Equal(1, CrucibleAutoPilotRules.PickGear([77, 10], id => id == 10));
    }

    /// <summary>The most hurt standing beast rests; a fallen one (0 HP) is passed over.</summary>
    [Fact]
    public void TheMostHurtStandingBeastRests()
        => Assert.Equal(2, CrucibleAutoPilotRules.CampsitePick([(500, 535), (0, 535), (100, 757), (700, 700)]));

    /// <summary>Field 2026-10-25: under half HP you rest alone (90%, not 45%) — your HP is the run.</summary>
    [Fact]
    public void UnderHalfHpYouRestAlone()
        => Assert.Equal(-1, CrucibleAutoPilotRules.CampsitePick([(500, 535), (100, 757)], selfHpPct: 0.13f));

    [Fact]
    public void AtHalfHpOrMoreTheMostHurtBeastShares()
        => Assert.Equal(1, CrucibleAutoPilotRules.CampsitePick([(500, 535), (100, 757)], selfHpPct: 0.6f));

    [Fact]
    public void NobodyStandingMeansNoCampsitePick()
        => Assert.Equal(-1, CrucibleAutoPilotRules.CampsitePick([(0, 535), (0, 757)]));

    [Fact]
    public void TheBattleStartsOnceTheHornsHaveStood()
    {
        Assert.True(CrucibleAutoPilotRules.ShouldStartBattle(7, 7, TimeSpan.FromSeconds(2), alreadyStarted: 0));
        Assert.False(CrucibleAutoPilotRules.ShouldStartBattle(7, 7, TimeSpan.FromSeconds(0.5), alreadyStarted: 0)); // too soon
        Assert.False(CrucibleAutoPilotRules.ShouldStartBattle(7, 0, TimeSpan.FromSeconds(9), alreadyStarted: 0));   // horns not set
        Assert.False(CrucibleAutoPilotRules.ShouldStartBattle(7, 6, TimeSpan.FromSeconds(9), alreadyStarted: 0));   // set for another battle
        Assert.False(CrucibleAutoPilotRules.ShouldStartBattle(7, 7, TimeSpan.FromSeconds(9), alreadyStarted: 7));   // already started
        Assert.False(CrucibleAutoPilotRules.ShouldStartBattle(0, 0, TimeSpan.FromSeconds(9), alreadyStarted: 0));   // no battle shown
    }

    // Effect texts as XBMItem column 12 has them (2026-10 client).
    private const string Belt = "Maximum HP +20%";
    private const string GenjiGreatshield = "Block Strength +30%\nEvasion +10%";
    private const string AstralWristlet = "Rapid Recast +15%\nMagic Vulnerability -8%";
    private const string RingOfCuring = "Recovers 25% of maximum HP at the end of battles.";
    private const string ThunderAxe = "Damage Dealt +30%\n5% chance to inflict Stun on self upon suffering damage.\nDuration: 3s";
    private const string IceShield = "Damage Taken -30%\nSlow +10%\n30% chance to absorb ice damage as HP.";

    /// <summary>
    /// Field 2026-10-25 (Saar, the coffer the Minerva session reported): Genji Greatshield, Belt of Constitution and
    /// Astral Wristlet. Every death was HP attrition, so the Belt's +20% Max HP is the pick.
    /// </summary>
    [Fact]
    public void TheCofferRanksSurvivalAboveTheRest()
    {
        var effects = new System.Collections.Generic.Dictionary<int, string>
            { [20] = GenjiGreatshield, [1] = Belt, [37] = AstralWristlet };
        Assert.Equal(1, CrucibleAutoPilotRules.PickTreasure([20, 1, 37, 0], id => effects.GetValueOrDefault(id)));
    }

    /// <summary>Scores read the stat lines: lower damage taken is good, a chance to stun yourself costs.</summary>
    [Fact]
    public void TreasureScoresReadTheEffectText()
    {
        Assert.Equal(20f, CrucibleAutoPilotRules.TreasureScore(Belt), 3);
        Assert.Equal(45f, CrucibleAutoPilotRules.TreasureScore(IceShield), 3);
        Assert.Equal(30f, CrucibleAutoPilotRules.TreasureScore(RingOfCuring), 3);
        Assert.True(CrucibleAutoPilotRules.TreasureScore(ThunderAxe) < CrucibleAutoPilotRules.TreasureScore(Belt));
        Assert.True(CrucibleAutoPilotRules.TreasureScore(AstralWristlet) > CrucibleAutoPilotRules.TreasureScore(GenjiGreatshield));
        Assert.Equal(0f, CrucibleAutoPilotRules.TreasureScore(""));
        Assert.Equal(0f, CrucibleAutoPilotRules.TreasureScore(null));
    }

    /// <summary>A thief's knife's second pick takes the next best offer; nothing left gives -1.</summary>
    [Fact]
    public void TheSecondPickSkipsWhatWasTaken()
    {
        var effects = new System.Collections.Generic.Dictionary<int, string>
            { [20] = GenjiGreatshield, [1] = Belt, [37] = AstralWristlet };
        string? Of(int id) => effects.GetValueOrDefault(id);
        Assert.Equal(2, CrucibleAutoPilotRules.PickTreasure([20, 1, 37, 0], Of, new System.Collections.Generic.HashSet<int> { 1 }));
        Assert.Equal(-1, CrucibleAutoPilotRules.PickTreasure([20, 1, 37, 0], Of, new System.Collections.Generic.HashSet<int> { 0, 1, 2 }));
        Assert.Equal(-1, CrucibleAutoPilotRules.PickTreasure([0, 0, 0, 0], Of));
    }

    private const string CrimsonRibbon = "Petrification Resistance +100%\nBlind Resistance +100%\nParalysis Resistance +100%";
    private const string AngelRobe = "Damage Taken -7%\nPoison Resistance +100%\n5% chance to grant one stack of Rehabilitation upon suffering damage.";

    /// <summary>
    /// Board 3's Guttler paralyses (uncleansable Thunderbolt), so a Crimson Ribbon outranks the Belt there; on Board 1
    /// it is worth nothing and the Belt wins.
    /// </summary>
    [Fact]
    public void TheBoardsResistanceOutranksPlainHp()
    {
        var effects = new System.Collections.Generic.Dictionary<int, string> { [1] = Belt, [18] = CrimsonRibbon };
        string? Of(int id) => effects.GetValueOrDefault(id);
        Assert.Equal(1, CrucibleAutoPilotRules.PickTreasure([1, 18, 0, 0], Of, null, CrucibleAutoPilotRules.WantedResistances(3)));
        Assert.Equal(0, CrucibleAutoPilotRules.PickTreasure([1, 18, 0, 0], Of, null, CrucibleAutoPilotRules.WantedResistances(1)));
        Assert.Equal(35f, CrucibleAutoPilotRules.TreasureScore(CrimsonRibbon, CrucibleAutoPilotRules.WantedResistances(3)), 3);
        Assert.Equal(0f, CrucibleAutoPilotRules.TreasureScore(CrimsonRibbon), 3);
    }

    /// <summary>First Master's Borgny is trivial with poison resistance: the Angel Robe scores its resistance there only.</summary>
    [Fact]
    public void PoisonResistanceCountsOnTheFirstMastersBoard()
    {
        Assert.Equal(10.5f + 35f, CrucibleAutoPilotRules.TreasureScore(AngelRobe, CrucibleAutoPilotRules.WantedResistances(4)), 3);
        Assert.Equal(10.5f, CrucibleAutoPilotRules.TreasureScore(AngelRobe, CrucibleAutoPilotRules.WantedResistances(3)), 3);
        Assert.Empty(CrucibleAutoPilotRules.WantedResistances(1));
        Assert.Contains("Doom", CrucibleAutoPilotRules.WantedResistances(2));
        Assert.Contains("Doom", CrucibleAutoPilotRules.WantedResistances(5));
    }

    /// <summary>Offers with no known effect still get picked (the first), so the coffer never stalls.</summary>
    [Fact]
    public void UnknownOffersStillGetAPick()
    {
        Assert.Equal(0, CrucibleAutoPilotRules.PickTreasure([25, 30, 9, 32], _ => null));
        Assert.Equal(6, CrucibleAutoPilotRules.TreasureItemIdIndex(0));
        Assert.Equal(21, CrucibleAutoPilotRules.TreasureItemIdIndex(3));
    }

    // --- edges and boundaries ---

    /// <summary>Gear still on offer is taken before leaving, even once the tokens have been tried.</summary>
    [Fact]
    public void GearStillComesFirstAfterTheTokens()
        => Assert.Equal(CrucibleLootStep.TakeItem, CrucibleAutoPilotRules.NextLootStep(takeItems: true, gearDone: false, tokensTried: true));

    /// <summary>With item taking off, whether the gear is done changes nothing: tokens, then leave.</summary>
    [Fact]
    public void WithItemsOffTheGearStateIsIgnored()
    {
        Assert.Equal(CrucibleLootStep.TakeTokens, CrucibleAutoPilotRules.NextLootStep(takeItems: false, gearDone: true, tokensTried: false));
        Assert.Equal(CrucibleLootStep.Leave, CrucibleAutoPilotRules.NextLootStep(takeItems: false, gearDone: true, tokensTried: true));
    }

    /// <summary>The take guard is a ceiling: one under it keeps going, at or past it stops.</summary>
    [Fact]
    public void TheGearGuardIsAtTheCap()
    {
        Assert.False(CrucibleAutoPilotRules.LootGearDone(gearOffered: true, takes: CrucibleAutoPilotRules.MaxGearTakes - 1, lastTakeRefused: false));
        Assert.True(CrucibleAutoPilotRules.LootGearDone(gearOffered: true, takes: CrucibleAutoPilotRules.MaxGearTakes + 1, lastTakeRefused: false));
    }

    /// <summary>Nothing taken yet and gear on offer: not done.</summary>
    [Fact]
    public void GearOnOfferBeforeAnyTakeIsNotDone()
        => Assert.False(CrucibleAutoPilotRules.LootGearDone(gearOffered: true, takes: 0, lastTakeRefused: false));

    [Fact]
    public void NoOffersMeansNoGearPick()
        => Assert.Equal(-1, CrucibleAutoPilotRules.PickGear([], IsGear));

    /// <summary>An empty row (id 0) is skipped before the gear lookup is asked about it.</summary>
    [Fact]
    public void AnEmptyRowIsNeverLookedUp()
        => Assert.Equal(2, CrucibleAutoPilotRules.PickGear([0, 0, 37], id =>
        {
            Assert.NotEqual(0, id);
            return IsGear(id);
        }));

    /// <summary>An empty set of taken rows is the same as none.</summary>
    [Fact]
    public void NoTakenRowsIsTheSameAsNone()
        => Assert.Equal(0, CrucibleAutoPilotRules.PickGear([2, 37], IsGear, new System.Collections.Generic.HashSet<int>()));

    /// <summary>Exactly half HP is not "under half": a beast still shares the rest.</summary>
    [Fact]
    public void AtExactlyHalfHpABeastStillShares()
        => Assert.Equal(1, CrucibleAutoPilotRules.CampsitePick([(500, 535), (100, 757)], selfHpPct: CrucibleAutoPilotRules.RestAloneBelowSelfHpPct));

    /// <summary>Two beasts equally hurt: the first listed rests.</summary>
    [Fact]
    public void EquallyHurtBeastsGoInListOrder()
        => Assert.Equal(0, CrucibleAutoPilotRules.CampsitePick([(100, 200), (50, 100), (300, 300)]));

    /// <summary>A beast whose max HP was not read (0) cannot be compared: it is passed over, and alone it is no pick.</summary>
    [Fact]
    public void ABeastWithUnknownMaxHpIsPassedOver()
    {
        Assert.Equal(1, CrucibleAutoPilotRules.CampsitePick([(100, 0), (500, 1000)]));
        Assert.Equal(-1, CrucibleAutoPilotRules.CampsitePick([(100, 0)]));
    }

    [Fact]
    public void AnEmptyTeamHasNoCampsitePick()
        => Assert.Equal(-1, CrucibleAutoPilotRules.CampsitePick([]));

    /// <summary>The wait is inclusive: at exactly 1.5 s the battle starts; a tick earlier it does not.</summary>
    [Fact]
    public void TheBattleStartsAtExactlyTheWait()
    {
        var wait = CrucibleAutoPilotRules.StartBattleAfter;
        Assert.True(CrucibleAutoPilotRules.ShouldStartBattle(7, 7, wait, alreadyStarted: 0));
        Assert.False(CrucibleAutoPilotRules.ShouldStartBattle(7, 7, wait - TimeSpan.FromTicks(1), alreadyStarted: 0));
    }

    /// <summary>Having started a different battle earlier does not block this one.</summary>
    [Fact]
    public void AnEarlierBattleDoesNotBlockTheNext()
        => Assert.True(CrucibleAutoPilotRules.ShouldStartBattle(8, 8, TimeSpan.FromSeconds(2), alreadyStarted: 7));

    // --- treasure scores: the text as it can come ---

    /// <summary>Lines split on CR as well as LF, so CRLF text scores the same as LF text.</summary>
    [Fact]
    public void CrLfEffectTextScoresLikeLf()
        => Assert.Equal(
            CrucibleAutoPilotRules.TreasureScore("Maximum HP +20%\nDamage Taken -10%"),
            CrucibleAutoPilotRules.TreasureScore("Maximum HP +20%\r\nDamage Taken -10%"), 3);

    [Fact]
    public void SurroundingSpacesAreTrimmed()
        => Assert.Equal(20f, CrucibleAutoPilotRules.TreasureScore("  Maximum HP +20%  "), 3);

    /// <summary>Stats are matched by their whole name: "Physical Damage Dealt" is not "Damage Dealt".</summary>
    [Fact]
    public void StatsAreMatchedByTheirWholeName()
    {
        Assert.Equal(5f, CrucibleAutoPilotRules.TreasureScore("Physical Damage Dealt +10%"), 3);
        Assert.Equal(1.5f, CrucibleAutoPilotRules.TreasureScore("Magic Damage Dealt +10%"), 3);
        Assert.Equal(6f, CrucibleAutoPilotRules.TreasureScore("Damage Dealt +10%"), 3);
    }

    /// <summary>A stat the table does not know (Slow) is worth nothing, not an error.</summary>
    [Fact]
    public void AnUnknownStatScoresNothing() => Assert.Equal(0f, CrucibleAutoPilotRules.TreasureScore("Slow +10%"));

    /// <summary>More damage taken is a drawback: the sign carries through the negative weight.</summary>
    [Fact]
    public void MoreDamageTakenCosts() => Assert.Equal(-15f, CrucibleAutoPilotRules.TreasureScore("Damage Taken +10%"), 3);

    /// <summary>Each "on self" chance costs on its own line.</summary>
    [Fact]
    public void EachChanceOnSelfCosts()
        => Assert.Equal(-16f, CrucibleAutoPilotRules.TreasureScore(
            "5% chance to inflict Stun on self upon suffering damage.\n5% chance to inflict Bind on self upon suffering damage."), 3);

    /// <summary>The revive line is worth a flat 40, whatever follows it.</summary>
    [Fact]
    public void TheReviveScoresFlat()
        => Assert.Equal(40f, CrucibleAutoPilotRules.TreasureScore("Revives beastmaster once upon being KO'd."), 3);

    /// <summary>An empty wanted list is the same as none: resistances score nothing.</summary>
    [Fact]
    public void AnEmptyWantedListIsLikeNone()
        => Assert.Equal(0f, CrucibleAutoPilotRules.TreasureScore(CrimsonRibbon, []), 3);

    /// <summary>A wanted resistance lowered by the item costs at the same weight.</summary>
    [Fact]
    public void ALoweredWantedResistanceCosts()
        => Assert.Equal(-17.5f, CrucibleAutoPilotRules.TreasureScore("Paralysis Resistance -50%", ["Paralysis"]), 3);

    /// <summary>The resistances each board calls for, and none for a board the research does not cover.</summary>
    [Fact]
    public void EachBoardsWantedResistances()
    {
        Assert.Equal(["Doom"], CrucibleAutoPilotRules.WantedResistances(2));
        Assert.Equal(["Paralysis"], CrucibleAutoPilotRules.WantedResistances(3));
        Assert.Equal(["Poison", "Paralysis"], CrucibleAutoPilotRules.WantedResistances(4));
        Assert.Equal(["Paralysis", "Doom", "Petrification"], CrucibleAutoPilotRules.WantedResistances(5));
        Assert.Empty(CrucibleAutoPilotRules.WantedResistances(0));
        Assert.Empty(CrucibleAutoPilotRules.WantedResistances(6));
        Assert.Empty(CrucibleAutoPilotRules.WantedResistances(-1));
    }

    /// <summary>Two offers that score the same: the first is picked.</summary>
    [Fact]
    public void ATreasureTieGoesToTheFirstOffer()
        => Assert.Equal(1, CrucibleAutoPilotRules.PickTreasure([0, 1, 2], _ => Belt));

    /// <summary>An offer that only costs is still picked when it is all there is: the coffer never stalls.</summary>
    [Fact]
    public void ANegativeOfferIsStillPickedWhenAlone()
        => Assert.Equal(0, CrucibleAutoPilotRules.PickTreasure([5], _ => "5% chance to inflict Stun on self upon suffering damage."));

    [Fact]
    public void NoOffersMeansNoTreasurePick()
        => Assert.Equal(-1, CrucibleAutoPilotRules.PickTreasure([], _ => Belt));

    /// <summary>A taken row that is not on offer changes nothing.</summary>
    [Fact]
    public void ATakenRowOutsideTheOffersIsIgnored()
        => Assert.Equal(0, CrucibleAutoPilotRules.PickTreasure([1], _ => Belt, new System.Collections.Generic.HashSet<int> { 7 }));

    /// <summary>Legendary takes a coffer's non-gear offer (Nameday Attire) even over better gear; gear only when all are gear.</summary>
    [Fact]
    public void LegendaryPicksNonGearFromACoffer()
    {
        var effects = new System.Collections.Generic.Dictionary<int, string> { [1] = Belt, [37] = AstralWristlet };
        string? Of(int id) => effects.GetValueOrDefault(id);
        bool IsGear(int id) => id is 1 or 37;
        Assert.Equal(2, CrucibleAutoPilotRules.PickTreasureAvoidingGear([1, 37, 136, 0], IsGear, Of)); // 136: a potion
        Assert.Equal(0, CrucibleAutoPilotRules.PickTreasureAvoidingGear([1, 37, 0, 0], IsGear, Of));   // only gear: the Belt
    }

    /// <summary>
    /// Legendary takes beast gear only where holding 10 pieces pays (Walking Armory: 0 on Board 1, 500 on Board 2, as
    /// the client's XBMContent rows say); on Board 1 none, for Nameday Attire.
    /// </summary>
    [Fact]
    public void LegendaryTakesGearOnlyWhereWalkingArmoryPays()
    {
        var bonuses1 = new int[33];
        var bonuses2 = new int[33];
        bonuses1[(int)CrucibleBonus.NamedayAttire] = bonuses2[(int)CrucibleBonus.NamedayAttire] = 750;
        bonuses2[(int)CrucibleBonus.WalkingArmory] = 500;
        Assert.False(CrucibleAutoPilotRules.LegendaryTakesGear(new CrucibleBoard(1, 1088, 1339, "First", 30, [6], bonuses1)));
        Assert.True(CrucibleAutoPilotRules.LegendaryTakesGear(new CrucibleBoard(2, 1089, 1340, "Second", 40, [13], bonuses2)));
    }
}
