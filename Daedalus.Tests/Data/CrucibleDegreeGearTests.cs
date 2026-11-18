using System.Collections.Generic;
using Daedalus.Data;
using Daedalus.Rotation.ArtemisCore.Helpers;
using Xunit;

namespace Daedalus.Tests.Data;

/// <summary>
/// Third Degree gear and items (user, 2026-11-06): finish holding ten pieces where Walking Armory pays, favour the axes
/// and damage gear with no uncountered downside, Briar Armor means the beastmaster tanks, and no serum for a status the
/// gear already blocks. Effect texts are the game's (docs/crucible-research/xbmitems.md).
/// </summary>
public class CrucibleDegreeGearTests
{
    private const string IceAxe = "Deals additional ice damage with a potency of 240 after each auto-attack.\nAdditional damage is affected by magic damage increase effects of other beast gear, items, and abilities.";
    private const string WindAxe = "Deals additional wind damage with a potency of 240 after each auto-attack.\nAdditional damage is affected by magic damage increase effects of other beast gear, items, and abilities.";
    private const string HeavyAxe = "Damage Dealt +25%\nMovement Speed -20%";
    private const string ThunderAxe = "Damage Dealt +30%\n5% chance to inflict Stun on self upon suffering damage.\nDuration: 3s";
    private const string BriarArmor = "Deals unaspected damage with a potency of 360 each time you suffer physical damage.\nCounter damage is affected by magic damage increase effects of other beast gear, items, and abilities.";
    private const string MysticBoots = "Evasion +8%\nMovement Speed +50%\nIncreases HP recovery via actions and crucible items by 15%.";
    private const string MythrilArmor = "Damage Taken -15%\nMaximum HP +15%";
    private const string HexedHat = "Magic Vulnerability -10%";
    private const string FlameKnife = "Damage Dealt +25%\nHaste +5%\n10% chance to inflict Chains of Condemnation on self upon landing a critical hit.";
    private const string DemonicHelm = "Damage Dealt +20%\nTP Accumulation +20%\n1% chance to inflict Doom on self upon suffering damage.\nDuration: 2s";
    private const string Soulreapers = "Maximum HP +10%\nDamage Taken -5%\nDoom Resistance +100%";
    private const string BlackCowl = "Maximum HP +6%\nEvasion +8%\nSleep Resistance +100%";
    private const string WardedShield = "Grants one stack of Magic Vulnerability Down upon blocking with shield.";
    private const string WaterAxe = "Deals additional water damage with a potency of 240 after each auto-attack.\nAdditional damage is affected by magic damage increase effects of other beast gear, items, and abilities.";
    private const string GreenBeret = "Critical Hit Power +35\nDamage Dealt +3%\nMaximum HP +7%";

    // Item ids: gear 1-13, a Crucible item 50.
    private static readonly Dictionary<int, string> Effects = new()
    {
        [1] = IceAxe, [2] = WindAxe, [3] = HeavyAxe, [4] = ThunderAxe, [5] = BriarArmor, [6] = MysticBoots,
        [7] = MythrilArmor, [8] = HexedHat, [9] = FlameKnife, [10] = DemonicHelm, [11] = Soulreapers, [12] = BlackCowl,
        [13] = WardedShield, [14] = WaterAxe, [15] = GreenBeret, [16] = "Physical Vulnerability -15%",
        [17] = "Critical Hit Rate +15%\nMaximum HP +2%",
        [18] = "20% chance to grant one stack of Blink upon suffering damage.",
        [50] = "Restores 30% of maximum HP.",
    };

    private static bool IsGear(int id) => id is > 0 and < 50;

    private static CrucibleShopOffer Offer(int row, int item, int price) => new(row, item, price, false);

    // --- Ten pieces for Walking Armory ---

    private static IReadOnlyList<int> Count(CrucibleShopOffer[] offers, int tokens, int? held, bool lastShop = false,
        params string[] owned)
        => CrucibleAutoPilotRules.ShopBuysForGearCount(offers, tokens, held, IsGear, Effects.GetValueOrDefault, owned, lastShop);

