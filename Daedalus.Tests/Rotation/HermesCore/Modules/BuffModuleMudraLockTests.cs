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
using Daedalus.Tests.Rotation.HermesCore;
using Xunit;

namespace Daedalus.Tests.Rotation.HermesCore.Modules;

/// <summary>
/// Burst oGCDs blocked only during active plugin mudra sequence with game Mudra status (496).
/// Orphaned mudra status alone must not stall burst oGCDs (ABB).
/// </summary>
[Collection("HermesMudraStaticState")] // HermesNinjutsuMudraExecutor holds per-frame statics
public class BuffModuleMudraLockTests
{
    private readonly BuffModule _module = new();

    [Fact]
    public void CollectCandidates_AfterMudraStatusClears_DoesNotKeepStaleBuffState()
    {
        var (scheduler, context) = CreateReadyContext(hasGameMudraStatus: false);
        context.Debug.BuffState = "Stalled (mudra status)";

        _module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.NotEqual("Stalled (mudra status)", context.Debug.BuffState);
    }

    [Fact]
    public void CollectCandidates_OrphanedGameMudraStatus_AllowsBurstOgcds()
    {
        var (scheduler, context) = CreateReadyContext(hasGameMudraStatus: true);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.Contains(scheduler.InspectOgcdQueue(), c => c.Behavior == HermesAbilities.Dokumori);
    }

    [Fact]
    public void CollectCandidates_GameMudraStatusWithActiveSequence_BlocksBurstOgcds()
    {
        var helper = new MudraHelper();
        helper.StartSequence(NINActions.NinjutsuType.Suiton);

        var (scheduler, context) = CreateReadyContext(hasGameMudraStatus: true);
        Mock.Get(context).Setup(x => x.MudraHelper).Returns(helper);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.Empty(scheduler.InspectOgcdQueue());
        Assert.Equal("Stalled (mudra status)", context.Debug.BuffState);
    }

    [Fact]
    public void CollectCandidates_RabbitSlotWithActiveSequence_AllowsBurstOgcds()
    {
        var helper = new MudraHelper();
        helper.StartSequence(NINActions.NinjutsuType.Suiton);

        var actionService = MockBuilders.CreateMockActionService();
        actionService.Setup(x => x.IsActionReady(It.IsAny<uint>())).Returns(true);
        actionService.Setup(x => x.GetAdjustedActionId(NINActions.Ninjutsu.ActionId))
            .Returns(NINActions.RabbitMedium.ActionId);

        var (scheduler, context) = CreateReadyContext(hasGameMudraStatus: true, actionService: actionService);
        Mock.Get(context).Setup(x => x.MudraHelper).Returns(helper);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.Contains(scheduler.InspectOgcdQueue(), c => c.Behavior == HermesAbilities.Dokumori);
        Assert.NotEqual("Stalled (mudra status)", context.Debug.BuffState);
    }

    [Fact]
    public void CollectCandidates_HelperSequenceOnly_AllowsBurstOgcds()
    {
        var (scheduler, context) = CreateReadyContext(isMudraSequenceActive: true);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.Contains(scheduler.InspectOgcdQueue(), c => c.Behavior == HermesAbilities.Dokumori);
    }

    private static (RotationScheduler Scheduler, IHermesContext Context) CreateReadyContext(
        bool hasGameMudraStatus = false,
        bool isMudraSequenceActive = false,
        Mock<IActionService>? actionService = null)
    {
        var enemy = new Mock<IBattleNpc>();
        enemy.Setup(x => x.GameObjectId).Returns(99999UL);
        enemy.Setup(x => x.Name).Returns(new Dalamud.Game.Text.SeStringHandling.SeString());

        var targeting = MockBuilders.CreateMockTargetingService();
        targeting.Setup(x => x.FindEnemy(
                It.IsAny<EnemyTargetingStrategy>(), It.IsAny<float>(), It.IsAny<IPlayerCharacter>()))
            .Returns(enemy.Object);

        actionService ??= MockBuilders.CreateMockActionService();
        actionService.Setup(x => x.IsActionReady(It.IsAny<uint>())).Returns(true);

        var config = HermesTestContext.CreateDefaultNinjaConfiguration();
        config.Ninja.EnableMug = true;

        var scheduler = SchedulerFactory.CreateForTest(actionService: actionService);
        var context = HermesTestContext.Create(
            config: config,
            actionService: actionService,
            targetingService: targeting,
            ninki: 60,
            hasGameMudraStatus: hasGameMudraStatus,
            isMudraSequenceActive: isMudraSequenceActive);

        return (scheduler, context);
    }
}
