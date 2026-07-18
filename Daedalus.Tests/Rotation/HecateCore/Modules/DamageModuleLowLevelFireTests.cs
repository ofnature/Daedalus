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
    public void LowLevelFirePhase_MpExhausted_TransposesToIce()
    {
        // Field round 2 (2026-07-18): plain Blizzard cast in ASTRAL FIRE only STRIPS the fire
        // stacks (no Umbral Ice granted — only B3 hard-swaps), which ping-ponged the loop
        // fire-ice-fire-ice. The correct sub-35 swap is an instant Transpose.
        var (context, scheduler) = Setup(level: 25, currentMp: 2000);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.Contains(scheduler.InspectOgcdQueue(), c => c.Behavior == HecateAbilities.Transpose);
        Assert.DoesNotContain(scheduler.InspectGcdQueue(), c => c.Behavior == HecateAbilities.Fire);
        Assert.DoesNotContain(scheduler.InspectGcdQueue(), c => c.Behavior == HecateAbilities.Blizzard);
    }

    [Fact]
    public void LowLevelFirePhase_MpExhausted_TransposeDown_FallsBackToBlizzardStrip()
    {
        // Transpose rolling: Blizzard strips AF to neutral, and the start rotation's low-MP
        // branch (below) then opens ICE — two GCDs to converge, never a Fire relapse.
        var (context, scheduler) = Setup(level: 25, currentMp: 2000, transposeReady: false);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.Contains(scheduler.InspectGcdQueue(), c => c.Behavior == HecateAbilities.Blizzard);
        Assert.DoesNotContain(scheduler.InspectGcdQueue(), c => c.Behavior == HecateAbilities.Fire);
    }

    [Fact]
    public void StartRotation_Neutral_LowMp_OpensWithIce_NotFire()
    {
        // The other half of the ping-pong: from neutral on a drained tank the start rotation
        // used to Fire again. RSR AddElementBase parity: MP < 7200 opens ICE.
        var (context, scheduler) = Setup(level: 25, currentMp: 2000, neutral: true);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.Contains(scheduler.InspectGcdQueue(), c => c.Behavior == HecateAbilities.Blizzard);
        Assert.DoesNotContain(scheduler.InspectGcdQueue(), c => c.Behavior == HecateAbilities.Fire);
    }

    [Fact]
    public void StartRotation_Neutral_FullMp_StillOpensWithFire()
    {
        var (context, scheduler) = Setup(level: 25, currentMp: 10000, neutral: true);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.Contains(scheduler.InspectGcdQueue(), c => c.Behavior == HecateAbilities.Fire);
        Assert.DoesNotContain(scheduler.InspectGcdQueue(), c => c.Behavior == HecateAbilities.Blizzard);
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
    public void LowLevelFirePhase_NeverLeavesAllQueuesEmpty()
    {
        // The deadlock shape: nothing queued means the toon idles while the game refuses the
        // unaffordable Fire — SOME transition (Transpose weave or Blizzard strip) must queue.
        var (context, scheduler) = Setup(level: 12, currentMp: 800);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.True(scheduler.InspectGcdQueue().Count > 0 || scheduler.InspectOgcdQueue().Count > 0);
    }

    // ── Transpose path: ice → fire at low level (2026-07-18) ────────────────
    // A plain Fire hardcast from Umbral Ice only REMOVES the ice stacks (no Astral Fire) —
    // dead GCD + MP. Sub-Fire III the transition is an instant Transpose weave instead;
    // Fire III transitions (UI3 → AF3) are untouched.

    [Fact]
    public void LowLevelIceExit_UsesTranspose_NotFireHardcast()
    {
        var (context, scheduler) = Setup(level: 25, currentMp: 10000, inIce: true);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.Contains(scheduler.InspectOgcdQueue(), c => c.Behavior == HecateAbilities.Transpose);
        Assert.DoesNotContain(scheduler.InspectGcdQueue(), c => c.Behavior == HecateAbilities.Fire);
    }

    [Fact]
    public void LowLevelIceExit_TransposeOnCooldown_FallsBackToFireHardcast()
    {
        var (context, scheduler) = Setup(level: 25, currentMp: 10000, inIce: true, transposeReady: false);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.DoesNotContain(scheduler.InspectOgcdQueue(), c => c.Behavior == HecateAbilities.Transpose);
        Assert.Contains(scheduler.InspectGcdQueue(), c => c.Behavior == HecateAbilities.Fire);
    }

    [Fact]
    public void FireIiiLevel_IceExit_KeepsFire3Transition()
    {
        var (context, scheduler) = Setup(level: 40, currentMp: 10000, inIce: true, fire3Learned: true);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.Contains(scheduler.InspectGcdQueue(), c => c.Behavior == HecateAbilities.Fire3);
        Assert.DoesNotContain(scheduler.InspectOgcdQueue(), c => c.Behavior == HecateAbilities.Transpose);
    }

    private static (Daedalus.Rotation.HecateCore.Context.IHecateContext context,
        Daedalus.Rotation.Common.Scheduling.RotationScheduler scheduler) Setup(
        byte level, int currentMp, bool inIce = false, bool transposeReady = true, bool fire3Learned = false,
        bool neutral = false)
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
        actionService.Setup(x => x.IsActionReady(Daedalus.Data.BLMActions.Transpose.ActionId))
            .Returns(transposeReady);
        actionService.Setup(x => x.IsActionLearned(It.IsAny<uint>())).Returns(fire3Learned);

        var scheduler = SchedulerFactory.CreateForTest(actionService: actionService);
        var context = HecateTestContext.Create(
            actionService: actionService,
            targetingService: targeting,
            level: level,
            inAstralFire: !inIce && !neutral,
            astralFireStacks: inIce || neutral ? 0 : 1,
            inUmbralIce: inIce,
            umbralIceStacks: inIce ? 3 : 0,
            elementStacks: neutral ? 0 : inIce ? 3 : 1,
            elementTimer: 12f,
            currentMp: currentMp,
            mpPercent: currentMp / 10000f,
            hasThunderhead: false,
            hasThunderDoT: true,
            thunderDoTRemaining: 20f);

        return (context, scheduler);
    }
}