    [Fact]
    public void TheCountIsFilledCheapestFirst()
    {
        // The First Master's run of 2026-11-06 05:28: Thief's Boots at 150 went unbought for a Swiftness potion.
        CrucibleShopOffer[] offers = [Offer(0, 7, 874), Offer(1, 8, 150), Offer(2, 12, 503), Offer(3, 13, 900)];
        Assert.Equal([1, 2, 0], Count(offers, 1815, 7));
    }

    [Fact]
    public void TheCountStopsAtTen()
        => Assert.Equal([1], Count([Offer(0, 7, 874), Offer(1, 8, 150)], 5000, 9));

    [Fact]
    public void GearWithAnUncounteredDownsideNeverCounts()
        => Assert.Equal([1], Count([Offer(0, 9, 50), Offer(1, 8, 150)], 5000, 9));

    [Fact]
    public void ACounterBoughtLetsThePieceItAnswersThrough()
        => Assert.Equal([1, 0], Count([Offer(0, 3, 100), Offer(1, 6, 200)], 5000, 8)); // Mystic Boots, then Heavy Axe

    [Fact]
    public void TheLastShopBuysForTheCountOnlyIfItFinishesTheTen()
    {
        CrucibleShopOffer[] offers = [Offer(0, 7, 833), Offer(1, 8, 1100)];
        Assert.Empty(Count(offers, 1732, 7, lastShop: true));   // 3 short, only 2 offered
        Assert.Equal([0], Count(offers, 1732, 7));              // an earlier shop still buys toward it
        Assert.Equal([0, 1], Count(offers, 1933, 8, lastShop: true));
    }

    [Fact]
    public void UnknownGearHeldBuysNothingForTheCount()
        => Assert.Empty(Count([Offer(0, 8, 150)], 5000, null));

    // --- The degree gear list (TPLemont's D3 beast-gear chapter, user 2026-11-08) ---

    private const string BeastEarring = "Critical Hit Rate +15%\nMaximum HP +2%";
    private const string SilverSpecs = "Critical Hit Rate +15%\nBlind Resistance +100%";
    private const string MirageVest = "20% chance to grant one stack of Blink upon suffering damage.\nBlink Effect: Nullifies most physical attacks, reducing one stack per nullification\nDuration: 120s";
    private const string AngelRobe = "Damage Taken -7%\nPoison Resistance +100%";

    [Theory]
    [InlineData(IceAxe, CrucibleElement.Ice)]
    [InlineData(WindAxe, CrucibleElement.Wind)]
    [InlineData(HeavyAxe, CrucibleElement.None)]
    [InlineData(BriarArmor, CrucibleElement.None)]
    public void AnElementalAxeIsReadFromItsEffect(string effect, CrucibleElement element)
        => Assert.Equal(element, CrucibleAutoPilotRules.AxeElement(effect));

    private static CrucibleAutoPilotRules.DegreeGearSlot Slot(string effect, params string[] owned)
        => CrucibleAutoPilotRules.DegreeSlotFor(effect, owned, BossDamageKind.Magic, ["Sleep", "Poison"]);

    [Fact]
    public void EachPieceFillsItsSlot()
    {
        Assert.Equal(CrucibleAutoPilotRules.DegreeGearSlot.Axe, Slot(WaterAxe));
        Assert.Equal(CrucibleAutoPilotRules.DegreeGearSlot.Crit, Slot(BeastEarring));
        Assert.Equal(CrucibleAutoPilotRules.DegreeGearSlot.SpecialEffect, Slot(MirageVest));
        Assert.Equal(CrucibleAutoPilotRules.DegreeGearSlot.SpecialEffect, Slot(BriarArmor));
        Assert.Equal(CrucibleAutoPilotRules.DegreeGearSlot.HpStacking, Slot(MythrilArmor));
        Assert.Equal(CrucibleAutoPilotRules.DegreeGearSlot.HpStacking, Slot(HexedHat)); // a magic cut for a magic boss
        Assert.Equal(CrucibleAutoPilotRules.DegreeGearSlot.None, Slot(HeavyAxe));     // pure damage is not on the list
    }

    [Fact]
    public void OnlyOneAxe()
        => Assert.Equal(CrucibleAutoPilotRules.DegreeGearSlot.None, Slot(WaterAxe, IceAxe));

