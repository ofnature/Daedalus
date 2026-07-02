using Dalamud.Game.ClientState.Objects.SubKinds;
using Moq;
using Daedalus.Config;
using Daedalus.Config.DPS;
using Daedalus.Data;
using Daedalus.Rotation.CirceCore.Context;
using Daedalus.Rotation.CirceCore.Helpers;
using Daedalus.Services;
using Daedalus.Services.Action;
using Xunit;

namespace Daedalus.Tests.Rotation.CirceCore.Helpers;

public class RdmSoloBurstHelperTests
{
    [Fact]
    public void AreBurstCooldownsPaired_BothReady_ReturnsTrue()
    {
        var ctx = MockContext(emboldenReady: true, manaficationReady: true);
        Assert.True(RdmSoloBurstHelper.AreBurstCooldownsPaired(ctx.Object, 5f));
    }

    [Fact]
    public void AreBurstCooldownsPaired_EmboldenReady_ManaficationWithinWindow_ReturnsTrue()
    {
        var ctx = MockContext(emboldenReady: true, manaficationReady: false, manaficationCd: 3f);
        Assert.True(RdmSoloBurstHelper.AreBurstCooldownsPaired(ctx.Object, 5f));
    }

    [Fact]
    public void AreBurstCooldownsPaired_ManaficationDisabled_ReturnsFalse()
    {
        var ctx = MockContext(emboldenReady: true, manaficationReady: true, enableManafication: false);
        Assert.False(RdmSoloBurstHelper.AreBurstCooldownsPaired(ctx.Object, 5f));
    }

    [Fact]
    public void IsManaficationImminent_UnderLevel_ReturnsFalse()
    {
        // Lv59: Manafication (Lv60) can never fire — nothing may wait on it.
        var ctx = MockContext(manaficationReady: false, manaficationCd: 3f, level: 59);
        Assert.False(RdmSoloBurstHelper.IsManaficationImminent(ctx.Object, 5f));
    }

    // --- Melee hold: the combo must never be parked through a live buff window ---

    [Fact]
    public void ShouldHoldMeleeForSoloBurstChain_EmboldenActive_ManaficationFarOnCd_ReturnsFalse()
    {
        // Regression (Vanguard log 2026-07-02): combo was held through the whole Embolden
        // window and released only after it expired, landing fully unbuffed.
        var ctx = MockContext(hasEmbolden: true, manaficationReady: false, manaficationCd: 60f);
        Assert.False(RdmSoloBurstHelper.ShouldHoldMeleeForSoloBurstChain(ctx.Object, SoloBurst()));
    }

    [Fact]
    public void ShouldHoldMeleeForSoloBurstChain_EmboldenActive_ManaficationReadyUnfired_ReturnsTrue()
    {
        var ctx = MockContext(hasEmbolden: true, manaficationReady: true);
        Assert.True(RdmSoloBurstHelper.ShouldHoldMeleeForSoloBurstChain(ctx.Object, SoloBurst()));
    }

    [Fact]
    public void ShouldHoldMeleeForSoloBurstChain_ManaficationUp_EmboldenFarOnCd_ReturnsFalse()
    {
        var ctx = MockContext(hasManafication: true, emboldenReady: false, emboldenCd: 110f);
        Assert.False(RdmSoloBurstHelper.ShouldHoldMeleeForSoloBurstChain(ctx.Object, SoloBurst()));
    }

    [Fact]
    public void ShouldHoldMeleeForSoloBurstChain_ManaficationUp_EmboldenWithinWindow_ReturnsTrue()
    {
        var ctx = MockContext(hasManafication: true, emboldenReady: false, emboldenCd: 3f);
        Assert.True(RdmSoloBurstHelper.ShouldHoldMeleeForSoloBurstChain(ctx.Object, SoloBurst()));
    }

    [Fact]
    public void ShouldHoldMeleeForSoloBurstChain_BothBuffsActive_ReturnsFalse()
    {
        var ctx = MockContext(hasManafication: true, hasEmbolden: true);
        Assert.False(RdmSoloBurstHelper.ShouldHoldMeleeForSoloBurstChain(ctx.Object, SoloBurst()));
    }

    [Fact]
    public void ShouldHoldMeleeForSoloBurstChain_NeitherActive_OnlyEmboldenReady_ReturnsFalse()
    {
        var ctx = MockContext(emboldenReady: true, manaficationReady: false, manaficationCd: 60f);
        Assert.False(RdmSoloBurstHelper.ShouldHoldMeleeForSoloBurstChain(ctx.Object, SoloBurst()));
    }

