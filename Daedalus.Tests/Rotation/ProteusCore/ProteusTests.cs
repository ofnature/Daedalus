using System.Linq;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Moq;
using Daedalus.Config.DPS;
using Daedalus.Data;
using Daedalus.Rotation.ProteusCore.Abilities;
using Daedalus.Rotation.ProteusCore.Context;
using Daedalus.Rotation.ProteusCore.Helpers;
using Daedalus.Rotation.ProteusCore.Modules;
using Daedalus.Services.Action;
using Daedalus.Services.Targeting;
using Daedalus.Tests.Mocks;
using Daedalus.Tests.Rotation.Common.Scheduling;
using Xunit;

namespace Daedalus.Tests.Rotation.ProteusCore;

/// <summary>
/// Blue Mage (Proteus) v1: role→archetype mapping, spellbook data integrity, and role-driven
/// module behavior (Mighty Guard stance, role fillers).
/// </summary>
public class ProteusTests
{
    // ── Mimicry role mapping ────────────────────────────────────────────────

    [Theory]
    [InlineData(JobRegistry.Paladin, BluRole.Tank, true)]
    [InlineData(JobRegistry.WhiteMage, BluRole.Healer, true)]
    [InlineData(JobRegistry.Samurai, BluRole.Dps, true)]
    [InlineData(JobRegistry.Paladin, BluRole.Dps, false)]
    [InlineData(JobRegistry.WhiteMage, BluRole.Tank, false)]
    [InlineData(0u, BluRole.Dps, false)] // unresolved job never matches
    public void MimicryScan_MatchesRole(uint jobId, BluRole role, bool expected)
    {
        Assert.Equal(expected, MimicryScanHelper.MatchesRole(jobId, role));
    }

    // ── Spellbook data integrity ────────────────────────────────────────────

    [Fact]
    public void Spellbook_Has124UniqueSpells()
    {
        Assert.Equal(124, BLUSpellbook.All.Length);
        Assert.Equal(124, BLUSpellbook.All.Select(e => e.Number).Distinct().Count());
        Assert.Equal(124, BLUSpellbook.All.Select(e => e.ActionId).Distinct().Count());
        Assert.All(BLUSpellbook.All, e => Assert.False(string.IsNullOrWhiteSpace(e.SourceName)));
    }

    [Fact]
    public void Spellbook_SourceKinds_MatchGameSheets()
    {
        // Pulled from AozActionTransient 2026-07-02: 1 unlock spell, 13 Whalaqee Totem quest
        // rewards, and the rest split between duties and open world.
        Assert.Equal(1, BLUSpellbook.All.Count(e => e.Source == BLUSpellSource.JobUnlock));
        Assert.Equal(13, BLUSpellbook.All.Count(e => e.Source == BLUSpellSource.WhalaqeeTotem));
        Assert.Equal(124, BLUSpellbook.All.Count(e =>
            e.Source is BLUSpellSource.JobUnlock or BLUSpellSource.WhalaqeeTotem
                     or BLUSpellSource.Duty or BLUSpellSource.OpenWorld));
    }

    [Fact]
    public void Spellbook_KitIdsAgree_WithActionDefinitions()
    {
        // The rotation kit's ids must match the sheet-generated spellbook (one source of truth).
        var byName = BLUSpellbook.All.ToDictionary(e => e.Name, e => e.ActionId);
        Assert.Equal(byName["Sonic Boom"], BLUActions.SonicBoom.ActionId);
        Assert.Equal(byName["Mighty Guard"], BLUActions.MightyGuard.ActionId);
        Assert.Equal(byName["Diamondback"], BLUActions.Diamondback.ActionId);
        Assert.Equal(byName["White Wind"], BLUActions.WhiteWind.ActionId);
        Assert.Equal(byName["Aetheric Mimicry"], BLUActions.AethericMimicry.ActionId);
        Assert.Equal(byName["Goblin Punch"], BLUActions.GoblinPunch.ActionId);
    }

    // ── Role-driven module behavior ─────────────────────────────────────────

    [Fact]
    public void BuffModule_TankRole_PushesMightyGuard_WhenStanceMissing()
    {
        var (context, scheduler) = Harness(role: BluRole.Tank, hasMightyGuard: false);
        new BuffModule().CollectCandidates(context, scheduler, isMoving: false);
        Assert.Contains(scheduler.InspectGcdQueue(), c => c.Behavior == ProteusAbilities.MightyGuard);
    }

    [Fact]
    public void BuffModule_DpsRole_DropsMightyGuard_WhenStanceStuckOn()
    {
        // Mighty Guard is -40% damage dealt: leaving tank role must toggle it OFF.
        var (context, scheduler) = Harness(role: BluRole.Dps, hasMightyGuard: true);
        new BuffModule().CollectCandidates(context, scheduler, isMoving: false);
        Assert.Contains(scheduler.InspectGcdQueue(), c => c.Behavior == ProteusAbilities.MightyGuard);
    }

