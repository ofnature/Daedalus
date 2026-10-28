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
/// Tests for ScoredHealSelectionStrategy.
/// Verifies the strategy name and basic structure.
/// Full integration testing requires Dalamud runtime.
/// </summary>
public class ScoredHealSelectionStrategyTests
{
    [Fact]
    public void ScoredStrategy_StrategyName_ReturnsScored()
    {
        var strategy = new ScoredHealSelectionStrategy();

        Assert.Equal("Scored", strategy.StrategyName);
    }

    [Fact]
    public void ScoredStrategy_ImplementsInterface()
    {
        var strategy = new ScoredHealSelectionStrategy();

        Assert.IsAssignableFrom<IHealSelectionStrategy>(strategy);
    }

    // ── Cure I fallback must be level AND learned (Cure II is a Lv30 quest unlock and syncs out) ──
    // A raw level check skipped Cure I whenever Cure II's MinLevel was met, so with Cure II unusable and no
    // lilies the strategy returned "No valid candidates" and the target went unhealed.

    [Fact]
    public void SingleHeal_CureIINotLearned_SelectsCure()
    {
        var (action, _, _) = SelectAt(level: 34, notLearned: new[] { WHMActions.CureII.ActionId });

        Assert.NotNull(action);
        Assert.Equal(WHMActions.Cure.ActionId, action!.ActionId);
    }

    [Fact]
    public void SingleHeal_CureIINotLearned_AtMaxLevel_SelectsCure()
    {
        var (action, _, _) = SelectAt(level: 100, notLearned: new[] { WHMActions.CureII.ActionId });

        Assert.NotNull(action);
        Assert.Equal(WHMActions.Cure.ActionId, action!.ActionId);
    }

    [Fact]
    public void SingleHeal_CureIILearned_SelectsCureII_NeverCure()
    {
        var (action, _, _) = SelectAt(level: 34);

        Assert.NotNull(action);
        Assert.Equal(WHMActions.CureII.ActionId, action!.ActionId);
    }

    [Fact]
    public void SingleHeal_BelowLv30_SelectsCure()
    {
        var (action, _, _) = SelectAt(level: 25);

        Assert.NotNull(action);
        Assert.Equal(WHMActions.Cure.ActionId, action!.ActionId);
    }

    private static (Daedalus.Models.Action.ActionDefinition? action, int healAmount, string reason)
        SelectAt(byte level, uint[]? notLearned = null)
    {
        const uint maxHp = 200_000;
        const int missingHp = 150_000;  // big enough that neither Cure nor Cure II overheals
        var player = MockBuilders.CreateMockPlayerCharacter(level: level);
        var target = new Mock<IBattleChara>();
        target.Setup(x => x.MaxHp).Returns(maxHp);
        target.Setup(x => x.CurrentHp).Returns(maxHp - missingHp);

        var actionService = MockBuilders.CreateMockActionService();
        foreach (var id in notLearned ?? System.Array.Empty<uint>())
            actionService.Setup(x => x.IsActionLearned(id)).Returns(false);
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
            LilyCount = 0,                    // no Afflatus Solace candidate
            BloodLilyCount = 0,
            IsWeaveWindow = false,
            HasFreecure = false,
            HasRegen = true,                  // no Regen candidate
            RegenRemaining = 15f,
            IsInMpConservationMode = false,
            LilyStrategy = LilyGenerationStrategy.Balanced,
            CombatDuration = 60f,
            Config = new HealingConfig(),
        };

        return new ScoredHealSelectionStrategy().SelectBestSingleHeal(context, evaluator);
    }
}
