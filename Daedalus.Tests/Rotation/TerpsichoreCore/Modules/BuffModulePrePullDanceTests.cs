using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using Moq;
using Daedalus.Rotation.TerpsichoreCore.Abilities;
using Daedalus.Rotation.TerpsichoreCore.Context;
using Daedalus.Rotation.TerpsichoreCore.Modules;
using Daedalus.Services.Targeting;
using Daedalus.Tests.Mocks;
using Daedalus.Tests.Rotation.Common.Scheduling;
using Xunit;

namespace Daedalus.Tests.Rotation.TerpsichoreCore.Modules;

/// <summary>
/// Field report 2026-07-26: an idle Dancer finisher-looped out of combat — the pre-pull
/// Standard Step re-fired every time the 60s buff dropped, gated only by "not in a
/// sanctuary". Contract now: out of combat, the dance only starts when a pull is
/// imminent (a live enemy hard target — players target the boss pre-pull, automation
/// bridges hard-target the mob before walking in).
/// </summary>
public class BuffModulePrePullDanceTests
{
    private readonly BuffModule _module = new();

    [Fact]
    public void OutOfCombat_NoTarget_NeverDances()
    {
        var (context, scheduler) = Setup(userTarget: null);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.DoesNotContain(scheduler.InspectGcdQueue(), c => c.Behavior == TerpsichoreAbilities.StandardStep);
    }

    [Fact]
    public void OutOfCombat_LiveHardTarget_PrePullDanceFires()
    {
        var (context, scheduler) = Setup(userTarget: MakeEnemy(isDead: false));

        _module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.Contains(scheduler.InspectGcdQueue(), c => c.Behavior == TerpsichoreAbilities.StandardStep);
    }

    [Fact]
    public void OutOfCombat_DeadTarget_NeverDances()
    {
        var (context, scheduler) = Setup(userTarget: MakeEnemy(isDead: true));

        _module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.DoesNotContain(scheduler.InspectGcdQueue(), c => c.Behavior == TerpsichoreAbilities.StandardStep);
    }

    [Fact]
    public void OutOfCombat_BuffAlreadyUp_NeverRedances()
    {
        var (context, scheduler) = Setup(userTarget: MakeEnemy(isDead: false), hasStandardFinish: true);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.DoesNotContain(scheduler.InspectGcdQueue(), c => c.Behavior == TerpsichoreAbilities.StandardStep);
    }

    private static IBattleNpc MakeEnemy(bool isDead)
    {
        var enemy = new Mock<IBattleNpc>();
        enemy.Setup(x => x.IsDead).Returns(isDead);
        enemy.Setup(x => x.GameObjectId).Returns(4242UL);
        return enemy.Object;
    }

    private static (ITerpsichoreContext Context, Daedalus.Rotation.Common.Scheduling.RotationScheduler Scheduler)
        Setup(IBattleNpc? userTarget, bool hasStandardFinish = false)
    {
        var targeting = MockBuilders.CreateMockTargetingService();
        targeting.Setup(x => x.GetUserEnemyTarget()).Returns(userTarget);

        var actionService = MockBuilders.CreateMockActionService();
        actionService.Setup(x => x.GetCooldownRemaining(It.IsAny<uint>())).Returns(0f);

        var scheduler = SchedulerFactory.CreateForTest(actionService: actionService);
        var context = TerpsichoreTestContext.Create(
            actionService: actionService,
            targetingService: targeting,
            level: 100,
            inCombat: false,
            hasStandardFinish: hasStandardFinish);

        return (context, scheduler);
    }
}
