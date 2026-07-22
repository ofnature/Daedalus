using System.Linq;
using Daedalus.Data;
using Daedalus.Models.Gear;
using Daedalus.Services.Gear;
using System.Collections.Generic;
using Xunit;

namespace Daedalus.Tests.Data;

/// <summary>Meld optimizer phase 4: tier math + priority data integrity + DPS model sanity.</summary>
public class GcdBreakpointsTests
{
    [Fact]
    public void Level100_BaseTier_RunsToFirstBoundary()
    {
        // First speed scalar point (x=1) already floors 2497.5ms → 2.49, so the 2.49 tier starts
        // at Δ = ceil(2780/130) = 22 → SkS 442 (matches the community tier tables: 442/527/…).
        var window = GcdBreakpoints.Window(420, 100);
        var current = window.First(t => 420 >= t.SpeedFrom && 420 <= t.SpeedTo);
        Assert.Equal(2.50f, current.GcdSeconds, 2);
        Assert.Equal(441, current.SpeedTo);
        Assert.Equal(22, GcdBreakpoints.PointsToNextTier(420, 100));

        // Second boundary: 2.48 needs x=5 → Δ=107 → SkS 527.
        Assert.Equal(2.49f, StatConversions.GcdSeconds(442, 100), 2);
        Assert.Equal(2.48f, StatConversions.GcdSeconds(527, 100), 2);
    }

    [Fact]
    public void Level100_TierWindow_ContainsPrevCurrentNext()
    {
        var window = GcdBreakpoints.Window(600, 100);
        // 600 SkS sits past the first boundary — the window must include a slower and faster tier.
        var current = window.First(t => 600 >= t.SpeedFrom && 600 <= t.SpeedTo);
        Assert.Contains(window, t => t.GcdSeconds > current.GcdSeconds);
        Assert.Contains(window, t => t.GcdSeconds < current.GcdSeconds);
    }

    [Fact]
    public void TierBoundaries_AreContiguous()
    {
        var window = GcdBreakpoints.Window(700, 100, tiersAfter: 3);
        for (var i = 1; i < window.Count; i++)
            Assert.Equal(window[i - 1].SpeedTo + 1, window[i].SpeedFrom);
    }
}

public class BalancePrioritiesTests
{
    [Fact]
    public void AllCombatJobs_HaveNonEmptyPriorities()
    {
        uint[] jobs =
        {
            JobRegistry.Paladin, JobRegistry.Warrior, JobRegistry.DarkKnight, JobRegistry.Gunbreaker,
            JobRegistry.WhiteMage, JobRegistry.Scholar, JobRegistry.Astrologian, JobRegistry.Sage,
            JobRegistry.Monk, JobRegistry.Dragoon, JobRegistry.Ninja, JobRegistry.Samurai,
            JobRegistry.Reaper, JobRegistry.Viper,
            JobRegistry.Bard, JobRegistry.Machinist, JobRegistry.Dancer,
            JobRegistry.BlackMage, JobRegistry.Summoner, JobRegistry.RedMage, JobRegistry.Pictomancer,
        };

        foreach (var job in jobs)
        {
            var priority = BalancePriorities.For(job);
            Assert.NotEmpty(priority.Order);
            Assert.NotEqual(0u, priority.SpeedStat);
            Assert.False(string.IsNullOrWhiteSpace(priority.Note));
        }
    }

    [Fact]
    public void Samurai_CritDetDh_SpeedDimmed()
    {
        var priority = BalancePriorities.For(JobRegistry.Samurai);
        Assert.Equal(new[] { GearStatIds.CriticalHit, GearStatIds.Determination, GearStatIds.DirectHit }, priority.Order);
        Assert.Equal(GearStatIds.SkillSpeed, priority.SpeedStat);
        Assert.DoesNotContain(GearStatIds.SkillSpeed, priority.Order);
    }

    [Fact]
    public void BlackMage_SpeedFirst()
    {
        var priority = BalancePriorities.For(JobRegistry.BlackMage);
        Assert.Equal(GearStatIds.SpellSpeed, priority.Order[0]);
    }
}

public class MeldDpsModelTests
{
    private static Dictionary<uint, int> Totals(int crit = 2500, int det = 2000, int dh = 1500, int sks = 420)
        => new()
        {
            [GearStatIds.CriticalHit] = crit,
            [GearStatIds.Determination] = det,
            [GearStatIds.DirectHit] = dh,
            [GearStatIds.SkillSpeed] = sks,
        };

    [Fact]
    public void MoreCrit_IsAlwaysPositiveDelta()
    {
        var delta = MeldDpsModel.DeltaPercent(Totals(), Totals(crit: 2554), 100, GearStatIds.SkillSpeed);
        Assert.True(delta > 0, $"expected positive, got {delta}");
    }

    [Fact]
    public void NoChange_IsZeroDelta()
    {
        Assert.Equal(0.0, MeldDpsModel.DeltaPercent(Totals(), Totals(), 100, GearStatIds.SkillSpeed), 6);
    }

    [Fact]
    public void SpeedWithinTier_IsWorthLessThanCrit()
    {
        // SkS that does NOT cross a tier (420 → 441 stays in base tier) contributes nothing in
        // this model; +54 Crit always contributes — the verdict math depends on this ordering.
        var critDelta = MeldDpsModel.DeltaPercent(Totals(), Totals(crit: 2554), 100, GearStatIds.SkillSpeed);
        var speedDelta = MeldDpsModel.DeltaPercent(Totals(), Totals(sks: 441), 100, GearStatIds.SkillSpeed);
        Assert.True(critDelta > speedDelta);
        Assert.Equal(0.0, speedDelta, 6);
    }

    [Fact]
    public void SpeedCrossingATier_IncreasesMultiplier()
    {
        // 420 → 506 crosses into 2.49 (level 100) — the GCD uptime multiplier must move.
        var delta = MeldDpsModel.DeltaPercent(Totals(), Totals(sks: 506), 100, GearStatIds.SkillSpeed);
        Assert.True(delta > 0, $"expected positive, got {delta}");
    }
}