    [Fact]
    public void BuffModule_TankRole_StanceActive_NoRedundantPush()
    {
        var (context, scheduler) = Harness(role: BluRole.Tank, hasMightyGuard: true);
        new BuffModule().CollectCandidates(context, scheduler, isMoving: false);
        Assert.DoesNotContain(scheduler.InspectGcdQueue(), c => c.Behavior == ProteusAbilities.MightyGuard);
    }

    [Fact]
    public void DamageModule_TankRole_PushesGoblinPunchFiller()
    {
        var (context, scheduler) = Harness(role: BluRole.Tank, hasMightyGuard: true);
        new DamageModule().CollectCandidates(context, scheduler, isMoving: false);
        var gcd = scheduler.InspectGcdQueue();
        Assert.Contains(gcd, c => c.Behavior == ProteusAbilities.GoblinPunch);
        Assert.Contains(gcd, c => c.Behavior == ProteusAbilities.SonicBoom);
    }

    [Fact]
    public void DamageModule_DpsRole_NoGoblinPunch_SonicBoomFiller()
    {
        var (context, scheduler) = Harness(role: BluRole.Dps, hasMightyGuard: false);
        new DamageModule().CollectCandidates(context, scheduler, isMoving: false);
        var gcd = scheduler.InspectGcdQueue();
        Assert.DoesNotContain(gcd, c => c.Behavior == ProteusAbilities.GoblinPunch);
        Assert.Contains(gcd, c => c.Behavior == ProteusAbilities.SonicBoom);
        Assert.Contains(gcd, c => c.Behavior == ProteusAbilities.WaterCannon);
    }

    [Fact]
    public void DamageModule_Diamondback_LocksEverything()
    {
        var (context, scheduler) = Harness(role: BluRole.Tank, hasMightyGuard: true, hasDiamondback: true);
        new DamageModule().CollectCandidates(context, scheduler, isMoving: false);
        Assert.Empty(scheduler.InspectGcdQueue());
    }

    // ── Learned/slotted gates (unslotted spells must not be pushed at all) ──

    [Fact]
    public void BuffModule_MimicryNotSlotted_NoPush_NoBlacklistChurn()
    {
        // Without the gate, an unslotted Mimicry never lands, the grace window expires, and the
        // scan blacklists the innocent target — cycling through every valid ally.
        var (context, scheduler) = Harness(role: BluRole.Tank, hasMightyGuard: true);
        Mock.Get(context).Setup(x => x.HasCorrectMimicry).Returns(false);
        Mock.Get(context).Setup(x => x.IsSpellUsable(BLUActions.AethericMimicry.ActionId)).Returns(false);

        new BuffModule().CollectCandidates(context, scheduler, isMoving: false);

        Assert.DoesNotContain(scheduler.InspectGcdQueue(), c => c.Behavior == ProteusAbilities.AethericMimicry);
        Assert.Equal("Mimicry not slotted", context.Debug.MimicryState);
    }

    [Fact]
    public void BuffModule_NeverCastsMimicryInsideDuty()
    {
        // Jobs are locked once inside an instance — mimicry must be grabbed BEFORE queuing. The
        // test harness reads as in-duty (Condition service unavailable → IsInInstancedDuty()==true),
        // so a missing mimicry must produce the "grab it before queuing" report, never a cast.
        var (context, scheduler) = Harness(role: BluRole.Tank, hasMightyGuard: true);
        Mock.Get(context).Setup(x => x.HasCorrectMimicry).Returns(false);

        new BuffModule().CollectCandidates(context, scheduler, isMoving: false);

        Assert.DoesNotContain(scheduler.InspectGcdQueue(), c => c.Behavior == ProteusAbilities.AethericMimicry);
        Assert.Contains("BEFORE queuing", context.Debug.MimicryState);
    }

    [Fact]
    public void BuffModule_MightyGuardNotSlotted_NoPush()
    {
        var (context, scheduler) = Harness(role: BluRole.Tank, hasMightyGuard: false);
        Mock.Get(context).Setup(x => x.IsSpellUsable(BLUActions.MightyGuard.ActionId)).Returns(false);

        new BuffModule().CollectCandidates(context, scheduler, isMoving: false);

        Assert.DoesNotContain(scheduler.InspectGcdQueue(), c => c.Behavior == ProteusAbilities.MightyGuard);
    }

    [Fact]
    public void HealingModule_WhiteWindNotSlotted_NoPush()
    {
        var (context, scheduler) = Harness(role: BluRole.Healer, hasMightyGuard: false);
        Mock.Get(context).Setup(x => x.PartyHealthMetrics).Returns((0.5f, 0.4f, 3)); // party hurting
        Mock.Get(context).Setup(x => x.IsSpellUsable(BLUActions.WhiteWind.ActionId)).Returns(false);

        new HealingModule().CollectCandidates(context, scheduler, isMoving: false);

        Assert.DoesNotContain(scheduler.InspectGcdQueue(), c => c.Behavior == ProteusAbilities.WhiteWind);
        Assert.Equal("White Wind not slotted", context.Debug.HealingState);
    }

