using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Moq;
using Daedalus.Data;
using Daedalus.Rotation.Common.Scheduling;
using Daedalus.Rotation.NikeCore.Abilities;
using Daedalus.Rotation.NikeCore.Modules;
using Daedalus.Services.Targeting;
using Daedalus.Tests.Mocks;
using Daedalus.Tests.Rotation.Common.Scheduling;
using Xunit;

namespace Daedalus.Tests.Rotation.NikeCore.Modules;

/// <summary>
/// Field report 2026-07-20 (first live anchor run): SAM started Midare/Ogi cast bars mid-step and
/// movement cancelled them. Cast-time GCDs must never be pushed while moving (RSR blocks moving
/// casts the same way); isMoving includes Daedalus's own vNav anchor hops. Instant GCDs (combo,
/// Tsubame) keep the GCD rolling meanwhile.
/// </summary>
public class NikeMovingCastGateTests
{
    private readonly DamageModule _module = new();

    private RotationScheduler Collect(
        bool isMoving,
        bool ogiReady = false,
        SAMActions.SenType sen = SAMActions.SenType.None)
    {
        var enemy = new Mock<IBattleNpc>();
        enemy.Setup(x => x.GameObjectId).Returns(99999UL);
        enemy.Setup(x => x.CurrentHp).Returns(100000u);
        enemy.Setup(x => x.MaxHp).Returns(100000u);

        var targeting = MockBuilders.CreateMockTargetingService();
        targeting.Setup(x => x.FindEnemyForAction(
                It.IsAny<EnemyTargetingStrategy>(), It.IsAny<uint>(), It.IsAny<IPlayerCharacter>()))
            .Returns(enemy.Object);
        targeting.Setup(x => x.FindEnemy(
                It.IsAny<EnemyTargetingStrategy>(), It.IsAny<float>(), It.IsAny<IPlayerCharacter>()))
            .Returns(enemy.Object);

        var actionService = MockBuilders.CreateMockActionService();
        var config = NikeTestContext.CreateDefaultSamuraiConfiguration();
        var scheduler = SchedulerFactory.CreateForTest(config: config, actionService: actionService);
        var context = NikeTestContext.Create(
            config: config,
            actionService: actionService,
            targetingService: targeting,
            level: 100,
            hasFugetsu: true,
            fugetsuRemaining: 30f,
            hasFuka: true,
            fukaRemaining: 30f,
            hasOgiNamikiriReady: ogiReady,
            sen: sen);

        _module.CollectCandidates(context, scheduler, isMoving: isMoving);
        return scheduler;
    }

    [Fact]
    public void Midare_Held_WhileMoving()
    {
        var scheduler = Collect(isMoving: true,
            sen: SAMActions.SenType.Setsu | SAMActions.SenType.Getsu | SAMActions.SenType.Ka);
        Assert.DoesNotContain(scheduler.InspectGcdQueue(), c => c.Behavior == NikeAbilities.MidareSetsugekka);
    }

    [Fact]
    public void Midare_Fires_WhenPlanted()
    {
        var scheduler = Collect(isMoving: false,
            sen: SAMActions.SenType.Setsu | SAMActions.SenType.Getsu | SAMActions.SenType.Ka);
        Assert.Contains(scheduler.InspectGcdQueue(), c => c.Behavior == NikeAbilities.MidareSetsugekka);
    }

    [Fact]
    public void OgiNamikiri_Held_WhileMoving()
    {
        var scheduler = Collect(isMoving: true, ogiReady: true);
        Assert.DoesNotContain(scheduler.InspectGcdQueue(), c => c.Behavior == NikeAbilities.OgiNamikiri);
    }

    [Fact]
    public void InstantGcds_StillPushed_WhileMoving()
    {
        // The GCD must keep rolling during a hop — only the cast-time GCDs are held.
        var scheduler = Collect(isMoving: true,
            sen: SAMActions.SenType.Setsu | SAMActions.SenType.Getsu | SAMActions.SenType.Ka);
        Assert.NotEmpty(scheduler.InspectGcdQueue());
    }
}