    [Fact]
    public void AThirdCritPieceWithHpCountsAsHp()
    {
        Assert.Equal(CrucibleAutoPilotRules.DegreeGearSlot.HpStacking, Slot(BeastEarring, SilverSpecs, SilverSpecs));
        Assert.Equal(CrucibleAutoPilotRules.DegreeGearSlot.None, Slot(SilverSpecs, SilverSpecs, SilverSpecs));
    }

    [Fact]
    public void TwoSpecialEffectsAtMost()
        => Assert.Equal(CrucibleAutoPilotRules.DegreeGearSlot.None, Slot(MirageVest, BriarArmor, MirageVest));

    [Fact]
    public void ThePoisonResistFillsTheResistSlotOnceHpIsFull()
    {
        string[] sixHp = [MythrilArmor, MythrilArmor, MythrilArmor, MythrilArmor, MythrilArmor, MythrilArmor];
        Assert.Equal(CrucibleAutoPilotRules.DegreeGearSlot.StatusResist, Slot(AngelRobe, sixHp));
        Assert.Equal(CrucibleAutoPilotRules.DegreeGearSlot.None, Slot(AngelRobe, [.. sixHp, BlackCowl])); // one resist
    }

    // --- Coffers on a degree ---

    private static int Coffer(int[] offers, CrucibleElement weak = CrucibleElement.None, string[]? wanted = null,
        params string[] owned)
        => CrucibleAutoPilotRules.PickTreasureOnDegree(offers, IsGear, Effects.GetValueOrDefault, owned, weak, null, wanted);

    private static int Coffer(int[] offers, CrucibleElement weak, string[]? wanted, BossDamageKind bossDamage,
        params string[] owned)
        => CrucibleAutoPilotRules.PickTreasureOnDegree(offers, IsGear, Effects.GetValueOrDefault, owned, weak, null, wanted,
            bossDamage);

    /// <summary>Korha 2026-11-08, the coffer before the Ogre: the Green Beret was taken over two axes.</summary>
    [Fact]
    public void AnAxeBeatsEverythingElse()
        => Assert.Equal(0, Coffer([2, 14, 7, 15])); // the first axe on a tie

    [Fact]
    public void TheAxeOfTheWeaknessAheadBeatsTheOtherAxe()
        => Assert.Equal(1, Coffer([2, 14, 7, 15], CrucibleElement.Water));

    /// <summary>The list's order: axe, crit, special effect, HP stacking, then the resist.</summary>
    [Fact]
    public void TheCofferFollowsTheList()
    {
        int[] offers = [12, 18, 17, 14]; // Black Cowl, Mirage Vest, Beast Earring, the water axe
        string[] wanted = ["Sleep", "Poison"];
        Assert.Equal(3, Coffer(offers, CrucibleElement.Water, wanted, BossDamageKind.Magic));
        Assert.Equal(2, Coffer(offers, CrucibleElement.Water, wanted, BossDamageKind.Magic, IceAxe));
        Assert.Equal(1, Coffer(offers, CrucibleElement.Water, wanted, BossDamageKind.Magic, IceAxe, SilverSpecs, SilverSpecs));
        Assert.Equal(0, Coffer(offers, CrucibleElement.Water, wanted, BossDamageKind.Magic,
            IceAxe, SilverSpecs, SilverSpecs, MirageVest, BriarArmor));
    }

    [Fact]
    public void HpComesBeforeDamageReduction()
    {
        Assert.Equal(1, Coffer([8, 15], CrucibleElement.None, null, BossDamageKind.Magic, IceAxe));  // Beret's HP over the hat
        Assert.Equal(1, Coffer([15, 7], CrucibleElement.None, null, BossDamageKind.Magic, IceAxe));  // Mythril's 15% over 7%
    }

    [Fact]
    public void APhysicalCutIsNotHpStackingAgainstAMagicBoss()
        => Assert.Equal(0, Coffer([8, 16], CrucibleElement.None, null, BossDamageKind.Magic, IceAxe));

    [Fact]
    public void GearIsTakenOverAnItem()
        => Assert.Equal(1, Coffer([50, 13]));

    [Fact]
    public void ASecondAxeIsJustGear()
        => Assert.Equal(0, Coffer([7, 2], owned: IceAxe));

    [Fact]
    public void GearWithAnUncounteredDownsideLeavesTheItem()
        => Assert.Equal(0, Coffer([50, 9]));

