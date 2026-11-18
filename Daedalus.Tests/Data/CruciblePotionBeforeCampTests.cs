using System.Collections.Generic;
using Daedalus.Data;
using Xunit;

namespace Daedalus.Tests.Data;

/// <summary>
/// A potion is kept when a campsite follows the battle (user, 2026-11-15). On the First Board Third Degree one of a
/// run's two potions went in the Arch Demon fight at 36-40% HP, just before the camp that heals 90%; the Banemite's
/// second Deadly Thrust then landed with nothing left to drink. With a potion there Korha cleared it; without, she
/// died with the Banemite at 8%, twice.
/// </summary>
public class CruciblePotionBeforeCampTests
{
    private static readonly IReadOnlyList<CrucibleStageOption> None = [];

    /// <summary>The First Board as the game lays it out: Bone, Arch Demon | Piscodemon, Camp | Treasure, Banemite, Treasure, Ogre.</summary>
    private static List<CrucibleStage> FirstBoard(bool withMap) =>
    [
        new(1, 0, CrucibleStageType.Enemy, 1, None, Node: 1, Next: withMap ? [2, 3] : null),
        new(2, 1, CrucibleStageType.Enemy, 2, None, Node: 2, Next: withMap ? [4] : null, Spaces: 2),
        new(2, 2, CrucibleStageType.Enemy, 3, None, Node: 3, Next: withMap ? [5] : null, Spaces: 2),
        new(3, 1, CrucibleStageType.Campsite, 0, None, Node: 4, Next: withMap ? [6] : null, Spaces: 2),
        new(3, 2, CrucibleStageType.Treasure, 0, None, Node: 5, Next: withMap ? [6] : null, Spaces: 2),
        new(4, 0, CrucibleStageType.Enemy, 4, None, Node: 6, Next: withMap ? [7] : null),
        new(5, 0, CrucibleStageType.Treasure, 0, None, Node: 7, Next: withMap ? [8] : null),
        new(6, 0, CrucibleStageType.Elite, 5, None, Node: 8, Next: withMap ? [] : null),
    ];

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ACampFollowsTheArchDemon(bool withMap) => Assert.True(CrucibleAutoPilotRules.CampAfterBattle(FirstBoard(withMap), 2));

    /// <summary>The other side of the fork leads to a treasure, and the Banemite to a treasure, then the Ogre.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NoCampFollowsThePiscodemonOrTheBanemite(bool withMap)
    {
        Assert.False(CrucibleAutoPilotRules.CampAfterBattle(FirstBoard(withMap), 3));
        Assert.False(CrucibleAutoPilotRules.CampAfterBattle(FirstBoard(withMap), 4));
    }

    /// <summary>The Bone pair leads to a fork of two battles; the Ogre here leads nowhere.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NoCampAfterABattleThatLeadsToBattlesOrToNothing(bool withMap)
    {
        Assert.False(CrucibleAutoPilotRules.CampAfterBattle(FirstBoard(withMap), 1));
        Assert.False(CrucibleAutoPilotRules.CampAfterBattle(FirstBoard(withMap), 5));
    }

    [Fact]
    public void AnUnknownBattleHasNoCamp()
    {
        Assert.False(CrucibleAutoPilotRules.CampAfterBattle(FirstBoard(true), 0));
        Assert.False(CrucibleAutoPilotRules.CampAfterBattle(FirstBoard(true), 99));
        Assert.False(CrucibleAutoPilotRules.CampAfterBattle([], 2));
    }

    [Fact]
    public void BeforeACampThePotionWaitsForTwentyPercent()
    {
        Assert.Equal(CrucibleAutoPilotRules.PotionBeforeCampAtHpPct, CrucibleAutoPilotRules.DrinkLine(CrucibleAutoPilotRules.PotionAtHpPct, campNext: true));
        Assert.Equal(CrucibleAutoPilotRules.PotionAtHpPct, CrucibleAutoPilotRules.DrinkLine(CrucibleAutoPilotRules.PotionAtHpPct, campNext: false));
    }

