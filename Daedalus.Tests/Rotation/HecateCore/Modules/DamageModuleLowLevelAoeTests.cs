using Daedalus.Rotation.HecateCore.Abilities;
using Xunit;

namespace Daedalus.Tests.Rotation.HecateCore.Modules;

/// <summary>
/// AoE below the AoE spells: GetFireAoe / GetIceAoe fall back to plain Fire (below Fire II, Lv18) and
/// Blizzard (below Blizzard II, Lv12), but the push mapped them to the Fire II / Blizzard II behaviours,
/// which the scheduler drops below their MinLevel — the method had already returned, so nothing castable
/// was queued.
/// </summary>
public class DamageModuleLowLevelAoeTests
{
    [Fact]
    public void Lv15_AstralFire_Pack_QueuesFire()
    {
        var scheduler = HecateDamageScenario.Run(level: 15, inAstralFire: true, elementStacks: 1, packSize: 4);
        Assert.Contains(scheduler.InspectGcdQueue(), c => c.Behavior == HecateAbilities.Fire);
        Assert.DoesNotContain(scheduler.InspectGcdQueue(), c => c.Behavior == HecateAbilities.Fire2);
    }

    [Fact]
    public void Lv20_AstralFire_Pack_QueuesFireII()
    {
        var scheduler = HecateDamageScenario.Run(level: 20, inAstralFire: true, elementStacks: 1, packSize: 4);
        Assert.Contains(scheduler.InspectGcdQueue(), c => c.Behavior == HecateAbilities.Fire2);
    }

    [Fact]
    public void Lv10_UmbralIce_Pack_QueuesBlizzard()
    {
        var scheduler = HecateDamageScenario.Run(level: 10, inUmbralIce: true, elementStacks: 3, packSize: 4,
            currentMp: 3000);
        Assert.Contains(scheduler.InspectGcdQueue(), c => c.Behavior == HecateAbilities.Blizzard);
        Assert.DoesNotContain(scheduler.InspectGcdQueue(), c => c.Behavior == HecateAbilities.Blizzard2);
    }

    [Fact]
    public void Lv20_UmbralIce_Pack_QueuesBlizzardII()
    {
        var scheduler = HecateDamageScenario.Run(level: 20, inUmbralIce: true, elementStacks: 3, packSize: 4,
            currentMp: 3000);
        Assert.Contains(scheduler.InspectGcdQueue(), c => c.Behavior == HecateAbilities.Blizzard2);
    }
}
