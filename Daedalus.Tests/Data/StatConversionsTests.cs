using Daedalus.Data;
using Xunit;

namespace Daedalus.Tests.Data;

/// <summary>
/// Meld optimizer phase 3: the characterstatus-refined formula port MUST reproduce in-game
/// Character-window values exactly. Level-90 anchors come from a live screenshot (2026-07-22,
/// tank at Crit 1576 / Det 1352 / DH 1076 / SkS 803); level-100 anchors are the definitional
/// bases (substat floor 420 → 5.0% crit chance, 2.50 GCD).
/// </summary>
public class StatConversionsTests
{
    // ── level-90 screenshot anchors ──

    [Fact]
    public void Level90_Crit1576_Chance17_3_Damage152_3()
    {
        Assert.Equal(17.3f, StatConversions.CritChancePercent(1576, 90), 2);
        Assert.Equal(152.3f, StatConversions.CritDamagePercent(1576, 90), 2);
    }

    [Fact]
    public void Level90_Det1352_Bonus7_0()
    {
        Assert.Equal(7.0f, StatConversions.DeterminationBonusPercent(1352, 90), 2);
    }

    [Fact]
    public void Level90_Dh1076_Rate19_5()
    {
        Assert.Equal(19.5f, StatConversions.DirectHitRatePercent(1076, 90), 2);
    }

    [Fact]
    public void Level90_Sks803_SpeedBonus2_7()
    {
        Assert.Equal(2.7f, StatConversions.SpeedBonusPercent(803, 90), 2);
    }

    // ── level-100 definitional bases ──

    [Fact]
    public void Level100_BaseSubstat_Floors()
    {
        Assert.Equal(5.0f, StatConversions.CritChancePercent(420, 100), 2);
        Assert.Equal(140.0f, StatConversions.CritDamagePercent(420, 100), 2);
        Assert.Equal(0.0f, StatConversions.DirectHitRatePercent(420, 100), 2);
        Assert.Equal(0.0f, StatConversions.DeterminationBonusPercent(440, 100), 2);
        Assert.Equal(2.50f, StatConversions.GcdSeconds(420, 100), 2);
        Assert.Equal(200, StatConversions.PietyMpPerTick(440, 100));
    }

    [Fact]
    public void Gcd_TierStaircase_IsMonotonicAndFloored()
    {
        // The staircase: GCD never increases with speed, and steps come in whole 0.01s tiers.
        var previous = StatConversions.GcdSeconds(420, 100);
        for (var speed = 421; speed <= 1200; speed += 7)
        {
            var gcd = StatConversions.GcdSeconds(speed, 100);
            Assert.True(gcd <= previous, $"GCD rose from {previous} to {gcd} at {speed} SkS");
            previous = gcd;
        }

        // Well into the stat range the tier must have moved off base.
        Assert.True(StatConversions.GcdSeconds(1200, 100) < 2.50f);
    }

    [Fact]
    public void ModsFor_ClampsToNearestMilestoneBelow()
    {
        Assert.Equal(StatConversions.ModsFor(90), StatConversions.ModsFor(95));
        Assert.Equal(StatConversions.ModsFor(100), StatConversions.ModsFor(100));
        Assert.Equal(StatConversions.ModsFor(50), StatConversions.ModsFor(53));
    }
}