    [Fact]
    public void ShouldHoldMeleeForSoloBurstChain_NeitherActive_PairQueued_ReturnsTrue()
    {
        var ctx = MockContext(emboldenReady: true, manaficationReady: true);
        Assert.True(RdmSoloBurstHelper.ShouldHoldMeleeForSoloBurstChain(ctx.Object, SoloBurst()));
    }

    [Fact]
    public void ShouldGapCloseForMeleeEntry_DuringManafication_ReturnsFalse()
    {
        // Manafication extends enchanted melee GCDs to 25y — never spend a dash to start.
        var ctx = MockContext(hasManafication: true);
        Assert.False(RdmSoloBurstHelper.ShouldGapCloseForMeleeEntry(ctx.Object, SoloBurst(), target: null));
    }

    // --- Filler oGCD hold: Fleche/Contre/Prefulgence leave the weave slot for Embolden ---

    [Fact]
    public void ShouldHoldFillerOgcdsForEmbolden_ChainMidFlight_ReturnsTrue()
    {
        var ctx = MockContext(hasManafication: true, emboldenReady: true);
        Assert.True(RdmSoloBurstHelper.ShouldHoldFillerOgcdsForEmbolden(ctx.Object, SoloBurst()));
    }

    [Fact]
    public void ShouldHoldFillerOgcdsForEmbolden_EmboldenActive_ReturnsFalse()
    {
        var ctx = MockContext(hasManafication: true, hasEmbolden: true);
        Assert.False(RdmSoloBurstHelper.ShouldHoldFillerOgcdsForEmbolden(ctx.Object, SoloBurst()));
    }

    [Fact]
    public void ShouldHoldFillerOgcdsForEmbolden_EmboldenOnCooldown_ReturnsFalse()
    {
        var ctx = MockContext(hasManafication: true, emboldenReady: false);
        Assert.False(RdmSoloBurstHelper.ShouldHoldFillerOgcdsForEmbolden(ctx.Object, SoloBurst()));
    }

    // --- Pack viability: lone targets (bosses / big single trash) burst via TTK ---
    // Regression (Mistwake log 2026-07-02): a 54s full-HP single-target pull got NO burst at
    // all because viability required ≥2 enemies near the target.

    [Fact]
    public void IsBurstPackViable_LoneHealthyTarget_ReturnsTrue()
    {
        var ctx = MockContext(enemiesNearTarget: 1, targetTtkSeconds: float.MaxValue);
        Assert.True(RdmSoloBurstHelper.IsBurstPackViable(ctx.Object, HealthyNpc(), ctx.Object.Player));
    }

    [Fact]
    public void IsBurstPackViable_LoneTargetDyingSoon_ReturnsFalse()
    {
        var ctx = MockContext(enemiesNearTarget: 1, targetTtkSeconds: 4f);
        Assert.False(RdmSoloBurstHelper.IsBurstPackViable(ctx.Object, HealthyNpc(), ctx.Object.Player));
    }

    [Fact]
    public void IsBurstPackViable_LoneTarget_TtkCheckDisabled_ReturnsFalse()
    {
        var ctx = MockContext(enemiesNearTarget: 1, targetTtkSeconds: float.MaxValue);
        ctx.Object.Configuration.RedMage.SoloBurstMinSingleTargetTtkSeconds = 0f;
        Assert.False(RdmSoloBurstHelper.IsBurstPackViable(ctx.Object, HealthyNpc(), ctx.Object.Player));
    }

    [Fact]
    public void IsBurstPackViable_Pack_IgnoresTtk()
    {
        var ctx = MockContext(enemiesNearTarget: 3, targetTtkSeconds: 4f);
        Assert.True(RdmSoloBurstHelper.IsBurstPackViable(ctx.Object, HealthyNpc(), ctx.Object.Player));
    }

    private static Dalamud.Game.ClientState.Objects.Types.IBattleNpc HealthyNpc()
    {
        var npc = new Mock<Dalamud.Game.ClientState.Objects.Types.IBattleNpc>();
        npc.Setup(n => n.CurrentHp).Returns(1000000u);
        npc.Setup(n => n.MaxHp).Returns(1000000u);
        npc.Setup(n => n.EntityId).Returns(42u);
        return npc.Object;
    }

