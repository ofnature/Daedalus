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
/// SMN first-run field regression (Worqor Zormor, 2026-07-05): Crimson Strike — the melee
/// follow-up Crimson Cyclone grants via the Crimson Strike Ready buff (4403, RSR StatusNeed
/// parity) — was never wired: every Ifrit phase dashed in with Cyclone and left the free
/// instant unspent. The push is range-guarded module-side (a knockback after the dash must
/// not park the GCD on an unreachable melee action) and ProcBuff-gated at dispatch.
/// </summary>
public class DamageModuleCrimsonStrikeTests
{
    private readonly DamageModule _module = new();

    private static (Daedalus.Rotation.PersephoneCore.Context.IPersephoneContext context,
        Daedalus.Rotation.Common.Scheduling.RotationScheduler scheduler) Setup(
        bool strikeReady, Vector3? enemyPosition = null, byte level = 90)
    {
        var enemy = new Mock<IBattleNpc>();
        enemy.Setup(x => x.GameObjectId).Returns(12345UL);
        enemy.Setup(x => x.CurrentHp).Returns(100000u);
        enemy.Setup(x => x.MaxHp).Returns(100000u);
        enemy.Setup(x => x.Position).Returns(enemyPosition ?? Vector3.Zero);
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
            hasCrimsonStrikeReady: strikeReady);

        return (context, scheduler);
    }

    [Fact]
    public void CrimsonStrikeReady_InMelee_QueuesCrimsonStrike()
    {
        var (context, scheduler) = Setup(strikeReady: true);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.Contains(scheduler.InspectGcdQueue(), c => c.Behavior == PersephoneAbilities.CrimsonStrike);
    }

    [Fact]
    public void NoReadyBuff_NeverQueuesCrimsonStrike()
    {
        var (context, scheduler) = Setup(strikeReady: false);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.DoesNotContain(scheduler.InspectGcdQueue(), c => c.Behavior == PersephoneAbilities.CrimsonStrike);
    }

    [Fact]
    public void CrimsonStrikeReady_OutOfMelee_DoesNotQueueAnUnreachablePush()
    {
        // Knocked back after the Cyclone dash: a melee push at 20y would just park the GCD.
        // The Ready buff rides until we're back in reach.
        var (context, scheduler) = Setup(strikeReady: true, enemyPosition: new Vector3(20f, 0f, 0f));

        _module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.DoesNotContain(scheduler.InspectGcdQueue(), c => c.Behavior == PersephoneAbilities.CrimsonStrike);
    }

    [Fact]
    public void CrimsonStrikeBehavior_CarriesReadyProcGate()
    {
        // Dispatch-level defense in depth (Thunderhead pattern): even if a module push slips
        // through, the scheduler's ProcBuff gate rejects a Strike without the Ready buff.
        Assert.Equal(SMNActions.StatusIds.CrimsonStrikeReady, PersephoneAbilities.CrimsonStrike.ProcBuff);
    }

    [Fact]
    public void BelowLevel86_NeverQueuesCrimsonStrike()
    {
        var (context, scheduler) = Setup(strikeReady: true, level: 85);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.DoesNotContain(scheduler.InspectGcdQueue(), c => c.Behavior == PersephoneAbilities.CrimsonStrike);
    }
}
