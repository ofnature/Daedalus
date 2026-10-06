using Daedalus.Config;
using Daedalus.Rotation.AstraeaCore.Modules.Healing;
using Xunit;

namespace Daedalus.Tests.Rotation.AstraeaCore;

/// <summary>
/// Always be casting: against a boss every GCD spent healing is a damage GCD lost, so the free oGCD heals
/// go first and a GCD heal only when they are not enough (RSR: ability 70/75%, spell 65%). The old defaults
/// had it backwards — Aspected Benefic at 75% against Essential Dignity's 60%.
/// </summary>
public class AstraeaAbcHealingTests
{
    // ── free heals above GCD heals ───────────────────────────────────────────────────────

    [Fact]
    public void SingleTarget_FreeHealFiresBeforeTheGcdHeal()
    {
        var c = new AstrologianConfig();
        Assert.True(c.EssentialDignitySpareChargeThreshold > c.AspectedBeneficThreshold);
        Assert.True(c.AspectedBeneficThreshold > c.BeneficIIThreshold);
        Assert.True(c.BeneficIIThreshold > c.BeneficThreshold);
    }

    [Fact]
    public void Group_FreeHealsFireBeforeHelios()
    {
        var c = new AstrologianConfig();
        Assert.True(c.CelestialOppositionThreshold > c.AoEHealThreshold);
        Assert.True(c.LadyOfCrownsThreshold > c.AoEHealThreshold);
        Assert.True(c.EarthlyStarDetonateThreshold > c.AoEHealThreshold);
    }

    // ── Essential Dignity charges ────────────────────────────────────────────────────────

    /// <summary>Below 78 there is one charge and nothing to bank: the higher threshold, ahead of the GCD heals.</summary>
    [Theory]
    [InlineData(1u, 1u, 0.70f)] // level 71: the only charge
    [InlineData(2u, 2u, 0.70f)] // a spare charge
    [InlineData(1u, 2u, 0.60f)] // the last of two, banked
    public void EssentialDignityThreshold(uint current, uint max, float expected)
        => Assert.Equal(expected, EssentialDignityHandler.ThresholdFor(current, max, spare: 0.70f, last: 0.60f));

    // ── the migration ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Migration_MovesTheOldDefaults()
    {
        var c = new AstrologianConfig
        {
            AspectedBeneficThreshold = 0.75f,
            AoEHealThreshold = 0.70f,
            EarthlyStarDetonateThreshold = 0.65f,
            LadyOfCrownsThreshold = 0.60f,
        };

        Assert.True(c.MigrateToOgcdFirstHealDefaults());
        Assert.Equal(0.65f, c.AspectedBeneficThreshold);
        Assert.Equal(0.65f, c.AoEHealThreshold);
        Assert.Equal(0.75f, c.EarthlyStarDetonateThreshold);
        Assert.Equal(0.75f, c.LadyOfCrownsThreshold);
    }

    /// <summary>A threshold the player set is theirs.</summary>
    [Fact]
    public void Migration_KeepsWhatThePlayerChose()
    {
        var c = new AstrologianConfig { AspectedBeneficThreshold = 0.80f, AoEHealThreshold = 0.55f };
        c.MigrateToOgcdFirstHealDefaults();
        Assert.Equal(0.80f, c.AspectedBeneficThreshold);
        Assert.Equal(0.55f, c.AoEHealThreshold);
    }

    [Fact]
    public void Migration_OfFreshDefaultsChangesNothing() => Assert.False(new AstrologianConfig().MigrateToOgcdFirstHealDefaults());

    // ── Earthly Star on cooldown, gated on time-to-kill ──────────────────────────────────

    [Theory]
    [InlineData(true, true, 3, float.MaxValue, true)]  // no estimate yet (pull start, a boss): place
    [InlineData(true, true, 1, 45f, true)]             // a boss with 45 s left
    [InlineData(true, true, 3, 12f, true)]             // a pack that outlives the 10 s maturing
    [InlineData(true, true, 3, 6f, false)]             // a pack about to die: keep the cooldown
    [InlineData(true, false, 3, 45f, false)]           // out of combat
    [InlineData(true, true, 0, 45f, false)]            // nothing to hit
    [InlineData(false, true, 3, 45f, false)]           // setting off: reactive only
    public void EarthlyStarOnCooldown(bool enabled, bool inCombat, int engaged, float ttk, bool place)
        => Assert.Equal(place, EarthlyStarPlacementHandler.PlaceOnCooldown(enabled, inCombat, engaged, ttk));

    // ── one group heal instead of a single heal each ────────────────────────────────────

    /// <summary>Tesleen, 2026-10-06: tank at 85%, three others near 60% — average 68%, so three Aspected Benefics.</summary>
    [Fact]
    public void TwoLowMembersGetAGroupHealEvenWithTheTankFull()
        => Assert.True(AoEHealingHandler.ShouldGroupHeal(avgHp: 0.68f, threshold: 0.65f, injured: 4, lowMembers: 2, minTargets: 2));

    [Fact]
    public void OneLowMemberIsASingleHeal()
        => Assert.False(AoEHealingHandler.ShouldGroupHeal(avgHp: 0.80f, threshold: 0.65f, injured: 3, lowMembers: 1, minTargets: 2));

    [Fact]
    public void ALowAverageStillCounts()
        => Assert.True(AoEHealingHandler.ShouldGroupHeal(avgHp: 0.60f, threshold: 0.65f, injured: 2, lowMembers: 1, minTargets: 2));
}
