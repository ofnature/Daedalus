using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Moq;
using Daedalus.Rotation.AresCore.Abilities;
using Daedalus.Rotation.AresCore.Modules;
using Daedalus.Services.Targeting;
using Daedalus.Tests.Mocks;
using Daedalus.Tests.Rotation.Common.Scheduling;
using Xunit;

namespace Daedalus.Tests.Rotation.AresCore.Modules;

/// <summary>
/// 2026-07-20 RSR-parity audit: Inner Release has NO gauge gate — its 3 stacks are free Fell
/// Cleaves, so the old "build to 50 gauge first" hold just drifted the 60s cooldown (fewer IR
/// windows per fight). RSR's only condition is Surging Tempest not about to lapse.
/// </summary>
public sealed class AresInnerReleaseGaugeTests
{
    private readonly BuffModule _module = new();

    private bool InnerReleaseQueued(int beastGauge, bool hasSurgingTempest, float stRemaining)
    {
        var enemy = new Mock<IBattleNpc>();
        enemy.Setup(x => x.GameObjectId).Returns(99UL);
        enemy.Setup(x => x.CurrentHp).Returns(10000u);
        enemy.Setup(x => x.MaxHp).Returns(10000u);

        var targeting = MockBuilders.CreateMockTargetingService();
        targeting.Setup(x => x.FindEnemy(
                It.IsAny<EnemyTargetingStrategy>(), It.IsAny<float>(), It.IsAny<IPlayerCharacter>()))
            .Returns(enemy.Object);

        var actionService = MockBuilders.CreateMockActionService(canExecuteOgcd: true);
        var config = AresTestContext.CreateDefaultWarriorConfiguration();
        var scheduler = SchedulerFactory.CreateForTest(config: config);
        var context = AresTestContext.CreateMock(
            config: config,
            actionService: actionService,
            targetingService: targeting,
            level: 100,
            beastGauge: beastGauge,
            hasInnerRelease: false,
            hasSurgingTempest: hasSurgingTempest,
            surgingTempestRemaining: stRemaining,
            enemyCount: 1);

        _module.CollectCandidates(context, scheduler, isMoving: false);
        return System.Linq.Enumerable.Any(
            scheduler.InspectOgcdQueue(), c => c.Behavior == AresAbilities.InnerRelease);
    }

    [Fact]
    public void InnerRelease_Fires_AtZeroGauge_WithSurgingTempestUp()
    {
        Assert.True(InnerReleaseQueued(beastGauge: 0, hasSurgingTempest: true, stRemaining: 30f));
    }

    [Fact]
    public void InnerRelease_Held_WithoutSurgingTempest()
    {
        Assert.False(InnerReleaseQueued(beastGauge: 100, hasSurgingTempest: false, stRemaining: 0f));
    }
}
