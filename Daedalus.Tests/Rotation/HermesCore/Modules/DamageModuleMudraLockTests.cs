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
using Daedalus.Services.Action;
using Daedalus.Services.Targeting;

using Daedalus.Tests.Mocks;

using Daedalus.Tests.Rotation.Common.Scheduling;

using Xunit;



namespace Daedalus.Tests.Rotation.HermesCore.Modules;



/// <summary>

/// RSR mudra gate: status 496 + GcdRemaining >= 0.625s blocks combo GCDs and Ninki only.

/// </summary>

[Collection("HermesMudraStaticState")] // HermesNinjutsuMudraExecutor holds per-frame statics
public class DamageModuleMudraLockTests

{

    private readonly DamageModule _module = new();



    [Fact]

    public void CollectCandidates_AfterMudraStatusClears_DoesNotKeepStaleDamageState()

    {

        var (scheduler, context) = CreateReadyContext(hasGameMudraStatus: false);

        context.Debug.DamageState = "Stalled (mudra status)";



        _module.CollectCandidates(context, scheduler, isMoving: false);



        Assert.NotEqual("Stalled (mudra status)", context.Debug.DamageState);

    }



    [Fact]
    public void CollectCandidates_OrphanedGameMudraStatusWithGcdTime_AllowsGcds()
    {
        var (scheduler, context) = CreateReadyContext(
            hasGameMudraStatus: true,
            gcdRemaining: HermesMudraGate.RsrGcdRemainingThresholdSeconds);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.NotEmpty(scheduler.InspectGcdQueue());
        Assert.NotEqual("Stalled (mudra status)", context.Debug.DamageState);
    }



    [Fact]

    public void CollectCandidates_GameMudraStatusBelowGcdThreshold_AllowsGcds()

    {

        var (scheduler, context) = CreateReadyContext(

            hasGameMudraStatus: true,

            gcdRemaining: HermesMudraGate.RsrGcdRemainingThresholdSeconds - 0.001f);



        _module.CollectCandidates(context, scheduler, isMoving: false);



        Assert.NotEmpty(scheduler.InspectGcdQueue());

    }



    [Fact]
    public void CollectCandidates_QueuedSequenceWithTenOnCd_AllowsComboGcds()
    {
        var helper = new MudraHelper();
        helper.StartSequence(NINActions.NinjutsuType.Raiton);

        var actionService = MockBuilders.CreateMockActionService();
        actionService.Setup(x => x.IsActionReady(It.IsAny<uint>())).Returns(true);
        actionService.Setup(x => x.GetAdjustedActionId(NINActions.Ninjutsu.ActionId))
            .Returns(NINActions.Ninjutsu.ActionId);
        actionService.Setup(x => x.GetAdjustedActionId(NINActions.Ten.ActionId))
            .Returns(NINActions.Ten.ActionId);
        actionService.Setup(x => x.GetCurrentCharges(NINActions.Ten.ActionId)).Returns(0u);
        actionService.Setup(x => x.GetCooldownRemaining(NINActions.Ten.ActionId)).Returns(5f);
        actionService.Setup(x => x.GcdRemaining).Returns(0f);

        var (scheduler, context) = CreateReadyContext(hasGameMudraStatus: false, actionService: actionService);
        Mock.Get(context).Setup(x => x.MudraHelper).Returns(helper);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.NotEmpty(scheduler.InspectGcdQueue());
        Assert.NotEqual("Stalled (mudra status)", context.Debug.DamageState);
    }

    [Fact]
    public void CollectCandidates_QueuedSequenceWithTenReady_BlocksComboGcds()
    {
        var helper = new MudraHelper();
        helper.StartSequence(NINActions.NinjutsuType.Raiton);

        var actionService = MockBuilders.CreateMockActionService();
        actionService.Setup(x => x.IsActionReady(It.IsAny<uint>())).Returns(true);
        actionService.Setup(x => x.GetAdjustedActionId(NINActions.Ninjutsu.ActionId))
            .Returns(NINActions.Ninjutsu.ActionId);
        actionService.Setup(x => x.GetAdjustedActionId(NINActions.Ten.ActionId))
            .Returns(NINActions.Ten.ActionId);
        actionService.Setup(x => x.GetCurrentCharges(NINActions.Ten.ActionId)).Returns(1u);

        var (scheduler, context) = CreateReadyContext(hasGameMudraStatus: false, actionService: actionService);
        Mock.Get(context).Setup(x => x.MudraHelper).Returns(helper);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.Empty(scheduler.InspectGcdQueue());
        Assert.Equal("Stalled (mudra status)", context.Debug.DamageState);
    }

