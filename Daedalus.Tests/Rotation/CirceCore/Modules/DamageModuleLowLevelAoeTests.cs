using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Moq;
using Daedalus.Rotation.CirceCore.Abilities;
using Daedalus.Rotation.CirceCore.Modules;
using Daedalus.Services.Targeting;
using Daedalus.Tests.Mocks;
using Daedalus.Tests.Rotation.Common.Scheduling;
using Xunit;

namespace Daedalus.Tests.Rotation.CirceCore.Modules;

/// <summary>
/// AoE hardcast below Verthunder II (Lv18): GetAoeHardcast falls back to Jolt there, but the push mapped anything
/// that wasn't Verthunder II to Veraero II (Lv22), which the scheduler drops — and the method returned before the
/// Jolt push, so a synced RDM in a pack had no hardcast filler at all. RSR's AoE choice (2d0c9761) falls through to
/// Jolt when Verthunder II / Veraero II can't be used.
/// </summary>
public class DamageModuleLowLevelAoeTests
{
    [Theory]
    [InlineData(15)]
    [InlineData(2)]
    public void PackBelowVerthunderIILevel_PushesJolt(byte level)
    {
        var scheduler = Collect(level);

        Assert.Contains(scheduler.InspectGcdQueue(), c => c.Behavior == CirceAbilities.Jolt);
        Assert.DoesNotContain(scheduler.InspectGcdQueue(), c => c.Behavior == CirceAbilities.Veraero2);
    }

    [Fact]
    public void PackAtVerthunderIILevel_PushesVerthunderII()
    {
        var scheduler = Collect(level: 20);

        Assert.Contains(scheduler.InspectGcdQueue(), c => c.Behavior == CirceAbilities.Verthunder2);
    }

    private static Daedalus.Rotation.Common.Scheduling.RotationScheduler Collect(byte level)
    {
        var enemy = new Mock<IBattleNpc>();
        enemy.Setup(x => x.GameObjectId).Returns(99999UL);
        enemy.Setup(x => x.CurrentHp).Returns(10000u);
        enemy.Setup(x => x.MaxHp).Returns(10000u);

        var targeting = MockBuilders.CreateMockTargetingService();
        targeting.Setup(x => x.FindEnemy(
                It.IsAny<EnemyTargetingStrategy>(), It.IsAny<float>(), It.IsAny<IPlayerCharacter>()))
            .Returns(enemy.Object);
        targeting.Setup(x => x.CountEnemiesInRangeOfTarget(
                It.IsAny<float>(), It.IsAny<IBattleNpc>(), It.IsAny<IPlayerCharacter>()))
            .Returns(4);

        var actionService = MockBuilders.CreateMockActionService();
        actionService.Setup(x => x.IsActionReady(It.IsAny<uint>())).Returns(true);

        var scheduler = SchedulerFactory.CreateForTest(actionService: actionService);
        var context = CirceTestContext.Create(actionService: actionService, targetingService: targeting, level: level);

        new DamageModule().CollectCandidates(context, scheduler, isMoving: false);
        return scheduler;
    }
}
