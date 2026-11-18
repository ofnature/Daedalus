using System.Collections.Generic;
using Daedalus.Data;
using Xunit;

namespace Daedalus.Tests.Data;

/// <summary>
/// Third Degree shopping (user, 2026-11-06): every piece of gear whose downsides are countered, the wanted resistances
/// first, a serum for each status the gear leaves uncovered, and selling what Daedalus never uses. Effect texts are the
/// game's (docs/crucible-research/xbmitems.md).
/// </summary>
public class CrucibleDegreeShopTests
{
    private const string AngelRobe = "Damage Taken -7%\nPoison Resistance +100%\n5% chance to grant one stack of Rehabilitation upon suffering damage.";
    private const string BlackCowl = "Maximum HP +6%\nEvasion +8%\nSleep Resistance +100%";
    private const string GenjiArmor = "Movement Speed -20%\nDamage Taken -20%\nMaximum HP +30%";
    private const string MysticBoots = "Evasion +8%\nMovement Speed +50%\nIncreases HP recovery via actions and crucible items by 15%.";
    private const string ThunderAxe = "Damage Dealt +30%\n5% chance to inflict Stun on self upon suffering damage.\nDuration: 3s";
    private const string UmbralWristlet = "Magic Vulnerability -15%\nMaximum HP +5%\nStun Resistance +100%";
    private const string DemonicHelm = "Damage Dealt +20%\nTP Accumulation +20%\n1% chance to inflict Doom on self upon suffering damage.\nDuration: 2s";
    private const string Soulreapers = "Maximum HP +10%\nDamage Taken -5%\nDoom Resistance +100%";
    private const string FlameKnife = "Damage Dealt +25%\nHaste +5%\n10% chance to inflict Chains of Condemnation on self upon landing a critical hit.";
    private const string IceShield = "Damage Taken -30%\nSlow +10%\n30% chance to absorb ice damage as HP.";
    private const string WindMask = "Magic Vulnerability -10%\nGrants Keen Edge upon evading an attack.\nKeen Edge Effect: Inflicts damage over time on self in exchange for increasing damage dealt by 30%";
    private const string EmpressHairpin = "Evasion +10%\nMaximum HP -8%";
    private const string HexedHat = "Magic Vulnerability -10%";

    private static CrucibleShopOffer Offer(int row, int item, int price) => new(row, item, price, false);

    // --- Downsides and their counters ---

    [Theory]
    [InlineData(HexedHat)]
    [InlineData(AngelRobe)]
    [InlineData(Soulreapers)]
    public void PlainGearHasNoDownside(string effect)
        => Assert.False(CrucibleAutoPilotRules.HasUncounteredDownside(effect, []));

    [Theory]
    [InlineData(FlameKnife)]
    [InlineData(IceShield)]
    [InlineData(WindMask)]
    [InlineData(EmpressHairpin)]
    public void NothingCountersTheseDownsides(string effect)
        => Assert.True(CrucibleAutoPilotRules.HasUncounteredDownside(effect, [MysticBoots, UmbralWristlet, Soulreapers]));

    [Fact]
    public void StunOnSelfNeedsStunResistance()
    {
        Assert.True(CrucibleAutoPilotRules.HasUncounteredDownside(ThunderAxe, []));
        Assert.False(CrucibleAutoPilotRules.HasUncounteredDownside(ThunderAxe, [UmbralWristlet]));
    }

    [Fact]
    public void DoomOnSelfNeedsDoomResistance()
    {
        Assert.True(CrucibleAutoPilotRules.HasUncounteredDownside(DemonicHelm, [UmbralWristlet]));
        Assert.False(CrucibleAutoPilotRules.HasUncounteredDownside(DemonicHelm, [Soulreapers]));
    }

    [Fact]
    public void LostSpeedNeedsAsMuchSpeedHeld()
    {
        Assert.True(CrucibleAutoPilotRules.HasUncounteredDownside(GenjiArmor, []));
        Assert.False(CrucibleAutoPilotRules.HasUncounteredDownside(GenjiArmor, [MysticBoots]));
    }

    // --- Buying gear on a degree ---

    private static readonly Dictionary<int, string> Effects = new()
    {
        [1] = HexedHat, [2] = GenjiArmor, [3] = MysticBoots, [4] = FlameKnife, [5] = DemonicHelm, [6] = Soulreapers,
        [7] = AngelRobe, [8] = BlackCowl,
    };

    [Fact]
    public void ADegreeBuysEveryCounteredPiece()
    {
        CrucibleShopOffer[] offers = [Offer(0, 1, 100), Offer(1, 2, 100), Offer(2, 3, 100), Offer(3, 4, 100)];
        var buys = CrucibleAutoPilotRules.ShopBuysForGear(offers, 10_000, 0, Effects.ContainsKey, Effects.GetValueOrDefault,
            anyGear: true);
        Assert.Contains(0, buys);         // Hexed Hat: no downside
        Assert.Contains(2, buys);         // Mystic Boots
        Assert.Contains(1, buys);         // Genji Armor, once the boots are bought
        Assert.DoesNotContain(3, buys);   // Flame Knife: nothing counters its chains
    }