    [Fact]
    public void DamageGearRanksTheWeaknessAxeFirst()
    {
        Assert.Equal(2, CrucibleAutoPilotRules.DamageGearRank(WaterAxe, CrucibleElement.Water));
        Assert.Equal(1, CrucibleAutoPilotRules.DamageGearRank(WindAxe, CrucibleElement.Water));
        Assert.Equal(1, CrucibleAutoPilotRules.DamageGearRank(WindAxe, CrucibleElement.None));
        Assert.Equal(0, CrucibleAutoPilotRules.DamageGearRank(GreenBeret, CrucibleElement.Water));
    }

    // --- The final boss's kind of damage ---

    [Fact]
    public void EachBoardsFinalBossHasAKindOfDamage()
    {
        Assert.Equal(BossDamageKind.Magic, CrucibleAutoPilotRules.FinalBossDamage(1));
        Assert.Equal(BossDamageKind.Physical, CrucibleAutoPilotRules.FinalBossDamage(2));
        Assert.Equal(BossDamageKind.Physical, CrucibleAutoPilotRules.FinalBossDamage(3));
        Assert.Equal(BossDamageKind.Magic, CrucibleAutoPilotRules.FinalBossDamage(4));
        Assert.Equal(BossDamageKind.Unknown, CrucibleAutoPilotRules.FinalBossDamage(5));
    }

    [Fact]
    public void TheCutMustMatchTheBossesKindOfDamage()
    {
        Assert.True(CrucibleAutoPilotRules.ReducesBossDamage(HexedHat, BossDamageKind.Magic));
        Assert.False(CrucibleAutoPilotRules.ReducesBossDamage(HexedHat, BossDamageKind.Physical));
        Assert.False(CrucibleAutoPilotRules.ReducesBossDamage(HexedHat, BossDamageKind.Unknown));
        Assert.True(CrucibleAutoPilotRules.ReducesBossDamage(MythrilArmor, BossDamageKind.Unknown)); // Damage Taken: any
    }

    // --- Shops on a degree ---

    private static IReadOnlyList<int> Shop(CrucibleShopOffer[] offers, int tokens = 10_000, int held = 0,
        params string[] owned)
        => CrucibleAutoPilotRules.ShopBuysForDegreeGear(offers, tokens, held, IsGear, Effects.GetValueOrDefault, owned,
            CrucibleElement.Water, BossDamageKind.Magic, ["Sleep", "Poison"]);

    [Fact]
    public void TheShopBuysInTheListsOrder()
        => Assert.Equal([3, 1, 2, 0], Shop([Offer(0, 7, 800), Offer(1, 17, 400), Offer(2, 18, 500), Offer(3, 14, 900)]));

    [Fact]
    public void TheShopBuysTheAxeFirstWhenOnlyOneIsAffordable()
        => Assert.Equal([3], Shop([Offer(0, 7, 800), Offer(1, 17, 400), Offer(3, 14, 900)], tokens: 1000));

    [Fact]
    public void NoSecondAxeIsBought()
        => Assert.Empty(Shop([Offer(0, 14, 840)], owned: IceAxe));

    [Fact]
    public void PureDamageAndUncounteredDownsidesAreNotBought()
        => Assert.Empty(Shop([Offer(0, 3, 500), Offer(1, 4, 500), Offer(2, 9, 500), Offer(3, 10, 500)]));

    [Fact]
    public void TheShopStopsAtTenHeld()
        => Assert.Empty(Shop([Offer(0, 14, 840)], held: 10));

    // --- Which element the axe should carry ---

    private static readonly CrucibleStage[] FirstBoardTail =
    [
        new(4, 0, CrucibleStageType.Enemy, 4, []),     // Banemite
        new(5, 0, CrucibleStageType.Treasure, 0, []),
        new(6, 0, CrucibleStageType.Enemy, 5, []),     // Ogre (water)
        new(7, 0, CrucibleStageType.Boss, 6, []),      // Pas de Seul (piercing)
    ];

    private static CrucibleElement WeaknessOf(int battle) => battle switch
    {
        4 => CrucibleElement.Fire, 5 => CrucibleElement.Water, 6 => CrucibleElement.Piercing, _ => CrucibleElement.None,
    };

