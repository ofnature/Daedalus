using Dalamud.Game.ClientState.Objects.Types;
using Moq;
using Daedalus.Config;
using Daedalus.Data;
using Daedalus.Services.Action;
using Daedalus.Services.Healing;
using Daedalus.Services.Healing.Models;
using Daedalus.Services.Healing.Strategies;
using Daedalus.Tests.Mocks;
using Xunit;

namespace Daedalus.Tests.Services.Healing.Strategies;

/// <summary>
/// Tests for TieredHealSelectionStrategy — including the Cure I regression (2026-07-02):
/// WHM cast Cure I at Lv.92 on an ~85% tank, twice. Two causes: a "fish for Freecure" conservation
/// branch (Freecure was REMOVED from the game in 7.0) and a fallback that dropped to Cure I whenever
/// Cure II got overheal-rejected. Contract now: Cure I only below Lv.30; when Cure II would overheal,
/// select NOTHING (small deficits belong to lilies/oGCDs/Regen, the GCD goes to Glare).
/// </summary>
public class TieredHealSelectionStrategyTests
{
    [Fact]
    public void TieredStrategy_StrategyName_ReturnsTierBased()
    {
        var strategy = new TieredHealSelectionStrategy();

        Assert.Equal("Tier-Based", strategy.StrategyName);
    }

    [Fact]
    public void TieredStrategy_ImplementsInterface()
    {
        var strategy = new TieredHealSelectionStrategy();

        Assert.IsAssignableFrom<IHealSelectionStrategy>(strategy);
    }

    [Fact]
    public void SingleHeal_Lv92_BigDeficit_SelectsCureII_NeverCure()
    {
        var (action, _, reason) = SelectAt(level: 92, missingHp: 60_000);

        Assert.NotNull(action);
        Assert.Equal(WHMActions.CureII.ActionId, action!.ActionId);
        Assert.Contains("Cure II", reason);
    }

    [Fact]
    public void SingleHeal_Lv92_SmallDeficit_SelectsNothing_NotCureI()
    {
        // The regression: Cure II overheal-rejected on a small deficit must NOT fall back to Cure I.
        var (action, _, _) = SelectAt(level: 92, missingHp: 1_000);

        Assert.Null(action);
    }

    [Fact]
    public void SingleHeal_Lv92_MpConservation_StillNeverSelectsCureI()
    {
        // The old "fish for Freecure" conservation branch picked Cure I; Freecure no longer exists.
        var (action, _, _) = SelectAt(level: 92, missingHp: 60_000, mpConservation: true);

        Assert.NotNull(action);
        Assert.Equal(WHMActions.CureII.ActionId, action!.ActionId);
    }

    [Fact]
    public void SingleHeal_BelowLv30_SelectsCureI()
    {
        var (action, _, reason) = SelectAt(level: 25, missingHp: 100_000);

        Assert.NotNull(action);
        Assert.Equal(WHMActions.Cure.ActionId, action!.ActionId);
        Assert.Contains("Cure", reason);
    }

    // ── Low-HP override (field report 2026-07-23: Lv.34 CNJ never healed in dungeons) ──
    // Low-level HP pools are tiny relative to Cure II, so EVERY heal tripped the overheal veto —
    // and pre-50 kits have no lily/oGCD alternative the veto assumes. Below the forced threshold
    // an overhealing heal must fire anyway.

    [Fact]
    public void SingleHeal_Lv34_TankAt55Percent_OverhealAccepted()
    {
        // Small pool: 5,000 max HP, 2,250 missing (55%). Cure II estimate exceeds the deficit →
        // old behavior selected NOTHING; the override must force it.
        var (action, _, reason) = SelectAt(level: 34, missingHp: 2_250, maxHp: 5_000);

        Assert.NotNull(action);
        Assert.Equal(WHMActions.CureII.ActionId, action!.ActionId);
        Assert.Contains("forced", reason);
    }

    [Fact]
    public void SingleHeal_Lv34_TankAt90Percent_StillSelectsNothing()
    {
        // Above the forced threshold the endgame contract holds: topping off is not the GCD's job.
        var (action, _, _) = SelectAt(level: 34, missingHp: 500, maxHp: 5_000);

        Assert.Null(action);
    }

    [Fact]
    public void SingleHeal_Lv92_SmallDeficitHighHp_ForcedPathDoesNotRegress()
    {
        // The 2026-07-02 Cure I regression stays fixed: a 99.5% target selects nothing.
        var (action, _, _) = SelectAt(level: 92, missingHp: 1_000);

        Assert.Null(action);
    }

    // ── Level sync (field report 2026-07-23): Lv.34 CNJ synced into a low-level dungeon ──
    // Cure II passes a raw MinLevel check but is SYNCED OUT — the ActionManager reports it not
    // learned. The old branch locked onto Cure II anyway; the scheduler rejected it every frame
    // and the healer "just cast nukes" while the tank sat at 19%.

    [Fact]
    public void SingleHeal_Lv34_CureIISyncedOut_FallsBackToCureI()
    {
        var (action, _, reason) = SelectAt(
            level: 34, missingHp: 1_300, maxHp: 1_600,
            notLearned: new[] { WHMActions.CureII.ActionId });

        Assert.NotNull(action);
        Assert.Equal(WHMActions.Cure.ActionId, action!.ActionId);
        Assert.Contains("synced", reason);
    }

    [Fact]
    public void SingleHeal_Lv34_NothingSyncedOut_StillPrefersCureII()
    {
        var (action, _, _) = SelectAt(level: 34, missingHp: 1_300, maxHp: 1_600);

        Assert.NotNull(action);
        Assert.Equal(WHMActions.CureII.ActionId, action!.ActionId);
    }

    private static (Daedalus.Models.Action.ActionDefinition? action, int healAmount, string reason)
        SelectAt(byte level, int missingHp, bool mpConservation = false, uint maxHp = 200_000,
            uint[]? notLearned = null)
    {
        var player = MockBuilders.CreateMockPlayerCharacter(level: level);
        var target = new Mock<IBattleChara>();
        target.Setup(x => x.MaxHp).Returns(maxHp);
        target.Setup(x => x.CurrentHp).Returns((uint)(maxHp - missingHp));

        var actionService = MockBuilders.CreateMockActionService();
        actionService.Setup(x => x.IsActionReady(It.IsAny<uint>())).Returns(true);
        foreach (var syncedOutId in notLearned ?? System.Array.Empty<uint>())
            actionService.Setup(x => x.IsActionLearned(syncedOutId)).Returns(false);
        var enablement = new Mock<ISpellEnablementService>();
        enablement.Setup(x => x.IsSpellEnabled(It.IsAny<uint>())).Returns(true);
        var evaluator = new SpellCandidateEvaluator(actionService.Object, enablement.Object);

        var context = new HealSelectionContext
        {
            Player = player.Object,
            Target = target.Object,
            Mind = 3000,
            Det = 2000,
            Wd = 130,
            MissingHp = missingHp,
            HpPercent = (maxHp - (float)missingHp) / maxHp,
            LilyCount = 0,                    // Tier 1 skipped
            BloodLilyCount = 0,
            IsWeaveWindow = false,
            HasFreecure = false,
            HasRegen = true,                  // Tier 2 skipped
            RegenRemaining = 15f,
            IsInMpConservationMode = mpConservation,
            LilyStrategy = LilyGenerationStrategy.Balanced,
            CombatDuration = 60f,
            Config = new HealingConfig(),
        };

        var strategy = new TieredHealSelectionStrategy();
        return strategy.SelectBestSingleHeal(context, evaluator);
    }
}
