using Daedalus.Config.DPS;
using Daedalus.Data;
using Daedalus.Models.Action;
using Daedalus.Rotation.ArtemisCore.Abilities;
using Daedalus.Rotation.ArtemisCore.Helpers;
using Xunit;

namespace Daedalus.Tests.Rotation.ArtemisCore;

/// <summary>
/// Five Beastmaster buttons the rotation never pressed: Rally, Rallying Cheer, Shield Charge, Borrow and the
/// action Beast Mode becomes. Action data is the game's own (Action sheet, read 2026-10-11).
/// </summary>
public class ArtemisCooldownRulesTests
{
    private const double Starved = ArtemisCooldownRules.StarvedSeconds;

    // ── Rally ────────────────────────────────────────────────────────────────────────────

    /// <summary>The axes spend the whole bar, so the refill goes in right after one has emptied it.</summary>
    [Fact]
    public void Rally_WhenTheAxesHaveRunDryAndDiamondsAreBanked()
        => Assert.True(ArtemisCooldownRules.ShouldRally(true, true, Starved, pairing: true, masteredEstimate: 2));

    /// <summary>With TP still in the bar there is nothing to refill; Rally would overcap it.</summary>
    [Fact]
    public void Rally_NotWhileAnAxeCanStillBeUsed()
        => Assert.False(ArtemisCooldownRules.ShouldRally(true, true, axeStarvedSeconds: 0, true, 3));

    /// <summary>An action is unusable for a moment in any animation lock. That is not an empty bar.</summary>
    [Fact]
    public void Rally_NotOnAMomentaryRefusal()
        => Assert.False(ArtemisCooldownRules.ShouldRally(true, true, axeStarvedSeconds: 0.3, true, 3));

    /// <summary>40 TP for a two-minute button: wait for the combos to bank something first.</summary>
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    public void Rally_WaitsForDiamondsWhileCombosAreBeingPaired(int mastered, bool expected)
        => Assert.Equal(expected, ArtemisCooldownRules.ShouldRally(true, true, Starved, pairing: true, mastered));

    /// <summary>No familiar, no Trick combos, no diamonds coming: the flat 40 TP beats an idle button.</summary>
    [Fact]
    public void Rally_WithoutPairingDoesNotWaitForDiamondsThatWillNeverCome()
        => Assert.True(ArtemisCooldownRules.ShouldRally(true, true, Starved, pairing: false, masteredEstimate: 0));

    [Fact]
    public void Rally_NotWhenOffOrNotReady()
    {
        Assert.False(ArtemisCooldownRules.ShouldRally(enabled: false, true, Starved, false, 3));
        Assert.False(ArtemisCooldownRules.ShouldRally(true, ready: false, Starved, false, 3));
    }

    // ── Rallying Cheer ───────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(true, true, true, 1.0, true)]
    [InlineData(true, true, true, 0.2, false)]  // Trick still usable, or only just refused
    [InlineData(true, true, false, 1.0, false)] // no familiar to cheer on
    [InlineData(true, false, true, 1.0, false)] // on cooldown
    [InlineData(false, true, true, 1.0, false)] // turned off
    public void RallyingCheer(bool enabled, bool ready, bool hasFamiliar, double trickStarved, bool expected)
        => Assert.Equal(expected,
            ArtemisCooldownRules.ShouldRallyingCheer(enabled, ready, hasFamiliar, trickStarved));

    // ── Shield Charge ────────────────────────────────────────────────────────────────────

    private static ShieldChargeUse Charge(int charges, float edge, bool dashSafe = true, bool gapClose = true)
        => ArtemisCooldownRules.DecideShieldCharge(true, gapClose, true, charges, edge, rangeYalms: 20f, dashSafe);

    /// <summary>
    /// Field 2026-10-25: the GCD gaps in a Board 1 clear were the beastmaster running after spread adds. A target 5-20y
    /// off is closed with the rush, even on the last charge.
    /// </summary>
    [Theory]
    [InlineData(1, 8f)]
    [InlineData(3, 15f)]
    [InlineData(1, 20f)]
    public void ShieldCharge_ClosesAGap(int charges, float edge)
        => Assert.Equal(ShieldChargeUse.GapClose, Charge(charges, edge));

    /// <summary>Unsafe dash (dodge in progress, bad landing, not your target, moving away): nothing.</summary>
    [Fact]
    public void ShieldCharge_NoGapCloseWhenTheDashIsUnsafe()
        => Assert.Equal(ShieldChargeUse.None, Charge(3, 10f, dashSafe: false));

    [Fact]
    public void ShieldCharge_NotBeyondItsRange() => Assert.Equal(ShieldChargeUse.None, Charge(3, 25f));

    /// <summary>In melee it is damage — but one charge always stays banked for the next gap.</summary>
    [Theory]
    [InlineData(3, ShieldChargeUse.Damage)]
    [InlineData(2, ShieldChargeUse.Damage)]
    [InlineData(1, ShieldChargeUse.None)]
    public void ShieldCharge_InMeleeBanksOneCharge(int charges, ShieldChargeUse expected)
        => Assert.Equal(expected, Charge(charges, 1.5f));

