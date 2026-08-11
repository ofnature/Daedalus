using Moq;
using Daedalus.Data;
using Daedalus.Rotation.HermesCore.Abilities;
using Daedalus.Rotation.HermesCore.Context;
using Daedalus.Rotation.HermesCore.Helpers;
using Daedalus.Rotation.HermesCore.Modules;
using Daedalus.Services;
using Daedalus.Tests.Mocks;
using Daedalus.Tests.Rotation.Common.Scheduling;
using Daedalus.Tests.Rotation.HermesCore;
using Xunit;

namespace Daedalus.Tests.Rotation.HermesCore.Modules;

[Collection("HermesMudraStaticState")] // HermesNinjutsuMudraExecutor holds per-frame statics
public class BuffModuleTcjGateTests
{
    private readonly BuffModule _module = new();

    [Fact]
    public void CollectCandidates_OutsideBurstWindow_DoesNotPushTenChiJin()
    {
        var actionService = MockBuilders.CreateMockActionService();
        actionService.Setup(x => x.IsActionReady(It.IsAny<uint>())).Returns(true);

        var scheduler = SchedulerFactory.CreateForTest(actionService: actionService);
        var context = HermesTestContext.Create(
            actionService: actionService,
            inTrickAttack: false,
            inCombat: true);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.DoesNotContain(scheduler.InspectOgcdQueue(), c => c.Behavior == HermesAbilities.TenChiJin);
    }

    [Fact]
    public void CollectCandidates_InTrickAttack_PushesTenChiJin()
    {
        var actionService = MockBuilders.CreateMockActionService();
        actionService.Setup(x => x.IsActionReady(It.IsAny<uint>())).Returns(true);

        var scheduler = SchedulerFactory.CreateForTest(actionService: actionService);
        var context = HermesTestContext.Create(
            actionService: actionService,
            inTrickAttack: true,
            inCombat: true);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.Contains(scheduler.InspectOgcdQueue(), c => c.Behavior == HermesAbilities.TenChiJin);
    }
}