    [Fact]
    public void HealingModule_WhiteWindSlotted_PushesOnHurtParty()
    {
        // Control for the gate test: same hurt party, spell usable → the heal IS pushed.
        var (context, scheduler) = Harness(role: BluRole.Healer, hasMightyGuard: false);
        Mock.Get(context).Setup(x => x.PartyHealthMetrics).Returns((0.5f, 0.4f, 3));

        new HealingModule().CollectCandidates(context, scheduler, isMoving: false);

        Assert.Contains(scheduler.InspectGcdQueue(), c => c.Behavior == ProteusAbilities.WhiteWind);
    }

    [Fact]
    public void MitigationModule_DiamondbackNotSlotted_ReportsInsteadOfPushing()
    {
        var (context, scheduler) = Harness(role: BluRole.Tank, hasMightyGuard: true);
        Mock.Get(context).Setup(x => x.IsSpellUsable(BLUActions.Diamondback.ActionId)).Returns(false);

        new MitigationModule().CollectCandidates(context, scheduler, isMoving: false);

        Assert.DoesNotContain(scheduler.InspectGcdQueue(), c => c.Behavior == ProteusAbilities.Diamondback);
        Assert.Equal("Diamondback not slotted", context.Debug.MitigationState);
    }

    [Fact]
    public void MitigationModule_HoldsDiamondbackWhileMoving()
    {
        // 2.0s hardcast: casting while moving start-cancel loops at exactly the panic moment.
        var (context, scheduler) = Harness(role: BluRole.Tank, hasMightyGuard: true);

        new MitigationModule().CollectCandidates(context, scheduler, isMoving: true);

        Assert.DoesNotContain(scheduler.InspectGcdQueue(), c => c.Behavior == ProteusAbilities.Diamondback);
        Assert.Equal("Diamondback (waiting: moving)", context.Debug.MitigationState);
    }

    // ── Harness ─────────────────────────────────────────────────────────────

    private static (IProteusContext context, Daedalus.Rotation.Common.Scheduling.RotationScheduler scheduler)
        Harness(BluRole role, bool hasMightyGuard, bool hasDiamondback = false)
    {
        var config = new Configuration();
        config.BlueMage.Role = role;

        var enemy = new Mock<IBattleNpc>();
        enemy.Setup(x => x.GameObjectId).Returns(4242UL);
        enemy.Setup(x => x.CurrentHp).Returns(100000u);
        enemy.Setup(x => x.MaxHp).Returns(100000u);

        var targeting = MockBuilders.CreateMockTargetingService();
        targeting.Setup(x => x.IsDamageTargetingPaused()).Returns(false);
        targeting.Setup(x => x.FindEnemy(
                It.IsAny<EnemyTargetingStrategy>(), It.IsAny<float>(), It.IsAny<IPlayerCharacter>()))
            .Returns(enemy.Object);
        targeting.Setup(x => x.FindEnemyNeedingDot(
                It.IsAny<uint>(), It.IsAny<float>(), It.IsAny<float>(), It.IsAny<IPlayerCharacter>()))
            .Returns((IBattleNpc?)null);
        targeting.Setup(x => x.CountEnemiesInRange(It.IsAny<float>(), It.IsAny<IPlayerCharacter>()))
            .Returns(1);

        var actionService = MockBuilders.CreateMockActionService();
        actionService.Setup(x => x.IsActionLearned(It.IsAny<uint>())).Returns(true);
        actionService.Setup(x => x.IsActionReady(It.IsAny<uint>())).Returns(true);

        var player = MockBuilders.CreateMockPlayerCharacter(level: 80, currentHp: 100000, maxHp: 100000);

        var mock = new Mock<IProteusContext>();
        mock.Setup(x => x.Player).Returns(player.Object);
        mock.Setup(x => x.InCombat).Returns(true);
        mock.Setup(x => x.IsMoving).Returns(false);
        mock.Setup(x => x.Configuration).Returns(config);
        mock.Setup(x => x.ActionService).Returns(actionService.Object);
        mock.Setup(x => x.TargetingService).Returns(targeting.Object);
        mock.Setup(x => x.Role).Returns(role);
        mock.Setup(x => x.HasMightyGuard).Returns(hasMightyGuard);
        mock.Setup(x => x.HasDiamondback).Returns(hasDiamondback);
        mock.Setup(x => x.HasCorrectMimicry).Returns(true); // mimicry not under test here
        // Learned+slotted availability: everything usable (loadout gating has its own tests)
        mock.Setup(x => x.IsSpellUsable(It.IsAny<uint>())).Returns(true);
        mock.Setup(x => x.CurrentMp).Returns(10000);
        mock.Setup(x => x.PartyHealthMetrics).Returns((1f, 1f, 0));
        mock.Setup(x => x.Debug).Returns(new ProteusDebugState());
        mock.Setup(x => x.TrainingService).Returns((Daedalus.Services.Training.ITrainingService?)null);

        var scheduler = SchedulerFactory.CreateForTest(actionService: actionService, config: config);
        return (mock.Object, scheduler);
    }
}
