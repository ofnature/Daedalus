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
/// BLM field regression (2026-07-05, Origenics): Umbral Ice does NOT passively regenerate MP
/// on the current patch — the refill triggers on CASTING an ice spell (full refill from
/// Blizzard/Blizzard IV at UI3). The ice phase's "Waiting for MP" tail queued nothing, so a
/// pull entered with 3 carried-over Umbral Hearts (Blizzard IV branch skipped) and
/// Despair-emptied MP idled 60-90s on natural regen alone (21% uptime pull). The phase must
/// actively cast an ice GCD to refill, and the fire-transition gate is 9600 MP (RSR-aligned)
/// — the old 99% gate sat one natural-regen tick short forever ("Waiting for MP (98%)").
/// </summary>
public class DamageModuleIceMpRefillTests
{
    private readonly DamageModule _module = new();

    [Fact]
    public void IcePhase_HeartsFullLowMp_QueuesBlizzardIvRefill()
    {
        // The exact field state: UI3, hearts carried over at 3, MP dumped by Despair.
        var (context, scheduler) = Setup(currentMp: 1600);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.Contains(scheduler.InspectGcdQueue(), c => c.Behavior == HecateAbilities.Blizzard4);
    }

    [Fact]
    public void IcePhase_HeartsFullLowMp_NeverLeavesGcdQueueEmpty()
    {
        // The core deadlock: nothing queued means nothing casts, and no cast means no MP refill.
        var (context, scheduler) = Setup(currentMp: 1600);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.NotEmpty(scheduler.InspectGcdQueue());
    }

    [Fact]
    public void IcePhase_HeartsFullJustBelowThreshold_RefillsInsteadOfWaiting()
    {
        // Just under the transition gate the phase must still queue a refill cast, not idle —
        // the field failure sat at "Waiting for MP (98%)" for 24s+ because the old tail
        // queued nothing and natural regen alone never crossed the 99% gate.
        var (context, scheduler) = Setup(currentMp: 9500);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.Contains(scheduler.InspectGcdQueue(), c => c.Behavior == HecateAbilities.Blizzard4);
    }

    [Fact]
    public void IcePhase_HeartsFullMpAtThreshold_TransitionsToFire()
    {
        // RSR-aligned gate: 9600 MP is enough to go to fire — don't demand a full bar.
        var (context, scheduler) = Setup(currentMp: 9600);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.Contains(scheduler.InspectGcdQueue(),
            c => c.Behavior == HecateAbilities.Fire3 || c.Behavior == HecateAbilities.Fire);
        Assert.DoesNotContain(scheduler.InspectGcdQueue(), c => c.Behavior == HecateAbilities.Blizzard4);
    }

    [Fact]
    public void IcePhase_BelowBlizzardIvLevel_LowMp_RefillsWithBlizzard()
    {
        // Below Lv58 there is no Blizzard IV; the refill falls back to Blizzard I,
        // whose Umbral Ice refill scales with stacks.
        var (context, scheduler) = Setup(currentMp: 1600, level: 50, umbralHearts: 0);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.Contains(scheduler.InspectGcdQueue(), c => c.Behavior == HecateAbilities.Blizzard);
    }

    private static (Daedalus.Rotation.HecateCore.Context.IHecateContext context,
        Daedalus.Rotation.Common.Scheduling.RotationScheduler scheduler) Setup(
        int currentMp, byte level = 100, int umbralHearts = 3)
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
            inUmbralIce: true,
            umbralIceStacks: 3,
            elementStacks: 3,
            elementTimer: 10f,
            isEnochianActive: true,
            umbralHearts: umbralHearts,
            currentMp: currentMp,
            mpPercent: currentMp / 10000f,
            hasThunderhead: false,
            hasThunderDoT: true,
            thunderDoTRemaining: 30f);

        return (context, scheduler);
    }
}
