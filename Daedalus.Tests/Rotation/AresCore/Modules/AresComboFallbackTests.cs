using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Moq;
using Daedalus.Data;
using Daedalus.Rotation.AresCore.Abilities;
using Daedalus.Rotation.AresCore.Modules;
using Daedalus.Services;
using Daedalus.Services.Targeting;
using Daedalus.Tests.Mocks;
using Daedalus.Tests.Rotation.Common.Scheduling;
using Xunit;

namespace Daedalus.Tests.Rotation.AresCore.Modules;

/// <summary>
/// Combo finisher at p6 with starter fallback at p7 (PLD parity).
/// </summary>
public class AresComboFallbackTests
{
    private readonly DamageModule _module = new();

    [Fact]
    public void StCombo_LowLevel_Step2_NoDoomedFinisher_HeavySwingRestarts()
    {
        // Below Storm's Path (Lv26) the finisher resolver falls back to Maim — there is no
        // step 3, and pushing the finisher behavior dispatched an unlearned id every chain.
        var enemy = CreateMockEnemy();
        var targeting = MockBuilders.CreateMockTargetingService();
        targeting.Setup(x => x.FindEnemyForAction(
                It.IsAny<EnemyTargetingStrategy>(), It.IsAny<uint>(), It.IsAny<IPlayerCharacter>()))
            .Returns(enemy.Object);
        targeting.Setup(x => x.FindEnemy(
                It.IsAny<EnemyTargetingStrategy>(), It.IsAny<float>(), It.IsAny<IPlayerCharacter>()))
            .Returns(enemy.Object);

        var config = AresTestContext.CreateDefaultWarriorConfiguration();
        MockBuilders.SetupEnemyPackCount(targeting, 1);

        var scheduler = SchedulerFactory.CreateForTest(config: config);
        var context = AresTestContext.CreateMock(
            config: config,
            targetingService: targeting,
            level: 20,
            comboStep: 2,
            lastComboAction: WARActions.Maim.ActionId,
            enemyCount: 1);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        var gcd = scheduler.InspectGcdQueue();
        Assert.DoesNotContain(gcd, c => c.Behavior == AresAbilities.StormsPath);
        Assert.DoesNotContain(gcd, c => c.Behavior == AresAbilities.StormsEye);
        Assert.Contains(gcd, c => c.Behavior == AresAbilities.HeavySwing);
    }

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
        MockBuilders.SetupEnemyPackCount(targeting, 1);

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
        MockBuilders.SetupEnemyPackCount(targeting, 3);

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