    // --- Embolden hold: chain order is Manafication → Embolden ---

    [Fact]
    public void ShouldHoldEmboldenForManafication_ManaficationReadyUnfired_ReturnsTrue()
    {
        var ctx = MockContext(manaficationReady: true);
        Assert.True(RdmSoloBurstHelper.ShouldHoldEmboldenForManafication(ctx.Object, 5f));
    }

    [Fact]
    public void ShouldHoldEmboldenForManafication_ManaficationActive_ReturnsFalse()
    {
        var ctx = MockContext(hasManafication: true, manaficationReady: false);
        Assert.False(RdmSoloBurstHelper.ShouldHoldEmboldenForManafication(ctx.Object, 5f));
    }

    [Fact]
    public void ShouldHoldEmboldenForManafication_ManaficationFarOnCd_ReturnsFalse()
    {
        var ctx = MockContext(manaficationReady: false, manaficationCd: 60f);
        Assert.False(RdmSoloBurstHelper.ShouldHoldEmboldenForManafication(ctx.Object, 5f));
    }

    [Fact]
    public void ShouldHoldEmboldenForManafication_ManaficationDisabled_ReturnsFalse()
    {
        var ctx = MockContext(manaficationReady: true, enableManafication: false);
        Assert.False(RdmSoloBurstHelper.ShouldHoldEmboldenForManafication(ctx.Object, 5f));
    }

    [Fact]
    public void ShouldHoldEmboldenForManafication_UnderLevel_ReturnsFalse()
    {
        var ctx = MockContext(manaficationReady: false, manaficationCd: 0f, level: 59);
        Assert.False(RdmSoloBurstHelper.ShouldHoldEmboldenForManafication(ctx.Object, 5f));
    }

    private static IBurstWindowService SoloBurst()
    {
        var burst = new Mock<IBurstWindowService>();
        burst.Setup(b => b.UseSoloBurstFallback).Returns(true);
        return burst.Object;
    }

    private static Mock<ICirceContext> MockContext(
        bool emboldenReady = false,
        bool manaficationReady = false,
        bool hasManafication = false,
        bool hasEmbolden = false,
        float emboldenCd = 120f,
        float manaficationCd = 120f,
        bool enableManafication = true,
        byte level = 100,
        int enemiesNearTarget = 0,
        float targetTtkSeconds = float.MaxValue)
    {
        var config = new Configuration { RedMage = new RedMageConfig { EnableManafication = enableManafication } };
        var actions = new Mock<IActionService>();
        actions.Setup(a => a.GetCooldownRemaining(RDMActions.Embolden.ActionId)).Returns(emboldenCd);
        actions.Setup(a => a.GetCooldownRemaining(RDMActions.Manafication.ActionId)).Returns(manaficationCd);

        var player = new Mock<IPlayerCharacter>();
        player.Setup(p => p.Level).Returns(level);

        var targeting = new Mock<Daedalus.Services.Targeting.ITargetingService>();
        targeting.Setup(t => t.CountEnemiesInRangeOfTarget(
                It.IsAny<float>(), It.IsAny<Dalamud.Game.ClientState.Objects.Types.IBattleNpc>(),
                It.IsAny<IPlayerCharacter>()))
            .Returns(enemiesNearTarget);

        var trend = new Mock<Daedalus.Services.Prediction.IDamageTrendService>();
        trend.Setup(t => t.EstimateTimeToDeath(It.IsAny<uint>(), It.IsAny<uint>(), It.IsAny<float>()))
            .Returns(targetTtkSeconds);

        var ctx = new Mock<ICirceContext>();
        ctx.Setup(c => c.Configuration).Returns(config);
        ctx.Setup(c => c.ActionService).Returns(actions.Object);
        ctx.Setup(c => c.Player).Returns(player.Object);
        ctx.Setup(c => c.TargetingService).Returns(targeting.Object);
        ctx.Setup(c => c.DamageTrendService).Returns(trend.Object);
        ctx.Setup(c => c.EmboldenReady).Returns(emboldenReady);
        ctx.Setup(c => c.ManaficationReady).Returns(manaficationReady);
        ctx.Setup(c => c.HasManafication).Returns(hasManafication);
        ctx.Setup(c => c.HasEmbolden).Returns(hasEmbolden);
        ctx.Setup(c => c.LowerMana).Returns(80);
        return ctx;
    }
}
