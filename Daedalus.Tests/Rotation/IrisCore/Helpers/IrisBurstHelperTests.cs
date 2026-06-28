using Moq;
using Daedalus.Data;
using Daedalus.Rotation.Common.Helpers;
using Daedalus.Rotation.IrisCore.Helpers;
using Daedalus.Services;
using Daedalus.Services.Action;
using Daedalus.Tests.Mocks;
using Daedalus.Tests.Rotation.IrisCore;
using Xunit;

namespace Daedalus.Tests.Rotation.IrisCore.Helpers;

public class IrisBurstHelperTests
{
    [Fact]
    public void ShouldHoldMogPortrait_True_WhenBurstImminentAndPoolingEnabled()
    {
        var burst = new Mock<IBurstWindowService>();
        burst.Setup(x => x.IsBurstImminent(8f)).Returns(true);
        burst.Setup(x => x.IsInBurstWindow).Returns(false);

        var ctx = IrisTestContext.Create(mogReady: true, hasStarryMuse: false);
        Assert.True(IrisBurstHelper.ShouldHoldMogPortrait(ctx, burst.Object));
    }

    [Fact]
    public void ShouldHoldMogPortrait_False_DuringStarryMuseBurst()
    {
        var ctx = IrisTestContext.Create(mogReady: true, hasStarryMuse: true);
        Assert.False(IrisBurstHelper.ShouldHoldMogPortrait(ctx, Mock.Of<IBurstWindowService>()));
    }

    // A burst service with real coordination active (not the solo fallback) — the only state in which
    // Striking/Hammer are pooled to align with Starry Muse.
    private static Mock<IBurstWindowService> CoordinatedBurst()
    {
        var burst = new Mock<IBurstWindowService>();
        burst.Setup(x => x.UseSoloBurstFallback).Returns(false);
        burst.Setup(x => x.IsInBurstWindow).Returns(false);
        return burst;
    }

    [Fact]
    public void ShouldHoldStrikingMuse_True_WhenStarryMuseWithinSixtySeconds_AndCoordinating()
    {
        var actionService = MockBuilders.CreateMockActionService();
        actionService.Setup(x => x.GetCooldownRemaining(PCTActions.ScenicMuse.ActionId)).Returns(45f);

        var ctx = IrisTestContext.Create(
            actionService: actionService,
            level: 100,
            hasWeaponCanvas: true,
            strikingMuseReady: true);

        Assert.True(IrisBurstHelper.ShouldHoldStrikingMuse(ctx, actionService.Object, CoordinatedBurst().Object));
    }

    [Fact]
    public void ShouldHoldStrikingMuse_False_InSolo_FiresOnCooldown()
    {
        // Solo / AutoDuty (no coordination): don't pool Striking for a Starry window short pulls won't reach.
        var actionService = MockBuilders.CreateMockActionService();
        actionService.Setup(x => x.GetCooldownRemaining(PCTActions.ScenicMuse.ActionId)).Returns(45f);

        var ctx = IrisTestContext.Create(
            actionService: actionService, level: 100, hasWeaponCanvas: true, strikingMuseReady: true);

        Assert.False(IrisBurstHelper.ShouldHoldStrikingMuse(ctx, actionService.Object, null));
    }

    [Fact]
    public void ShouldHoldHammerStart_True_WhenStarryMuseBetweenOneAndThirtySeconds_AndCoordinating()
    {
        var actionService = MockBuilders.CreateMockActionService();
        actionService.Setup(x => x.GetCooldownRemaining(PCTActions.ScenicMuse.ActionId)).Returns(20f);

        var ctx = IrisTestContext.Create(
            actionService: actionService,
            level: 100,
            hasHammerTime: true,
            hammerTimeStacks: 3);

        Assert.True(IrisBurstHelper.ShouldHoldHammerStart(ctx, actionService.Object, CoordinatedBurst().Object));
    }

    [Fact]
    public void ShouldHoldHammerStart_False_InSolo_FiresOnCooldown()
    {
        var actionService = MockBuilders.CreateMockActionService();
        actionService.Setup(x => x.GetCooldownRemaining(PCTActions.ScenicMuse.ActionId)).Returns(20f);

        var ctx = IrisTestContext.Create(
            actionService: actionService, level: 100, hasHammerTime: true, hammerTimeStacks: 3);

        Assert.False(IrisBurstHelper.ShouldHoldHammerStart(ctx, actionService.Object, null));
    }

    [Fact]
    public void ShouldHoldHammerStart_False_WhenStarryMuseOffCooldownButNotReady()
    {
        var actionService = MockBuilders.CreateMockActionService();
        actionService.Setup(x => x.GetCooldownRemaining(PCTActions.ScenicMuse.ActionId)).Returns(0f);

        var ctx = IrisTestContext.Create(
            actionService: actionService,
            level: 100,
            hasHammerTime: true,
            hammerTimeStacks: 3,
            starryMuseReady: false);

        Assert.False(IrisBurstHelper.ShouldHoldHammerStart(ctx, actionService.Object, null));
    }

    [Fact]
    public void IsLivingMuseInBurst_True_WhenStarryMuseActive()
    {
        var ctx = IrisTestContext.Create(hasStarryMuse: true);
        Assert.True(IrisBurstHelper.IsLivingMuseInBurst(ctx));
    }
}