    [Fact]
    public void TheDemonicHelmFollowsSoulreapersArmor()
    {
        CrucibleShopOffer[] offers = [Offer(0, 5, 100), Offer(1, 6, 100)];
        Assert.Equal([1, 0], CrucibleAutoPilotRules.ShopBuysForGear(offers, 10_000, 0, Effects.ContainsKey,
            Effects.GetValueOrDefault, anyGear: true));
        Assert.Equal([0], CrucibleAutoPilotRules.ShopBuysForGear([Offer(0, 6, 100)], 10_000, 0, Effects.ContainsKey,
            Effects.GetValueOrDefault, anyGear: true));
        Assert.Empty(CrucibleAutoPilotRules.ShopBuysForGear([Offer(0, 5, 100)], 10_000, 0, Effects.ContainsKey,
            Effects.GetValueOrDefault, anyGear: true));
    }

    [Fact]
    public void StandardStillBuysOnlyWhatScores()
    {
        CrucibleShopOffer[] offers = [Offer(0, 1, 100), Offer(1, 2, 100), Offer(2, 4, 100)];
        var buys = CrucibleAutoPilotRules.ShopBuysForGear(offers, 10_000, 0, Effects.ContainsKey, Effects.GetValueOrDefault);
        Assert.Contains(1, buys); // Genji Armor scores despite its speed: Standard doesn't look at counters
    }

    [Fact]
    public void TheWantedResistancesGoFirst()
    {
        CrucibleShopOffer[] offers = [Offer(0, 1, 100), Offer(1, 7, 100), Offer(2, 8, 100)];
        var first = CrucibleAutoPilotRules.ShopBuysForGear(offers, 10_000, 0, Effects.ContainsKey, Effects.GetValueOrDefault,
            CrucibleAutoPilotRules.WantedResistances(1), anyGear: true, wantedOnly: true);
        Assert.Equal(2, first.Count);
        Assert.DoesNotContain(0, first); // the Hexed Hat waits for the rest of the tokens
    }

    // --- Boss serums ---

    [Fact]
    public void PasDeSeulGetsASerumForEachStatusTheGearLeftOpen()
    {
        CrucibleShopOffer[] offers = [Offer(0, 95, 300), Offer(1, 87, 300), Offer(2, 79, 158)];
        Assert.Equal([0, 1], CrucibleAutoPilotRules.ShopBuysForBossSerums(offers, 1000, [], 1));
        Assert.Equal([0], CrucibleAutoPilotRules.ShopBuysForBossSerums(offers, 1000, [], 1, ["Poison"])); // Angel Robe held
        Assert.Empty(CrucibleAutoPilotRules.ShopBuysForBossSerums(offers, 1000, [], 1, ["Poison", "Sleep"]));
    }

    [Fact]
    public void BothSerumsGoAtPasDeSeul()
    {
        uint sleep = 95 + CrucibleAutoPilotRules.CrucibleItemIconOffset, poison = 86 + CrucibleAutoPilotRules.CrucibleItemIconOffset;
        Assert.Equal(0, CrucibleAutoPilotRules.PickBossSerum([sleep, poison], 1, new HashSet<uint>()));
        Assert.Equal(1, CrucibleAutoPilotRules.PickBossSerum([sleep, poison], 1, new HashSet<uint> { sleep }));
        Assert.Equal(-1, CrucibleAutoPilotRules.PickBossSerum([sleep, poison], 1, new HashSet<uint> { sleep, poison }));
    }

    // --- Selling what nothing uses ---

    [Theory]
    [InlineData(136)] // Tome of Reflection
    [InlineData(137)] // Tome of the Impervious (Blink)
    [InlineData(138)] // Temporal Sand
    [InlineData(140)] // Beast Potion Kit
    public void UnusedItemsAreSold(int row)
        => Assert.Equal(1, CrucibleAutoPilotRules.ShopSellSlot([78, row, 0, 0, 0, 0, 0, 0, 0, 0], 1));

    [Theory]
    [InlineData(79)]  // G4 Beast Potion
    [InlineData(100)] // Thief's Eye
    [InlineData(129)] // Fang of Ice
    [InlineData(139)] // Celestial Sand
    [InlineData(95)]  // G2 Antisleep: Pas de Seul's serum
    [InlineData(87)]  // G2 Antipoison: Pas de Seul's too
    public void ItemsInUseAreKept(int row)
        => Assert.Equal(-1, CrucibleAutoPilotRules.ShopSellSlot([row, 0, 0, 0, 0, 0, 0, 0, 0, 0], 1));

    [Fact]
    public void AnotherBoardsSerumIsSold()
        => Assert.Equal(0, CrucibleAutoPilotRules.ShopSellSlot([89, 0, 0, 0, 0, 0, 0, 0, 0, 0], 1)); // Antiparalysis on Board 1

    [Fact]
    public void CelestialSandIsTheStrongestFang()
        => Assert.Equal(1, CrucibleAutoPilotRules.PickFang([243188, 243199]));
}
