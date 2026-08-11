using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Moq;
using Daedalus.Data;
using Daedalus.Rotation.Common.Scheduling;
using Daedalus.Rotation.HermesCore.Context;
using Daedalus.Rotation.HermesCore.Helpers;
using Daedalus.Rotation.HermesCore.Modules;
using Daedalus.Services;
using Daedalus.Services.Targeting;
using Daedalus.Tests.Mocks;
using Daedalus.Tests.Rotation.Common.Scheduling;
using Daedalus.Tests.Rotation.HermesCore;
using Xunit;

namespace Daedalus.Tests.Rotation.HermesCore.Helpers;

[Collection("HermesMudraStaticState")] // HermesNinjutsuMudraExecutor holds per-frame statics
public class HermesNinjutsuExecutorTests
{
    [Fact]
    public void TryExecuteTenChiJin_WhenStepNotExecutable_SetsIsTcjStepPending()
    {
        var (context, _) = CreateTcjContext(canExecute: false);

        var executor = new HermesNinjutsuExecutor();
        executor.TryExecuteTenChiJin(context, null, enemyCount: 1);

        Assert.True(executor.IsTcjStepPending);
    }

    [Fact]
    public void ResetTcjTrack_ClearsPendingAfterBuffEnds()
    {
        var (context, _) = CreateTcjContext(canExecute: false);
        var executor = new HermesNinjutsuExecutor();

        executor.TryExecuteTenChiJin(context, null, 1);
        Assert.True(executor.IsTcjStepPending);

        executor.ResetTcjTrack();
        Assert.False(executor.IsTcjStepPending);
    }

    [Fact]
    public void CollectCandidates_SameExecutor_PendingVisibleToDamageModule()
    {
        var (context, scheduler) = CreateTcjContext(canExecute: false);
        var executor = new HermesNinjutsuExecutor();
        var ninjutsu = new NinjutsuModule(executor);
        var damage = new DamageModule(executor: executor);

        ninjutsu.CollectCandidates(context, scheduler, isMoving: false);
        Assert.True(executor.IsTcjStepPending);

        damage.CollectCandidates(context, scheduler, isMoving: false);
        Assert.True(context.Debug.IsTcjStepPending);
    }

    private static (IHermesContext Context, RotationScheduler Scheduler) CreateTcjContext(bool canExecute)
    {
        var enemy = new Mock<IBattleNpc>();
        enemy.Setup(x => x.GameObjectId).Returns(99999UL);

        var targeting = MockBuilders.CreateMockTargetingService();
        targeting.Setup(x => x.FindEnemy(
                It.IsAny<EnemyTargetingStrategy>(), It.IsAny<float>(), It.IsAny<IPlayerCharacter>()))
            .Returns(enemy.Object);
        targeting.Setup(x => x.FindEnemyForAction(
                It.IsAny<EnemyTargetingStrategy>(), It.IsAny<uint>(), It.IsAny<IPlayerCharacter>()))
            .Returns(enemy.Object);
        targeting.Setup(x => x.CountEnemiesInRange(It.IsAny<float>(), It.IsAny<IPlayerCharacter>()))
            .Returns(1);

        var actionService = MockBuilders.CreateMockActionService();
        actionService.Setup(x => x.CanExecuteActionId(It.IsAny<uint>())).Returns(canExecute);
        actionService.Setup(x => x.GetAdjustedActionId(NINActions.Ten.ActionId))
            .Returns(NINActions.TenChiJinAdjusted.FumaShurikenSt);
        actionService.Setup(x => x.GetAdjustedActionId(NINActions.Chi.ActionId)).Returns(0u);
        actionService.Setup(x => x.GetAdjustedActionId(NINActions.Jin.ActionId)).Returns(0u);

        var scheduler = SchedulerFactory.CreateForTest(actionService: actionService);
        var context = HermesTestContext.Create(
            actionService: actionService,
            targetingService: targeting,
            hasTenChiJin: true,
            inCombat: true);

        return (context, scheduler);
    }
}
