using Moq;
using Daedalus.Data;
using Daedalus.Rotation.ZeusCore.Abilities;
using Daedalus.Rotation.ZeusCore.Modules;
using Daedalus.Tests.Mocks;
using Daedalus.Tests.Rotation.Common.Scheduling;
using Xunit;

namespace Daedalus.Tests.Rotation.ZeusCore.Modules;

/// <summary>
/// RSR DRG_Reborn (8153eea2): Lance Charge is held while Battle Litany is cooling down and back
/// within 10s, so both buffs go out together. With Battle Litany ready, further away, or not
/// learned yet, Lance Charge goes out as before.
/// </summary>
public class BuffModuleLanceChargeHoldTests
{
    private readonly BuffModule _module = new();

    [Theory]
    [InlineData(5f)]
    [InlineData(10f)]
    public void BattleLitanyBackWithin10s_HoldsLanceCharge(float litanyCooldown)
    {
        var (context, scheduler) = Setup(litanyCooldown: litanyCooldown);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.DoesNotContain(scheduler.InspectOgcdQueue(), c => c.Behavior == ZeusAbilities.LanceCharge);
    }

    [Fact]
    public void BattleLitanyMoreThan10sAway_FiresLanceCharge()
    {
        var (context, scheduler) = Setup(litanyCooldown: 10.5f);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.Contains(scheduler.InspectOgcdQueue(), c => c.Behavior == ZeusAbilities.LanceCharge);
    }

    [Fact]
    public void BattleLitanyReady_FiresLanceChargeAndLitanyTogether()
    {
        // RSR releases the hold once Battle Litany is off cooldown (!IsCoolingDown).
        var (context, scheduler) = Setup(litanyCooldown: 0f);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        var queue = scheduler.InspectOgcdQueue();
        Assert.Contains(queue, c => c.Behavior == ZeusAbilities.LanceCharge);
        Assert.Contains(queue, c => c.Behavior == ZeusAbilities.BattleLitany);
    }

    [Fact]
    public void BelowBattleLitanyLevel_FiresLanceCharge()
    {
        // Lv40: Lance Charge (30) is learned, Battle Litany (52) isn't. Whatever the cooldown
        // read for an unlearned action says, it must not starve Lance Charge.
        var (context, scheduler) = Setup(litanyCooldown: 5f, level: 40);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.Contains(scheduler.InspectOgcdQueue(), c => c.Behavior == ZeusAbilities.LanceCharge);
    }

    [Fact]
    public void BattleLitanyQuestNotDone_FiresLanceCharge()
    {
        // Level met but the job quest isn't: ActionAvailability reports it unlearned.
        var (context, scheduler) = Setup(litanyCooldown: 5f, litanyLearned: false);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.Contains(scheduler.InspectOgcdQueue(), c => c.Behavior == ZeusAbilities.LanceCharge);
    }

    private static (Daedalus.Rotation.ZeusCore.Context.IZeusContext context,
        Daedalus.Rotation.Common.Scheduling.RotationScheduler scheduler) Setup(
        float litanyCooldown,
        byte level = 100,
        bool litanyLearned = true)
    {
        var litanyId = DRGActions.BattleLitany.ActionId;
        var actionService = MockBuilders.CreateMockActionService(
            isActionReady: id => id != litanyId || litanyCooldown <= 0f);
        actionService.Setup(x => x.GetCooldownRemaining(litanyId)).Returns(litanyCooldown);
        actionService.Setup(x => x.IsActionLearned(litanyId)).Returns(litanyLearned);

        var scheduler = SchedulerFactory.CreateForTest(actionService: actionService);
        var context = ZeusTestContext.Create(
            actionService: actionService,
            level: level,
            hasPowerSurge: true,
            powerSurgeRemaining: 30f);

        return (context, scheduler);
    }
}
