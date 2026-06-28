using Daedalus.Config;
using Daedalus.Data;
using Daedalus.Models.Action;
using Daedalus.Rotation.AstraeaCore.Context;
using Daedalus.Rotation.Common.Helpers;
using Daedalus.Services;

namespace Daedalus.Rotation.AstraeaCore.Helpers;

/// <summary>
/// Card timing, burst alignment, and lockout rules for Astraea (RSR parity + dump mode).
/// </summary>
public static class AstraeaCardHelper
{
    private const float BalanceSpearDriftSeconds = 66f;
    private const float LordDriftSeconds = 45f;

    public enum CardCategory
    {
        Dps,
        TankSupport,
        HealingSupport,
    }

    public static CardCategory GetCategory(uint actionId) => actionId switch
    {
        var id when id == ASTActions.TheBalance.ActionId || id == ASTActions.TheSpear.ActionId => CardCategory.Dps,
        var id when id == ASTActions.TheBole.ActionId => CardCategory.TankSupport,
        _ => CardCategory.HealingSupport,
    };

    public static bool IsInBurstWindow(IAstraeaContext context, IBurstWindowService? burstWindowService)
    {
        if (context.HasDivination) return true;
        if (burstWindowService?.IsInBurstWindow == true) return true;

        var partyCoord = context.PartyCoordinationService;
        if (partyCoord?.IsInBurstWindow() == true) return true;

        if (context.Player.Level < ASTActions.Divination.MinLevel) return true;
        if (context.ActionService.IsActionReady(ASTActions.Divination.ActionId)) return true;

        var divRemaining = context.CardService.GetDivinationCooldownRemaining();
        return divRemaining <= 5f;
    }

    public static bool ShouldUseDivination(IAstraeaContext context, IBurstWindowService? burstWindowService, bool isMoving)
    {
        var config = context.Configuration.Astrologian;
        if (!config.EnableDivination) return false;
        if (context.Player.Level < ASTActions.Divination.MinLevel) return false;
        if (!context.ActionService.IsActionReady(ASTActions.Divination.ActionId)) return false;
        if (!context.InCombat) return false;
        if (isMoving) return false;

        if (!config.DivinationOnBurst)
            return true;

        if (context.HasDivination) return false;

        if (burstWindowService?.IsInBurstWindow == true) return true;
        if (context.PartyCoordinationService?.IsInBurstWindow() == true) return true;

        // Solo / Trust / AutoDuty: with no IPC burst coordination to align to, holding Divination for a
        // party-burst signal that never comes means it never fires (a major DPS loss). Fall back to using
        // it on cooldown via the shared solo-burst fallback (same mechanism RDM uses). When real IPC
        // coordination is present, UseSoloBurstFallback is false and Divination still holds for the window.
        if (burstWindowService is null || burstWindowService.UseSoloBurstFallback) return true;

        return false;
    }

    public static bool ShouldExpireBeforeDraw(IAstraeaContext context)
    {
        if (!context.HasCard && !context.CardService.HasMinorArcana) return false;
        var drawRemaining = context.CardService.GetDrawCooldownRemaining();
        return drawRemaining > 0f
               && drawRemaining <= context.Configuration.Astrologian.ExpireCardsBeforeDrawSeconds;
    }

    public static bool ShouldPlayDpsCard(IAstraeaContext context, IBurstWindowService? burstWindowService)
    {
        var config = context.Configuration.Astrologian;
        if (config.DumpCardsWhenIdle) return true;
        if (ShouldExpireBeforeDraw(context)) return true;
        if (IsInBurstWindow(context, burstWindowService)) return true;

        if (!config.CardsUnderDivinationOnly)
            return config.CardStrategy != CardPlayStrategy.SafetyFocused;

        return !WillDivinationRechargeWithin(context, BalanceSpearDriftSeconds);
    }

    public static bool ShouldPlayLord(IAstraeaContext context, IBurstWindowService? burstWindowService)
    {
        var config = context.Configuration.Astrologian;
        if (!config.EnableMinorArcana) return false;
        if (config.DumpCardsWhenIdle) return true;
        if (ShouldExpireBeforeDraw(context)) return true;
        if (IsInBurstWindow(context, burstWindowService)) return true;

        if (!config.CardsUnderDivinationOnly) return true;

        return !WillDivinationRechargeWithin(context, LordDriftSeconds)
               || context.CardService.GetDrawCooldownRemaining() <= config.ExpireCardsBeforeDrawSeconds;
    }

    public static bool ShouldPlaySupportCard(IAstraeaContext context, bool hasValidTarget)
    {
        if (ShouldExpireBeforeDraw(context)) return true;
        if (hasValidTarget) return true;
        return context.Configuration.Astrologian.DumpCardsWhenIdle;
    }

    public static bool ShouldPlayCard(
        IAstraeaContext context,
        ActionDefinition action,
        IBurstWindowService? burstWindowService,
        bool hasValidTarget)
    {
        return GetCategory(action.ActionId) switch
        {
            CardCategory.Dps => ShouldPlayDpsCard(context, burstWindowService),
            CardCategory.TankSupport or CardCategory.HealingSupport => ShouldPlaySupportCard(context, hasValidTarget),
            _ => false,
        };
    }

    public static bool HasHealingLockout(IAstraeaContext context)
    {
        if (!context.Configuration.Astrologian.EnableHealingLockout) return false;
        if (context.HasDivining) return true;
        if (context.HasMacrocosmos) return true;
        if (context.IsStarMature) return true;
        return false;
    }

    public static bool HasAstlock(IAstraeaContext context) =>
        AstraeaStatusHelper.HasCollectiveUnconscious(context.Player);

    public static bool ShouldUseLightspeedBurst(IAstraeaContext context, IBurstWindowService? burstWindowService)
    {
        var config = context.Configuration.Astrologian;
        if (!config.EnableLightspeed || !config.LightspeedDuringBurst) return false;
        if (context.HasLightspeed) return false;
        if (!context.InCombat) return false;

        if (IsInBurstWindow(context, burstWindowService)) return true;

        var divRemaining = context.CardService.GetDivinationCooldownRemaining();
        return divRemaining <= 0f || divRemaining >= 115f;
    }

    private static bool WillDivinationRechargeWithin(IAstraeaContext context, float seconds)
    {
        if (context.Player.Level < ASTActions.Divination.MinLevel) return false;
        var remaining = context.CardService.GetDivinationCooldownRemaining();
        return remaining <= seconds;
    }
}
