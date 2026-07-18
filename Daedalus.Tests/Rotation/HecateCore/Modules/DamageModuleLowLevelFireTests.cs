using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Moq;
using Daedalus.Rotation.HecateCore.Abilities;
using Daedalus.Rotation.HecateCore.Modules;
using Daedalus.Services.Targeting;
using Daedalus.Tests.Mocks;
using Daedalus.Tests.Rotation.Common.Scheduling;
using Xunit;

namespace Daedalus.Tests.Rotation.HecateCore.Modules;

/// <summary>
/// BLM field regression (2026-07-17): the sub-Lv60 fire branch pushed Fire I UNCONDITIONALLY
/// and returned, so the ice transition below it was unreachable — a sub-30 BLM burned its MP
/// dry on Fire and never cast Blizzard at all. RSR parity: keep firing while
/// CurrentMp ≥ live fire cost + 800 (Astral Fire doubles fire costs → static floor 2400),
/// then Blizzard into Umbral Ice (free — opposing-element casts cost no MP in AF).
/// </summary>
public class DamageModuleLowLevelFireTests
{
    private readonly DamageModule _module = new();

    [Fact]
    public void LowLevelFirePhase_MpHealthy_CastsFire()
    {
        var (context, scheduler) = Setup(level: 25, currentMp: 10000);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.Contains(scheduler.InspectGcdQueue(), c => c.Behavior == HecateAbilities.Fire);
        Assert.DoesNotContain(scheduler.InspectGcdQueue(), c => c.Behavior == HecateAbilities.Blizzard);
    }

    [Fact]
    public void LowLevelFirePhase_MpExhausted_TransitionsToBlizzard()
    {
        // The exact field failure: sub-30, MP below the doubled-cost floor → Blizzard, not
        // another (refused) Fire.
        var (context, scheduler) = Setup(level: 25, currentMp: 2000);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.Contains(scheduler.InspectGcdQueue(), c => c.Behavior == HecateAbilities.Blizzard);
        Assert.DoesNotContain(scheduler.InspectGcdQueue(), c => c.Behavior == HecateAbilities.Fire);
    }

    [Fact]
    public void LowLevelFirePhase_MpAtFloor_StillFires()
    {
        // 2400 = doubled Fire cost (1600) + the RSR 800 buffer — the last legal Fire.
        var (context, scheduler) = Setup(level: 25, currentMp: 2400);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.Contains(scheduler.InspectGcdQueue(), c => c.Behavior == HecateAbilities.Fire);
    }

    [Fact]
    public void LowLevelFirePhase_NeverLeavesGcdQueueEmpty()
    {
        // The deadlock shape: an empty queue means the toon idles while the game refuses the
        // unaffordable Fire — some ice cast must always be queued when MP runs out.
        var (context, scheduler) = Setup(level: 12, currentMp: 800);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.NotEmpty(scheduler.InspectGcdQueue());
    }

    private static (Daedalus.Rotation.HecateCore.Context.IHecateContext context,
        Daedalus.Rotation.Common.Scheduling.RotationScheduler scheduler) Setup(
        byte level, int currentMp)
    {
        var enemy = new Mock<IBattleNpc>();
        enemy.Setup(x => x.GameObjectId).Returns(99999UL);
        enemy.Setup(x => x.CurrentHp).Returns(100000u);
        enemy.Setup(x => x.MaxHp).Returns(100000u);

        var targeting = MockBuilders.CreateMockTargetingService();
        targeting.Setup(x => x.FindEnemy(
                It.IsAny<EnemyTargetingStrategy>(), It.IsAny<float>(), It.IsAny<IPlayerCharacter>()))
            .Returns(enemy.Object);

        var actionService = MockBuilders.CreateMockActionService();
        actionService.Setup(x => x.IsActionReady(It.IsAny<uint>())).Returns(true);

        var scheduler = SchedulerFactory.CreateForTest(actionService: actionService);
        var context = HecateTestContext.Create(
            actionService: actionService,
            targetingService: targeting,
            level: level,
            inAstralFire: true,
            astralFireStacks: 1,
            elementStacks: 1,
            elementTimer: 12f,
            currentMp: currentMp,
            mpPercent: currentMp / 10000f,
            hasThunderhead: false,
            hasThunderDoT: true,
            thunderDoTRemaining: 20f);

        return (context, scheduler);
    }
}
