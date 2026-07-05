using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Moq;
using Daedalus.Data;
using Daedalus.Rotation.HecateCore.Abilities;
using Daedalus.Rotation.HecateCore.Modules;
using Daedalus.Services.Targeting;
using Daedalus.Tests.Mocks;
using Daedalus.Tests.Rotation.Common.Scheduling;
using Xunit;

namespace Daedalus.Tests.Rotation.HecateCore.Modules;

/// <summary>
/// BLM field regression (2026-07-04, Aitiascope): every Thunder spell in the current patch
/// REQUIRES the Thunderhead proc, but the ice phase kept a pre-Dawntrail "hard cast Thunder"
/// fallback for when the DoT lapsed with no proc up — the game refused each attempt with
/// "Cannot use yet" (ActionStatus 572) and the stuck candidate held the GCD hostage
/// ("Stuck — Thunder IV: ActionStatus 572"). No Thunderhead → no Thunder push, ever; the
/// phase rotation must keep producing castable GCDs instead.
/// </summary>
public class DamageModuleThunderGateTests
{
    private readonly DamageModule _module = new();

    [Fact]
    public void IcePhase_DotDownNoThunderhead_NeverPushesThunder()
    {
        // The exact field state: UI3, hearts full, MP full, DoT missing, no proc.
        var (context, scheduler) = Setup(hasThunderhead: false);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.DoesNotContain(scheduler.InspectGcdQueue(), c => IsThunder(c.Behavior));
    }

    [Fact]
    public void IcePhase_DotDownNoThunderhead_StillQueuesACastableGcd()
    {
        // Removing the fallback must not leave the GCD empty — the fire transition
        // (or another phase GCD) has to carry the rotation.
        var (context, scheduler) = Setup(hasThunderhead: false);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.NotEmpty(scheduler.InspectGcdQueue());
    }

    [Fact]
    public void IcePhase_DotDownWithThunderhead_PushesThunder()
    {
        var (context, scheduler) = Setup(hasThunderhead: true);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.Contains(scheduler.InspectGcdQueue(), c => IsThunder(c.Behavior));
    }

    [Fact]
    public void ThunderBehaviors_AllCarryThunderheadProcGate()
    {
        // Dispatch-level defense in depth: even if a module push slips through, the
        // scheduler's ProcBuff gate must reject a Thunder without the proc.
        Assert.Equal(BLMActions.StatusIds.Thunderhead, HecateAbilities.Thunder.ProcBuff);
        Assert.Equal(BLMActions.StatusIds.Thunderhead, HecateAbilities.Thunder3.ProcBuff);
        Assert.Equal(BLMActions.StatusIds.Thunderhead, HecateAbilities.HighThunder.ProcBuff);
        Assert.Equal(BLMActions.StatusIds.Thunderhead, HecateAbilities.Thunder2.ProcBuff);
        Assert.Equal(BLMActions.StatusIds.Thunderhead, HecateAbilities.Thunder4.ProcBuff);
        Assert.Equal(BLMActions.StatusIds.Thunderhead, HecateAbilities.HighThunder2.ProcBuff);
    }

    private static bool IsThunder(Daedalus.Rotation.Common.Scheduling.AbilityBehavior behavior)
        => behavior == HecateAbilities.Thunder || behavior == HecateAbilities.Thunder3
        || behavior == HecateAbilities.HighThunder || behavior == HecateAbilities.Thunder2
        || behavior == HecateAbilities.Thunder4 || behavior == HecateAbilities.HighThunder2;

    private static (Daedalus.Rotation.HecateCore.Context.IHecateContext context,
        Daedalus.Rotation.Common.Scheduling.RotationScheduler scheduler) Setup(bool hasThunderhead)
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
            level: 90,
            inUmbralIce: true,
            umbralIceStacks: 3,
            elementStacks: 3,
            elementTimer: 10f,
            isEnochianActive: true,
            umbralHearts: 3,
            mpPercent: 1f,
            hasThunderhead: hasThunderhead,
            hasThunderDoT: false,
            thunderDoTRemaining: 0f);

        return (context, scheduler);
    }
}
