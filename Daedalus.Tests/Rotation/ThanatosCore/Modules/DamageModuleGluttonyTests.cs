using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Moq;
using Daedalus.Data;
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
/// Regression tests for the Gluttony soul-spender gate. Gluttony was gated on
/// <c>!IsActionReady(Enshroud)</c>, but Enshroud is gauge-gated so its short cooldown is up almost
/// always — that clause was permanently false and Gluttony never fired (no Executioner stacks, slow
/// Shroud generation, weak burst). It must now fire on cooldown when Soul is available.
/// </summary>
public class DamageModuleGluttonyTests
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
        // Everything ready — crucially INCLUDING Enshroud, which is the old bug condition that
        // (incorrectly) blocked Gluttony.
        actions.Setup(x => x.IsActionReady(It.IsAny<uint>())).Returns(true);
        return (targeting, actions);
    }

    [Fact]
    public void Gluttony_Pushed_WhenSoulReady_EvenIfEnshroudCooldownIsUp()
    {
        var (targeting, actions) = Setup();
        var scheduler = SchedulerFactory.CreateForTest(actionService: actions);
        var context = ThanatosTestContext.Create(
            actionService: actions, targetingService: targeting, level: 100,
            soul: 50, shroud: 0);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.Contains(scheduler.InspectOgcdQueue(), c => c.Behavior == ThanatosAbilities.Gluttony);
    }

    [Fact]
    public void Gluttony_Pushed_EvenWhenShroudAtOrAbove50()
    {
        // Old code also gated on Shroud < 50; Gluttony (Soul) and Enshroud (Shroud) are independent.
        var (targeting, actions) = Setup();
        var scheduler = SchedulerFactory.CreateForTest(actionService: actions);
        var context = ThanatosTestContext.Create(
            actionService: actions, targetingService: targeting, level: 100,
            soul: 50, shroud: 60);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.Contains(scheduler.InspectOgcdQueue(), c => c.Behavior == ThanatosAbilities.Gluttony);
    }

    [Fact]
    public void Gluttony_NotPushed_WhenSoulBelowMinimum()
    {
        var (targeting, actions) = Setup();
        var scheduler = SchedulerFactory.CreateForTest(actionService: actions);
        var context = ThanatosTestContext.Create(
            actionService: actions, targetingService: targeting, level: 100,
            soul: 40, shroud: 0);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.DoesNotContain(scheduler.InspectOgcdQueue(), c => c.Behavior == ThanatosAbilities.Gluttony);
    }
}
