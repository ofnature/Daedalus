using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Moq;
using Daedalus.Rotation.Common.Scheduling;
using Daedalus.Rotation.IrisCore.Abilities;
using Daedalus.Rotation.IrisCore.Modules;
using Daedalus.Services.Targeting;
using Daedalus.Tests.Mocks;
using Daedalus.Tests.Rotation.Common.Scheduling;
using Daedalus.Tests.Rotation.IrisCore;
using Xunit;

namespace Daedalus.Tests.Rotation.IrisCore.Modules;

/// <summary>
/// Hammer Time below Hammer Brush (Lv86): the Stamp never morphs, so every Hammer Time stack is another
/// Hammer Stamp. The old gate required 3 stacks unless a combo step was set, and step 1 then waited for a
/// Hammer Brush that never appears — so the leftover Stamps were never pressed and Hammer Time expired.
/// RSR ModifyHammerStampPvE: HammerStacks == 3 || (!EnhancedPictomancyIiTrait.EnoughLevel &amp;&amp; HammerStacks > 0).
/// </summary>
public class DamageModuleHammerLowLevelTests
{
    [Fact]
    public void Lv70_TwoStacksLeft_NoComboStep_PushesHammerStamp()
    {
        var scheduler = Collect(level: 70, hasHammerTime: true, stacks: 2, comboStep: 0);
        Assert.Contains(scheduler.InspectGcdQueue(), c => c.Behavior == IrisAbilities.HammerStamp);
    }

    [Fact]
    public void Lv70_TwoStacksLeft_ComboStepOne_PushesHammerStamp()
    {
        // Step 1 (last GCD was a Stamp) must not wait for a Hammer Brush that doesn't exist below Lv86.
        var scheduler = Collect(level: 70, hasHammerTime: true, stacks: 2, comboStep: 1);
        Assert.Contains(scheduler.InspectGcdQueue(), c => c.Behavior == IrisAbilities.HammerStamp);
    }

    [Fact]
    public void Lv70_NoHammerTime_NoStampPushed()
    {
        var scheduler = Collect(level: 70, hasHammerTime: false, stacks: 0, comboStep: 0);
        Assert.DoesNotContain(scheduler.InspectGcdQueue(), c => c.Behavior == IrisAbilities.HammerStamp);
    }

    [Fact]
    public void Lv100_TwoStacks_NoComboStep_DoesNotRestartStamp()
    {
        // Guard: at 86+ the combo continues through Brush/Polish; a fresh Stamp still needs all 3 stacks.
        var scheduler = Collect(level: 100, hasHammerTime: true, stacks: 2, comboStep: 0);
        Assert.DoesNotContain(scheduler.InspectGcdQueue(), c => c.Behavior == IrisAbilities.HammerStamp);
    }

    private static RotationScheduler Collect(byte level, bool hasHammerTime, int stacks, int comboStep)
    {
        var enemy = new Mock<IBattleNpc>();
        enemy.Setup(x => x.GameObjectId).Returns(99999UL);
        enemy.Setup(x => x.CurrentHp).Returns(10000u);
        enemy.Setup(x => x.MaxHp).Returns(10000u);
        var targeting = MockBuilders.CreateMockTargetingService();
        targeting.Setup(x => x.FindEnemy(
                It.IsAny<EnemyTargetingStrategy>(), It.IsAny<float>(), It.IsAny<IPlayerCharacter>()))
            .Returns(enemy.Object);

        var actionService = MockBuilders.CreateMockActionService();
        var scheduler = SchedulerFactory.CreateForTest(actionService: actionService);
        var context = IrisTestContext.Create(
            actionService: actionService,
            targetingService: targeting,
            level: level,
            inCombat: true,
            hasHammerTime: hasHammerTime,
            isInHammerCombo: hasHammerTime,
            hammerTimeStacks: stacks,
            hammerComboStep: comboStep,
            needsCreatureMotif: false,
            needsWeaponMotif: false,
            needsLandscapeMotif: false);

        new DamageModule().CollectCandidates(context, scheduler, isMoving: false);
        return scheduler;
    }
}
