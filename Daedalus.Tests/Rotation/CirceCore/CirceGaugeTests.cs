using Daedalus.Data;
using Daedalus.Rotation;
using Daedalus.Rotation.CirceCore.Context;
using Xunit;

namespace Daedalus.Tests.Rotation.CirceCore;

/// <summary>
/// Unit tests for Circe.ComputeMeleeComboStep and Circe.ComputeMoulinetStep.
/// </summary>
public class CirceGaugeTests
{
    private const float ActiveComboTimer = 5f;

    // ----- ComputeMeleeComboStep -----

    [Fact]
    public void ComputeMeleeComboStep_Returns_0_When_Idle()
    {
        var step = Circe.ComputeMeleeComboStep(0, 0, 0f);
        Assert.Equal(0, step);
    }

    [Fact]
    public void ComputeMeleeComboStep_Returns_0_When_ManaStacks_AtLeast_1_But_TimerExpired()
    {
        var step = Circe.ComputeMeleeComboStep(1, 0, 0f);
        Assert.Equal(0, step);
    }

    [Fact]
    public void ComputeMeleeComboStep_Returns_1_When_ManaStacks_AtLeast_1_And_TimerActive()
    {
        var step = Circe.ComputeMeleeComboStep(1, 0, ActiveComboTimer);
        Assert.Equal(1, step);
    }

    [Fact]
    public void ComputeMeleeComboStep_Returns_1_When_ComboAction_IsRiposte_And_TimerActive()
    {
        var step = Circe.ComputeMeleeComboStep(0, RDMActions.Riposte.ActionId, ActiveComboTimer);
        Assert.Equal(1, step);
    }

    [Fact]
    public void ComputeMeleeComboStep_Returns_0_When_ComboAction_IsZwerchhau_But_TimerExpired()
    {
        var step = Circe.ComputeMeleeComboStep(0, RDMActions.EnchantedZwerchhau.ActionId, 0f);
        Assert.Equal(0, step);
    }

    [Fact]
    public void ComputeMeleeComboStep_Returns_2_When_ManaStacks_AtLeast_2_And_TimerActive()
    {
        var step = Circe.ComputeMeleeComboStep(2, 0, ActiveComboTimer);
        Assert.Equal(2, step);
    }

    [Fact]
    public void ComputeMeleeComboStep_Returns_2_When_ComboAction_IsZwerchhau_And_TimerActive()
    {
        var step = Circe.ComputeMeleeComboStep(0, RDMActions.Zwerchhau.ActionId, ActiveComboTimer);
        Assert.Equal(2, step);
    }

    [Fact]
    public void ComputeMeleeComboStep_Returns_3_When_ManaStacks_AtLeast_3_And_TimerActive()
    {
        var step = Circe.ComputeMeleeComboStep(3, 0, ActiveComboTimer);
        Assert.Equal(3, step);
    }

    [Fact]
    public void ComputeMeleeComboStep_Returns_3_When_ComboAction_IsRedoublement_And_TimerActive()
    {
        var step = Circe.ComputeMeleeComboStep(0, RDMActions.Redoublement.ActionId, ActiveComboTimer);
        Assert.Equal(3, step);
    }

    [Fact]
    public void ComputeMeleeComboStep_Returns_4_When_Verflare_And_TimerActive()
    {
        var step = Circe.ComputeMeleeComboStep(0, RDMActions.Verflare.ActionId, ActiveComboTimer);
        Assert.Equal(4, step);
    }

    [Fact]
    public void ComputeMeleeComboStep_Returns_5_When_Scorch_And_TimerActive()
    {
        var step = Circe.ComputeMeleeComboStep(0, RDMActions.Scorch.ActionId, ActiveComboTimer);
        Assert.Equal(5, step);
    }

    [Fact]
    public void ComputeMeleeComboStep_Returns_0_When_TimerExpired_For_LateChain()
    {
        var step = Circe.ComputeMeleeComboStep(0, RDMActions.Scorch.ActionId, 0f);
        Assert.Equal(0, step);
    }

    [Fact]
    public void ComputeMeleeComboStep_Returns_0_When_ManaStacks_And_Stale_ComboAction_Without_Timer()
    {
        var step = Circe.ComputeMeleeComboStep(2, RDMActions.EnchantedRiposte.ActionId, 0f);
        Assert.Equal(0, step);
    }

    [Fact]
    public void ComputeMeleeComboStep_Uses_ManaStacks_When_TimerActive_And_ComboAction_Unrecognized()
    {
        var step = Circe.ComputeMeleeComboStep(2, 0, ActiveComboTimer);
        Assert.Equal(2, step);
    }

