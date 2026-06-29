using System.Linq;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Moq;
using Daedalus.Rotation.Common.Scheduling;
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
/// Regression tests for Death's Design priority. DD was pushed at priority 7 (below Soul Reaver /
/// Plentiful Harvest / Harvest Moon), so when it was absent — pull start, or after swapping to an
/// un-DoT'd mob in AoE — it lost the GCD to everything else and stayed off the current target. That
/// blocked Enshroud all fight (Enshroud requires Death's Design). When absent it must apply urgently.
/// </summary>
public class DamageModuleDeathsDesignTests
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

    private static System.Collections.Generic.List<Daedalus.Rotation.Common.Scheduling.AbilityCandidate> DeathsDesignCandidates(
        RotationScheduler scheduler) =>
        scheduler.InspectGcdQueue()
            .Where(c => c.Behavior == ThanatosAbilities.ShadowOfDeath || c.Behavior == ThanatosAbilities.WhorlOfDeath)
            .ToList();

    [Fact]
    public void DeathsDesign_HighPriority_WhenAbsent()
    {
        var (targeting, actions) = Setup();
        var scheduler = SchedulerFactory.CreateForTest(actionService: actions);
        var context = ThanatosTestContext.Create(
            actionService: actions, targetingService: targeting, level: 100,
            hasDeathsDesign: false);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        var dd = DeathsDesignCandidates(scheduler);
        Assert.Single(dd);
        Assert.Equal(5, dd[0].Priority);
    }

    [Fact]
    public void DeathsDesign_LowPriority_WhenRefreshing()
    {
        var (targeting, actions) = Setup();
        var scheduler = SchedulerFactory.CreateForTest(actionService: actions);
        var context = ThanatosTestContext.Create(
            actionService: actions, targetingService: targeting, level: 100,
            hasDeathsDesign: true, deathsDesignRemaining: 5f); // below the 10s refresh threshold

        _module.CollectCandidates(context, scheduler, isMoving: false);

        var dd = DeathsDesignCandidates(scheduler);
        Assert.Single(dd);
        Assert.Equal(7, dd[0].Priority);
    }

    [Fact]
    public void DeathsDesign_NotPushed_WhenHealthy()
    {
        var (targeting, actions) = Setup();
        var scheduler = SchedulerFactory.CreateForTest(actionService: actions);
        var context = ThanatosTestContext.Create(
            actionService: actions, targetingService: targeting, level: 100,
            hasDeathsDesign: true, deathsDesignRemaining: 20f); // healthy, above threshold

        _module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.Empty(DeathsDesignCandidates(scheduler));
    }
}
