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
/// 2026-07-20 RSR-parity audit: Ogi Namikiri (1000p) and Higanbana (60s DoT snapshot) must only
/// fire with BOTH Fugetsu and Fuka up (RSR HasFugetsuAndFuka). Without the gates, a post-downtime
/// Ogi lost Fugetsu's +13% and a badly timed Higanbana locked in a full minute of unbuffed ticks.
/// </summary>
public class NikeBuffGateTests
{
    private readonly DamageModule _module = new();

    private RotationScheduler Collect(
        bool hasFugetsu,
        bool hasFuka,
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
            hasFugetsu: hasFugetsu,
            fugetsuRemaining: hasFugetsu ? 30f : 0f,
            hasFuka: hasFuka,
            fukaRemaining: hasFuka ? 30f : 0f,
            hasOgiNamikiriReady: ogiReady,
            sen: sen);

        _module.CollectCandidates(context, scheduler, isMoving: false);
        return scheduler;
    }

    [Fact]
    public void OgiNamikiri_Held_WithoutBothBuffs()
    {
        var missingFugetsu = Collect(hasFugetsu: false, hasFuka: true, ogiReady: true);
        Assert.DoesNotContain(missingFugetsu.InspectGcdQueue(), c => c.Behavior == NikeAbilities.OgiNamikiri);

        var missingFuka = Collect(hasFugetsu: true, hasFuka: false, ogiReady: true);
        Assert.DoesNotContain(missingFuka.InspectGcdQueue(), c => c.Behavior == NikeAbilities.OgiNamikiri);
    }

    [Fact]
    public void OgiNamikiri_Fires_WithBothBuffs()
    {
        var scheduler = Collect(hasFugetsu: true, hasFuka: true, ogiReady: true);
        Assert.Contains(scheduler.InspectGcdQueue(), c => c.Behavior == NikeAbilities.OgiNamikiri);
    }

    [Fact]
    public void Higanbana_Held_WithoutBothBuffs()
    {
        var scheduler = Collect(hasFugetsu: true, hasFuka: false, sen: SAMActions.SenType.Setsu);
        Assert.DoesNotContain(scheduler.InspectGcdQueue(), c => c.Behavior == NikeAbilities.Higanbana);
    }

    [Fact]
    public void Higanbana_Fires_WithBothBuffs()
    {
        var scheduler = Collect(hasFugetsu: true, hasFuka: true, sen: SAMActions.SenType.Setsu);
        Assert.Contains(scheduler.InspectGcdQueue(), c => c.Behavior == NikeAbilities.Higanbana);
    }
}
