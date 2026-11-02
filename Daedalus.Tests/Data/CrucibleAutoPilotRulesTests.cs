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
    [InlineData("Purchase the g3 beast potion?", "Purchase the ", true)]
    [InlineData("Purchase the lily simular and feed it to your buffalo?", "Purchase the ", false)] // a feed picks a beast
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

    /// <summary>Legendary takes no beast gear on any board (Nameday Attire): no run reached Walking Armory's ten from free loot.</summary>
    [Fact]
    public void LegendaryTakesNoGear()
        => Assert.False(CrucibleAutoPilotRules.LegendaryTakesGear);

    /// <summary>Legendary drinks at 20% while Economic Ethos holds, at 45% once an item has been used.</summary>
    [Fact]
    public void LegendaryDrinksEarlierOnceEthosIsGone()
    {
        Assert.Equal(CrucibleAutoPilotRules.LegendaryPotionAtHpPct, CrucibleAutoPilotRules.LegendaryDrinkAtHpPct(itemUsedOnBoard: false));
        Assert.Equal(0.45f, CrucibleAutoPilotRules.LegendaryDrinkAtHpPct(itemUsedOnBoard: true));
        uint[] held = [243139];
        Assert.Equal(-1, CrucibleAutoPilotRules.PickPotion(held, 0.40f, CrucibleAutoPilotRules.LegendaryDrinkAtHpPct(false)));
        Assert.Equal(0, CrucibleAutoPilotRules.PickPotion(held, 0.40f, CrucibleAutoPilotRules.LegendaryDrinkAtHpPct(true)));
    }

    /// <summary>The final boss top-off: Legendary, Ethos gone, not while ten held items still pay Habitual Hoarder.</summary>
    [Fact]
    public void TheFinalBossTopOffWaitsForEthosAndHoarder()
    {
        Assert.True(CrucibleAutoPilotRules.TopsOffForTheEnd(true, finalBoss: true, itemUsedOnBoard: true, hoarderPays: true, itemsHeld: 8));
        Assert.True(CrucibleAutoPilotRules.TopsOffForTheEnd(true, true, true, hoarderPays: false, itemsHeld: 10));
        Assert.False(CrucibleAutoPilotRules.TopsOffForTheEnd(true, true, true, hoarderPays: true, itemsHeld: 10));
        Assert.False(CrucibleAutoPilotRules.TopsOffForTheEnd(true, true, itemUsedOnBoard: false, false, 5));
        Assert.False(CrucibleAutoPilotRules.TopsOffForTheEnd(true, finalBoss: false, true, false, 5));
        Assert.False(CrucibleAutoPilotRules.TopsOffForTheEnd(legendary: false, true, true, false, 5));
    }

    /// <summary>Top-off picks the strongest potion that does not overheal: G4 (50%) at 45%, G3 (36%) at 60%, none at 90%.</summary>
    [Fact]
    public void TopOffNeverOverheals()
    {
        uint[] held = [243137, 243138, 243139]; // G2 23%, G3 36%, G4 50%
        Assert.Equal(2, CrucibleAutoPilotRules.PickTopOff(held, 0.45f));
        Assert.Equal(1, CrucibleAutoPilotRules.PickTopOff(held, 0.60f));
        Assert.Equal(0, CrucibleAutoPilotRules.PickTopOff(held, 0.75f));
        Assert.Equal(-1, CrucibleAutoPilotRules.PickTopOff(held, 0.90f));
        Assert.Equal(-1, CrucibleAutoPilotRules.PickTopOff(held, 0f));
    }

    /// <summary>Spoils: gear first, then Crucible items (never feed), only while fewer than ten items are held.</summary>
    [Fact]
    public void SpoilsTakeGearThenPotionsUpToTen()
    {
        bool Gear(int id) => id == 1;
        bool Item(int id) => id is 136 or 140;
        Assert.Equal(1, CrucibleAutoPilotRules.PickLoot([136, 1, 140, 0], Gear, Item, takeGear: true, takeItems: true, itemsHeld: 0));
        Assert.Equal(0, CrucibleAutoPilotRules.PickLoot([136, 1, 140, 0], Gear, Item, takeGear: false, takeItems: true, itemsHeld: 0));
        Assert.Equal(2, CrucibleAutoPilotRules.PickLoot([136, 1, 140, 0], Gear, Item, false, true, 0, new System.Collections.Generic.HashSet<int> { 0 }));
        Assert.Equal(-1, CrucibleAutoPilotRules.PickLoot([136, 1, 140, 0], Gear, Item, false, true, itemsHeld: 10));
        Assert.Equal(-1, CrucibleAutoPilotRules.PickLoot([136, 0, 0, 0], Gear, Item, true, false, 0));
        Assert.Equal(-1, CrucibleAutoPilotRules.PickLoot([999, 0, 0, 0], Gear, Item, true, true, 0)); // feed: neither
    }

    /// <summary>The held Crucible items' rows sit at 30 + 5·k in the spoils window.</summary>
    [Fact]
    public void HeldItemSlotsAreEveryFifthValueFrom30()
    {
        Assert.Equal(30, CrucibleAutoPilotRules.HeldItemRowIndex(0));
        Assert.Equal(75, CrucibleAutoPilotRules.HeldItemRowIndex(9));
    }

    /// <summary>At 40% or less the strongest held potion is drunk; above it, or with none held, nothing.</summary>
    [Fact]
    public void PotionsAreDrunkAtFortyPercentStrongestFirst()
    {
        uint[] held = [0, 243136, 0, 243138, 243141, 0, 0, 0, 0, 0]; // G1 potion, G3 potion, G2 ash
        Assert.Equal(3, CrucibleAutoPilotRules.PickPotion(held, 0.23f));   // G3 (36%) over G2 ash (25%)
        Assert.Equal(3, CrucibleAutoPilotRules.PickPotion(held, 0.40f));
        Assert.Equal(-1, CrucibleAutoPilotRules.PickPotion(held, 0.41f));
        Assert.Equal(-1, CrucibleAutoPilotRules.PickPotion(held, 0f));    // dead: nothing to drink for
        Assert.Equal(-1, CrucibleAutoPilotRules.PickPotion([0, 243204, 0], 0.2f)); // no potion (another item) held
    }

    /// <summary>A pressed potion counts as drunk only once it leaves the HUD; one still there after 2.5s is tried again.</summary>
    [Fact]
    public void APotionCountsOnlyOnceItLeavesTheHud()
    {
        uint[] held = [243138, 0, 243138, 243141];
        Assert.Equal(2, CrucibleAutoPilotRules.CountHeld(held, 243138));
        Assert.Equal(0, CrucibleAutoPilotRules.CountHeld(held, 243136));
        Assert.True(CrucibleAutoPilotRules.PotionOutcome(2, 1, 0.4));    // gone: drunk
        Assert.Null(CrucibleAutoPilotRules.PotionOutcome(2, 2, 1.0));    // still there: wait
        Assert.False(CrucibleAutoPilotRules.PotionOutcome(2, 2, 2.5));   // never went off: try again
        Assert.True(CrucibleAutoPilotRules.PotionOutcome(1, 0, 9.0));    // late but gone still counts
    }

    /// <summary>The HUD's further values: usable flag at 10 + 5·k, gear held at 60 + 5·k, campsite feed counter per row.</summary>
    [Fact]
    public void HudAndCampIndices()
    {
        Assert.Equal(10, CrucibleAutoPilotRules.HudItemUsableIndex(0));
        Assert.Equal(55, CrucibleAutoPilotRules.HudItemUsableIndex(9));
        Assert.Equal(60, CrucibleAutoPilotRules.HudGearHeldIndex(0));
        Assert.Equal(105, CrucibleAutoPilotRules.HudGearHeldIndex(9));
        Assert.Equal(78, CrucibleAutoPilotRules.PetPartyFedIndex(0));
        Assert.Equal(155, CrucibleAutoPilotRules.PetPartyFedIndex(1));
    }

    /// <summary>Tokens are read from text; only a fall is spending.</summary>
    [Fact]
    public void TokensAreReadAndOnlyAFallIsSpent()
    {
        Assert.Equal(1234, CrucibleAutoPilotRules.ParseTokens("1,234"));
        Assert.Equal(0, CrucibleAutoPilotRules.ParseTokens("0"));
        Assert.Null(CrucibleAutoPilotRules.ParseTokens(""));
        Assert.Equal(0, CrucibleAutoPilotRules.TokensSpent(null, 500));  // first reading
        Assert.Equal(0, CrucibleAutoPilotRules.TokensSpent(500, 900));   // spoils paid out
        Assert.Equal(300, CrucibleAutoPilotRules.TokensSpent(900, 600)); // a shop purchase
    }

    /// <summary>A slot the HUD says cannot be used right now is not offered to the potion pick.</summary>
    [Fact]
    public void AnUnusableSlotIsNotPicked()
    {
        uint[] held = [243138, 0, 243137];
        var both = CrucibleAutoPilotRules.UsableOnly(held, [true, true, true]);
        Assert.Equal(0, CrucibleAutoPilotRules.PickPotion(both, 0.3f));
        var firstBusy = CrucibleAutoPilotRules.UsableOnly(held, [false, true, true]);
        Assert.Equal(2, CrucibleAutoPilotRules.PickPotion(firstBusy, 0.3f));      // the usable G2 instead
        Assert.Equal(-1, CrucibleAutoPilotRules.PickPotion(
            CrucibleAutoPilotRules.UsableOnly(held, [false, true, false]), 0.3f)); // none usable: wait
    }

    /// <summary>Legendary drinks only at 20% or less: Economic Ethos (750) is worth keeping until death is the cost.</summary>
    [Fact]
    public void LegendaryDrinksOnlyAsALastResort()
    {
        uint[] held = [0, 243138, 0];
        var at = CrucibleAutoPilotRules.LegendaryPotionAtHpPct;
        Assert.Equal(-1, CrucibleAutoPilotRules.PickPotion(held, 0.38f, at)); // Clear would drink here
        Assert.Equal(-1, CrucibleAutoPilotRules.PickPotion(held, 0.21f, at));
        Assert.Equal(1, CrucibleAutoPilotRules.PickPotion(held, 0.20f, at));
        Assert.Equal(1, CrucibleAutoPilotRules.PickPotion(held, 0.06f, at)); // Korha's boss death started at 6%
        Assert.Equal(-1, CrucibleAutoPilotRules.PickPotion(held, 0f, at));
    }

    /// <summary>The HUD's item slots: id at 11 + 5·k, row right after.</summary>
    [Fact]
    public void HudItemSlotsStartAtEleven()
    {
        Assert.Equal(11, CrucibleAutoPilotRules.HudItemIdIndex(0));
        Assert.Equal(12, CrucibleAutoPilotRules.HudItemRowIndex(0));
        Assert.Equal(56, CrucibleAutoPilotRules.HudItemIdIndex(9));
    }

    /// <summary>Legendary closes a coffer only when every untaken offer is gear (Nameday Attire); any item keeps it open.</summary>
    [Fact]
    public void ACofferOfOnlyGearIsClosedForNameday()
    {
        bool Gear(int id) => id is 1 or 37;
        Assert.True(CrucibleAutoPilotRules.CloseCofferForNameday([1, 37, 0, 0], Gear));
        Assert.False(CrucibleAutoPilotRules.CloseCofferForNameday([1, 37, 136, 0], Gear));
        Assert.False(CrucibleAutoPilotRules.CloseCofferForNameday([0, 0, 0, 0], Gear));
        Assert.True(CrucibleAutoPilotRules.CloseCofferForNameday([136, 1, 0, 0], Gear, new System.Collections.Generic.HashSet<int> { 0 }));
        Assert.Contains("Close the coffer", CrucibleAutoPilotRules.KnownPrompts);
    }

    // --- the Siren's Fang: one item kills the bunched adds ---

    private static (ulong, System.Numerics.Vector2) At(ulong id, float x, float z) => (id, new System.Numerics.Vector2(x, z));

    /// <summary>Fire (2,500) over ice (2,000) over Vampiric (1,500); potions are not Fangs.</summary>
    [Fact]
    public void PickFangTakesTheStrongest()
    {
        Assert.Equal(2, CrucibleAutoPilotRules.PickFang([243137, 243194, 243188, 243189]));
        Assert.Equal(0, CrucibleAutoPilotRules.PickFang([243189, 0, 243194]));
        Assert.Equal(-1, CrucibleAutoPilotRules.PickFang([243137, 243197, 0]));
    }

    /// <summary>Summon's spawn (Minerva replay 2026-10-31): the crawler 21y from the far shambling — no Fang yet.</summary>
    [Fact]
    public void FangWaitsWhileTheAddsAreSpread()
    {
        (ulong, System.Numerics.Vector2)[] adds =
            [At(1, 120f, -410f), At(2, 127.1f, -412.9f), At(3, 109.4f, -409.4f), At(4, 130f, -420f)];
        Assert.Equal(0ul, CrucibleAutoPilotRules.FangAim(adds, adds, hardTarget: 3));
    }

    /// <summary>Bunched on the familiar: the hard target already covers them, so it stays the aim.</summary>
    [Fact]
    public void FangKeepsAHardTargetThatCoversThemAll()
    {
        (ulong, System.Numerics.Vector2)[] adds = [At(1, 113f, -418f), At(2, 114f, -419f), At(3, 112f, -417f)];
        Assert.Equal(2ul, CrucibleAutoPilotRules.FangAim(adds, adds, hardTarget: 2));
    }

    /// <summary>Held on something outside the bunch: aim at the first candidate that covers every add.</summary>
    [Fact]
    public void FangMovesTheAimOntoTheBunch()
    {
        (ulong, System.Numerics.Vector2)[] adds = [At(1, 100f, -400f), At(2, 108f, -400f)];
        (ulong, System.Numerics.Vector2)[] candidates = [At(1, 100f, -400f), At(2, 108f, -400f), At(9, 130f, -430f)];
        Assert.Equal(1ul, CrucibleAutoPilotRules.FangAim(adds, candidates, hardTarget: 9));
    }

    /// <summary>Parting Blow's 8y waits for a tighter bunch than the Fang's 12y: 9y apart is a Fang, not a Blow.</summary>
    [Fact]
    public void PartingBlowNeedsATighterBunch()
    {
        (ulong, System.Numerics.Vector2)[] adds = [At(1, 100f, -400f), At(2, 109f, -400f)];
        Assert.Equal(1ul, CrucibleAutoPilotRules.FangAim(adds, adds, hardTarget: 1));
        Assert.Equal(0ul, CrucibleAutoPilotRules.FangAim(adds, adds, hardTarget: 1, CrucibleAutoPilotRules.PartingBlowReachYalms));
    }

    /// <summary>
    /// Morbol Piece, first wave as it spawns (Minerva replay 2026-10-31 23:32): four Seedlings on an arc in front of the
    /// Morbol. The Morbol (12y off) catches none, but the westernmost Seedling has the other three within 7y.
    /// </summary>
    [Fact]
    public void TheMorbolsFirstSeedlingWaveIsBunchedAsItSpawns()
    {
        (ulong, System.Numerics.Vector2)[] seedlings =
            [At(1, 117.172f, -422.828f), At(2, 116f, -420f), At(3, 120f, -416f), At(4, 117.172f, -417.172f)];
        (ulong, System.Numerics.Vector2)[] candidates = [.. seedlings, At(9, 120f, -428f)];
        Assert.Equal(2ul, CrucibleAutoPilotRules.FangAim(seedlings, candidates, hardTarget: 9, CrucibleAutoPilotRules.PartingBlowReachYalms));
    }

    /// <summary>The replay's second Summon: five adds within a few yalms of the familiar — one Parting Blow takes them all.</summary>
    [Fact]
    public void PartingBlowCatchesABunchOnTheFamiliar()
    {
        (ulong, System.Numerics.Vector2)[] adds =
            [At(1, 115f, -418f), At(2, 116f, -419f), At(3, 114f, -417f), At(4, 117f, -416f), At(5, 115f, -421f)];
        Assert.Equal(3ul, CrucibleAutoPilotRules.FangAim(adds, adds, hardTarget: 3, CrucibleAutoPilotRules.PartingBlowReachYalms));
    }

    /// <summary>One add left is not worth a Fang.</summary>
    [Fact]
    public void FangIsNotSpentOnASingleAdd()
        => Assert.Equal(0ul, CrucibleAutoPilotRules.FangAim([At(1, 0f, 0f)], [At(1, 0f, 0f)], hardTarget: 1));

    // XBMItem rows (scratchpad dump of the sheet): 77 G2 Beast Potion, 78 G3, 79 G4, 81 G2 Crucible Ash,
    // 108 Potion of Tempered Intensity (20), 110 Tempered Evasion (21), 166 Lily Simular (feed), 1 Belt of Constitution (gear).
    private static bool IsItem(int row) => row is >= 76 and <= 143;

    private static CrucibleShopOffer Offer(int row, int item, int price, bool sold = false) => new(row, item, price, sold);

    [Theory]
    [InlineData("130", 130)]
    [InlineData("64 (-50%)", 64)]
    [InlineData("1,200", 1200)]
    [InlineData("", null)]
    public void ShopPriceReadsTheNumberBeforeTheDiscount(string text, int? price)
        => Assert.Equal(price, CrucibleAutoPilotRules.ParsePrice(text));

    /// <summary>The row → icon offset the potion table needs: G1 Beast Potion and Steelsting Tome, the two ends.</summary>
    [Fact]
    public void CrucibleItemRowsMapToTheirIcons()
    {
        Assert.Equal(243136, 76 + CrucibleAutoPilotRules.CrucibleItemIconOffset);
        Assert.Equal(243203, 143 + CrucibleAutoPilotRules.CrucibleItemIconOffset);
        Assert.Equal(50, CrucibleAutoPilotRules.ShopRestorePct(79));
        Assert.Equal(0, CrucibleAutoPilotRules.ShopRestorePct(166));
    }

    /// <summary>The recorded shop's layout: row 0's item at [4], its price at [5], sold at [7]; held rows at [157 + 5k].</summary>
    [Fact]
    public void ShopIndicesMatchTheRecording()
    {
        Assert.Equal(4, CrucibleAutoPilotRules.ShopItemIdIndex(0));
        Assert.Equal(5, CrucibleAutoPilotRules.ShopPriceIndex(0));
        Assert.Equal(7, CrucibleAutoPilotRules.ShopSoldIndex(0));
        Assert.Equal(79, CrucibleAutoPilotRules.ShopItemIdIndex(15));
        Assert.Equal(157, CrucibleAutoPilotRules.ShopHeldItemRowIndex(0));
    }

    /// <summary>Strongest healing first, never feed or gear, nothing sold, nothing unaffordable, and no more than the free slots.</summary>
    [Fact]
    public void PotionsAreBoughtStrongestFirst()
    {
        CrucibleShopOffer[] offers =
            [Offer(0, 166, 10), Offer(1, 1, 10), Offer(2, 77, 72), Offer(3, 79, 158), Offer(4, 78, 113, sold: true), Offer(5, 81, 158), Offer(6, 108, 20)];
        Assert.Equal([3, 5, 2], CrucibleAutoPilotRules.ShopBuysForPotions(offers, tokens: 1000, itemsHeld: 0, IsItem));
        Assert.Equal([3], CrucibleAutoPilotRules.ShopBuysForPotions(offers, tokens: 1000, itemsHeld: 9, IsItem));
        Assert.Equal([2], CrucibleAutoPilotRules.ShopBuysForPotions(offers, tokens: 100, itemsHeld: 0, IsItem));
        Assert.Empty(CrucibleAutoPilotRules.ShopBuysForPotions(offers, tokens: 1000, itemsHeld: 10, IsItem));
    }

    // --- buying gear (Korha 2026-10-31: every shop was left with its gear unbought) ---

    private static readonly System.Collections.Generic.Dictionary<int, string> GearEffects = new()
        { [1] = Belt, [2] = IceShield, [3] = RingOfCuring, [4] = ThunderAxe };

    private static bool IsShopGear(int id) => GearEffects.ContainsKey(id);

    /// <summary>Most useful first (Ice Shield 45, Ring of Curing 30, Belt 20); never an item, nothing sold.</summary>
    [Fact]
    public void GearIsBoughtMostUsefulFirst()
    {
        CrucibleShopOffer[] offers = [Offer(0, 1, 300), Offer(1, 2, 500), Offer(2, 3, 400), Offer(3, 79, 158), Offer(4, 2, 100, sold: true)];
        Assert.Equal([1, 2, 0], CrucibleAutoPilotRules.ShopBuysForGear(offers, 1500, 0, IsShopGear, GearEffects.GetValueOrDefault));
    }

    /// <summary>What the tokens cannot cover is skipped for the next piece down.</summary>
    [Fact]
    public void GearSkipsWhatItCannotAfford()
    {
        CrucibleShopOffer[] offers = [Offer(0, 1, 300), Offer(1, 2, 900), Offer(2, 3, 400)];
        Assert.Equal([2, 0], CrucibleAutoPilotRules.ShopBuysForGear(offers, 800, 0, IsShopGear, GearEffects.GetValueOrDefault));
    }

    [Fact]
    public void GearStopsAtTenHeld()
    {
        CrucibleShopOffer[] offers = [Offer(0, 1, 300), Offer(1, 2, 500), Offer(2, 3, 400)];
        Assert.Equal([1], CrucibleAutoPilotRules.ShopBuysForGear(offers, 1500, 9, IsShopGear, GearEffects.GetValueOrDefault));
        Assert.Empty(CrucibleAutoPilotRules.ShopBuysForGear(offers, 1500, 10, IsShopGear, GearEffects.GetValueOrDefault));
        Assert.Equal(3, CrucibleAutoPilotRules.ShopBuysForGear(offers, 1500, null, IsShopGear, GearEffects.GetValueOrDefault).Count);
    }

    /// <summary>Gear that scores nothing or less (more damage taken, unknown text) is not worth tokens.</summary>
    [Fact]
    public void GearThatHurtsIsNeverBought()
    {
        CrucibleShopOffer[] offers = [Offer(0, 5, 100)];
        Assert.Empty(CrucibleAutoPilotRules.ShopBuysForGear(offers, 1500, 0, _ => true, _ => "Damage Taken +10%"));
        Assert.Empty(CrucibleAutoPilotRules.ShopBuysForGear(offers, 1500, 0, _ => true, _ => null));
    }

    /// <summary>Third Board, no Fang held: the strongest affordable Fang first (Fire 128 over Ice 129 over Vampiric 134), then potions.</summary>
    [Fact]
    public void AFangIsBoughtForTheSiren()
    {
        CrucibleShopOffer[] offers = [Offer(0, 134, 68), Offer(1, 129, 45), Offer(2, 128, 45), Offer(3, 79, 158)];
        Assert.Equal([2, 3], CrucibleAutoPilotRules.ShopBuysForPotions(offers, 1000, 0, IsItem, wantFang: true));
        Assert.Equal([3], CrucibleAutoPilotRules.ShopBuysForPotions(offers, 1000, 0, IsItem, wantFang: false));
        Assert.Equal([1], CrucibleAutoPilotRules.ShopBuysForPotions([Offer(0, 128, 45, sold: true), Offer(1, 129, 45)], 50, 0, IsItem, wantFang: true));
        Assert.Empty(CrucibleAutoPilotRules.ShopBuysForPotions(offers, 1000, 10, IsItem, wantFang: true));
    }

    /// <summary>
    /// A Treant slug wave as recorded (Korha 2026-11-01 05:05): spawned spread over ~20 yalms no Fang catches, then
    /// crawled in to within 8 yalms of one slug, which the Fang aims at.
    /// </summary>
    [Fact]
    public void TheTreantSlugsGetAFangOnceTheyHaveCrawledIn()
    {
        (ulong, System.Numerics.Vector2)[] spawned =
        [
            At(1, 126.9f, -416f), At(2, 120f, -428f), At(3, 135.5f, -415.9f),
            At(4, 104.5f, -424.1f), At(5, 133.9f, -428f), At(6, 104f, -420f),
        ];
        Assert.Equal(0ul, CrucibleAutoPilotRules.FangAim(spawned, spawned, hardTarget: 1));

        (ulong, System.Numerics.Vector2)[] crawledIn =
        [
            At(1, 120f, -424f), At(2, 121f, -426f), At(3, 124f, -421f),
            At(4, 115f, -423f), At(5, 126f, -427f),
        ];
        Assert.Equal(1ul, CrucibleAutoPilotRules.FangAim(crawledIn, crawledIn, hardTarget: 1));
    }

    /// <summary>Legendary never drinks on the First Board (Economic Ethos, nothing to make up for it); elsewhere it may.</summary>
    [Theory]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    [InlineData(4, true)]
    [InlineData(5, true)]
    public void LegendaryDrinksOnEveryBoardButTheFirst(int board, bool mayDrink)
        => Assert.Equal(mayDrink, CrucibleAutoPilotRules.LegendaryMayDrink(board));

    /// <summary>The last slug alive is not worth a Fang.</summary>
    [Fact]
    public void ALoneTreantSlugGetsNoFang()
    {
        (ulong, System.Numerics.Vector2)[] last = [At(1, 120f, -424f)];
        Assert.Equal(0ul, CrucibleAutoPilotRules.FangAim(last, last, hardTarget: 1));
    }

    /// <summary>
    /// Slugs bunched to 8 yalms (Korha 2026-11-01 05:05, where they stalled there): a Fang reaches them all, a Parting
    /// Blow's 7 does not, so a ready Blow keeps waiting for them to close in.
    /// </summary>
    [Fact]
    public void SlugsAtEightYalmsAreAFangsNotAPartingBlows()
    {
        (ulong, System.Numerics.Vector2)[] slugs = [At(1, 120f, -424f), At(2, 128f, -424f), At(3, 112f, -424f)];
        Assert.Equal(1ul, CrucibleAutoPilotRules.FangAim(slugs, slugs, hardTarget: 1));
        Assert.Equal(0ul, CrucibleAutoPilotRules.FangAim(slugs, slugs, hardTarget: 1, CrucibleAutoPilotRules.PartingBlowReachYalms));
    }

    /// <summary>The Treant and its slugs, as the Minerva replays name them (BNpcName 14618 / 14619).</summary>
    [Fact]
    public void TreantAndSlugNameIds()
    {
        Assert.Equal(14618u, Daedalus.Rotation.ArtemisCore.Helpers.ArtemisCooldownRules.TreantPieceNameId);
        Assert.Equal(14619u, Daedalus.Rotation.ArtemisCore.Helpers.ArtemisCooldownRules.SlugPieceNameId);
    }

    /// <summary>The Eyes: Clear and Level only, never in the final boss's battle (its win ends the board), nothing out of battle.</summary>
    [Fact]
    public void EyesAreForBattlesThatPaySpoils()
    {
        var trash = new CrucibleEnemy(14564, "cavalier piece", 2, false, default, [], []);
        var boss = new CrucibleEnemy(14592, "guttler", 1, true, default, [], []);
        Assert.True(CrucibleAutoPilotRules.ShouldUseEyes(legendary: false, trash));
        Assert.False(CrucibleAutoPilotRules.ShouldUseEyes(legendary: true, trash));
        Assert.False(CrucibleAutoPilotRules.ShouldUseEyes(legendary: false, boss));
        Assert.False(CrucibleAutoPilotRules.ShouldUseEyes(legendary: false, null));
    }

    /// <summary>Korha 2026-10-31 19:33: "Cannot execute at this time." — the Eye stayed on the HUD, so it is not counted and is pressed again.</summary>
    [Fact]
    public void ARefusedEyeIsPressedAgain()
    {
        Assert.Null(CrucibleAutoPilotRules.PotionOutcome(heldWhenPressed: 1, heldNow: 1, secondsSincePress: 0.5));
        Assert.False(CrucibleAutoPilotRules.PotionOutcome(1, 1, CrucibleAutoPilotRules.PotionConfirmSeconds));
        Assert.True(CrucibleAutoPilotRules.PotionOutcome(1, 0, 0.5));
        Assert.True(CrucibleAutoPilotRules.EyeMaxTries >= 2);
    }

    /// <summary>Each Eye once a battle: Thief's (243160) and Merchant's (243161), whichever slot they sit in.</summary>
    [Fact]
    public void EachEyeOnceABattle()
    {
        uint[] held = [243138, 243161, 0, 243160, 243160];
        Assert.Equal(1, CrucibleAutoPilotRules.PickEye(held, new System.Collections.Generic.HashSet<uint>()));
        Assert.Equal(3, CrucibleAutoPilotRules.PickEye(held, new System.Collections.Generic.HashSet<uint> { 243161 }));
        Assert.Equal(-1, CrucibleAutoPilotRules.PickEye(held, new System.Collections.Generic.HashSet<uint> { 243160, 243161 }));
        Assert.Equal(-1, CrucibleAutoPilotRules.PickEye([243138, 243188], new System.Collections.Generic.HashSet<uint>()));
    }

    /// <summary>The last shop finishes the ten with the cheapest items: Hoarder 1,000 beats Coin Clutcher 500.</summary>
    [Fact]
    public void HoarderBuysTheCheapestAtTheLastShop()
    {
        CrucibleShopOffer[] offers = [Offer(0, 166, 5), Offer(1, 79, 158), Offer(2, 108, 20), Offer(3, 110, 21), Offer(4, 77, 72)];
        Assert.Equal([2, 3], CrucibleAutoPilotRules.ShopBuysForHoarder(offers, 1500, itemsHeld: 8, IsItem, shopAhead: false, 1000, 500, 500));
    }

    /// <summary>Not while a shop lies ahead (loot may fill it for free), not when this shop cannot finish ten.</summary>
    [Fact]
    public void HoarderWaitsAndNeverBuysHalfway()
    {
        CrucibleShopOffer[] offers = [Offer(0, 108, 20), Offer(1, 110, 21)];
        Assert.Empty(CrucibleAutoPilotRules.ShopBuysForHoarder(offers, 1500, 8, IsItem, shopAhead: true, 1000, 500, 500));
        Assert.Empty(CrucibleAutoPilotRules.ShopBuysForHoarder(offers, 1500, 7, IsItem, shopAhead: false, 1000, 500, 500));
        Assert.Empty(CrucibleAutoPilotRules.ShopBuysForHoarder(offers, 30, 8, IsItem, shopAhead: false, 1000, 500, 500));
        Assert.Empty(CrucibleAutoPilotRules.ShopBuysForHoarder(offers, 1500, 10, IsItem, shopAhead: false, 1000, 500, 500));
        Assert.Empty(CrucibleAutoPilotRules.ShopBuysForHoarder(offers, 1500, 8, IsItem, shopAhead: false, 0, 500, 500)); // board pays no Hoarder
    }

    /// <summary>Dropping under 2,000 tokens costs Rolling in Riches too: 1,000 for 1,000 is no gain. Once Clutcher is gone it is.</summary>
    [Fact]
    public void HoarderWeighsRollingInRiches()
    {
        CrucibleShopOffer[] offers = [Offer(0, 108, 20), Offer(1, 110, 21)];
        Assert.Empty(CrucibleAutoPilotRules.ShopBuysForHoarder(offers, 2030, 8, IsItem, shopAhead: false, 1000, 500, 500));
        Assert.Equal([0, 1], CrucibleAutoPilotRules.ShopBuysForHoarder(offers, 2030, 8, IsItem, shopAhead: false, 1000, 0, 500));
        Assert.Equal([0, 1], CrucibleAutoPilotRules.ShopBuysForHoarder(offers, 2100, 8, IsItem, shopAhead: false, 1000, 500, 500));
    }

    /// <summary>Board 3's shops are on moves 3, 7R and 11: only 11 is the last. Standing short of the row still counts as there.</summary>
    [Fact]
    public void OnlyTheLastShopHasNoShopAhead()
    {
        CrucibleStage[] route =
        [
            new(3, 0, CrucibleStageType.Shop, 0, []),
            new(7, 1, CrucibleStageType.Treasure, 0, []), new(7, 2, CrucibleStageType.Shop, 0, []),
            new(10, 1, CrucibleStageType.Random, 0, [new(CrucibleStageType.Enemy, 5), new(CrucibleStageType.Treasure, 0)]),
            new(11, 0, CrucibleStageType.Shop, 0, []),
            new(12, 0, CrucibleStageType.Boss, 1, []),
        ];
        Assert.True(CrucibleAutoPilotRules.ShopAhead(route, 3));
        Assert.True(CrucibleAutoPilotRules.ShopAhead(route, 7));
        Assert.False(CrucibleAutoPilotRules.ShopAhead(route, 11));
        Assert.False(CrucibleAutoPilotRules.ShopAhead(route, 10)); // short of the shop's row: it is the one on 11
        Assert.True(CrucibleAutoPilotRules.ShopAhead([], 11));
        Assert.True(CrucibleAutoPilotRules.ShopAhead(
            [new(4, 0, CrucibleStageType.Shop, 0, []), new(6, 0, CrucibleStageType.Random, 0, [new(CrucibleStageType.Shop, 0)])], 4));
    }

    // --- the final boss's resistance serum (research 2026-11-01) ---

    /// <summary>Guttler's board: G2 Antiparalysis over G1; nothing on Board 1 or 2.</summary>
    [Fact]
    public void TheBossSerumIsTheBoardsResistance()
    {
        Assert.Equal([89, 88], CrucibleAutoPilotRules.BossSerumRows(3));
        Assert.Equal(87, CrucibleAutoPilotRules.BossSerumRows(4)[0]);
        Assert.Empty(CrucibleAutoPilotRules.BossSerumRows(1));
    }

    [Fact]
    public void TheBestHeldSerumIsPickedOnceAFight()
    {
        uint g1 = 88 + CrucibleAutoPilotRules.CrucibleItemIconOffset, g2 = 89 + CrucibleAutoPilotRules.CrucibleItemIconOffset;
        Assert.Equal(2, CrucibleAutoPilotRules.PickBossSerum([243136, g1, g2], 3, new System.Collections.Generic.HashSet<uint>()));
        Assert.Equal(-1, CrucibleAutoPilotRules.PickBossSerum([243136, g1, g2], 3, new System.Collections.Generic.HashSet<uint> { g2 }));
        Assert.Equal(-1, CrucibleAutoPilotRules.PickBossSerum([g1, g2], 2, new System.Collections.Generic.HashSet<uint>()));
    }

    [Fact]
    public void OneSerumIsBoughtForTheBossWhenNoneIsHeld()
    {
        CrucibleShopOffer[] offers = [Offer(0, 88, 100), Offer(1, 89, 300), Offer(2, 79, 158)];
        Assert.Equal(1, CrucibleAutoPilotRules.ShopBuyForBossSerum(offers, 1000, [], 3));
        Assert.Equal(0, CrucibleAutoPilotRules.ShopBuyForBossSerum(offers, 200, [], 3));   // G2 too dear: G1
        Assert.Equal(-1, CrucibleAutoPilotRules.ShopBuyForBossSerum(offers, 1000, [88], 3)); // one held already
        Assert.Equal(-1, CrucibleAutoPilotRules.ShopBuyForBossSerum(offers, 1000, [], 1));    // no boss serum on Board 1
    }

    [Fact]
    public void TheSerumGoesAtTheFinalBossOffLegendary()
    {
        var boss = new CrucibleEnemy(14592, "Guttler the Gutter", 1, true, CrucibleElement.None, [5, 5, 5, 5, 5], []);
        Assert.True(CrucibleAutoPilotRules.ShouldUseBossSerum(false, true, boss));
        Assert.False(CrucibleAutoPilotRules.ShouldUseBossSerum(true, true, boss));
        Assert.False(CrucibleAutoPilotRules.ShouldUseBossSerum(false, false, boss));
        Assert.False(CrucibleAutoPilotRules.ShouldUseBossSerum(false, true, boss with { IsFinalBoss = false }));
    }
}
