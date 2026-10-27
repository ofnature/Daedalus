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
}
