using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Moq;
using Daedalus.Data;
using Daedalus.Rotation.AresCore.Abilities;
using Daedalus.Rotation.AresCore.Modules;
using Daedalus.Services.Action;
using Daedalus.Services.Targeting;
using Daedalus.Tests.Mocks;
using Daedalus.Tests.Rotation.Common.Scheduling;
using Xunit;

namespace Daedalus.Tests.Rotation.AresCore.Modules;

/// <summary>
/// Surging Tempest guard: outside Inner Release, don't dump Beast Gauge (Fell Cleave) when Surging
/// Tempest is about to fall off — let the combo refresh it first. With healthy ST, gauge spending resumes.
/// </summary>
public sealed class AresSurgingTempestGuardTests
{
    private readonly DamageModule _module = new();

    private static Mock<IBattleNpc> Enemy(ulong id = 99u)
    {
        var m = new Mock<IBattleNpc>();
        m.Setup(x => x.GameObjectId).Returns(id);
        m.Setup(x => x.CurrentHp).Returns(10000u);
        m.Setup(x => x.MaxHp).Returns(10000u);
        return m;
    }

    private bool FellCleaveQueued(float surgingTempestRemaining, bool hasSurgingTempest)
    {
        var enemy = Enemy();
        var targeting = MockBuilders.CreateMockTargetingService();
        targeting.Setup(x => x.FindEnemyForAction(
                It.IsAny<EnemyTargetingStrategy>(), It.IsAny<uint>(), It.IsAny<IPlayerCharacter>()))
            .Returns(enemy.Object);
        targeting.Setup(x => x.FindEnemy(
                It.IsAny<EnemyTargetingStrategy>(), It.IsAny<float>(), It.IsAny<IPlayerCharacter>()))
            .Returns(enemy.Object);
        MockBuilders.SetupEnemyPackCount(targeting, 1);

        var actionService = MockBuilders.CreateMockActionService();
        actionService.Setup(x => x.GcdDuration).Returns(2.5f); // 3 GCDs ≈ 7.5s window

        var config = AresTestContext.CreateDefaultWarriorConfiguration();
        var scheduler = SchedulerFactory.CreateForTest(config: config);
        var context = AresTestContext.CreateMock(
            config: config,
            actionService: actionService,
            targetingService: targeting,
            level: 100,
            beastGauge: 100,                 // can spend
            hasInnerRelease: false,          // filler (guard applies)
            hasSurgingTempest: hasSurgingTempest,
            surgingTempestRemaining: surgingTempestRemaining,
            enemyCount: 1);

        _module.CollectCandidates(context, scheduler, isMoving: false);
        return System.Linq.Enumerable.Any(scheduler.InspectGcdQueue(), c => c.Behavior == AresAbilities.FellCleave);
    }

    [Fact]
    public void BlocksFellCleave_WhenSurgingTempestAboutToDrop()
    {
        Assert.False(FellCleaveQueued(surgingTempestRemaining: 5f, hasSurgingTempest: true));
    }

    [Fact]
    public void BlocksFellCleave_WhenSurgingTempestAlreadyDown()
    {
        Assert.False(FellCleaveQueued(surgingTempestRemaining: 0f, hasSurgingTempest: false));
    }

    [Fact]
    public void AllowsFellCleave_WhenSurgingTempestHealthy()
    {
        Assert.True(FellCleaveQueued(surgingTempestRemaining: 30f, hasSurgingTempest: true));
    }
}