    [Fact]
    public void TheAxeIsForTheNextBattleAheadAnAxeCanHit()
        => Assert.Equal(CrucibleElement.Water,
            CrucibleAutoPilotRules.AxeElementAhead(FirstBoardTail, currentMove: 5, WeaknessOf, CrucibleElement.Piercing));

    [Fact]
    public void BattlesBehindDoNotCount()
        => Assert.Equal(CrucibleElement.Fire,
            CrucibleAutoPilotRules.AxeElementAhead(FirstBoardTail, currentMove: 3, WeaknessOf, CrucibleElement.Piercing));

    [Fact]
    public void AForksBattleOptionsCount()
        => Assert.Equal(CrucibleElement.Water, CrucibleAutoPilotRules.AxeElementAhead(
            [new(6, 1, CrucibleStageType.Random, 0, [new CrucibleStageOption(CrucibleStageType.Enemy, 5)])],
            0, WeaknessOf, CrucibleElement.None));

    [Fact]
    public void WithNoElementalWeaknessAheadTheFinalBossDecides()
        => Assert.Equal(CrucibleElement.Piercing,
            CrucibleAutoPilotRules.AxeElementAhead(FirstBoardTail, currentMove: 6, WeaknessOf, CrucibleElement.Piercing));

    [Fact]
    public void AnAxeIsTakenOverBetterArmor()
        => Assert.Equal(1, Coffer([7, 2]));

    [Fact]
    public void TheAxeComesBeforeTheFinalBossesResistance()
        => Assert.Equal(0, Coffer([1, 12], CrucibleElement.Ice, ["Sleep"]));

    // --- Briar Armor: the beastmaster tanks ---

    [Fact]
    public void BriarArmorHitsBack()
    {
        Assert.True(CrucibleAutoPilotRules.CountersPhysicalHits(BriarArmor));
        Assert.False(CrucibleAutoPilotRules.CountersPhysicalHits(MythrilArmor));
    }

    [Fact]
    public void WithBriarArmorOnADegreeTheBeastmasterTanks()
    {
        Assert.True(ArtemisCooldownRules.BeastmasterTanksForCounterArmor(3, counterArmorHeld: true));
        Assert.False(ArtemisCooldownRules.BeastmasterTanksForCounterArmor(3, counterArmorHeld: false));
        Assert.False(ArtemisCooldownRules.BeastmasterTanksForCounterArmor(0, counterArmorHeld: true));
    }

    // --- Tankbusters on a degree: the beastmaster takes them ---

    [Fact]
    public void OnADegreeTheBeastmasterTakesTheTankbuster()
    {
        Assert.True(ArtemisCooldownRules.BeastmasterTakesTheBuster(3, ArtemisCooldownRules.TankbusterLeadSeconds));
        Assert.True(ArtemisCooldownRules.BeastmasterTakesTheBuster(1, 2f));
        Assert.False(ArtemisCooldownRules.BeastmasterTakesTheBuster(3, ArtemisCooldownRules.TankbusterLeadSeconds + 1f));
        Assert.False(ArtemisCooldownRules.BeastmasterTakesTheBuster(0, 2f)); // Standard keeps handing it to the familiar
    }

    // --- Serums ---

    [Fact]
    public void TheEyesAreKept()
    {
        var thiefsEye = (int)CrucibleAutoPilotRules.EyeItemIds[0] - CrucibleAutoPilotRules.CrucibleItemIconOffset;
        Assert.Equal(-1, CrucibleAutoPilotRules.ShopSellSlot([thiefsEye, 0, 0, 0, 0, 0, 0, 0, 0, 0], 1));
    }

    [Fact]
    public void NoSerumForAStatusTheGearBlocks()
    {
        var sleep = (uint)(95 + CrucibleAutoPilotRules.CrucibleItemIconOffset);
        var poison = (uint)(87 + CrucibleAutoPilotRules.CrucibleItemIconOffset);
        Assert.Equal(1, CrucibleAutoPilotRules.PickBossSerum([sleep, poison], 1, new HashSet<uint>(), ["Sleep"]));
        Assert.Equal(-1, CrucibleAutoPilotRules.PickBossSerum([sleep, poison], 1, new HashSet<uint>(), ["Sleep", "Poison"]));
    }
}
