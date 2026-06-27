using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Moq;
using Daedalus.Data;
using Daedalus.Rotation.NikeCore.Abilities;
using Daedalus.Rotation.NikeCore.Modules;
using Daedalus.Services.Targeting;
using Daedalus.Tests.Mocks;
using Daedalus.Tests.Rotation.Common.Scheduling;
using Xunit;

namespace Daedalus.Tests.Rotation.NikeCore.Modules;

/// <summary>
/// Combo finisher at p6 with starter fallback at p7 (PLD/WAR parity).
/// </summary>
public class NikeComboFallbackTests
{
    private readonly DamageModule _module = new();

    [Fact]
    public void StCombo_QueuesGekkoAt6AndHakazeAt7_WhenAfterJinpu()
    {
        var enemy = CreateMockEnemy();
        var targeting = MockBuilders.CreateMockTargetingService();
        targeting.Setup(x => x.FindEnemyForAction(
                It.IsAny<EnemyTargetingStrategy>(), It.IsAny<uint>(), It.IsAny<IPlayerCharacter>()))
            .Returns(enemy.Object);
        targeting.Setup(x => x.FindEnemy(
                It.IsAny<EnemyTargetingStrategy>(), It.IsAny<float>(), It.IsAny<IPlayerCharacter>()))
            .Returns(enemy.Object);
        MockBuilders.SetupEnemyPackCount(targeting, 1);

        var config = NikeTestContext.CreateDefaultSamuraiConfiguration();
        config.Samurai.EnableAoERotation = true;
        config.Samurai.AoEMinTargets = 3;

        var scheduler = SchedulerFactory.CreateForTest(config: config);
        var context = NikeTestContext.Create(
            config: config,
            targetingService: targeting,
            level: 90,
            comboStep: 2,
            lastComboAction: SAMActions.Jinpu.ActionId);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        var gcd = scheduler.InspectGcdQueue();
        Assert.Contains(gcd, c => c.Behavior == NikeAbilities.Gekko && c.Priority == 6);
        Assert.Contains(gcd, c => c.Behavior == NikeAbilities.Hakaze && c.Priority == 7);
    }

    [Fact]
    public void AoECombo_QueuesMangetsuAt6AndFukoAt7_WhenAfterFuko()
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

        var config = NikeTestContext.CreateDefaultSamuraiConfiguration();
        config.Samurai.EnableAoERotation = true;
        config.Samurai.AoEMinTargets = 3;

        var scheduler = SchedulerFactory.CreateForTest(config: config);
        var context = NikeTestContext.Create(
            config: config,
            targetingService: targeting,
            level: 100,
            comboStep: 1,
            lastComboAction: SAMActions.Fuko.ActionId);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        var gcd = scheduler.InspectGcdQueue();
        Assert.Contains(gcd, c => c.Behavior == NikeAbilities.Mangetsu && c.Priority == 6);
        Assert.Contains(gcd, c => c.Behavior == NikeAbilities.Fuko && c.Priority == 7);
    }

    [Fact]
    public void AoECombo_QueuesMangetsuFinisher_WhenComboStepStaleButFukoJustFired()
    {
        // Regression: the "Fuko lockup". Right after Fuko fires, the gauge combo-step read can lag and
        // report 0. With only the gauge as the source, onStarter is false, no finisher is queued, and the
        // p7 Fuko re-push is blocked by ShouldBlockRepeatGcd (Fuko was the last GCD) → autoattack lockup.
        // WasLastGcd(Fuko) must rescue the finisher even though comboStep == 0.
        var gcd = CollectWithStaleComboAfterLastGcd(SAMActions.Fuko.ActionId, packCount: 3);
        Assert.Contains(gcd, c => c.Behavior == NikeAbilities.Mangetsu && c.Priority == 6);
    }

    [Fact]
    public void StCombo_QueuesStep2_WhenComboStepStaleAndOgcdWeavedAfterStarter()
    {
        // Regression: the "Shinten lockup". After Gyofu fires, a Kenki spender (Shinten, oGCD) weaves —
        // overwriting LastAction with Shinten while the last *GCD* is still Gyofu. The gauge combo-step
        // read then lags to 0. Detection MUST key off WasLastGcd (Gyofu), not WasLastAction (Shinten),
        // or onStarter is false, only the blocked p7 Gyofu is queued, and the rotation stalls.
        var gcd = CollectWithStaleComboAfterLastGcd(SAMActions.Gyofu.ActionId, packCount: 1);
        Assert.Contains(gcd, c => c.Behavior == NikeAbilities.Jinpu && c.Priority == 6);
    }

    /// <summary>
    /// Builds a context where the gauge combo state is stale (step 0) but the given action was the last
    /// dispatched GCD, then collects candidates. Mirrors the post-oGCD-weave lockup conditions.
    /// </summary>
    private System.Collections.Generic.IReadOnlyList<Daedalus.Rotation.Common.Scheduling.AbilityCandidate>
        CollectWithStaleComboAfterLastGcd(uint lastGcdId, int packCount)
    {
        var enemy = CreateMockEnemy();
        var targeting = MockBuilders.CreateMockTargetingService();
        targeting.Setup(x => x.FindEnemyForAction(
                It.IsAny<EnemyTargetingStrategy>(), It.IsAny<uint>(), It.IsAny<IPlayerCharacter>()))
            .Returns(enemy.Object);
        targeting.Setup(x => x.FindEnemy(
                It.IsAny<EnemyTargetingStrategy>(), It.IsAny<float>(), It.IsAny<IPlayerCharacter>()))
            .Returns(enemy.Object);
        MockBuilders.SetupEnemyPackCount(targeting, packCount);

        var config = NikeTestContext.CreateDefaultSamuraiConfiguration();
        config.Samurai.EnableAoERotation = true;
        config.Samurai.AoEMinTargets = 3;

        var actionService = MockBuilders.CreateMockActionService();
        actionService.Setup(x => x.WasLastGcd(lastGcdId)).Returns(true);

        var scheduler = SchedulerFactory.CreateForTest(config: config);
        var context = NikeTestContext.Create(
            config: config,
            actionService: actionService,
            targetingService: targeting,
            level: 100,
            comboStep: 0,            // stale gauge read
            lastComboAction: 0);     // stale gauge read

        _module.CollectCandidates(context, scheduler, isMoving: false);
        return scheduler.InspectGcdQueue();
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
