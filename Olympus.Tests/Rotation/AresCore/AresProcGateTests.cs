using Moq;
using Olympus.Data;
using Olympus.Services.Action;
using Olympus.Tests.Mocks;
using Xunit;

namespace Olympus.Tests.Rotation.AresCore;

/// <summary>
/// Regression guards for WAR proc readiness via GetAdjustedActionId slot replacement (RSR parity).
/// </summary>
public class AresProcGateTests
{
    private static Mock<IActionService> WithAdjust(uint baseId, uint adjustedId)
    {
        var mock = MockBuilders.CreateMockActionService();
        mock.Setup(x => x.GetAdjustedActionId(baseId)).Returns(adjustedId);
        return mock;
    }

    [Fact]
    public void InnerChaosReady_True_WhenFellCleaveReplacedByInnerChaos()
    {
        var svc = WithAdjust(WARActions.FellCleave.ActionId, WARActions.InnerChaos.ActionId);
        Assert.True(WARActions.IsInnerChaosReady(svc.Object));
    }

    [Fact]
    public void ChaoticCycloneReady_True_WhenDecimateReplacedByChaoticCyclone()
    {
        var svc = WithAdjust(WARActions.Decimate.ActionId, WARActions.ChaoticCyclone.ActionId);
        Assert.True(WARActions.IsChaoticCycloneReady(svc.Object));
    }

    [Fact]
    public void PrimalWrathReady_True_WhenInnerReleaseReplacedByPrimalWrath()
    {
        var svc = WithAdjust(WARActions.InnerRelease.ActionId, WARActions.PrimalWrath.ActionId);
        Assert.True(WARActions.IsPrimalWrathReady(svc.Object));
    }

    [Fact]
    public void PrimalRuinationReady_True_WhenPrimalRendReplacedByPrimalRuination()
    {
        var svc = WithAdjust(WARActions.PrimalRend.ActionId, WARActions.PrimalRuination.ActionId);
        Assert.True(WARActions.IsPrimalRuinationReady(svc.Object));
    }

    [Fact]
    public void SlotProbes_False_WhenProcInactive()
    {
        var svc = WithAdjust(WARActions.FellCleave.ActionId, WARActions.FellCleave.ActionId);
        Assert.False(WARActions.IsInnerChaosReady(svc.Object));
        Assert.False(WARActions.IsChaoticCycloneReady(svc.Object));
        Assert.False(WARActions.IsPrimalWrathReady(svc.Object));
        Assert.False(WARActions.IsPrimalRuinationReady(svc.Object));
    }
}