    [Fact]
    public void ComputeMeleeComboStep_Prefers_LateChain_Over_ManaStacks()
    {
        var step = Circe.ComputeMeleeComboStep(3, RDMActions.Verflare.ActionId, ActiveComboTimer);
        Assert.Equal(4, step);
    }

    // Regression (Vanguard log 2026-07-02): Enchanted Moulinet grants a Mana Stack while keeping
    // the combo timer alive, and its combo id is not an ST-chain action — the stacks fallback
    // mapped it to ST step 1, so an uncombo'd Zwerchhau fired mid-Moulinet and broke the chain.

    [Fact]
    public void ComputeMeleeComboStep_Returns_0_MidMoulinetChain_OneStack()
    {
        var step = Circe.ComputeMeleeComboStep(1, RDMActions.EnchantedMoulinet.ActionId, ActiveComboTimer);
        Assert.Equal(0, step);
    }

    [Fact]
    public void ComputeMeleeComboStep_Returns_0_MidMoulinetChain_TwoStacks()
    {
        var step = Circe.ComputeMeleeComboStep(2, RDMActions.EnchantedMoulinetDeux.ActionId, ActiveComboTimer);
        Assert.Equal(0, step);
    }

    [Fact]
    public void ComputeMeleeComboStep_Returns_3_AfterMoulinetTrois_ThreeStacks()
    {
        // Finisher (Verflare/Verholy) legitimately follows Moulinet Trois at 3 stacks.
        var step = Circe.ComputeMeleeComboStep(3, RDMActions.EnchantedMoulinetTrois.ActionId, ActiveComboTimer);
        Assert.Equal(3, step);
    }

    [Fact]
    public void ComputeMeleeComboStep_Returns_3_MoulinetComboAction_ThreeStacks()
    {
        // A Moulinet started at 2 pre-existing stacks reaches 3 after one hit: finisher next.
        var step = Circe.ComputeMeleeComboStep(3, RDMActions.EnchantedMoulinet.ActionId, ActiveComboTimer);
        Assert.Equal(3, step);
    }

    // ----- ComputeCanStartMeleeCombo -----
    // Regression (Mistwake log 2026-07-02): Manafication's Magicked Swordplay grants 3 free
    // enchanted melee GCDs, but combo entry was gated on real mana ≥ floor — with mana below
    // 50|50 the free combo never fired and the whole Manafication window ran out on hardcasts.

    [Fact]
    public void ComputeCanStartMeleeCombo_MagickedSwordplay_NoMana_ReturnsTrue()
    {
        Assert.True(CirceContext.ComputeCanStartMeleeCombo(
            inCombat: true, hasMeleeMana: false, hasMagickedSwordplay: true,
            combatElapsed: 10f, minCombatSeconds: 3f));
    }

    [Fact]
    public void ComputeCanStartMeleeCombo_NoManaNoSwordplay_ReturnsFalse()
    {
        Assert.False(CirceContext.ComputeCanStartMeleeCombo(
            inCombat: true, hasMeleeMana: false, hasMagickedSwordplay: false,
            combatElapsed: 10f, minCombatSeconds: 3f));
    }

    [Fact]
    public void ComputeCanStartMeleeCombo_ManaFloor_NoSwordplay_ReturnsTrue()
    {
        Assert.True(CirceContext.ComputeCanStartMeleeCombo(
            inCombat: true, hasMeleeMana: true, hasMagickedSwordplay: false,
            combatElapsed: 10f, minCombatSeconds: 3f));
    }

    [Fact]
    public void ComputeCanStartMeleeCombo_Swordplay_BeforeMinCombatTime_ReturnsFalse()
    {
        Assert.False(CirceContext.ComputeCanStartMeleeCombo(
            inCombat: true, hasMeleeMana: true, hasMagickedSwordplay: true,
            combatElapsed: 1f, minCombatSeconds: 3f));
    }

    // ----- ComputeMoulinetStep -----

    [Fact]
    public void ComputeMoulinetStep_Returns_0_When_NoReplacement()
    {
        var step = Circe.ComputeMoulinetStep(RDMActions.EnchantedMoulinet.ActionId);
        Assert.Equal(0, step);
    }

    [Fact]
    public void ComputeMoulinetStep_Returns_1_When_AdjustedToDeux()
    {
        var step = Circe.ComputeMoulinetStep(RDMActions.EnchantedMoulinetDeux.ActionId);
        Assert.Equal(1, step);
    }

    [Fact]
    public void ComputeMoulinetStep_Returns_2_When_AdjustedToTrois()
    {
        var step = Circe.ComputeMoulinetStep(RDMActions.EnchantedMoulinetTrois.ActionId);
        Assert.Equal(2, step);
    }
}
