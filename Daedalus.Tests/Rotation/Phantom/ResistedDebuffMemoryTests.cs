using Daedalus.Rotation.Phantom;
using Daedalus.Services;
using Xunit;

namespace Daedalus.Tests.Rotation.Phantom;

/// <summary>
/// Occult Slowga on an enemy that resists or is immune: the "not already slowed" gate never closes, so
/// without a memory of the resist it is recast every GCD for the enemy's whole life.
/// </summary>
public class ResistedDebuffMemoryTests
{
    private const uint Slowga = 41621;
    private const uint Mob = 0x4000_1234;

    [Theory]
    [InlineData(LocalActionOutcome.StatusResisted)]
    [InlineData(LocalActionOutcome.NoEffect)]
    public void AResist_IsRemembered(LocalActionOutcome outcome)
    {
        var memory = new ResistedDebuffMemory(Slowga);
        memory.Observe(Slowga, Mob, outcome);
        Assert.True(memory.HasResisted(Mob));
    }

    /// <summary>A landed Slowga, or an unreadable packet, says nothing about immunity.</summary>
    [Theory]
    [InlineData(LocalActionOutcome.Unknown)]
    [InlineData(LocalActionOutcome.Missed)]
    [InlineData(LocalActionOutcome.DamageDealt)]
    public void OtherOutcomes_AreNotResists(LocalActionOutcome outcome)
    {
        var memory = new ResistedDebuffMemory(Slowga);
        memory.Observe(Slowga, Mob, outcome);
        Assert.False(memory.HasResisted(Mob));
    }

    /// <summary>Another action resisted by the same enemy says nothing about Slow.</summary>
    [Fact]
    public void OtherActions_AreIgnored()
    {
        var memory = new ResistedDebuffMemory(Slowga);
        memory.Observe(41622, Mob, LocalActionOutcome.StatusResisted);
        Assert.False(memory.HasResisted(Mob));
    }

    /// <summary>Only the enemy that resisted; the rest of the pack still gets slowed.</summary>
    [Fact]
    public void OnlyThatEnemy()
    {
        var memory = new ResistedDebuffMemory(Slowga);
        memory.Observe(Slowga, Mob, LocalActionOutcome.StatusResisted);
        Assert.False(memory.HasResisted(Mob + 1));
    }

    /// <summary>Mage Masher keeps its own memory: a Slowga resist says nothing about it.</summary>
    [Fact]
    public void EachActionHasItsOwnMemory()
    {
        var masher = new ResistedDebuffMemory(41624);
        masher.Observe(Slowga, Mob, LocalActionOutcome.StatusResisted);
        Assert.False(masher.HasResisted(Mob));
        masher.Observe(41624, Mob, LocalActionOutcome.StatusResisted);
        Assert.True(masher.HasResisted(Mob));
    }

    [Fact]
    public void Prune_ForgetsTheDead_KeepsTheLiving()
    {
        var memory = new ResistedDebuffMemory(Slowga);
        memory.Observe(Slowga, Mob, LocalActionOutcome.StatusResisted);
        memory.Observe(Slowga, Mob + 1, LocalActionOutcome.StatusResisted);

        memory.Prune(id => id == Mob);

        Assert.True(memory.HasResisted(Mob));
        Assert.False(memory.HasResisted(Mob + 1));
    }
}

/// <summary>How one target's effect entries become a single outcome.</summary>
public class LocalOutcomeClassificationTests
{
    [Theory]
    // delta, fullResist/invuln, statusResist, miss, expected
    [InlineData(0, false, true, false, LocalActionOutcome.StatusResisted)] // Slowga: "Resist" / "Immune"
    [InlineData(-500, false, true, false, LocalActionOutcome.DamageDealt)] // hit landed, rider resisted
    [InlineData(0, true, true, false, LocalActionOutcome.NoEffect)]        // whole action resisted
    [InlineData(0, false, true, true, LocalActionOutcome.StatusResisted)]
    [InlineData(0, false, false, true, LocalActionOutcome.Missed)]
    [InlineData(0, false, false, false, LocalActionOutcome.Unknown)]       // e.g. a debuff that landed
    public void Classify(int delta, bool noEffect, bool statusResist, bool miss, LocalActionOutcome expected)
        => Assert.Equal(expected, CombatEventService.ClassifyLocalOutcome(delta, noEffect, statusResist, miss));
}
