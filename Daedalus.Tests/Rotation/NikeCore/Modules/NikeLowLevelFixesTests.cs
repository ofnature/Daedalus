using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Moq;
using Daedalus.Data;
using Daedalus.Models.Action;
using Daedalus.Rotation.Common.Scheduling;
using Daedalus.Rotation.NikeCore.Abilities;
using Daedalus.Rotation.NikeCore.Modules;
using Daedalus.Services.Action;
using Daedalus.Services.Targeting;
using Daedalus.Tests.Mocks;
using Daedalus.Tests.Rotation.Common.Scheduling;
using Xunit;

namespace Daedalus.Tests.Rotation.NikeCore.Modules;

/// <summary>
/// Samurai below max level: packs before Fuga (Lv26) fall back to the single-target combo; Fuga is aimed at
/// the enemy (hostile-only on the game sheet) while Fuko stays self-centred; Tsubame-gaeshi and its Kaeshi
/// follow-ups unlock at Lv74 (game sheet), not 76; and at Lv40-49, where Sen caps at Getsu+Ka, single target
/// spends 2 Sen on Tenka Goken instead of holding them forever.
/// </summary>
public class NikeLowLevelFixesTests
{
    private const ulong EnemyId = 99999UL;
    private const ulong PlayerId = 1UL; // MockBuilders.CreateMockPlayerCharacter

    private readonly DamageModule _module = new();

    // --- AoE below Fuga ---

    [Fact]
    public void Lv20_Pack_QueuesHakaze_NotFuga()
    {
        var gcd = Collect(level: 20, packCount: 4);

        Assert.Contains(gcd, c => c.Behavior == NikeAbilities.Hakaze);
        Assert.DoesNotContain(gcd, c => c.Behavior == NikeAbilities.Fuga);
    }

    [Fact]
    public void Lv40_Pack_FugaNotLearned_QueuesHakaze()
    {
        var actionService = MockBuilders.CreateMockActionService();
        actionService.Setup(x => x.IsActionLearned(SAMActions.Fuga.ActionId)).Returns(false);

        var gcd = Collect(level: 40, packCount: 4, actionService: actionService);

        Assert.Contains(gcd, c => c.Behavior == NikeAbilities.Hakaze);
        Assert.DoesNotContain(gcd, c => c.Behavior == NikeAbilities.Fuga);
    }

    [Fact]
    public void Lv30_Pack_QueuesFuga()
    {
        var gcd = Collect(level: 30, packCount: 4);

        Assert.Contains(gcd, c => c.Behavior == NikeAbilities.Fuga);
        Assert.DoesNotContain(gcd, c => c.Behavior == NikeAbilities.Hakaze);
    }

    // --- Starter target ---

    [Theory]
    [InlineData(26)]
    [InlineData(60)]
    public void Pack_FugaTargetsTheEnemy(byte level)
    {
        var gcd = Collect(level: level, packCount: 4);

        Assert.Contains(gcd, c => c.Behavior == NikeAbilities.Fuga && c.TargetId == EnemyId);
    }

    [Fact]
    public void Lv90_Pack_FukoTargetsThePlayer()
    {
        var gcd = Collect(level: 90, packCount: 4);

        Assert.Contains(gcd, c => c.Behavior == NikeAbilities.Fuko && c.TargetId == PlayerId);
    }

    [Fact]
    public void Fuga_IsHostileTargeted_WithGameRange()
    {
        Assert.Equal(ActionTargetType.SingleEnemy, SAMActions.Fuga.TargetType);
        Assert.Equal(8f, SAMActions.Fuga.Range);
    }

    // --- Tsubame-gaeshi at 74 ---

    [Fact]
    public void Tsubame_MinLevels_MatchGameSheet()
    {
        Assert.Equal(74, SAMActions.TsubameGaeshi.MinLevel);
        Assert.Equal(74, SAMActions.KaeshiGoken.MinLevel);
        Assert.Equal(74, SAMActions.KaeshiSetsugekka.MinLevel);
    }

    [Fact]
    public void Lv74_TsubameReady_QueuesKaeshiSetsugekka()
    {
        var gcd = CollectTsubame(74, SAMActions.KaeshiSetsugekka.ActionId);

        Assert.Contains(gcd, c => c.Behavior == NikeAbilities.KaeshiSetsugekka && c.Priority == 1);
    }

    [Fact]
    public void Lv75_TsubameReady_QueuesKaeshiGoken()
    {
        var gcd = CollectTsubame(75, SAMActions.KaeshiGoken.ActionId);

        Assert.Contains(gcd, c => c.Behavior == NikeAbilities.KaeshiGoken && c.Priority == 1);
    }

    [Fact]
    public void Lv73_TsubameSlotReplaced_QueuesNoKaeshi()
    {
        var gcd = CollectTsubame(73, SAMActions.KaeshiSetsugekka.ActionId);

        Assert.DoesNotContain(gcd, c => c.Behavior == NikeAbilities.KaeshiSetsugekka);
    }