    /// <summary>Between a step and a run (3-5y) it is neither: the step is the movement code's.</summary>
    [Fact]
    public void ShieldCharge_NotForAStep() => Assert.Equal(ShieldChargeUse.None, Charge(3, 4f));

    [Fact]
    public void ShieldCharge_NotWhenOffOrOutOfCharges()
    {
        Assert.Equal(ShieldChargeUse.None, ArtemisCooldownRules.DecideShieldCharge(false, true, true, 3, 1f, 20f, true));
        Assert.Equal(ShieldChargeUse.None, ArtemisCooldownRules.DecideShieldCharge(true, true, false, 3, 10f, 20f, true));
        Assert.Equal(ShieldChargeUse.None, Charge(0, 10f));
        Assert.Equal(ShieldChargeUse.None, Charge(3, 10f, gapClose: false));
    }

    // ── Borrow ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Borrow_OnceTheReleaseIsSpent()
        => Assert.True(ArtemisCooldownRules.ShouldBorrow(true, true, hasFamiliar: true, releaseOwed: false));

    /// <summary>Both need One with Nature; the familiar's own move goes first.</summary>
    [Fact]
    public void Borrow_NeverAheadOfATemperedReleaseStillOwed()
        => Assert.False(ArtemisCooldownRules.ShouldBorrow(true, true, true, releaseOwed: true));

    [Fact]
    public void Borrow_NeedsAFamiliarToBorrowFrom()
        => Assert.False(ArtemisCooldownRules.ShouldBorrow(true, true, hasFamiliar: false, false));

    // ── Beast Mode ───────────────────────────────────────────────────────────────────────

    private static BeastModeUse Mode(uint id, float hp = 1f, float distance = 2f, bool debuff = false)
        => ArtemisCooldownRules.DecideBeastMode(true, id, hp, distance, debuff);

    /// <summary>Still plain Beast Mode: no Kinship, so the button does nothing.</summary>
    [Fact]
    public void BeastMode_NothingToPressWithoutAKinship()
        => Assert.Equal(BeastModeUse.None, Mode(BSTActions.BeastMode.ActionId));

    /// <summary>+5% critical hit rate is damage, so it does not wait to be hit.</summary>
    [Fact]
    public void Beastskin_OnCooldown() => Assert.Equal(BeastModeUse.Self, Mode(BSTActions.BeastskinId));

    [Theory]
    [InlineData(BSTActions.VileskinId)]
    [InlineData(BSTActions.ScaleskinId)]
    public void TheDefensiveSkinsWaitUntilWeAreHurt(uint id)
    {
        Assert.Equal(BeastModeUse.None, Mode(id, hp: 1f));
        Assert.Equal(BeastModeUse.Self, Mode(id, hp: 0.60f));
    }

    [Fact]
    public void Seedsower_NeedsEnemiesInItsSixYalms()
    {
        Assert.Equal(BeastModeUse.Self, Mode(BSTActions.SeedsowerId, distance: 5f));
        Assert.Equal(BeastModeUse.None, Mode(BSTActions.SeedsowerId, distance: 12f));
    }

    /// <summary>The one Kinship action aimed at the enemy.</summary>
    [Fact]
    public void SoulCrush_GoesAtTheEnemyInMelee()
    {
        Assert.Equal(BeastModeUse.Enemy, Mode(BSTActions.SoulCrushId, distance: 2f));
        Assert.Equal(BeastModeUse.None, Mode(BSTActions.SoulCrushId, distance: 10f));
    }

    /// <summary>It only pays (10% HP, 100 TP) when it removes something.</summary>
    [Fact]
    public void ScouringAsh_OnlyWithSomethingToCleanse()
    {
        Assert.Equal(BeastModeUse.None, Mode(BSTActions.ScouringAshId, debuff: false));
        Assert.Equal(BeastModeUse.Self, Mode(BSTActions.ScouringAshId, debuff: true));
    }

    /// <summary>A dash in a chosen direction, and a GCD spell that loses to the axe combo: the player's call.</summary>
    [Theory]
    [InlineData(BSTActions.CloudSkimId)]
    [InlineData(BSTActions.QuellingWaveId)]
    public void CloudSkimAndQuellingWaveAreNeverPressed(uint id)
        => Assert.Equal(BeastModeUse.None, Mode(id, hp: 0.10f, distance: 1f, debuff: true));

    [Fact]
    public void BeastMode_NotWhenTurnedOff()
        => Assert.Equal(BeastModeUse.None,
            ArtemisCooldownRules.DecideBeastMode(false, BSTActions.BeastskinId, 1f, 1f, false));

    // ── edges ────────────────────────────────────────────────────────────────────────────

    /// <summary>Exactly 5y off is already a run: the gap close applies from the threshold itself.</summary>
    [Fact]
    public void ShieldCharge_TheGapStartsAtExactlyFiveYalms()
        => Assert.Equal(ShieldChargeUse.GapClose, Charge(1, ArtemisCooldownRules.ShieldChargeGapYalms));

