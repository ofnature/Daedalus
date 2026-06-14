using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Moq;
using Olympus.Data;
using Olympus.Rotation.AresCore.Abilities;
using Olympus.Rotation.AresCore.Modules;
using Olympus.Services;
using Olympus.Services.Targeting;
using Olympus.Tests.Mocks;
using Olympus.Tests.Rotation.Common.Scheduling;
using Xunit;

namespace Olympus.Tests.Rotation.AresCore.Modules;

/// <summary>
/// Combo finisher at p6 with starter fallback at p7 (PLD parity).
/// </summary>
public class AresComboFallbackTests
{
    private readonly DamageModule _module = new();

    [Fact]
    public void StCombo_QueuesMaimAt6AndHeavySwingAt7_WhenAfterHeavySwing()
    {
        var enemy = CreateMockEnemy();
        var targeting = MockBuilders.CreateMockTargetingService();
        targeting.Setup(x => x.FindEnemyForAction(
                It.IsAny<EnemyTargetingStrategy>(), It.IsAny<uint>(), It.IsAny<IPlayerCharacter>()))
            .Returns(enemy.Object);
        targeting.Setup(x => x.FindEnemy(
                It.IsAny<EnemyTargetingStrategy>(), It.IsAny<float>(), It.IsAny<IPlayerCharacter>()))
            .Returns(enemy.Object);

        var config = AresTestContext.CreateDefaultWarriorConfiguration();
        config.Tank.EnableAoEDamage = true;
        config.Tank.WarriorAoEMinTargetsOverride = 2;
        targeting.Setup(x => x.CountEnemiesInRange(It.IsAny<float>(), It.IsAny<IPlayerCharacter>()))
            .Returns(1);

        var scheduler = SchedulerFactory.CreateForTest(config: config);
        var context = AresTestContext.CreateMock(
            config: config,
            targetingService: targeting,
            comboStep: 1,
            lastComboAction: WARActions.HeavySwing.ActionId,
            enemyCount: 1);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        var gcd = scheduler.InspectGcdQueue();
        Assert.Contains(gcd, c => c.Behavior == AresAbilities.Maim && c.Priority == 6);
        Assert.Contains(gcd, c => c.Behavior == AresAbilities.HeavySwing && c.Priority == 7);
    }

    [Fact]
    public void AoECombo_QueuesMythrilTempestAt6AndOverpowerAt7_WhenAfterOverpower()
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
            .Returns(3);

        var config = AresTestContext.CreateDefaultWarriorConfiguration();
        config.Tank.EnableAoEDamage = true;
        config.Tank.WarriorAoEMinTargetsOverride = 2;

        var scheduler = SchedulerFactory.CreateForTest(config: config);
        var context = AresTestContext.CreateMock(
            config: config,
            targetingService: targeting,
            comboStep: 1,
            lastComboAction: WARActions.Overpower.ActionId,
            enemyCount: 3);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        var gcd = scheduler.InspectGcdQueue();
        Assert.Contains(gcd, c => c.Behavior == AresAbilities.MythrilTempest && c.Priority == 6);
        Assert.Contains(gcd, c => c.Behavior == AresAbilities.Overpower && c.Priority == 7);
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
