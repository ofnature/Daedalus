using System.Numerics;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Moq;
using Daedalus.Data;
using Daedalus.Rotation.PersephoneCore.Abilities;
using Daedalus.Rotation.PersephoneCore.Modules;
using Daedalus.Services.Targeting;
using Daedalus.Tests.Mocks;
using Daedalus.Tests.Rotation.Common.Scheduling;
using Xunit;

namespace Daedalus.Tests.Rotation.PersephoneCore.Modules;

/// <summary>
/// The Porta Decumana at Lv.50, 2026-09-27: Ruin II was defined as instant and used as the movement filler below Ruin IV,
/// but it is a 1.5s cast -- every GCD on the move started a cast the next step cancelled, 50 times in six minutes.
/// </summary>
public class DamageModuleMovingFillerTests
{
    private readonly DamageModule _module = new();

    private static (Daedalus.Rotation.PersephoneCore.Context.IPersephoneContext context,
        Daedalus.Rotation.Common.Scheduling.RotationScheduler scheduler) Setup(
        byte level, bool isMoving, bool hasInstantCast = false, bool hasFurtherRuin = false)
    {
        var enemy = new Mock<IBattleNpc>();
        enemy.Setup(x => x.GameObjectId).Returns(12345UL);
        enemy.Setup(x => x.CurrentHp).Returns(100000u);
        enemy.Setup(x => x.MaxHp).Returns(100000u);
        enemy.Setup(x => x.Position).Returns(Vector3.Zero);
        enemy.Setup(x => x.HitboxRadius).Returns(0.5f);

        var targeting = MockBuilders.CreateMockTargetingService();
        targeting.Setup(x => x.FindEnemy(
                It.IsAny<EnemyTargetingStrategy>(), It.IsAny<float>(), It.IsAny<IPlayerCharacter>()))
            .Returns(enemy.Object);

        var actionService = MockBuilders.CreateMockActionService();
        actionService.Setup(x => x.IsActionReady(It.IsAny<uint>())).Returns(true);

        var scheduler = SchedulerFactory.CreateForTest(actionService: actionService);
        var context = PersephoneTestContext.Create(
            actionService: actionService,
            targetingService: targeting,
            level: level,
            hasPetSummoned: true,
            isMoving: isMoving,
            hasInstantCast: hasInstantCast,
            hasFurtherRuin: hasFurtherRuin);

        return (context, scheduler);
    }

    [Fact]
    public void RuinII_IsACast_NotInstant()
        => Assert.True(SMNActions.Ruin2.CastTime > 0f);

    [Fact]
    public void Level50_Moving_NoInstant_QueuesNoRuinCastToBeCancelled()
    {
        var (context, scheduler) = Setup(level: 50, isMoving: true);

        _module.CollectCandidates(context, scheduler, isMoving: true);

        var queue = scheduler.InspectGcdQueue();
        Assert.DoesNotContain(queue, c => c.Behavior == PersephoneAbilities.Ruin2);
        Assert.DoesNotContain(queue, c => c.Behavior == PersephoneAbilities.Ruin);
        Assert.DoesNotContain(queue, c => c.Behavior == PersephoneAbilities.Ruin3);
    }

    [Fact]
    public void Level50_Standing_CastsTheFiller()
    {
        var (context, scheduler) = Setup(level: 50, isMoving: false);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.Contains(scheduler.InspectGcdQueue(), c => c.Behavior == PersephoneAbilities.Ruin);
    }

    [Fact]
    public void Level50_Moving_WithSwiftcast_StillCasts()
    {
        var (context, scheduler) = Setup(level: 50, isMoving: true, hasInstantCast: true);

        _module.CollectCandidates(context, scheduler, isMoving: true);

        Assert.Contains(scheduler.InspectGcdQueue(), c => c.Behavior == PersephoneAbilities.Ruin);
    }

    [Fact]
    public void Level90_Moving_WithFurtherRuin_StillUsesRuinIV()
    {
        var (context, scheduler) = Setup(level: 90, isMoving: true, hasFurtherRuin: true);

        _module.CollectCandidates(context, scheduler, isMoving: true);

        Assert.Contains(scheduler.InspectGcdQueue(), c => c.Behavior == PersephoneAbilities.Ruin4);
    }
}
