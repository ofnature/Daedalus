using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Moq;
using Daedalus.Data;
using Daedalus.Rotation.Common.Scheduling;
using Daedalus.Rotation.HermesCore.Abilities;
using Daedalus.Rotation.HermesCore.Context;
using Daedalus.Rotation.HermesCore.Helpers;
using Daedalus.Rotation.HermesCore.Modules;
using Daedalus.Services;
using Daedalus.Services.Targeting;
using Daedalus.Tests.Mocks;
using Daedalus.Tests.Rotation.Common.Scheduling;
using Daedalus.Tests.Rotation.HermesCore;
using Xunit;

namespace Daedalus.Tests.Rotation.HermesCore.Modules;

[Collection("HermesMudraStaticState")] // HermesNinjutsuMudraExecutor holds per-frame statics
public sealed class DamageModuleTenChiJinLockTests
{
    [Fact]
    public void CollectCandidates_TcjStepPending_SuppressesComboGcds()
    {
        var module = new DamageModule(executor: new StubTcjExecutor(isPending: true));
        var (scheduler, context) = CreateContext(hasTenChiJin: true);

        module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.Empty(scheduler.InspectGcdQueue());
        Assert.Equal("Paused (TCJ step pending)", context.Debug.DamageState);
    }

    [Fact]
    public void CollectCandidates_TcjActiveWithoutPending_AllowsComboGcds()
    {
        var module = new DamageModule(executor: new StubTcjExecutor(isPending: false));
        var (scheduler, context) = CreateContext(hasTenChiJin: true);

        module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.NotEmpty(scheduler.InspectGcdQueue());
    }

    [Fact]
    public void CollectCandidates_TcjActive_StillAllowsFeint()
    {
        var module = new DamageModule(executor: new StubTcjExecutor(isPending: false));
        var (scheduler, context) = CreateContext(hasTenChiJin: true, level: 22);

        module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.Contains(scheduler.InspectOgcdQueue(), c => c.Behavior == HermesAbilities.Feint);
    }

    private static (RotationScheduler Scheduler, IHermesContext Context) CreateContext(
        bool hasTenChiJin,
        byte level = 100)
    {
        var enemy = CreateMockEnemy();
        var targeting = MockBuilders.CreateMockTargetingService();
        targeting.Setup(x => x.FindEnemyForAction(
                It.IsAny<EnemyTargetingStrategy>(), It.IsAny<uint>(), It.IsAny<IPlayerCharacter>()))
            .Returns(enemy.Object);
        targeting.Setup(x => x.CountEnemiesInRange(It.IsAny<float>(), It.IsAny<IPlayerCharacter>()))
            .Returns(1);

        var actionService = MockBuilders.CreateMockActionService();
        var scheduler = SchedulerFactory.CreateForTest(actionService: actionService);
        var context = HermesTestContext.Create(
            actionService: actionService,
            targetingService: targeting,
            level: level,
            hasTenChiJin: hasTenChiJin,
            hasRaijuReady: true,
            hasPhantomKamaitachiReady: true,
            comboStep: 2,
            lastComboAction: NINActions.GustSlash.ActionId,
            comboTimeRemaining: 10f);

        return (scheduler, context);
    }

    private static Mock<IBattleNpc> CreateMockEnemy(ulong objectId = 99999UL)
    {
        var mock = new Mock<IBattleNpc>();
        mock.Setup(x => x.GameObjectId).Returns(objectId);
        mock.Setup(x => x.CurrentHp).Returns(10000u);
        mock.Setup(x => x.MaxHp).Returns(10000u);
        return mock;
    }

    private sealed class StubTcjExecutor(bool isPending) : IHermesNinjutsuExecutor
    {
        public bool IsTcjStepPending { get; } = isPending;

        public void ResetTcjTrack() { }

        public bool TryExecuteTenChiJin(IHermesContext context, IBattleChara? target, int enemyCount) => false;
    }
}
