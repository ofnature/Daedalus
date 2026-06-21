using System;
using Dalamud.Game.ClientState.Objects.Types;
using Olympus.Config;
using Olympus.Data;
using Olympus.Models.Action;
using Olympus.Rotation.AstraeaCore.Abilities;
using Olympus.Rotation.AstraeaCore.Context;
using Olympus.Rotation.AstraeaCore.Helpers;
using Olympus.Rotation.Common.Scheduling;
using Olympus.Services;
using Olympus.Services.Training;

namespace Olympus.Rotation.AstraeaCore.Modules;

/// <summary>
/// Card draw/play, Divination, and Minor Arcana (scheduler-driven).
/// </summary>
public sealed class CardModule : IAstraeaModule
{
    private readonly IBurstWindowService? _burstWindowService;

    public CardModule(IBurstWindowService? burstWindowService = null)
    {
        _burstWindowService = burstWindowService;
    }

    public int Priority => 3;
    public string Name => "Card";

    public bool TryExecute(IAstraeaContext context, bool isMoving) => false;

    public void CollectCandidates(IAstraeaContext context, RotationScheduler scheduler, bool isMoving)
    {
        var config = context.Configuration.Astrologian;
        if (!config.EnableCards) return;

        TryPushDivination(context, scheduler, isMoving);
        TryPushAstrodyne(context, scheduler);
        TryPushPlayCards(context, scheduler);
        TryPushDraw(context, scheduler);
        TryPushMinorArcana(context, scheduler);
    }

    public void UpdateDebugState(IAstraeaContext context)
    {
        context.Debug.CurrentCardType = context.CurrentCard.ToString();
        context.Debug.MinorArcanaType = context.MinorArcana.ToString();
        context.Debug.SealCount = context.SealCount;
        context.Debug.UniqueSealCount = context.UniqueSealCount;

        var totalCards = context.TotalCardsInHand;
        var astralCount = context.BalanceCount;
        var umbralCount = context.SpearCount;
        var rawTypes = context.CardService.RawCardTypes;
        context.Debug.CardState = totalCards > 0
            ? $"{totalCards} cards ({astralCount} Astral/{umbralCount} Umbral) Raw: {rawTypes}"
            : "No cards";

        if (context.HasCard)
            context.Debug.DrawState = $"Cards: {astralCount} Astral, {umbralCount} Umbral";
        else if (context.CardService.CanAstralDraw || context.CardService.CanUmbralDraw)
            context.Debug.DrawState = "Ready to Draw";
        else if (context.ActionService.IsActionReady(ASTActions.AstralDraw.ActionId)
                 || context.ActionService.IsActionReady(ASTActions.UmbralDraw.ActionId))
            context.Debug.DrawState = "Draw on CD / wrong ActiveDraw";
        else
            context.Debug.DrawState = "On Cooldown";

        context.Debug.AstrodyneState = "N/A (Dawntrail)";

        if (context.HasDivining)
            context.Debug.DivinationState = "Oracle Ready";
        else if (context.HasDivination)
            context.Debug.DivinationState = "Active";
        else if (context.ActionService.IsActionReady(ASTActions.Divination.ActionId))
            context.Debug.DivinationState = "Ready";
        else
            context.Debug.DivinationState = "On Cooldown";
    }

    private void TryPushDivination(IAstraeaContext context, RotationScheduler scheduler, bool isMoving)
    {
        if (!AstraeaCardHelper.ShouldUseDivination(context, _burstWindowService, isMoving)) return;

        var player = context.Player;
        scheduler.PushOgcd(AstraeaAbilities.Divination, player.GameObjectId, priority: 0,
            onDispatched: _ =>
            {
                context.Debug.PlannedAction = ASTActions.Divination.Name;
                context.Debug.DivinationState = "Used";
                context.LogCardDecision("Divination", "Party", "Burst window");
                context.TrainingService?.RecordConceptApplication(AstConcepts.DivinationTiming, wasSuccessful: true, "Divination burst buff deployed");
            });
    }

    private void TryPushAstrodyne(IAstraeaContext context, RotationScheduler scheduler)
    {
        var config = context.Configuration.Astrologian;
        var player = context.Player;

        if (!config.EnableAstrodyne) return;
        if (player.Level < ASTActions.Astrodyne.MinLevel) return;
        if (!context.CanUseAstrodyne) return;
        if (context.UniqueSealCount < config.AstrodyneMinSeals) return;
        if (!context.ActionService.IsActionReady(ASTActions.Astrodyne.ActionId)) return;

        var capturedUniqueCount = context.UniqueSealCount;
        scheduler.PushOgcd(AstraeaAbilities.Astrodyne, player.GameObjectId, priority: 1,
            onDispatched: _ =>
            {
                context.Debug.PlannedAction = ASTActions.Astrodyne.Name;
                context.Debug.AstrodyneState = $"Used ({capturedUniqueCount} unique)";
                context.LogCardDecision("Astrodyne", "Self", $"{capturedUniqueCount} unique seals");
            });
    }

