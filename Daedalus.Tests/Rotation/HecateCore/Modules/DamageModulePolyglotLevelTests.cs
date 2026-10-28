using Daedalus.Data;
using Daedalus.Rotation.HecateCore.Abilities;
using Xunit;

namespace Daedalus.Tests.Rotation.HecateCore.Modules;

/// <summary>
/// Polyglot cap by level (Trait sheet: Enhanced Polyglot Lv80 → 2 stacks, Enhanced Polyglot II Lv98 → 3;
/// 1 below 80 — RSR IsPolyglotStacksMaxed). The old "level >= 98 ? 3 : 2" never saw the cap at Lv70-79,
/// and the only other spend path needed Xenoglossy (Lv80) or an AoE pack, so Foul was never cast single
/// target (RSR UsePolyglot: Foul skips the AoE check while Xenoglossy isn't learned).
/// </summary>
public class DamageModulePolyglotLevelTests
{
    [Fact]
    public void Lv75_OnePolyglot_AtCap_SpendsFoulSingleTarget()
    {
        var scheduler = HecateDamageScenario.Run(level: 75, inAstralFire: true, polyglotStacks: 1, umbralHearts: 3);
        Assert.Contains(scheduler.InspectGcdQueue(), c => c.Behavior == HecateAbilities.Foul);
    }

    [Fact]
    public void Lv75_Moving_OnePolyglot_UsesFoulSingleTarget()
    {
        var scheduler = HecateDamageScenario.Run(level: 75, inAstralFire: true, polyglotStacks: 1, umbralHearts: 3,
            isMoving: true);
        Assert.Contains(scheduler.InspectGcdQueue(), c => c.Behavior == HecateAbilities.Foul);
    }

    [Fact]
    public void Lv85_OnePolyglot_NotAtCap_NoForcedSpend()
    {
        var scheduler = HecateDamageScenario.Run(level: 85, inAstralFire: true, polyglotStacks: 1, umbralHearts: 3);
        Assert.DoesNotContain(scheduler.InspectGcdQueue(), c => c.Behavior == HecateAbilities.Xenoglossy);
        Assert.DoesNotContain(scheduler.InspectGcdQueue(), c => c.Behavior == HecateAbilities.Foul);
    }

    [Fact]
    public void Lv85_TwoPolyglot_AtCap_SpendsXenoglossy()
    {
        var scheduler = HecateDamageScenario.Run(level: 85, inAstralFire: true, polyglotStacks: 2, umbralHearts: 3);
        Assert.Contains(scheduler.InspectGcdQueue(), c => c.Behavior == HecateAbilities.Xenoglossy);
    }

    [Fact]
    public void Lv75_FoulNotLearned_NoPolyglotPush()
    {
        var scheduler = HecateDamageScenario.Run(level: 75, inAstralFire: true, polyglotStacks: 1, umbralHearts: 3,
            notLearned: new[] { BLMActions.Foul.ActionId });
        Assert.DoesNotContain(scheduler.InspectGcdQueue(), c => c.Behavior == HecateAbilities.Foul);
        Assert.Contains(scheduler.InspectGcdQueue(), c => c.Behavior == HecateAbilities.Fire4);
    }
}
