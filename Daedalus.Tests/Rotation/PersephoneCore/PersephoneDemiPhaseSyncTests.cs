using Moq;
using Daedalus.Data;
using Daedalus.Rotation.PersephoneCore.Modules;
using Daedalus.Services.Action;
using Daedalus.Tests.Mocks;
using Xunit;

namespace Daedalus.Tests.Rotation.PersephoneCore;

/// <summary>
/// SMN demi/primal desync field regressions (Worqor Zormor 2026-07-05):
/// (1) the demi-type latch captured Bahamut on the summon frame of a real Phoenix window and a
///     WRONG latch never self-corrected (only a missing one re-probed) — Enkindle Bahamut and
///     Deathflare were submitted while the game's GCDs read Fountain of Fire, losing the
///     Rekindle. The latch now live-re-probes every frame and a definitive Astral Flow result
///     overwrites it; a none-result (button reverted after the flow spend) keeps it.
/// (2) the demi entry (Aethercharge) fired in the pet-arrival gap right after a primal summon
///     GCD, when attunement stacks read 0 and no primals are ready — burying Phoenix inside the
///     Garuda window (Brand/Fountain interleaving Emerald Rites).
/// </summary>
public class PersephoneDemiPhaseSyncTests
{
    private static Mock<IActionService> WithAstralFlowAdjust(uint adjustedId)
    {
        var mock = MockBuilders.CreateMockActionService();
        mock.Setup(x => x.GetAdjustedActionId(SMNActions.AstralFlow.ActionId)).Returns(adjustedId);
        return mock;
    }

    [Fact]
    public void Latch_WrongPhase_CorrectedByLiveProbe()
    {
        // Latched Bahamut, but the button says Rekindle — the game is in Phoenix. The live
        // re-probe must overwrite the wrong latch (the exact 2026-07-05 failure).
        var svc = WithAstralFlowAdjust(SMNActions.Rekindle.ActionId);

        var result = Daedalus.Rotation.Persephone.UpdateDemiPhaseLatch(
            timerActive: true, svc.Object, current: (Bahamut: true, Phoenix: false, SolarBahamut: false));

        Assert.Equal((false, true, false), result);
    }

    [Fact]
    public void Latch_ProbeReadsNone_KeepsCurrentPhase()
    {
        // After Rekindle is spent the button reverts and the probe reads none — the phase must
        // stay latched for the rest of the window (Enkindle/GCD labels still need it).
        var svc = WithAstralFlowAdjust(SMNActions.AstralFlow.ActionId);

        var result = Daedalus.Rotation.Persephone.UpdateDemiPhaseLatch(
            timerActive: true, svc.Object, current: (Bahamut: false, Phoenix: true, SolarBahamut: false));

        Assert.Equal((false, true, false), result);
    }

    [Fact]
    public void Latch_TimerDown_ClearsAllPhases()
    {
        var svc = WithAstralFlowAdjust(SMNActions.Rekindle.ActionId);

        var result = Daedalus.Rotation.Persephone.UpdateDemiPhaseLatch(
            timerActive: false, svc.Object, current: (Bahamut: false, Phoenix: true, SolarBahamut: false));

        Assert.Equal((false, false, false), result);
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public void PetArrivalGap_True_AfterEachPrimalSummonGcd(bool ifrit, bool titan, bool garuda)
    {
        var actionService = MockBuilders.CreateMockActionService();
        actionService.Setup(x => x.WasLastGcd(SMNActions.SummonIfrit.ActionId)).Returns(ifrit);
        actionService.Setup(x => x.WasLastGcd(SMNActions.SummonTitan.ActionId)).Returns(titan);
        actionService.Setup(x => x.WasLastGcd(SMNActions.SummonGaruda.ActionId)).Returns(garuda);
        var context = PersephoneTestContext.Create(actionService: actionService);

        Assert.True(DamageModule.IsPrimalSummonPetArrivalGap(context));
    }

    [Fact]
    public void PetArrivalGap_False_WhenLastGcdWasNotAPrimalSummon()
    {
        var actionService = MockBuilders.CreateMockActionService();
        actionService.Setup(x => x.WasLastGcd(It.IsAny<uint>())).Returns(false);
        var context = PersephoneTestContext.Create(actionService: actionService);

        Assert.False(DamageModule.IsPrimalSummonPetArrivalGap(context));
    }
}
