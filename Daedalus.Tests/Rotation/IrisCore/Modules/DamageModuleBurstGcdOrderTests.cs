using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Moq;
using Daedalus.Rotation.Common.Scheduling;
using Daedalus.Rotation.IrisCore.Abilities;
using Daedalus.Rotation.IrisCore.Modules;
using Daedalus.Services.Targeting;
using Daedalus.Tests.Mocks;
using Daedalus.Tests.Rotation.Common.Scheduling;
using Daedalus.Tests.Rotation.IrisCore;
using Xunit;

namespace Daedalus.Tests.Rotation.IrisCore.Modules;

/// <summary>
/// Regression suite (2026-07-20 field report: "PCT doesn't do much damage") — three RSR-parity
/// deviations in the burst GCD order:
/// (1) Rainbow Drip HARDCAST inside every burst window at priority 3 (above hammer/Comet/
///     subtractive), re-pushed on each 6s recast — the buffed Starry window drained into ~4s
///     unbuffed-tier hardcasts. In combat the spell is Rainbow Bright (instant) ONLY.
/// (2) Comet in Black sat below the hammer combo during Starry; RSR makes it the FIRST GCD
///     priority under the buff.
/// (3) Holy in White ran as generic filler above the subtractive spells (and spammed inside
///     Starry); RSR keeps stationary Holy for paint-cap protection only, outside Starry.
/// </summary>
public class DamageModuleBurstGcdOrderTests
{
    private readonly DamageModule _module = new();

    [Fact]
    public void RainbowDrip_NeverHardcast_InBurstWindow()
    {
        var scheduler = Collect(hasStarryMuse: true, isInBurstWindow: true, hasRainbowBright: false);
        Assert.DoesNotContain(scheduler.InspectGcdQueue(), c => c.Behavior == IrisAbilities.RainbowDrip);
    }

    [Fact]
    public void RainbowDrip_Pushed_WithRainbowBright()
    {
        var scheduler = Collect(hasRainbowBright: true);
        Assert.Contains(scheduler.InspectGcdQueue(), c => c.Behavior == IrisAbilities.RainbowDrip);
    }

    [Fact]
    public void Comet_FirstPriority_UnderStarryMuse()
    {
        var scheduler = Collect(hasStarryMuse: true, isInBurstWindow: true, hasBlackPaint: true, hasMonochromeTones: true);
        var comet = Assert.Single(scheduler.InspectGcdQueue(), c => c.Behavior == IrisAbilities.CometInBlack);
        Assert.Equal(1, comet.Priority); // above Star Prism (2) and the hammer combo (4)
    }

    [Fact]
    public void Comet_NormalPriority_OutsideStarry()
    {
        var scheduler = Collect(hasBlackPaint: true, hasMonochromeTones: true);
        var comet = Assert.Single(scheduler.InspectGcdQueue(), c => c.Behavior == IrisAbilities.CometInBlack);
        Assert.Equal(5, comet.Priority);
    }

    [Fact]
    public void Holy_NotPushed_DuringStarry_Stationary()
    {
        // The Starry GCDs belong to Comet/hammer/subtractive — no stationary Holy spam.
        var scheduler = Collect(hasStarryMuse: true, isInBurstWindow: true, whitePaint: 5);
        Assert.DoesNotContain(scheduler.InspectGcdQueue(), c => c.Behavior == IrisAbilities.HolyInWhite);
    }

    [Fact]
    public void Holy_NotPushed_BelowPaintCap_Stationary()
    {
        // Cap protection only: at 4 paint the color cycle still has room.
        var scheduler = Collect(whitePaint: 4);
        Assert.DoesNotContain(scheduler.InspectGcdQueue(), c => c.Behavior == IrisAbilities.HolyInWhite);
    }

    [Fact]
    public void Holy_Pushed_AtPaintCap_Stationary()
    {
        var scheduler = Collect(whitePaint: 5);
        Assert.Contains(scheduler.InspectGcdQueue(), c => c.Behavior == IrisAbilities.HolyInWhite);
    }

    [Fact]
    public void Holy_Pushed_WhileMoving_AnyPaint()
    {
        var scheduler = Collect(whitePaint: 1, isMoving: true);
        Assert.Contains(scheduler.InspectGcdQueue(), c => c.Behavior == IrisAbilities.HolyInWhite);
    }

    private RotationScheduler Collect(
        bool hasStarryMuse = false,
        bool isInBurstWindow = false,
        bool hasRainbowBright = false,
        bool hasBlackPaint = false,
        bool hasMonochromeTones = false,
        int whitePaint = 0,
        bool isMoving = false)
    {
        var enemy = CreateMockEnemy();
        var targeting = MockBuilders.CreateMockTargetingService();
        targeting.Setup(x => x.FindEnemy(
                It.IsAny<EnemyTargetingStrategy>(), It.IsAny<float>(), It.IsAny<IPlayerCharacter>()))
            .Returns(enemy.Object);
        targeting.Setup(x => x.FindEnemyForAction(
                It.IsAny<EnemyTargetingStrategy>(), It.IsAny<uint>(), It.IsAny<IPlayerCharacter>()))
            .Returns(enemy.Object);

        var actionService = MockBuilders.CreateMockActionService();
        var scheduler = SchedulerFactory.CreateForTest(actionService: actionService);
        var context = IrisTestContext.Create(
            actionService: actionService,
            targetingService: targeting,
            level: 100,
            inCombat: true,
            hasStarryMuse: hasStarryMuse,
            isInBurstWindow: isInBurstWindow,
            hasRainbowBright: hasRainbowBright,
            whitePaint: whitePaint,
            hasWhitePaint: whitePaint > 0,
            hasBlackPaint: hasBlackPaint,
            hasMonochromeTones: hasMonochromeTones);

        _module.CollectCandidates(context, scheduler, isMoving: isMoving);
        return scheduler;
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