    // --- 2 Sen below Midare ---

    [Fact]
    public void Lv45_GetsuKa_SingleTarget_QueuesTenkaGoken()
    {
        var gcd = CollectTwoSen(level: 45, packCount: 1);

        Assert.Contains(gcd, c => c.Behavior == NikeAbilities.TenkaGoken);
    }

    [Fact]
    public void Lv55_MidareNotLearned_GetsuKa_SingleTarget_QueuesTenkaGoken()
    {
        var actionService = MockBuilders.CreateMockActionService();
        actionService.Setup(x => x.IsActionLearned(SAMActions.MidareSetsugekka.ActionId)).Returns(false);

        var gcd = CollectTwoSen(level: 55, packCount: 1, actionService: actionService);

        Assert.Contains(gcd, c => c.Behavior == NikeAbilities.TenkaGoken);
    }

    [Fact]
    public void Lv60_GetsuKa_SingleTarget_HoldsForMidare()
    {
        var gcd = CollectTwoSen(level: 60, packCount: 1);

        Assert.DoesNotContain(gcd, c => c.Behavior == NikeAbilities.TenkaGoken);
    }

    [Fact]
    public void Lv45_GetsuKa_Pack_QueuesTenkaGoken()
    {
        var gcd = CollectTwoSen(level: 45, packCount: 4);

        Assert.Contains(gcd, c => c.Behavior == NikeAbilities.TenkaGoken);
    }

    private IReadOnlyList<AbilityCandidate> Collect(byte level, int packCount, Mock<IActionService>? actionService = null)
    {
        var targeting = CreateTargeting(packCount);
        actionService ??= MockBuilders.CreateMockActionService();
        var config = NikeTestContext.CreateDefaultSamuraiConfiguration();
        config.Samurai.EnableAoERotation = true;
        config.Samurai.AoEMinTargets = 3;

        var scheduler = SchedulerFactory.CreateForTest(config: config, actionService: actionService);
        var context = NikeTestContext.Create(config: config, actionService: actionService,
            targetingService: targeting, level: level);

        _module.CollectCandidates(context, scheduler, isMoving: false);
        return scheduler.InspectGcdQueue();
    }

    private IReadOnlyList<AbilityCandidate> CollectTsubame(byte level, uint slotId)
    {
        var targeting = CreateTargeting(1);
        var actionService = MockBuilders.CreateMockActionService();
        actionService.Setup(x => x.GetAdjustedActionId(SAMActions.TsubameGaeshi.ActionId)).Returns(slotId);

        var config = NikeTestContext.CreateDefaultSamuraiConfiguration();
        var scheduler = SchedulerFactory.CreateForTest(config: config, actionService: actionService);
        var context = NikeTestContext.Create(config: config, actionService: actionService,
            targetingService: targeting, level: level, tsubameGaeshiActionReady: true);

        _module.CollectCandidates(context, scheduler, isMoving: false);
        return scheduler.InspectGcdQueue();
    }

    private IReadOnlyList<AbilityCandidate> CollectTwoSen(byte level, int packCount, Mock<IActionService>? actionService = null)
    {
        var targeting = CreateTargeting(packCount);
        actionService ??= MockBuilders.CreateMockActionService();
        var config = NikeTestContext.CreateDefaultSamuraiConfiguration();
        config.Samurai.EnableAoERotation = true;
        config.Samurai.AoEMinTargets = 3;

        var scheduler = SchedulerFactory.CreateForTest(config: config, actionService: actionService);
        var context = NikeTestContext.Create(config: config, actionService: actionService,
            targetingService: targeting, level: level,
            sen: SAMActions.SenType.Getsu | SAMActions.SenType.Ka,
            hasFugetsu: true, fugetsuRemaining: 30f, hasFuka: true, fukaRemaining: 30f,
            hasHiganbanaOnTarget: true, higanbanaRemaining: 50f);

        _module.CollectCandidates(context, scheduler, isMoving: false);
        return scheduler.InspectGcdQueue();
    }

    private static Mock<ITargetingService> CreateTargeting(int packCount)
    {
        var enemy = new Mock<IBattleNpc>();
        enemy.Setup(x => x.GameObjectId).Returns(EnemyId);
        enemy.Setup(x => x.CurrentHp).Returns(10000u);
        enemy.Setup(x => x.MaxHp).Returns(10000u);

        var targeting = MockBuilders.CreateMockTargetingService();
        targeting.Setup(x => x.FindEnemyForAction(
                It.IsAny<EnemyTargetingStrategy>(), It.IsAny<uint>(), It.IsAny<IPlayerCharacter>()))
            .Returns(enemy.Object);
        targeting.Setup(x => x.FindEnemy(
                It.IsAny<EnemyTargetingStrategy>(), It.IsAny<float>(), It.IsAny<IPlayerCharacter>()))
            .Returns(enemy.Object);
        MockBuilders.SetupEnemyPackCount(targeting, packCount);
        return targeting;
    }
}
