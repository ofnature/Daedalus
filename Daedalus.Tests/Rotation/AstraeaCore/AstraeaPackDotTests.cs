using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Daedalus.Config;
using Daedalus.Data;
using Daedalus.Rotation.AstraeaCore.Helpers;
using Daedalus.Rotation.AstraeaCore.Modules;
using Daedalus.Services.Combat;
using Daedalus.Services.Targeting;
using Daedalus.Tests.Mocks;
using Daedalus.Tests.Rotation.Common.Scheduling;
using Moq;
using Xunit;

namespace Daedalus.Tests.Rotation.AstraeaCore;

/// <summary>
/// Combust in a pack Gravity would hit: worth a GCD only on a mob that lives past the break-even (about 6.5 s
/// per enemy Gravity hits). Saar, 2026-10-05: with Gravity learned, packs got no DoTs at all.
/// </summary>
public class AstraeaPackDotTests
{
    [Theory]
    [InlineData(3, 19.5f)]
    [InlineData(4, 26f)]
    [InlineData(5, 32.5f)] // past the 30 s DoT: never worth it
    public void BreakEven(int hits, float seconds) => Assert.Equal(seconds, AstraeaPackDot.BreakEvenSeconds(hits));

    [Fact]
    public void FiveOrMoreNeverDots() => Assert.True(AstraeaPackDot.BreakEvenSeconds(5) > AstraeaPackDot.DotDurationSeconds);

    // ── the cutoff the targeting uses ─────────────────────────────────────────────────────

    [Fact]
    public void Cutoff_ThePackRaisesTheConfiguredOne()
        => Assert.Equal(19.5f, DotTtkGate.Cutoff(Mock.Of<ITimeToKillService>(), new TargetingConfig(), 19.5f));

    [Fact]
    public void Cutoff_TheConfiguredOneStandsAlone()
        => Assert.Equal(new TargetingConfig().DotTimeToKillThresholdSeconds,
            DotTtkGate.Cutoff(Mock.Of<ITimeToKillService>(), new TargetingConfig()));

    [Fact]
    public void Cutoff_NoEstimateNoGate() => Assert.Null(DotTtkGate.Cutoff(null, new TargetingConfig(), 19.5f));

    // ── the module ───────────────────────────────────────────────────────────────────────

    private static (Mock<ITargetingService> Targeting, Daedalus.Rotation.Common.Scheduling.RotationScheduler Scheduler,
        Daedalus.Rotation.AstraeaCore.Context.AstraeaContext Context) Pack(int hits, Mock<IBattleNpc> dotTarget)
    {
        var config = AstraeaTestContext.CreateDefaultAstrologianConfiguration();
        config.Astrologian.EnableAoEDamage = true;
        config.Astrologian.EnableDot = true;
        config.Astrologian.AoEDamageMinTargets = 3;

        var targeting = MockBuilders.CreateMockTargetingService(findEnemy: (_, _, _) => dotTarget.Object);
        targeting.Setup(t => t.FindBestAoETarget(It.IsAny<float>(), It.IsAny<float>(), It.IsAny<IPlayerCharacter>()))
            .Returns((dotTarget.Object, hits));
        targeting.Setup(t => t.FindEnemyNeedingDot(It.IsAny<uint>(), It.IsAny<float>(), It.IsAny<float>(),
                It.IsAny<IPlayerCharacter>(), It.IsAny<float>()))
            .Returns(dotTarget.Object);

        var actionService = MockBuilders.CreateMockActionService(canExecuteGcd: true);
        var context = AstraeaTestContext.Create(config: config, actionService: actionService,
            targetingService: targeting, level: 72, inCombat: true);
        return (targeting, SchedulerFactory.CreateForTest(actionService), context);
    }

    [Fact]
    public void AThreePack_DotsMobsThatLiveTwentySeconds()
    {
        var mob = new Mock<IBattleNpc>();
        mob.Setup(m => m.GameObjectId).Returns(0x40000001UL);
        var (targeting, scheduler, context) = Pack(hits: 3, mob);

        new DamageModule().CollectCandidates(context, scheduler, isMoving: false);

        targeting.Verify(t => t.FindEnemyNeedingDot(It.IsAny<uint>(), It.IsAny<float>(), It.IsAny<float>(),
            It.IsAny<IPlayerCharacter>(), 19.5f), Times.AtLeastOnce);
        Assert.Contains(scheduler.InspectGcdQueue(), c => c.Behavior.Action.ActionId == ASTActions.CombustIII.ActionId);
    }

    [Fact]
    public void AFivePack_IsGravityOnly()
    {
        var mob = new Mock<IBattleNpc>();
        mob.Setup(m => m.GameObjectId).Returns(0x40000002UL);
        var (_, scheduler, context) = Pack(hits: 5, mob);

        new DamageModule().CollectCandidates(context, scheduler, isMoving: false);

        Assert.DoesNotContain(scheduler.InspectGcdQueue(), c => c.Behavior.Action.ActionId == ASTActions.CombustIII.ActionId);
        Assert.Contains(scheduler.InspectGcdQueue(), c => c.Behavior.Action.ActionId == ASTActions.Gravity.ActionId);
    }
}
