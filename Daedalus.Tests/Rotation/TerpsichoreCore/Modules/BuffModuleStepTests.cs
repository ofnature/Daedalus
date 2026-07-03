using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Moq;
using Daedalus.Rotation.TerpsichoreCore.Abilities;
using Daedalus.Rotation.TerpsichoreCore.Helpers;
using Daedalus.Rotation.TerpsichoreCore.Modules;
using Daedalus.Services.Targeting;
using Daedalus.Tests.Mocks;
using Daedalus.Tests.Rotation.Common.Scheduling;
using Xunit;

namespace Daedalus.Tests.Rotation.TerpsichoreCore.Modules;

/// <summary>
/// Regression tests from the first DNC live run (Porta Decumana, 2026-07-02): Standard and
/// Technical Step are WEAPONSKILLS (own recast group + roll the global) but were pushed as
/// oGCDs behind an IsActionReady gate — the combination meant NEITHER step ever fired.
/// </summary>
public class BuffModuleStepTests
{
    private readonly BuffModule _module = new();

    [Fact]
    public void StandardStep_PushedAsGcd_WhenReady()
    {
        var (context, scheduler) = Setup();

        _module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.Contains(scheduler.InspectGcdQueue(), c => c.Behavior == TerpsichoreAbilities.StandardStep);
    }

    [Fact]
    public void StandardStep_NeverInOgcdQueue()
    {
        var (context, scheduler) = Setup();

        _module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.DoesNotContain(scheduler.InspectOgcdQueue(), c => c.Behavior == TerpsichoreAbilities.StandardStep);
    }

    [Fact]
    public void TechnicalStep_PushedAsGcd_WhenReady()
    {
        var (context, scheduler) = Setup();

        _module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.Contains(scheduler.InspectGcdQueue(), c => c.Behavior == TerpsichoreAbilities.TechnicalStep);
    }

    [Fact]
    public void WhileDancing_NoOgcdsArePushed()
    {
        // Dancing locks every other weaponskill/ability game-side — pushing them just spams
        // dispatch rejections.
        var (context, scheduler) = Setup(isDancing: true);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.Empty(scheduler.InspectOgcdQueue());
    }

    [Fact]
    public void GetJobPriority_UnknownJob_IsStillSelectable()
    {
        // int.MaxValue could never win the `< bestPriority` scan — unknown jobs left the
        // dancer partnerless instead of being a last-resort partner.
        Assert.True(TerpsichorePartyHelper.GetJobPriority(9999) < int.MaxValue);
    }

    private static (Daedalus.Rotation.TerpsichoreCore.Context.ITerpsichoreContext context,
        Daedalus.Rotation.Common.Scheduling.RotationScheduler scheduler) Setup(bool isDancing = false)
    {
        var enemy = new Mock<IBattleNpc>();
        enemy.Setup(x => x.GameObjectId).Returns(4242UL);
        enemy.Setup(x => x.CurrentHp).Returns(1000000u);
        enemy.Setup(x => x.MaxHp).Returns(1000000u);

        var targeting = MockBuilders.CreateMockTargetingService();
        targeting.Setup(x => x.FindEnemy(
                It.IsAny<EnemyTargetingStrategy>(), It.IsAny<float>(), It.IsAny<IPlayerCharacter>()))
            .Returns(enemy.Object);

        var actionService = MockBuilders.CreateMockActionService();
        actionService.Setup(x => x.GetCooldownRemaining(It.IsAny<uint>())).Returns(0f);

        var scheduler = SchedulerFactory.CreateForTest(actionService: actionService);
        var context = TerpsichoreTestContext.Create(
            actionService: actionService,
            targetingService: targeting,
            level: 100,
            isDancing: isDancing);

        return (context, scheduler);
    }
}
