using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Moq;
using Daedalus.Data;
using Daedalus.Rotation.KratosCore.Abilities;
using Daedalus.Rotation.KratosCore.Modules;
using Daedalus.Services.Targeting;
using Daedalus.Tests.Mocks;
using Daedalus.Tests.Rotation.Common.Scheduling;
using Xunit;

namespace Daedalus.Tests.Rotation.KratosCore;

/// <summary>
/// MNK automation-engage regressions (field 2026-07-18, Pugilist + Henchman hunt farming):
/// (1) beyond melee the module pushed NOTHING at a passive hard-targeted mark — the
/// engaged-or-hostile scan can't see it and a sub-15 Pugilist has no ranged tool, so the
/// driver waited for a kill that never started; the opener now stays queued at the hard
/// target (dispatch range-gates it until in reach). (2) The pre-7.0 positional hold could
/// deadlock the opener on a single front-approached target — Dawntrail removed ALL Monk
/// positionals (RSR carries zero positional metadata), so the hold is gone.
/// </summary>
public class KratosAutomationEngageTests
{
    private readonly DamageModule _module = new();

    [Fact]
    public void OutOfMelee_AutomationOverride_KeepsOpenerQueuedAtHardTarget()
    {
        var mark = new Mock<IBattleNpc>();
        mark.Setup(x => x.GameObjectId).Returns(7777UL);
        mark.Setup(x => x.IsDead).Returns(false);
        mark.Setup(x => x.CurrentHp).Returns(50000u);
        mark.Setup(x => x.MaxHp).Returns(50000u);

        var targeting = MockBuilders.CreateMockTargetingService();
        targeting.Setup(x => x.FindEnemyForAction(
                It.IsAny<EnemyTargetingStrategy>(), It.IsAny<uint>(), It.IsAny<IPlayerCharacter>()))
            .Returns((IBattleNpc?)null); // beyond Bootshine range
        targeting.Setup(x => x.FindNearbyEnemy(It.IsAny<float>(), It.IsAny<IPlayerCharacter>()))
            .Returns((IBattleNpc?)null); // passive mark — engaged/hostile scan can't see it
        targeting.Setup(x => x.GetUserEnemyTarget()).Returns(mark.Object);

        var config = KratosTestContext.CreateDefaultMonkConfiguration();
        var context = KratosTestContext.Create(
            config: config, targetingService: targeting, level: 10, inCombat: true);
        var scheduler = SchedulerFactory.CreateForTest(config: config);

        config.ExternalCombatOverride = true; // process-wide static — always restore
        try
        {
            _module.CollectCandidates(context, scheduler, isMoving: false);
        }
        finally
        {
            config.ExternalCombatOverride = false;
        }

        Assert.Contains(scheduler.InspectGcdQueue(),
            c => c.Behavior == KratosAbilities.Bootshine && c.TargetId == 7777UL);
    }

    [Fact]
    public void OutOfMelee_NoOverride_PushesNothingAtPassiveMobs()
    {
        // Manual play: never auto-open on a mob the engaged scan can't justify.
        var targeting = MockBuilders.CreateMockTargetingService();
        targeting.Setup(x => x.FindEnemyForAction(
                It.IsAny<EnemyTargetingStrategy>(), It.IsAny<uint>(), It.IsAny<IPlayerCharacter>()))
            .Returns((IBattleNpc?)null);
        targeting.Setup(x => x.FindNearbyEnemy(It.IsAny<float>(), It.IsAny<IPlayerCharacter>()))
            .Returns((IBattleNpc?)null);
        targeting.Setup(x => x.GetUserEnemyTarget()).Returns((IBattleNpc?)null);

        var context = KratosTestContext.Create(targetingService: targeting, level: 10, inCombat: true);
        var scheduler = SchedulerFactory.CreateForTest();

        _module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.Empty(scheduler.InspectGcdQueue());
    }

    [Fact]
    public void Opener_WrongSide_EnforcePositionalsOn_NeverHeld()
    {
        // Dawntrail removed all MNK positionals — the old hold ("Moving to rear") deadlocked
        // a front-approached single target when enforcement was on and nothing could move us.
        var enemy = new Mock<IBattleNpc>();
        enemy.Setup(x => x.GameObjectId).Returns(4242UL);
        enemy.Setup(x => x.CurrentHp).Returns(50000u);
        enemy.Setup(x => x.MaxHp).Returns(50000u);

        var targeting = MockBuilders.CreateMockTargetingService();
        targeting.Setup(x => x.FindEnemyForAction(
                It.IsAny<EnemyTargetingStrategy>(), It.IsAny<uint>(), It.IsAny<IPlayerCharacter>()))
            .Returns(enemy.Object);

        var config = KratosTestContext.CreateDefaultMonkConfiguration();
        config.Monk.EnforcePositionals = true;
        config.Monk.AllowPositionalLoss = false;

        var context = KratosTestContext.Create(
            config: config, targetingService: targeting, level: 10, inCombat: true,
            isAtRear: false, isAtFlank: false, currentForm: Daedalus.Rotation.KratosCore.Context.MonkForm.None);
        var scheduler = SchedulerFactory.CreateForTest(config: config);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.Contains(scheduler.InspectGcdQueue(), c => c.Behavior == KratosAbilities.Bootshine);
    }
}
