using Moq;
using Olympus.Data;
using Olympus.Ipc;
using Olympus.Rotation.AsclepiusCore.Context;
using Olympus.Rotation.AsclepiusCore.Helpers;
using Olympus.Services;
using Olympus.Services.Action;
using Olympus.Services.Party;
using Olympus.Tests.Mocks;
using Xunit;

namespace Olympus.Tests.Rotation.AsclepiusCore.Helpers;

public class AsclepiusPhlegmaHelperTests
{
    [Fact]
    public void ShouldPushPhlegma_InBurstWithFullCharges_ReturnsTrue()
    {
        var (ctx, actionService) = CreateContext(burstActive: true);
        actionService.Setup(x => x.GetCurrentCharges(SGEActions.PhlegmaIII.ActionId)).Returns((ushort)2);
        actionService.Setup(x => x.GetMaxCharges(SGEActions.PhlegmaIII.ActionId, 100)).Returns((ushort)2);
        actionService.Setup(x => x.GetCooldownRemaining(SGEActions.PhlegmaIII.ActionId)).Returns(0f);

        var shouldPush = AsclepiusPhlegmaHelper.ShouldPushPhlegma(
            ctx, isMoving: false, SGEActions.PhlegmaIII.ActionId, 100, out var debugState);

        Assert.True(shouldPush);
        Assert.Contains("Burst", debugState);
    }

    [Fact]
    public void ShouldPushPhlegma_InBurstWithOneChargeRemaining_ReturnsFalse()
    {
        var (ctx, actionService) = CreateContext(burstActive: true);
        actionService.Setup(x => x.GetCurrentCharges(SGEActions.PhlegmaIII.ActionId)).Returns((ushort)1);
        actionService.Setup(x => x.GetMaxCharges(SGEActions.PhlegmaIII.ActionId, 100)).Returns((ushort)2);
        actionService.Setup(x => x.GetCooldownRemaining(SGEActions.PhlegmaIII.ActionId)).Returns(30f);

        var shouldPush = AsclepiusPhlegmaHelper.ShouldPushPhlegma(
            ctx, isMoving: false, SGEActions.PhlegmaIII.ActionId, 100, out var debugState);

        Assert.False(shouldPush);
        Assert.Contains("Holding spare", debugState);
    }

    [Fact]
    public void ShouldPushPhlegma_OffBurstAtCap_ReturnsTrue()
    {
        var (ctx, actionService) = CreateContext(burstActive: false);
        actionService.Setup(x => x.GetCurrentCharges(SGEActions.PhlegmaIII.ActionId)).Returns((ushort)2);
        actionService.Setup(x => x.GetMaxCharges(SGEActions.PhlegmaIII.ActionId, 100)).Returns((ushort)2);
        actionService.Setup(x => x.GetCooldownRemaining(SGEActions.PhlegmaIII.ActionId)).Returns(0f);

        var shouldPush = AsclepiusPhlegmaHelper.ShouldPushPhlegma(
            ctx, isMoving: false, SGEActions.PhlegmaIII.ActionId, 100, out var debugState);

        Assert.True(shouldPush);
        Assert.Contains("Cap dump", debugState);
    }

    [Fact]
    public void ShouldPushPhlegma_OffBurstPartialCharge_ReturnsFalse()
    {
        var (ctx, actionService) = CreateContext(burstActive: false);
        actionService.Setup(x => x.GetCurrentCharges(SGEActions.PhlegmaIII.ActionId)).Returns((ushort)1);
        actionService.Setup(x => x.GetMaxCharges(SGEActions.PhlegmaIII.ActionId, 100)).Returns((ushort)2);
        actionService.Setup(x => x.GetCooldownRemaining(SGEActions.PhlegmaIII.ActionId)).Returns(30f);

        var shouldPush = AsclepiusPhlegmaHelper.ShouldPushPhlegma(
            ctx, isMoving: false, SGEActions.PhlegmaIII.ActionId, 100, out var debugState);

        Assert.False(shouldPush);
        Assert.Contains("Holding for burst", debugState);
    }

    private static (IAsclepiusContext ctx, Mock<IActionService> actionService) CreateContext(bool burstActive)
    {
        var config = AsclepiusTestContext.CreateDefaultSageConfiguration();
        config.PartyCoordination.EnableHealerBurstAwareness = true;

        var actionService = MockBuilders.CreateMockActionService(canExecuteGcd: true);
        var partyCoord = new Mock<IPartyCoordinationService>();
        partyCoord.Setup(x => x.GetBurstWindowState()).Returns(new BurstWindowState
        {
            IsActive = burstActive,
            HasBurstInfo = true,
        });

        var ctx = new Mock<IAsclepiusContext>();
        ctx.Setup(x => x.Configuration).Returns(config);
        ctx.Setup(x => x.ActionService).Returns(actionService.Object);
        ctx.Setup(x => x.PartyCoordinationService).Returns(partyCoord.Object);

        return (ctx.Object, actionService);
    }
}
