using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Moq;
using Daedalus.Data;
using Daedalus.Rotation.ZeusCore.Abilities;
using Daedalus.Rotation.ZeusCore.Modules;
using Daedalus.Services.Targeting;
using Daedalus.Tests.Mocks;
using Daedalus.Tests.Rotation.Common.Scheduling;
using Xunit;

namespace Daedalus.Tests.Rotation.ZeusCore.Modules;

/// <summary>
/// Regression tests from the open-world Lancer log (2026-07-03): a Lv6 toon with combo state
/// at "step 3 pending" pushed the unlearned Full Thrust forever (IsActionReady is cooldown-only
/// and passes for unlearned actions), and the early return meant no starter existed to restart
/// the 1-2-3 — the rotation sat dead until the mob died (23-45% uptime pulls). Every combo step
/// push is now backed by the starter at a lower priority (the Echidna pattern), and finishers
/// are level-gated.
/// </summary>
public class DamageModuleComboFallbackTests
{
    private readonly DamageModule _module = new();

    [Fact]
    public void LowLevel_VorpalComboPending_RestartsTrueThrust()
    {
        var (context, scheduler) = Setup(
            level: 6, lastComboAction: DRGActions.VorpalThrust.ActionId, comboTimeRemaining: 20f);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        var gcds = scheduler.InspectGcdQueue();
        Assert.Contains(gcds, c => c.Behavior == ZeusAbilities.TrueThrust);
        Assert.DoesNotContain(gcds, c => c.Behavior == ZeusAbilities.VorpalFinisher);
    }

    [Fact]
    public void MaxLevel_VorpalComboPending_PushesFinisher_WithStarterFallback()
    {
        var (context, scheduler) = Setup(
            level: 100, lastComboAction: DRGActions.VorpalThrust.ActionId, comboTimeRemaining: 20f);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        var gcds = scheduler.InspectGcdQueue();
        var finisher = Assert.Single(gcds, c => c.Behavior == ZeusAbilities.VorpalFinisher);
        var starter = Assert.Single(gcds, c => c.Behavior == ZeusAbilities.TrueThrust);
        Assert.True(finisher.Priority < starter.Priority);
    }

    [Fact]
    public void MaxLevel_Step2Pending_PushesStarterFallback()
    {
        var (context, scheduler) = Setup(
            level: 100, lastComboAction: DRGActions.TrueThrust.ActionId, comboTimeRemaining: 20f);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        var gcds = scheduler.InspectGcdQueue();
        Assert.Contains(gcds, c => c.Behavior == ZeusAbilities.Disembowel || c.Behavior == ZeusAbilities.VorpalThrust);
        Assert.Contains(gcds, c => c.Behavior == ZeusAbilities.TrueThrust);
    }

    [Fact]
    public void LowLevel_AoePack_FallsBackToSingleTargetCombo()
    {
        // Below Doom Spike (Lv40) there is no AoE chain — a 3+ pack must still run the ST combo.
        var (context, scheduler) = Setup(level: 6, enemiesInRange: 4);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.Contains(scheduler.InspectGcdQueue(), c => c.Behavior == ZeusAbilities.TrueThrust);
    }

    [Fact]
    public void MaxLevel_AoeStep2Pending_PushesDoomSpikeFallback()
    {
        var (context, scheduler) = Setup(
            level: 100, lastComboAction: DRGActions.SonicThrust.ActionId, comboTimeRemaining: 20f,
            enemiesInRange: 4);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        var gcds = scheduler.InspectGcdQueue();
        Assert.Contains(gcds, c => c.Behavior == ZeusAbilities.CoerthanTorment);
        Assert.Contains(gcds, c => c.Behavior == ZeusAbilities.DoomSpike);
    }

    private static (Daedalus.Rotation.ZeusCore.Context.IZeusContext context,
        Daedalus.Rotation.Common.Scheduling.RotationScheduler scheduler) Setup(
        byte level,
        uint lastComboAction = 0,
        float comboTimeRemaining = 0f,
        int enemiesInRange = 1)
    {
        var enemy = new Mock<IBattleNpc>();
        enemy.Setup(x => x.GameObjectId).Returns(777UL);
        enemy.Setup(x => x.CurrentHp).Returns(100000u);
        enemy.Setup(x => x.MaxHp).Returns(100000u);

        var targeting = MockBuilders.CreateMockTargetingService(countEnemiesInRange: enemiesInRange);
        targeting.Setup(x => x.FindEnemyForAction(
                It.IsAny<EnemyTargetingStrategy>(), It.IsAny<uint>(), It.IsAny<IPlayerCharacter>()))
            .Returns(enemy.Object);

        var actionService = MockBuilders.CreateMockActionService();

        var scheduler = SchedulerFactory.CreateForTest(actionService: actionService);
        var context = ZeusTestContext.Create(
            actionService: actionService,
            targetingService: targeting,
            level: level,
            lastComboAction: lastComboAction,
            comboTimeRemaining: comboTimeRemaining);

        return (context, scheduler);
    }
}