    /// <summary>Korha drank at 36-40% in the Arch Demon fight: not any more. At 18% she still does.</summary>
    [Fact]
    public void AtFortyPercentBeforeACampNothingIsDrunkAndAtEighteenItIs()
    {
        const uint G2Potion = 243137;
        var line = CrucibleAutoPilotRules.DrinkLine(CrucibleAutoPilotRules.PotionAtHpPct, campNext: true);
        Assert.Equal(-1, CrucibleAutoPilotRules.PickPotion([G2Potion], 0.38f, line));
        Assert.Equal(0, CrucibleAutoPilotRules.PickPotion([G2Potion], 0.18f, line));
    }

    /// <summary>
    /// Korha, 2026-11-15 17:22: the Arch Demon dead, a potion went at 19% on the way to the camp — 144 HP, two seconds
    /// before the camp healed 1,323. Out of the fight with a camp next, nothing is drunk whatever the HP.
    /// </summary>
    [Theory]
    [InlineData(0.19f)]
    [InlineData(0.05f)]
    public void OnTheWayToTheCampNothingIsDrunk(float hp)
    {
        const uint G2Potion = 243137;
        var line = CrucibleAutoPilotRules.DrinkLine(CrucibleAutoPilotRules.PotionAtHpPct, campNext: true, fighting: false);
        Assert.Equal(-1, CrucibleAutoPilotRules.PickPotion([G2Potion], hp, line));
    }

    /// <summary>
    /// Korha, 2026-11-15 17:28: the Ogre dead, the walk chose the campsite over the treasure and a potion went at 14%
    /// a second later. A fork with a campsite on it counts once the fight is over; in the fight it does not.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ACampOnAForkCountsOnceTheFightIsOver(bool withMap)
    {
        List<CrucibleStage> route =
        [
            new(6, 0, CrucibleStageType.Elite, 5, None, Node: 8, Next: withMap ? [9, 10] : null),
            new(7, 1, CrucibleStageType.Treasure, 0, None, Node: 9, Next: withMap ? [11] : null),
            new(7, 2, CrucibleStageType.Campsite, 0, None, Node: 10, Next: withMap ? [11] : null),
            new(8, 0, CrucibleStageType.Boss, 6, None, Node: 11, Next: withMap ? [] : null),
        ];
        Assert.True(CrucibleAutoPilotRules.CampAfterBattle(route, 5, campWinsFork: true));
        Assert.False(CrucibleAutoPilotRules.CampAfterBattle(route, 5));
        Assert.False(CrucibleAutoPilotRules.CampAfterBattle(route, 6, campWinsFork: true));
    }

    /// <summary>With no camp ahead the usual line holds between battles too: HP carries into the next one.</summary>
    [Fact]
    public void BetweenBattlesWithNoCampAheadTheUsualLineHolds()
        => Assert.Equal(CrucibleAutoPilotRules.PotionAtHpPct,
            CrucibleAutoPilotRules.DrinkLine(CrucibleAutoPilotRules.PotionAtHpPct, campNext: false, fighting: false));

    /// <summary>Still in the fight before a camp, the 20% line stands.</summary>
    [Fact]
    public void InTheFightBeforeACampTheTwentyPercentLineStands()
        => Assert.Equal(CrucibleAutoPilotRules.PotionBeforeCampAtHpPct,
            CrucibleAutoPilotRules.DrinkLine(CrucibleAutoPilotRules.PotionAtHpPct, campNext: true, fighting: true));

    /// <summary>A line already lower (Legendary's 20%) is not raised.</summary>
    [Fact]
    public void ALowerLineStaysAsItIs() => Assert.Equal(0.15f, CrucibleAutoPilotRules.DrinkLine(0.15f, campNext: true));
}
