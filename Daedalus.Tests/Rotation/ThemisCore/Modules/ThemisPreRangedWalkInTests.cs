using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Moq;
using Daedalus.Rotation.ThemisCore.Abilities;
using Daedalus.Rotation.ThemisCore.Modules;
using Daedalus.Services.Tank;
using Daedalus.Services.Targeting;
using Daedalus.Tests.Mocks;
using Daedalus.Tests.Rotation.Common.Scheduling;
using Xunit;

namespace Daedalus.Tests.Rotation.ThemisCore.Modules;

/// <summary>
/// Pre-Lv15 Gladiator gathering (field request 2026-07-05): below Shield Lob a GLD facing a mob
/// outside melee has NO tool — no ranged GCD, no gap closer (Intervene is 66), and tanks had no
/// walk-in movement. The out-of-melee branch must not queue an uncastable Shield Lob, must name
/// the walk-in state in Why Stuck (the base rotation's pre-ranged walk-in owns the movement),
/// and at 15+ the ranged path must be unchanged.
/// </summary>
public sealed class ThemisPreRangedWalkInTests
{
    private readonly DamageModule _module = new();

    private static Mock<ITargetingService> BuildOutOfMeleeTargeting(IBattleNpc enemy)
    {
        var targeting = MockBuilders.CreateMockTargetingService();
        targeting.Setup(x => x.FindEnemyForAction(
                It.IsAny<EnemyTargetingStrategy>(), It.IsAny<uint>(), It.IsAny<IPlayerCharacter>()))
            .Returns((IBattleNpc?)null);
        targeting.Setup(x => x.FindEnemy(
                It.IsAny<EnemyTargetingStrategy>(), It.IsAny<float>(), It.IsAny<IPlayerCharacter>()))
            .Returns(enemy);
        return targeting;
    }

    private static Mock<IBattleNpc> CreateMockEnemy(ulong id = 12345UL)
    {
        var mock = new Mock<IBattleNpc>();
        mock.Setup(x => x.GameObjectId).Returns(id);
        mock.Setup(x => x.CurrentHp).Returns(10000u);
        mock.Setup(x => x.MaxHp).Returns(10000u);
        return mock;
    }

    private static Mock<IEnmityService> NoLostAggro()
    {
        var enmity = new Mock<IEnmityService>();
        enmity.Setup(x => x.HasLostAggroToOther(It.IsAny<IBattleChara>(), It.IsAny<uint>())).Returns(false);
        return enmity;
    }

    [Fact]
    public void Gladiator_Sub15_OutOfMelee_QueuesNoShieldLob()
    {
        var enemy = CreateMockEnemy();
        var targeting = BuildOutOfMeleeTargeting(enemy.Object);
        var actionService = MockBuilders.CreateMockActionService();
        var scheduler = SchedulerFactory.CreateForTest(actionService: actionService);
        var context = ThemisTestContext.Create(
            actionService: actionService,
            targetingService: targeting,
            enmityService: NoLostAggro(),
            level: 10);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.DoesNotContain(scheduler.InspectGcdQueue(), c => c.Behavior == ThemisAbilities.ShieldLob);
        Assert.DoesNotContain(scheduler.InspectOgcdQueue(), c => c.Behavior == ThemisAbilities.Intervene);
    }

    [Fact]
    public void Gladiator_Sub15_OutOfMelee_NamesWalkInStateInsteadOfSilence()
    {
        var enemy = CreateMockEnemy();
        var targeting = BuildOutOfMeleeTargeting(enemy.Object);
        var actionService = MockBuilders.CreateMockActionService();
        var scheduler = SchedulerFactory.CreateForTest(actionService: actionService);
        var context = ThemisTestContext.Create(
            actionService: actionService,
            targetingService: targeting,
            enmityService: NoLostAggro(),
            level: 10);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.Contains("walking in", context.Debug.DamageState);
    }

    [Fact]
    public void Gladiator_At15_OutOfMelee_StillQueuesShieldLob()
    {
        // Guard the existing behavior: once Shield Lob unlocks, ranged gathering resumes and the
        // walk-in state text must not appear.
        var enemy = CreateMockEnemy();
        var targeting = BuildOutOfMeleeTargeting(enemy.Object);
        var actionService = MockBuilders.CreateMockActionService();
        var scheduler = SchedulerFactory.CreateForTest(actionService: actionService);
        var context = ThemisTestContext.Create(
            actionService: actionService,
            targetingService: targeting,
            enmityService: NoLostAggro(),
            level: 15);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.Contains(scheduler.InspectGcdQueue(), c => c.Behavior == ThemisAbilities.ShieldLob);
        Assert.DoesNotContain("walking in", context.Debug.DamageState ?? "");
    }

    [Fact]
    public void WalkToTargetWithoutRangedTool_DefaultsOn()
    {
        // The walk-in is the only recovery a sub-15 tank has — it must be on out of the box.
        Assert.True(new Configuration().Tank.WalkToTargetWithoutRangedTool);
    }
}
