using Moq;
using Daedalus.Data;
using Daedalus.Rotation.NyxCore.Abilities;
using Daedalus.Rotation.NyxCore.Modules;
using Daedalus.Tests.Mocks;
using Daedalus.Tests.Rotation.Common.Scheduling;
using Xunit;

namespace Daedalus.Tests.Rotation.NyxCore.Modules;

/// <summary>
/// Oblation has 2 charges, so the 1s repeat-oGCD block lets a second press through, and its status (2682)
/// can land after the next weave slot. Without a last-oGCD guard the second charge was burnt on a refresh
/// (RSR 7b8a2f52 "Fix double Oblation usage").
/// </summary>
public sealed class NyxOblationRepeatTests
{
    [Fact]
    public void CollectCandidates_LowHp_PushesOblation()
    {
        var scheduler = Collect(oblationWasLastOgcd: false);

        Assert.Contains(scheduler.InspectOgcdQueue(), c => c.Behavior == NyxAbilities.Oblation);
    }

    [Fact]
    public void CollectCandidates_OblationWasLastOgcd_DoesNotPushItAgain()
    {
        var scheduler = Collect(oblationWasLastOgcd: true);

        Assert.DoesNotContain(scheduler.InspectOgcdQueue(), c => c.Behavior == NyxAbilities.Oblation);
    }

    private static Daedalus.Rotation.Common.Scheduling.RotationScheduler Collect(bool oblationWasLastOgcd)
    {
        var actionService = MockBuilders.CreateMockActionService();
        actionService.Setup(x => x.IsActionReady(It.IsAny<uint>())).Returns(true);
        actionService.Setup(x => x.WasLastOgcd(DRKActions.Oblation.ActionId)).Returns(oblationWasLastOgcd);

        // 40% HP is under the default 80% mitigation threshold; the null status list means no Oblation buff yet.
        var context = NyxTestContext.Create(actionService: actionService, currentHp: 20000, maxHp: 50000,
            level: 100, inCombat: true);
        var scheduler = SchedulerFactory.CreateForTest(actionService: actionService);

        new MitigationModule().CollectCandidates(context, scheduler, isMoving: false);
        return scheduler;
    }
}