    /// <summary>Melee reach is inclusive: at exactly 3y it is damage, just past it nothing.</summary>
    [Fact]
    public void ShieldCharge_MeleeEndsAtThreeYalms()
    {
        Assert.Equal(ShieldChargeUse.Damage, Charge(2, ArtemisCooldownRules.MeleeYalms));
        Assert.Equal(ShieldChargeUse.None, Charge(2, ArtemisCooldownRules.MeleeYalms + 0.01f));
    }

    /// <summary>The gap-close switch governs only the gap close: in melee the damage use stands.</summary>
    [Fact]
    public void ShieldCharge_GapCloseOffStillHitsInMelee()
        => Assert.Equal(ShieldChargeUse.Damage, Charge(3, 1f, gapClose: false));

    /// <summary>A range shorter than the gap threshold never closes a gap.</summary>
    [Fact]
    public void ShieldCharge_NoGapCloseWhenItsRangeIsShorterThanTheGap()
        => Assert.Equal(ShieldChargeUse.None, ArtemisCooldownRules.DecideShieldCharge(true, true, true, 3, 6f, rangeYalms: 4f, dashSafe: true));

    [Fact]
    public void ShieldCharge_NegativeChargesAreNone()
        => Assert.Equal(ShieldChargeUse.None, Charge(-1, 1f));

    /// <summary>The skins wait for HP strictly under 80%: at exactly 80% nothing is hitting hard enough.</summary>
    [Theory]
    [InlineData(BSTActions.VileskinId)]
    [InlineData(BSTActions.ScaleskinId)]
    public void TheDefensiveSkinsStartJustUnderEightyPercent(uint id)
    {
        Assert.Equal(BeastModeUse.None, Mode(id, hp: ArtemisCooldownRules.SkinHpPct));
        Assert.Equal(BeastModeUse.Self, Mode(id, hp: ArtemisCooldownRules.SkinHpPct - 0.01f));
    }

    /// <summary>Seedsower's 6y and Soul Crush's 3y are both inclusive.</summary>
    [Fact]
    public void BeastMode_RangesAreInclusive()
    {
        Assert.Equal(BeastModeUse.Self, Mode(BSTActions.SeedsowerId, distance: ArtemisCooldownRules.SeedsowerYalms));
        Assert.Equal(BeastModeUse.None, Mode(BSTActions.SeedsowerId, distance: ArtemisCooldownRules.SeedsowerYalms + 0.01f));
        Assert.Equal(BeastModeUse.Enemy, Mode(BSTActions.SoulCrushId, distance: ArtemisCooldownRules.MeleeYalms));
        Assert.Equal(BeastModeUse.None, Mode(BSTActions.SoulCrushId, distance: ArtemisCooldownRules.MeleeYalms + 0.01f));
    }

    /// <summary>An id that is no known Kinship action (or nothing at all) presses nothing.</summary>
    [Theory]
    [InlineData(0u)]
    [InlineData(1u)]
    public void BeastMode_AnUnknownIdPressesNothing(uint id)
        => Assert.Equal(BeastModeUse.None, Mode(id, hp: 0.1f, distance: 1f, debuff: true));

    // ── the data ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheActionsAreTheGames()
    {
        Assert.Equal((44905u, 28), (BSTActions.Rally.ActionId, (int)BSTActions.Rally.MinLevel));
        Assert.Equal((44904u, 40), (BSTActions.RallyingCheer.ActionId, (int)BSTActions.RallyingCheer.MinLevel));
        Assert.Equal((44893u, 24), (BSTActions.ShieldCharge.ActionId, (int)BSTActions.ShieldCharge.MinLevel));
        Assert.Equal((44895u, 22), (BSTActions.Borrow.ActionId, (int)BSTActions.Borrow.MinLevel));
        Assert.Equal((44886u, 22), (BSTActions.BeastMode.ActionId, (int)BSTActions.BeastMode.MinLevel));
        Assert.Equal(ActionTargetType.SingleEnemy, BSTActions.ShieldCharge.TargetType);
        Assert.Equal(ActionTargetType.SingleEnemy, BSTActions.BeastModeAtEnemy.TargetType);
    }

    /// <summary>Beast Mode is pressed by its base id; the game substitutes the Kinship action.</summary>
    [Fact]
    public void BeastModeIsSentByItsBaseId()
    {
        Assert.Equal(BSTActions.BeastMode.ActionId, ArtemisAbilities.BeastMode.ReplacementBaseId);
        Assert.Equal(BSTActions.BeastMode.ActionId, ArtemisAbilities.BeastModeAtEnemy.ReplacementBaseId);
        Assert.Equal(BSTActions.BeastMode.ActionId, BSTActions.BeastModeAtEnemy.ActionId);
    }

    [Fact]
    public void AllOnByDefault()
    {
        var cfg = new BeastmasterConfig();
        Assert.True(cfg.EnableRally);
        Assert.True(cfg.EnableShieldCharge);
        Assert.True(cfg.EnableBorrow);
    }
}
