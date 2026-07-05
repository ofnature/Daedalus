using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Moq;
using Daedalus.Services.Targeting;
using Daedalus.Rotation.ZeusCore.Abilities;
using Daedalus.Rotation.ZeusCore.Modules;
using Daedalus.Tests.Mocks;
using Daedalus.Tests.Rotation.Common.Scheduling;
using Xunit;

namespace Daedalus.Tests.Rotation.ZeusCore.Modules;

/// <summary>
/// DRG regression (2026-07-04 review): Rise of the Dragon was gated on Draconian Fire — the
/// combo-starter morph buff from the 5th combo hit — instead of Dragon's Flight, the actual
/// enabler granted by Dragonfire Dive. Same wrong-status double-kill class as the a202bc1
/// combo bug (push gate AND behavior ProcBuff both pointed at the wrong status), so at 92+
/// the follow-up only fired when the combo position happened to overlap the dive window.
/// </summary>
public class DamageModuleRiseOfTheDragonTests
{
    private readonly DamageModule _module = new();

    [Fact]
    public void DragonsFlight_PushesRiseOfTheDragon()
    {
        var (context, scheduler) = Setup(level: 100, hasDragonsFlight: true, hasDraconianFire: false);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.Contains(scheduler.InspectOgcdQueue(), c => c.Behavior == ZeusAbilities.RiseOfTheDragon);
    }

    [Fact]
    public void DraconianFireAlone_DoesNotPush()
    {
        // The regression: the combo-morph buff must NOT enable the Dragonfire Dive follow-up.
        var (context, scheduler) = Setup(level: 100, hasDragonsFlight: false, hasDraconianFire: true);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.DoesNotContain(scheduler.InspectOgcdQueue(), c => c.Behavior == ZeusAbilities.RiseOfTheDragon);
    }

    [Fact]
    public void NoEnablerStatus_DoesNotPush()
    {
        var (context, scheduler) = Setup(level: 100, hasDragonsFlight: false, hasDraconianFire: false);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.DoesNotContain(scheduler.InspectOgcdQueue(), c => c.Behavior == ZeusAbilities.RiseOfTheDragon);
    }

    [Fact]
    public void BelowLevel92_DoesNotPush()
    {
        var (context, scheduler) = Setup(level: 90, hasDragonsFlight: true, hasDraconianFire: false);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.DoesNotContain(scheduler.InspectOgcdQueue(), c => c.Behavior == ZeusAbilities.RiseOfTheDragon);
    }

    private static (Daedalus.Rotation.ZeusCore.Context.IZeusContext context,
        Daedalus.Rotation.Common.Scheduling.RotationScheduler scheduler) Setup(
        byte level, bool hasDragonsFlight, bool hasDraconianFire)
    {
        var enemy = new Mock<IBattleNpc>();
        enemy.Setup(x => x.GameObjectId).Returns(99999UL);
        enemy.Setup(x => x.CurrentHp).Returns(100000u);
        enemy.Setup(x => x.MaxHp).Returns(100000u);

        var targeting = MockBuilders.CreateMockTargetingService();
        targeting.Setup(x => x.FindEnemyForAction(
                It.IsAny<EnemyTargetingStrategy>(), It.IsAny<uint>(), It.IsAny<IPlayerCharacter>()))
            .Returns(enemy.Object);

        var actionService = MockBuilders.CreateMockActionService();
        actionService.Setup(x => x.IsActionReady(It.IsAny<uint>())).Returns(true);
        actionService.Setup(x => x.PlayerHasStatus(It.IsAny<uint>())).Returns(false);

        var scheduler = SchedulerFactory.CreateForTest(actionService: actionService);
        var context = ZeusTestContext.Create(
            actionService: actionService,
            targetingService: targeting,
            level: level,
            hasDragonsFlight: hasDragonsFlight,
            hasDraconianFire: hasDraconianFire);

        return (context, scheduler);
    }
}
