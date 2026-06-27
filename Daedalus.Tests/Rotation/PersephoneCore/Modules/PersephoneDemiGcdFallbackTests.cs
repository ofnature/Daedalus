using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Moq;
using Daedalus.Rotation.PersephoneCore.Abilities;
using Daedalus.Rotation.PersephoneCore.Modules;
using Daedalus.Services;
using Daedalus.Services.Targeting;
using Daedalus.Tests.Mocks;
using Daedalus.Tests.Rotation.Common.Scheduling;
using Xunit;

namespace Daedalus.Tests.Rotation.PersephoneCore.Modules;

/// <summary>
/// Demi GCD fallback chain pushes all phase candidates; scheduler dispatches the first valid.
/// </summary>
public class PersephoneDemiGcdFallbackTests
{
    private readonly DamageModule _module = new();

    [Fact]
    public void DemiGcdChain_PushesStFallbacksAtPriorities2Through4_WhenBelowAoEThreshold()
    {
        var enemy = CreateMockEnemy();
        var targeting = MockBuilders.CreateMockTargetingService();
        targeting.Setup(x => x.FindEnemy(
                It.IsAny<EnemyTargetingStrategy>(), It.IsAny<float>(), It.IsAny<IPlayerCharacter>()))
            .Returns(enemy.Object);
        MockBuilders.SetupEnemyPackCount(targeting, 1);

        var config = PersephoneTestContext.CreateDefaultSmnConfiguration();
        config.Summoner.EnableAoERotation = true;
        config.Summoner.AoEMinTargets = 3;

        var actionService = MockBuilders.CreateMockActionService();
        var scheduler = SchedulerFactory.CreateForTest(actionService: actionService, config: config);
        var context = PersephoneTestContext.Create(
            config: config,
            actionService: actionService,
            targetingService: targeting,
            level: 100,
            hasPetSummoned: true,
            isDemiSummonActive: true,
            isBahamutActive: true,
            demiSummonTimer: 15f);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        var gcd = scheduler.InspectGcdQueue();
        Assert.Equal(PersephoneAbilities.FountainOfFire, gcd[0].Behavior);
        Assert.Equal(2, gcd[0].Priority);
        Assert.Equal(PersephoneAbilities.UmbralImpulse, gcd[1].Behavior);
        Assert.Equal(3, gcd[1].Priority);
        Assert.Equal(PersephoneAbilities.AstralImpulse, gcd[2].Behavior);
        Assert.Equal(4, gcd[2].Priority);
    }

    [Fact]
    public void DemiGcdChain_PushesAoEFallbacks_WhenAtAoEMinTargets()
    {
        var enemy = CreateMockEnemy();
        var targeting = MockBuilders.CreateMockTargetingService();
        targeting.Setup(x => x.FindEnemy(
                It.IsAny<EnemyTargetingStrategy>(), It.IsAny<float>(), It.IsAny<IPlayerCharacter>()))
            .Returns(enemy.Object);
        MockBuilders.SetupEnemyPackCount(targeting, 3);

        var config = PersephoneTestContext.CreateDefaultSmnConfiguration();
        config.Summoner.EnableAoERotation = true;
        config.Summoner.AoEMinTargets = 3;

        var actionService = MockBuilders.CreateMockActionService();
        var scheduler = SchedulerFactory.CreateForTest(actionService: actionService, config: config);
        var context = PersephoneTestContext.Create(
            config: config,
            actionService: actionService,
            targetingService: targeting,
            level: 100,
            hasPetSummoned: true,
            isDemiSummonActive: true,
            isPhoenixActive: true,
            demiSummonTimer: 15f);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        var gcd = scheduler.InspectGcdQueue();
        Assert.Equal(PersephoneAbilities.BrandOfPurgatory, gcd[0].Behavior);
        Assert.Equal(PersephoneAbilities.UmbralFlare, gcd[1].Behavior);
        Assert.Equal(PersephoneAbilities.AstralFlare, gcd[2].Behavior);
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
