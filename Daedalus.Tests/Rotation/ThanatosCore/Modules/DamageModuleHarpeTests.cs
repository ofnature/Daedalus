using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Moq;
using Daedalus.Rotation.ThanatosCore.Abilities;
using Daedalus.Rotation.ThanatosCore.Modules;
using Daedalus.Services.Action;
using Daedalus.Services.Targeting;
using Daedalus.Tests.Mocks;
using Daedalus.Tests.Rotation.Common.Scheduling;
using Daedalus.Tests.Rotation.ThanatosCore;
using Xunit;

namespace Daedalus.Tests.Rotation.ThanatosCore.Modules;

/// <summary>
/// Tests for the Harpe ranged filler — keeps the GCD rolling when forced out of melee for an AoE
/// mechanic and Harvest Moon (Soulsow) isn't up. Harpe has a cast time, so it's only used while
/// standing at range, never while moving without the instant Enhanced Harpe proc. (IsActionInRange
/// is native and returns false under test, so the target always reads as out of melee.)
/// </summary>
public class DamageModuleHarpeTests
{
    private readonly DamageModule _module = new();

    private static (Mock<ITargetingService> targeting, Mock<IActionService> actions) Setup()
    {
        var enemy = new Mock<IBattleNpc>();
        enemy.Setup(x => x.GameObjectId).Returns(99999UL);
        enemy.Setup(x => x.CurrentHp).Returns(10000u);
        enemy.Setup(x => x.MaxHp).Returns(10000u);

        var targeting = MockBuilders.CreateMockTargetingService();
        targeting.Setup(x => x.FindEnemyForAction(
                It.IsAny<EnemyTargetingStrategy>(), It.IsAny<uint>(), It.IsAny<IPlayerCharacter>()))
            .Returns(enemy.Object);

        var actions = MockBuilders.CreateMockActionService();
        actions.Setup(x => x.IsActionReady(It.IsAny<uint>())).Returns(true);
        return (targeting, actions);
    }

    [Fact]
    public void Harpe_Pushed_WhenOutOfMelee_AndStanding()
    {
        var (targeting, actions) = Setup();
        var scheduler = SchedulerFactory.CreateForTest(actionService: actions);
        var context = ThanatosTestContext.Create(
            actionService: actions, targetingService: targeting, level: 100, isMoving: false);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.Contains(scheduler.InspectGcdQueue(), c => c.Behavior == ThanatosAbilities.Harpe);
    }

    [Fact]
    public void Harpe_NotPushed_WhileMoving_WithoutEnhancedHarpe()
    {
        var (targeting, actions) = Setup();
        var scheduler = SchedulerFactory.CreateForTest(actionService: actions);
        var context = ThanatosTestContext.Create(
            actionService: actions, targetingService: targeting, level: 100,
            isMoving: true, hasEnhancedHarpe: false);

        _module.CollectCandidates(context, scheduler, isMoving: true);

        Assert.DoesNotContain(scheduler.InspectGcdQueue(), c => c.Behavior == ThanatosAbilities.Harpe);
    }

    [Fact]
    public void Harpe_Pushed_WhileMoving_WithEnhancedHarpe()
    {
        var (targeting, actions) = Setup();
        var scheduler = SchedulerFactory.CreateForTest(actionService: actions);
        var context = ThanatosTestContext.Create(
            actionService: actions, targetingService: targeting, level: 100,
            isMoving: true, hasEnhancedHarpe: true);

        _module.CollectCandidates(context, scheduler, isMoving: true);

        Assert.Contains(scheduler.InspectGcdQueue(), c => c.Behavior == ThanatosAbilities.Harpe);
    }
}
