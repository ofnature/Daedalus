using Moq;
using Olympus.Data;
using Olympus.Services.Action;
using Olympus.Tests.Mocks;
using Xunit;

namespace Olympus.Tests.Rotation.NikeCore;

/// <summary>
/// Regression guards for SAM proc readiness via GetAdjustedActionId slot replacement (RSR parity).
/// </summary>
public class NikeProcGateTests
{
    private static Mock<IActionService> WithAdjust(uint baseId, uint adjustedId)
    {
        var mock = MockBuilders.CreateMockActionService();
        mock.Setup(x => x.GetAdjustedActionId(baseId)).Returns(adjustedId);
        return mock;
    }

    [Fact]
    public void KaeshiNamikiriReady_True_WhenOgiNamikiriReplacedByKaeshiNamikiri()
    {
        var svc = WithAdjust(SAMActions.OgiNamikiri.ActionId, SAMActions.KaeshiNamikiri.ActionId);
        Assert.True(SAMActions.IsKaeshiNamikiriReady(svc.Object));
    }

    [Fact]
    public void TsubameGaeshiActionReady_True_WhenSlotReplacedByKaeshiSetsugekka()
    {
        var svc = WithAdjust(SAMActions.TsubameGaeshi.ActionId, SAMActions.KaeshiSetsugekka.ActionId);
        Assert.True(SAMActions.IsTsubameGaeshiActionReady(svc.Object));
        Assert.True(SAMActions.IsKaeshiSetsugekkaReady(svc.Object));
    }

    [Fact]
    public void GetTsubameKaeshiAction_ReturnsKaeshiGoken_WhenSlotIsGoken()
    {
        var svc = WithAdjust(SAMActions.TsubameGaeshi.ActionId, SAMActions.KaeshiGoken.ActionId);
        var action = SAMActions.GetTsubameKaeshiAction(svc.Object);
        Assert.NotNull(action);
        Assert.Equal(SAMActions.KaeshiGoken.ActionId, action!.ActionId);
    }

    [Fact]
    public void IaijutsuSlotProbes_MatchExpectedReplacements()
    {
        var svc = MockBuilders.CreateMockActionService();
        svc.Setup(x => x.GetAdjustedActionId(SAMActions.Iaijutsu.ActionId))
            .Returns(SAMActions.MidareSetsugekka.ActionId);
        Assert.True(SAMActions.IsMidareSetsugekkaReady(svc.Object));
        Assert.False(SAMActions.IsHiganbanaReady(svc.Object));
    }

    [Fact]
    public void SlotProbes_False_WhenProcInactive()
    {
        var svc = WithAdjust(SAMActions.OgiNamikiri.ActionId, SAMActions.OgiNamikiri.ActionId);
        Assert.False(SAMActions.IsKaeshiNamikiriReady(svc.Object));
        Assert.False(SAMActions.IsTsubameGaeshiActionReady(svc.Object));
    }
}
