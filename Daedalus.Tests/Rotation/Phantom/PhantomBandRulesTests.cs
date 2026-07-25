using Daedalus.Config;
using Daedalus.Data;
using Daedalus.Rotation.Phantom;
using Xunit;

namespace Daedalus.Tests.Rotation.Phantom;

/// <summary>
/// Tests for the Phase 3 phantom band rules (docs/occult-phantom-plan.md) — the pure
/// predicates behind the survival / mitigation / interrupt / MP bands.
/// </summary>
public class PhantomBandRulesTests
{
    private static PhantomConfig DefaultConfig() => new();

    [Fact]
    public void Potion_FiresOnlyInCombat_WithItems_BelowThreshold()
    {
        var cfg = DefaultConfig(); // 0.50

        Assert.True(PhantomBandRules.ShouldUsePotion(cfg, 0.40f, potionCount: 5, inCombat: true));
        Assert.False(PhantomBandRules.ShouldUsePotion(cfg, 0.40f, potionCount: 0, inCombat: true));
        Assert.False(PhantomBandRules.ShouldUsePotion(cfg, 0.40f, potionCount: 5, inCombat: false));
        Assert.False(PhantomBandRules.ShouldUsePotion(cfg, 0.60f, potionCount: 5, inCombat: true));
    }

    [Fact]
    public void Elixir_UsesItsOwnLowerThreshold()
    {
        var cfg = DefaultConfig(); // elixir 0.30 vs potion 0.50

        Assert.False(PhantomBandRules.ShouldUseElixir(cfg, 0.40f, elixirCount: 2, inCombat: true));
        Assert.True(PhantomBandRules.ShouldUseElixir(cfg, 0.25f, elixirCount: 2, inCombat: true));
    }

    [Fact]
    public void Ether_GatesOnPotionInventory_BecauseEtherConsumesPotions()
    {
        var cfg = DefaultConfig(); // 2000 MP

        Assert.True(PhantomBandRules.ShouldUseEther(cfg, currentMp: 1500, maxMp: 10000, potionCount: 3, inCombat: true));
        Assert.False(PhantomBandRules.ShouldUseEther(cfg, currentMp: 1500, maxMp: 10000, potionCount: 0, inCombat: true));
        Assert.False(PhantomBandRules.ShouldUseEther(cfg, currentMp: 5000, maxMp: 10000, potionCount: 3, inCombat: true));
        // Jobs without an MP pool must never trigger ether usage.
        Assert.False(PhantomBandRules.ShouldUseEther(cfg, currentMp: 0, maxMp: 0, potionCount: 3, inCombat: true));
    }

    [Fact]
    public void Chakra_SplitsHpAndMpTriggers()
    {
        var cfg = DefaultConfig(); // HP 0.30, MP 3000

        Assert.True(PhantomBandRules.ShouldUseChakraForHp(cfg, 0.20f, inCombat: true));
        Assert.False(PhantomBandRules.ShouldUseChakraForHp(cfg, 0.50f, inCombat: true));
        Assert.True(PhantomBandRules.ShouldUseChakraForMp(cfg, currentMp: 2000, maxMp: 10000, inCombat: true));
        Assert.False(PhantomBandRules.ShouldUseChakraForMp(cfg, currentMp: 2000, maxMp: 0, inCombat: true));
    }

    [Fact]
    public void SelfMit_RequiresCombatAndLowHp()
    {
        Assert.True(PhantomBandRules.ShouldSelfMit(0.40f, inCombat: true));
        Assert.False(PhantomBandRules.ShouldSelfMit(0.40f, inCombat: false));
        Assert.False(PhantomBandRules.ShouldSelfMit(0.60f, inCombat: true));
    }

    [Fact]
    public void Interrupt_RequiresInterruptibleCastInRange()
    {
        Assert.True(PhantomBandRules.ShouldInterrupt(targetIsCasting: true, castInterruptible: true, distanceYalms: 3f, maxRangeYalms: 5f));
        Assert.False(PhantomBandRules.ShouldInterrupt(targetIsCasting: false, castInterruptible: true, distanceYalms: 3f, maxRangeYalms: 5f));
        Assert.False(PhantomBandRules.ShouldInterrupt(targetIsCasting: true, castInterruptible: false, distanceYalms: 3f, maxRangeYalms: 5f));
        Assert.False(PhantomBandRules.ShouldInterrupt(targetIsCasting: true, castInterruptible: true, distanceYalms: 8f, maxRangeYalms: 5f));
    }

    [Fact]
    public void Resuscitation_AndPray_UseConfigThresholds()
    {
        var cfg = DefaultConfig(); // resus 0.70; Pray off by default

        Assert.True(PhantomBandRules.ShouldResuscitate(cfg, 0.60f));
        Assert.False(PhantomBandRules.ShouldResuscitate(cfg, 0.80f));

        Assert.False(PhantomBandRules.ShouldPray(cfg, 0.50f)); // disabled by default
        cfg.KnightPrayAsHeal = true;
        Assert.True(PhantomBandRules.ShouldPray(cfg, 0.50f));
        Assert.False(PhantomBandRules.ShouldPray(cfg, 0.95f));
    }

    [Fact]
    public void LockoutStatusList_CoversTheRsrParitySet()
    {
        // RSR RotationLockoutStatus + Reassembled (its GeneralGCD gate).
        Assert.Equal(8, PhantomActions.LockoutStatusIds.Count);
        Assert.Contains(3670u, PhantomActions.LockoutStatusIds); // Reawakened
        Assert.Contains(2688u, PhantomActions.LockoutStatusIds); // Overheated
        Assert.Contains(1177u, PhantomActions.LockoutStatusIds); // Inner Release
        Assert.Contains(2606u, PhantomActions.LockoutStatusIds); // Eukrasia
        Assert.Contains(496u, PhantomActions.LockoutStatusIds);  // Mudra
        Assert.Contains(1186u, PhantomActions.LockoutStatusIds); // Ten Chi Jin
        Assert.Contains(851u, PhantomActions.LockoutStatusIds);  // Reassembled
    }
}
