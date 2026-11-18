using System.Collections.Generic;
using Daedalus.Data;
using Xunit;

namespace Daedalus.Tests.Data;

/// <summary>
/// The Crimson Ribbon makes every soul serum a 50% heal ("When carrying a crimson ribbon, restores 50% of target's total
/// HP"; TPLemont's D3 guides, user 2026-11-08): drunk as a heal, kept at the shop, bought as a heal, and the Ribbon taken
/// for the list's resist slot on any board.
/// </summary>
public class CrucibleRibbonSerumTests
{
    private const int Offset = CrucibleAutoPilotRules.CrucibleItemIconOffset;
    private const uint G2Potion = 243137;                     // 23%
    private const uint G4Potion = 243139;                     // 50%
    private const uint G1Antiblind = 90 + Offset;
    private const uint G2Antisleep = 95 + Offset;             // the First Board boss's serum
    private const uint G2Antipoison = 87 + Offset;            // the First Board boss's serum

    private static readonly IReadOnlyList<int> BoardOneBossSerums = CrucibleAutoPilotRules.BossSerumRows(1);

    [Fact]
    public void WithoutTheRibbonASerumIsNoHeal()
        => Assert.Equal(-1, CrucibleAutoPilotRules.PickPotion([G1Antiblind], 0.3f));

    [Fact]
    public void WithTheRibbonASerumHealsHalf()
        => Assert.Equal(1, CrucibleAutoPilotRules.PickPotion([G2Potion, G1Antiblind], 0.3f, ribbonHeld: true));

    [Fact]
    public void APotionAsBigWinsOnTheTieBySlot()
        => Assert.Equal(0, CrucibleAutoPilotRules.PickPotion([G4Potion, G1Antiblind], 0.3f, ribbonHeld: true));

    [Fact]
    public void TheBossesSerumIsKeptWhileAnythingElseHeals()
        => Assert.Equal(0, CrucibleAutoPilotRules.PickPotion([G2Potion, G2Antisleep], 0.3f, ribbonHeld: true,
            keepSerumRows: BoardOneBossSerums));

    [Fact]
    public void TheBossesSerumHealsWhenNothingElseCan()
        => Assert.Equal(1, CrucibleAutoPilotRules.PickPotion([0, G2Antipoison], 0.3f, ribbonHeld: true,
            keepSerumRows: BoardOneBossSerums));

    [Fact]
    public void AboveTheLineNothingIsDrunk()
        => Assert.Equal(-1, CrucibleAutoPilotRules.PickPotion([G1Antiblind], 0.9f, ribbonHeld: true));

    [Fact]
    public void WithTheRibbonNoSerumIsSold()
    {
        Assert.False(CrucibleAutoPilotRules.UsesItem(90, board: 1));
        Assert.True(CrucibleAutoPilotRules.UsesItem(90, board: 1, ribbonHeld: true));
        Assert.Equal(0, CrucibleAutoPilotRules.ShopSellSlot([90, 76], 1));
        Assert.Equal(-1, CrucibleAutoPilotRules.ShopSellSlot([90, 76], 1, ribbonHeld: true));
    }

    [Fact]
    public void WithTheRibbonTheShopBuysSerumsAsHeals()
    {
        CrucibleShopOffer[] offers = [new(0, 77, 72, false), new(1, 91, 60, false)]; // G2 Beast Potion, G2 Antiblind
        Assert.Equal([0], CrucibleAutoPilotRules.ShopBuysForPotions(offers, 1000, 0, row => row is >= 76 and <= 143));
        Assert.Equal([1, 0], CrucibleAutoPilotRules.ShopBuysForPotions(offers, 1000, 0, row => row is >= 76 and <= 143,
            ribbonHeld: true));
    }

    private const string Ribbon = "Petrification Resistance +100%\nBlind Resistance +100%\nParalysis Resistance +100%";
    private const string AngelRobe = "Damage Taken -7%\nPoison Resistance +100%";

    [Fact]
    public void TheRibbonIsKnownByItsEffect()
    {
        Assert.True(CrucibleAutoPilotRules.IsCrimsonRibbon(Ribbon));
        Assert.False(CrucibleAutoPilotRules.IsCrimsonRibbon(AngelRobe));
    }

    [Fact]
    public void TheRibbonFillsTheResistSlotOnABoardThatDoesNotAskForItsStatuses()
        => Assert.Equal(CrucibleAutoPilotRules.DegreeGearSlot.StatusResist,
            CrucibleAutoPilotRules.DegreeSlotFor(Ribbon, [], BossDamageKind.Magic, ["Sleep", "Poison"]));

    [Fact]
    public void WithTheRibbonHeldNoOtherResistIsWanted()
        => Assert.Equal(CrucibleAutoPilotRules.DegreeGearSlot.None,
            CrucibleAutoPilotRules.DegreeSlotFor("Sleep Resistance +100%", [Ribbon], BossDamageKind.Magic, ["Sleep", "Poison"]));

    [Fact]
    public void TheRibbonRanksFirstInTheResistSlot()
        => Assert.True(
            CrucibleAutoPilotRules.DegreeSlotRank(CrucibleAutoPilotRules.DegreeGearSlot.StatusResist, Ribbon, CrucibleElement.None, BossDamageKind.Magic)
            > CrucibleAutoPilotRules.DegreeSlotRank(CrucibleAutoPilotRules.DegreeGearSlot.StatusResist, "Poison Resistance +100%", CrucibleElement.None, BossDamageKind.Magic));
}
