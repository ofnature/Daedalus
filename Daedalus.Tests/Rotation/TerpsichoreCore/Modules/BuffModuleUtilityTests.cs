using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Moq;
using Daedalus.Rotation.TerpsichoreCore.Abilities;
using Daedalus.Rotation.TerpsichoreCore.Modules;
using Daedalus.Services.Targeting;
using Daedalus.Tests.Mocks;
using Daedalus.Tests.Rotation.Common.Scheduling;
using Xunit;

namespace Daedalus.Tests.Rotation.TerpsichoreCore.Modules;

/// <summary>
/// DNC role utility / defensives (2026-07-03): Second Wind, Curing Waltz, and Shield Samba
/// existed only as status checks — never pushed. All three physical ranged jobs shared the gap.
/// </summary>
public class BuffModuleUtilityTests
{
    private readonly BuffModule _module = new();

    [Fact]
    public void ShieldSamba_PushedOnBigPull()
    {
        var (context, scheduler) = Setup(engagedEnemies: 4);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.Contains(scheduler.InspectOgcdQueue(), c => c.Behavior == TerpsichoreAbilities.ShieldSamba);
    }

    [Fact]
    public void ShieldSamba_NotPushed_OnSmallPull()
    {
        var (context, scheduler) = Setup(engagedEnemies: 1);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.DoesNotContain(scheduler.InspectOgcdQueue(), c => c.Behavior == TerpsichoreAbilities.ShieldSamba);
    }

    [Fact]
    public void SecondWindAndCuringWaltz_PushedAtLowHp()
    {
        var (context, scheduler) = Setup(currentHp: 20000);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        var ogcds = scheduler.InspectOgcdQueue();
        Assert.Contains(ogcds, c => c.Behavior == TerpsichoreAbilities.SecondWind);
        Assert.Contains(ogcds, c => c.Behavior == TerpsichoreAbilities.CuringWaltz);
    }

    [Fact]
    public void SecondWindAndCuringWaltz_NotPushed_AtFullHp()
    {
        var (context, scheduler) = Setup(currentHp: 50000);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        var ogcds = scheduler.InspectOgcdQueue();
        Assert.DoesNotContain(ogcds, c => c.Behavior == TerpsichoreAbilities.SecondWind);
        Assert.DoesNotContain(ogcds, c => c.Behavior == TerpsichoreAbilities.CuringWaltz);
    }

    private static (Daedalus.Rotation.TerpsichoreCore.Context.ITerpsichoreContext context,
        Daedalus.Rotation.Common.Scheduling.RotationScheduler scheduler) Setup(
        int engagedEnemies = 1,
        uint currentHp = 50000)
    {
        var enemy = new Mock<IBattleNpc>();
        enemy.Setup(x => x.GameObjectId).Returns(31337UL);
        enemy.Setup(x => x.CurrentHp).Returns(1000000u);
        enemy.Setup(x => x.MaxHp).Returns(1000000u);

        var targeting = MockBuilders.CreateMockTargetingService(countEnemiesInRange: engagedEnemies);
        targeting.Setup(x => x.FindEnemy(
                It.IsAny<EnemyTargetingStrategy>(), It.IsAny<float>(), It.IsAny<IPlayerCharacter>()))
            .Returns(enemy.Object);

        var actionService = MockBuilders.CreateMockActionService();
        var scheduler = SchedulerFactory.CreateForTest(actionService: actionService);
        var context = TerpsichoreTestContext.Create(
            actionService: actionService,
            targetingService: targeting,
            level: 100,
            currentHp: currentHp);

        return (context, scheduler);
    }
}
