using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Moq;
using Olympus.Rotation.AresCore.Abilities;
using Olympus.Rotation.AresCore.Modules;
using Olympus.Services;
using Olympus.Services.Targeting;
using Olympus.Tests.Mocks;
using Olympus.Tests.Rotation.Common.Scheduling;
using Xunit;

namespace Olympus.Tests.Rotation.AresCore.Modules;

/// <summary>
/// Inner Release burst sequencing — Primal Rend only after IR stacks are spent.
/// </summary>
public class AresBurstSequenceTests
{
    private readonly DamageModule _module = new();

    [Fact]
    public void PrimalRend_NotQueued_WhenInnerReleaseStacksRemain()
    {
        var enemy = CreateMockEnemy();
        var targeting = MockBuilders.CreateMockTargetingService();
        targeting.Setup(x => x.FindEnemyForAction(
                It.IsAny<EnemyTargetingStrategy>(), It.IsAny<uint>(), It.IsAny<IPlayerCharacter>()))
            .Returns(enemy.Object);
        targeting.Setup(x => x.FindEnemy(
                It.IsAny<EnemyTargetingStrategy>(), It.IsAny<float>(), It.IsAny<IPlayerCharacter>()))
            .Returns(enemy.Object);
        targeting.Setup(x => x.CountEnemiesInRange(It.IsAny<float>(), It.IsAny<IPlayerCharacter>()))
            .Returns(1);

        var config = AresTestContext.CreateDefaultWarriorConfiguration();
        var scheduler = SchedulerFactory.CreateForTest(config: config);
        var context = AresTestContext.CreateMock(
            config: config,
            targetingService: targeting,
            hasInnerRelease: true,
            innerReleaseStacks: 3,
            hasPrimalRendReady: true,
            beastGauge: 50);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        var gcd = scheduler.InspectGcdQueue();
        Assert.DoesNotContain(gcd, c => c.Behavior == AresAbilities.PrimalRend);
    }

    [Fact]
    public void PrimalRend_Queued_WhenInnerReleaseStacksSpent()
    {
        var enemy = CreateMockEnemy();
        var targeting = MockBuilders.CreateMockTargetingService();
        targeting.Setup(x => x.FindEnemyForAction(
                It.IsAny<EnemyTargetingStrategy>(), It.IsAny<uint>(), It.IsAny<IPlayerCharacter>()))
            .Returns(enemy.Object);
        targeting.Setup(x => x.FindEnemy(
                It.IsAny<EnemyTargetingStrategy>(), It.IsAny<float>(), It.IsAny<IPlayerCharacter>()))
            .Returns(enemy.Object);
        targeting.Setup(x => x.CountEnemiesInRange(It.IsAny<float>(), It.IsAny<IPlayerCharacter>()))
            .Returns(1);

        var config = AresTestContext.CreateDefaultWarriorConfiguration();
        var scheduler = SchedulerFactory.CreateForTest(config: config);
        var context = AresTestContext.CreateMock(
            config: config,
            targetingService: targeting,
            hasInnerRelease: true,
            innerReleaseStacks: 0,
            hasPrimalRendReady: true);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        var gcd = scheduler.InspectGcdQueue();
        Assert.Contains(gcd, c => c.Behavior == AresAbilities.PrimalRend);
    }

    private static Mock<IBattleNpc> CreateMockEnemy(ulong objectId = 99999UL)
    {
        var mock = new Mock<IBattleNpc>();
        mock.Setup(x => x.GameObjectId).Returns(objectId);
        mock.Setup(x => x.CurrentHp).Returns(10000u);
        mock.Setup(x => x.MaxHp).Returns(10000u);
        return mock;
    }
}