    [Fact]
    public void CollectCandidates_SequenceWithMudraStatus_TenOnCd_AllowsComboGcds()
    {
        var helper = new MudraHelper();
        helper.StartSequence(NINActions.NinjutsuType.Raiton);

        var actionService = MockBuilders.CreateMockActionService();
        actionService.Setup(x => x.IsActionReady(It.IsAny<uint>())).Returns(true);
        actionService.Setup(x => x.GetAdjustedActionId(NINActions.Ninjutsu.ActionId))
            .Returns(NINActions.Ninjutsu.ActionId);
        actionService.Setup(x => x.GetAdjustedActionId(NINActions.Ten.ActionId))
            .Returns(NINActions.Ten.ActionId);
        actionService.Setup(x => x.GetCurrentCharges(NINActions.Ten.ActionId)).Returns(0u);
        actionService.Setup(x => x.GetCooldownRemaining(NINActions.Ten.ActionId)).Returns(5f);
        actionService.Setup(x => x.GcdRemaining).Returns(1f);

        var (scheduler, context) = CreateReadyContext(
            hasGameMudraStatus: true,
            gcdRemaining: 1f,
            actionService: actionService);
        Mock.Get(context).Setup(x => x.MudraHelper).Returns(helper);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.NotEmpty(scheduler.InspectGcdQueue());
        Assert.NotEqual("Stalled (mudra status)", context.Debug.DamageState);
    }

    [Fact]
    public void CollectCandidates_SequenceWithMudraStatus_BlocksComboGcds()
    {
        var helper = new MudraHelper();
        helper.StartSequence(NINActions.NinjutsuType.Suiton);

        var (scheduler, context) = CreateReadyContext(
            hasGameMudraStatus: true,
            gcdRemaining: HermesMudraGate.RsrGcdRemainingThresholdSeconds);
        Mock.Get(context).Setup(x => x.MudraHelper).Returns(helper);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.Empty(scheduler.InspectGcdQueue());
        Assert.Equal("Stalled (mudra status)", context.Debug.DamageState);
    }

    [Fact]
    public void CollectCandidates_SequenceWithRabbitSlot_BlocksComboGcds()
    {
        var helper = new MudraHelper();
        helper.StartSequence(NINActions.NinjutsuType.Suiton);

        var actionService = MockBuilders.CreateMockActionService();
        actionService.Setup(x => x.IsActionReady(It.IsAny<uint>())).Returns(true);
        actionService.Setup(x => x.GetAdjustedActionId(NINActions.Ninjutsu.ActionId))
            .Returns(NINActions.RabbitMedium.ActionId);
        actionService.Setup(x => x.GcdRemaining).Returns(1f);

        var (scheduler, context) = CreateReadyContext(
            hasGameMudraStatus: true,
            gcdRemaining: 1f,
            actionService: actionService);
        Mock.Get(context).Setup(x => x.MudraHelper).Returns(helper);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.Empty(scheduler.InspectGcdQueue());
        Assert.Equal("Stalled (mudra status)", context.Debug.DamageState);
    }

    [Fact]
    public void CollectCandidates_DotonFinishStepPending_BlocksComboGcds()
    {
        var helper = new MudraHelper();
        helper.StartSequence(NINActions.NinjutsuType.Doton);

        var actionService = MockBuilders.CreateMockActionService();
        actionService.Setup(x => x.IsActionReady(It.IsAny<uint>())).Returns(true);
        actionService.Setup(x => x.GetAdjustedActionId(NINActions.Ninjutsu.ActionId))
            .Returns(NINActions.Doton.ActionId);
        actionService.Setup(x => x.CanExecuteActionId(NINActions.Ninjutsu.ActionId)).Returns(false);

        var (scheduler, context) = CreateReadyContext(
            hasGameMudraStatus: false,
            actionService: actionService);
        Mock.Get(context).Setup(x => x.MudraHelper).Returns(helper);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.Empty(scheduler.InspectGcdQueue());
        Assert.Equal("Stalled (mudra status)", context.Debug.DamageState);
    }

    [Fact]
    public void CollectCandidates_SequenceWithMudraStatus_StillAllowsFeint()
    {
        var helper = new MudraHelper();
        helper.StartSequence(NINActions.NinjutsuType.Suiton);

        var (scheduler, context) = CreateReadyContext(
            hasGameMudraStatus: true,
            gcdRemaining: 1f,
            level: 22);
        Mock.Get(context).Setup(x => x.MudraHelper).Returns(helper);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        var ogcd = scheduler.InspectOgcdQueue();
        Assert.Contains(ogcd, c => c.Behavior == HermesAbilities.Feint);
        Assert.Empty(scheduler.InspectGcdQueue());
    }



    [Fact]

    public void CollectCandidates_TcjStepPending_SuppressesComboGcds()

    {

        var executor = new StubTcjExecutor(isPending: true);

        var module = new DamageModule(executor: executor);

        var (scheduler, context) = CreateReadyContext(hasTenChiJin: true);



        module.CollectCandidates(context, scheduler, isMoving: false);



        Assert.Empty(scheduler.InspectGcdQueue());

        Assert.Equal("Paused (TCJ step pending)", context.Debug.DamageState);

        Assert.True(context.Debug.IsTcjStepPending);

    }



