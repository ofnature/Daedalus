using System;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Moq;
using Daedalus.Services.Targeting;
using Daedalus.Tests.Mocks;
using Daedalus.Tests.Rotation.Common.Scheduling;
using Xunit;

namespace Daedalus.Tests.Rotation.Common;

/// <summary>
/// Melee audit 2026-07-18 (generalizing the MNK Henchman lesson to every melee DPS):
/// (1) beyond melee, a PASSIVE automation hard target must keep the combo starter queued —
/// every module used to bail with "No target" and the driver waited forever;
/// (2) positional enforce-holds against Dawntrail-REMOVED positionals are deleted
/// (DRG: all removed in 7.0; VPR finisher family: removed in 7.05 — RSR-verified), and
/// RPR's holds (Gibbet/Gallows are REAL) never fire solo, where the positional mover is
/// disabled by design.
/// </summary>
public class MeleeAutomationEngageTests
{
    private static Mock<IBattleNpc> PassiveMark(ulong id = 7777UL)
    {
        var mark = new Mock<IBattleNpc>();
        mark.Setup(x => x.GameObjectId).Returns(id);
        mark.Setup(x => x.IsDead).Returns(false);
        mark.Setup(x => x.CurrentHp).Returns(50000u);
        mark.Setup(x => x.MaxHp).Returns(50000u);
        return mark;
    }

    private static Mock<ITargetingService> OutOfMeleeTargeting(Mock<IBattleNpc> mark)
    {
        var targeting = MockBuilders.CreateMockTargetingService();
        targeting.Setup(x => x.FindEnemyForAction(
                It.IsAny<EnemyTargetingStrategy>(), It.IsAny<uint>(), It.IsAny<IPlayerCharacter>()))
            .Returns((IBattleNpc?)null);
        targeting.Setup(x => x.GetUserEnemyTarget()).Returns(mark.Object);
        return targeting;
    }

    private static void AssertQueued(
        Daedalus.Rotation.Common.Scheduling.RotationScheduler scheduler,
        Daedalus.Rotation.Common.Scheduling.AbilityBehavior starter)
        => Assert.Contains(scheduler.InspectGcdQueue(),
            c => c.Behavior == starter && c.TargetId == 7777UL);

    [Fact]
    public void Sam_OutOfMelee_AutomationOverride_QueuesHakazeAtMark()
    {
        var mark = PassiveMark();
        var config = new Configuration();
        var context = NikeCore.NikeTestContext.Create(
            config: config, targetingService: OutOfMeleeTargeting(mark), inCombat: true);
        var scheduler = SchedulerFactory.CreateForTest(config: config);

        config.ExternalCombatOverride = true;
        try { new Daedalus.Rotation.NikeCore.Modules.DamageModule().CollectCandidates(context, scheduler, isMoving: false); }
        finally { config.ExternalCombatOverride = false; }

        AssertQueued(scheduler, Daedalus.Rotation.NikeCore.Abilities.NikeAbilities.Hakaze);
    }

    [Fact]
    public void Nin_OutOfMelee_AutomationOverride_QueuesSpinningEdgeAtMark()
    {
        var mark = PassiveMark();
        var config = new Configuration();
        var context = HermesCore.HermesTestContext.Create(
            config: config, targetingService: OutOfMeleeTargeting(mark), inCombat: true);
        var scheduler = SchedulerFactory.CreateForTest(config: config);

        config.ExternalCombatOverride = true;
        try { new Daedalus.Rotation.HermesCore.Modules.DamageModule().CollectCandidates(context, scheduler, isMoving: false); }
        finally { config.ExternalCombatOverride = false; }

        AssertQueued(scheduler, Daedalus.Rotation.HermesCore.Abilities.HermesAbilities.SpinningEdge);
    }

    [Fact]
    public void Drg_OutOfMelee_AutomationOverride_QueuesTrueThrustAtMark()
    {
        var mark = PassiveMark();
        var config = new Configuration();
        var context = ZeusCore.ZeusTestContext.Create(
            config: config, targetingService: OutOfMeleeTargeting(mark), inCombat: true);
        var scheduler = SchedulerFactory.CreateForTest(config: config);

        config.ExternalCombatOverride = true;
        try { new Daedalus.Rotation.ZeusCore.Modules.DamageModule().CollectCandidates(context, scheduler, isMoving: false); }
        finally { config.ExternalCombatOverride = false; }

        AssertQueued(scheduler, Daedalus.Rotation.ZeusCore.Abilities.ZeusAbilities.TrueThrust);
    }

    [Fact]
    public void Rpr_OutOfMelee_AutomationOverride_QueuesSliceAtMark()
    {
        var mark = PassiveMark();
        var config = new Configuration();
        var context = ThanatosCore.ThanatosTestContext.Create(
            config: config, targetingService: OutOfMeleeTargeting(mark), inCombat: true);
        var scheduler = SchedulerFactory.CreateForTest(config: config);

        config.ExternalCombatOverride = true;
        try { new Daedalus.Rotation.ThanatosCore.Modules.DamageModule().CollectCandidates(context, scheduler, isMoving: false); }
        finally { config.ExternalCombatOverride = false; }

        AssertQueued(scheduler, Daedalus.Rotation.ThanatosCore.Abilities.ThanatosAbilities.Slice);
    }

    [Fact]
    public void Vpr_OutOfMelee_AutomationOverride_QueuesSteelFangsAtMark()
    {
        var mark = PassiveMark();
        var config = new Configuration();
        var context = EchidnaCore.EchidnaTestContext.Create(
            config: config, targetingService: OutOfMeleeTargeting(mark), inCombat: true);
        var scheduler = SchedulerFactory.CreateForTest(config: config);

        config.ExternalCombatOverride = true;
        try { new Daedalus.Rotation.EchidnaCore.Modules.DamageModule().CollectCandidates(context, scheduler, isMoving: false); }
        finally { config.ExternalCombatOverride = false; }

        AssertQueued(scheduler, Daedalus.Rotation.EchidnaCore.Abilities.EchidnaAbilities.SteelFangs);
    }

    [Fact]
    public void AllMelee_OutOfMelee_NoOverride_PushNothingAtPassiveMobs()
    {
        // Manual play unchanged: without the override, no module opens on a passive mob.
        var targeting = MockBuilders.CreateMockTargetingService();
        targeting.Setup(x => x.FindEnemyForAction(
                It.IsAny<EnemyTargetingStrategy>(), It.IsAny<uint>(), It.IsAny<IPlayerCharacter>()))
            .Returns((IBattleNpc?)null);
        targeting.Setup(x => x.GetUserEnemyTarget()).Returns((IBattleNpc?)null);

        var scheduler = SchedulerFactory.CreateForTest();
        new Daedalus.Rotation.NikeCore.Modules.DamageModule().CollectCandidates(
            NikeCore.NikeTestContext.Create(targetingService: targeting, inCombat: true), scheduler, isMoving: false);
        Assert.Empty(scheduler.InspectGcdQueue());
    }
}