    private void TryPushPlayCards(IAstraeaContext context, RotationScheduler scheduler)
    {
        if (!context.HasCard)
        {
            context.Debug.PlayState = "No cards in hand";
            return;
        }

        var player = context.Player;
        var config = context.Configuration.Astrologian;

        TryPushCard(context, scheduler, AstraeaAbilities.TheBalance, ASTActions.TheBalance, context.HasTheBalance, priority: 2);
        TryPushCard(context, scheduler, AstraeaAbilities.TheBole, ASTActions.TheBole, context.HasTheBole, priority: 3);
        TryPushCard(context, scheduler, AstraeaAbilities.TheArrow, ASTActions.TheArrow, context.HasTheArrow, priority: 4);
        TryPushCard(context, scheduler, AstraeaAbilities.TheSpear, ASTActions.TheSpear, context.HasTheSpear, priority: 5);
        TryPushCard(context, scheduler, AstraeaAbilities.TheEwer, ASTActions.TheEwer, context.HasTheEwer, priority: 6);
        TryPushCard(context, scheduler, AstraeaAbilities.TheSpire, ASTActions.TheSpire, context.HasTheSpire, priority: 7);
    }

    private void TryPushCard(
        IAstraeaContext context,
        RotationScheduler scheduler,
        AbilityBehavior behavior,
        ActionDefinition action,
        bool hasCardInHand,
        int priority)
    {
        if (!hasCardInHand) return;
        if (context.Player.Level < action.MinLevel) return;

        var config = context.Configuration.Astrologian;
        var hasValidSupport = context.PartyHelper.HasValidSupportTarget(context.Player, action, config);
        if (!AstraeaCardHelper.ShouldPlayCard(context, action, _burstWindowService, hasValidSupport)) return;

        var target = context.PartyHelper.ResolveCardTarget(context.Player, action, config);
        if (target == null)
        {
            if (!config.DumpCardsWhenIdle) return;
            target = context.Player;
        }

        var capturedAction = action;
        var capturedTarget = target;

        scheduler.PushOgcd(behavior, target.GameObjectId, priority: priority,
            onDispatched: _ =>
            {
                context.Debug.PlannedAction = capturedAction.Name;
                var targetName = capturedTarget.Name?.TextValue ?? "Unknown";
                context.Debug.PlayState = $"{capturedAction.Name} -> {targetName}";
                context.LogCardDecision(capturedAction.Name, targetName, "Card played");
                context.TrainingService?.RecordConceptApplication(AstConcepts.CardManagement, wasSuccessful: true, "Card played");
            });
    }

    private void TryPushDraw(IAstraeaContext context, RotationScheduler scheduler)
    {
        var player = context.Player;
        if (player.Level < ASTActions.AstralDraw.MinLevel) return;
        if (!context.InCombat) return;
        if (context.HasCard && !AstraeaCardHelper.ShouldExpireBeforeDraw(context)
            && !context.Configuration.Astrologian.DumpCardsWhenIdle) return;

        if (context.CardService.CanAstralDraw
            && context.ActionService.IsActionReady(ASTActions.AstralDraw.ActionId))
        {
            scheduler.PushOgcd(AstraeaAbilities.AstralDraw, player.GameObjectId, priority: 8,
                onDispatched: _ =>
                {
                    context.Debug.PlannedAction = ASTActions.AstralDraw.Name;
                    context.Debug.DrawState = "Drawing (Astral)";
                    context.LogCardDecision("Astral Draw", "Self", "Draw astral cards");
                });
        }

        if (context.CardService.CanUmbralDraw
            && context.ActionService.IsActionReady(ASTActions.UmbralDraw.ActionId))
        {
            scheduler.PushOgcd(AstraeaAbilities.UmbralDraw, player.GameObjectId, priority: 9,
                onDispatched: _ =>
                {
                    context.Debug.PlannedAction = ASTActions.UmbralDraw.Name;
                    context.Debug.DrawState = "Drawing (Umbral)";
                    context.LogCardDecision("Umbral Draw", "Self", "Draw umbral cards");
                });
        }
    }

    private void TryPushMinorArcana(IAstraeaContext context, RotationScheduler scheduler)
    {
        var config = context.Configuration.Astrologian;
        var player = context.Player;

        if (!config.EnableMinorArcana) return;
        if (player.Level < ASTActions.MinorArcana.MinLevel) return;
        if (context.HasMinorArcana) return;

        bool shouldDraw = config.MinorArcanaStrategy switch
        {
            MinorArcanaUsageStrategy.OnCooldown => true,
            MinorArcanaUsageStrategy.SaveForBurst => AstraeaCardHelper.IsInBurstWindow(context, _burstWindowService),
            MinorArcanaUsageStrategy.EmergencyOnly => false,
            _ => false,
        };
        if (!shouldDraw) return;
        if (!context.ActionService.IsActionReady(ASTActions.MinorArcana.ActionId)) return;

        scheduler.PushOgcd(AstraeaAbilities.MinorArcana, player.GameObjectId, priority: 10,
            onDispatched: _ =>
            {
                context.Debug.PlannedAction = ASTActions.MinorArcana.Name;
                context.Debug.CardState = "Minor Arcana";
            });
    }
}