    [Fact]

    public void CollectCandidates_TenChiJinActiveWithoutPending_AllowsGcds()

    {

        var executor = new StubTcjExecutor(isPending: false);

        var module = new DamageModule(executor: executor);

        var (scheduler, context) = CreateReadyContext(hasTenChiJin: true);



        module.CollectCandidates(context, scheduler, isMoving: false);



        Assert.NotEmpty(scheduler.InspectGcdQueue());

    }



    [Fact]

    public void CollectCandidates_GameMudraStatus_StillAllowsFeint()

    {

        var (scheduler, context) = CreateReadyContext(

            hasGameMudraStatus: true,

            gcdRemaining: 1f,

            level: 22);



        _module.CollectCandidates(context, scheduler, isMoving: false);



        var ogcd = scheduler.InspectOgcdQueue();

        Assert.Contains(ogcd, c => c.Behavior == HermesAbilities.Feint);

        Assert.DoesNotContain(ogcd, c => c.Behavior == HermesAbilities.Bhavacakra);

    }



    [Fact]
    public void CollectCandidates_OrphanedGameMudraStatus_AllowsNinkiSpender()
    {
        var (scheduler, context) = CreateReadyContext(
            hasGameMudraStatus: true,
            gcdRemaining: 1f,
            ninki: 100);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.Contains(scheduler.InspectOgcdQueue(), c => c.Behavior == HermesAbilities.Bhavacakra);
    }



    [Fact]
    public void NinkiSpender_HeldDuringMugWindow_OutsideTrickAttack_WhenBurstPoolingEnabled()
    {
        var (scheduler, context) = CreateReadyContext(ninki: 80, inMug: true, inTrickAttack: false);
        context.Configuration.Ninja.EnableBurstPooling = true;

        _module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.DoesNotContain(scheduler.InspectOgcdQueue(), c => c.Behavior == HermesAbilities.Bhavacakra);
    }

    [Fact]
    public void NinkiSpender_AllowedDuringMugWindow_OutsideTrickAttack_WhenAbb()
    {
        var (scheduler, context) = CreateReadyContext(ninki: 100, inMug: true, inTrickAttack: false);
        context.Configuration.Ninja.EnableBurstPooling = false;

        _module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.Contains(scheduler.InspectOgcdQueue(), c => c.Behavior == HermesAbilities.Bhavacakra);
    }



    [Fact]

    public void NinkiSpender_AllowedDuringTrickAttack_EvenInMugWindow()

    {

        var (scheduler, context) = CreateReadyContext(ninki: 100, inMug: true, inTrickAttack: true);



        _module.CollectCandidates(context, scheduler, isMoving: false);



        Assert.Contains(scheduler.InspectOgcdQueue(), c => c.Behavior == HermesAbilities.Bhavacakra);

    }



    private static (RotationScheduler Scheduler, IHermesContext Context) CreateReadyContext(

        bool hasGameMudraStatus = false,

        bool isMudraSequenceActive = false,

        bool hasTenChiJin = false,

        int ninki = 0,

        bool inMug = false,

        bool inTrickAttack = false,

        float? gcdRemaining = null,

        byte level = 100,

        Mock<IActionService>? actionService = null)

    {

        var enemy = CreateMockEnemy();

        var targeting = MockBuilders.CreateMockTargetingService();

        targeting.Setup(x => x.FindEnemyForAction(

                It.IsAny<EnemyTargetingStrategy>(), It.IsAny<uint>(), It.IsAny<IPlayerCharacter>()))

            .Returns(enemy.Object);

        targeting.Setup(x => x.CountEnemiesInRange(It.IsAny<float>(), It.IsAny<IPlayerCharacter>()))

            .Returns(1);



        actionService ??= MockBuilders.CreateMockActionService();

        actionService.Setup(x => x.IsActionReady(It.IsAny<uint>())).Returns(true);

        if (gcdRemaining.HasValue)

            actionService.Setup(x => x.GcdRemaining).Returns(gcdRemaining.Value);



        var scheduler = SchedulerFactory.CreateForTest(actionService: actionService);

        var context = HermesTestContext.Create(

            actionService: actionService,

            targetingService: targeting,

            level: level,

            ninki: ninki,

            hasRaijuReady: true,

            hasPhantomKamaitachiReady: true,

            comboStep: 2,

            lastComboAction: NINActions.GustSlash.ActionId,

            comboTimeRemaining: 10f,

            hasGameMudraStatus: hasGameMudraStatus,

            isMudraSequenceActive: isMudraSequenceActive,

            hasTenChiJin: hasTenChiJin,

            inMug: inMug,

            inTrickAttack: inTrickAttack);



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


