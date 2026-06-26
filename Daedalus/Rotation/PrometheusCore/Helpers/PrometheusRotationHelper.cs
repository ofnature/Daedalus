using Dalamud.Game.ClientState.Objects.Types;
using Daedalus;
using Daedalus.Data;
using Daedalus.Rotation.Common.Helpers;
using Daedalus.Rotation.PrometheusCore.Context;
using Daedalus.Services.Action;
using Daedalus.Services.Content;
using Daedalus.Timeline;

namespace Daedalus.Rotation.PrometheusCore.Helpers;

/// <summary>
/// RSR-parity rotation helpers for Machinist (MCH_Reborn reference).
/// </summary>
internal static class PrometheusRotationHelper
{
    internal const float ToolChargeWindowSeconds = 8f;
    internal const float ProcExpiryWindowSeconds = 3f;

    /// <summary>
    /// True when combo timer is in the 1–2 GCD rescue window after Split or Slug (RSR GeneralGCD lines 305–331).
    /// </summary>
    internal static bool NeedsComboRescue(IPrometheusContext context)
    {
        if (context.IsOverheated || context.ComboTimeRemaining <= 0f)
            return false;

        var gcd = context.ActionService.GcdDuration;
        if (gcd <= 0f)
            gcd = 2.5f;

        var min = gcd;
        var max = gcd * 2f;
        if (context.ComboTimeRemaining < min || context.ComboTimeRemaining > max)
            return false;

        var last = context.LastComboAction;
        return last == MCHActions.SplitShot.ActionId
               || last == MCHActions.HeatedSplitShot.ActionId
               || last == MCHActions.SlugShot.ActionId
               || last == MCHActions.HeatedSlugShot.ActionId;
    }

    internal static bool IsComboRescueStep3(IPrometheusContext context)
    {
        var last = context.LastComboAction;
        return last == MCHActions.SlugShot.ActionId || last == MCHActions.HeatedSlugShot.ActionId;
    }

    /// <summary>
    /// RSR defer FMF until major tools are spent or procs are expiring.
    /// </summary>
    internal static bool ShouldUseFullMetalFieldNow(IPrometheusContext context)
    {
        if (!context.HasFullMetalMachinist)
            return false;

        if (!context.ActionService.IsActionReady(MCHActions.FullMetalField.ActionId))
            return false;

        if (StatusExpiringSoon(context.Player, MCHActions.StatusIds.FullMetalMachinist, ProcExpiryWindowSeconds))
            return true;

        if (context.HasExcavatorReady
            && StatusExpiringSoon(context.Player, MCHActions.StatusIds.ExcavatorReady, ProcExpiryWindowSeconds))
            return false;

        return !AnyMajorToolAvailable(context)
               && !context.HasExcavatorReady
               && context.DrillCharges < 2
               && (!IsWildfireOnCooldown(context)
                   || context.ActionService.WasLastAction(MCHActions.Wildfire.ActionId));
    }

    internal static bool AnyMajorToolAvailable(IPrometheusContext context)
    {
        var level = context.Player.Level;
        var cfg = context.Configuration.Machinist;
        var actionService = context.ActionService;

        if (cfg.EnableAirAnchor)
        {
            var aa = MCHActions.GetAirAnchor((byte)level, actionService);
            if (level >= aa.MinLevel && context.Battery <= 80 && actionService.IsActionReady(aa.ActionId))
                return true;
        }

        if (cfg.EnableDrill && level >= MCHActions.Drill.MinLevel
            && context.DrillCharges > 0 && actionService.IsActionReady(MCHActions.Drill.ActionId))
            return true;

        if (cfg.EnableChainSaw && level >= MCHActions.ChainSaw.MinLevel
            && context.Battery <= 80 && actionService.IsActionReady(MCHActions.ChainSaw.ActionId))
            return true;

        if (cfg.EnableExcavator && context.HasExcavatorReady
            && level >= MCHActions.Excavator.MinLevel
            && actionService.IsActionReady(MCHActions.Excavator.ActionId))
            return true;

        return false;
    }

    /// <summary>
    /// RSR ToolChargeSoon — hold Hypercharge when a major tool returns within 8s (ST pulls).
    /// </summary>
    internal static bool ShouldHoldHyperchargeForTools(IPrometheusContext context, int enemyCount)
    {
        if (context.IsOverheated)
            return false;

        var cfg = context.Configuration.Machinist;
        var useAoE = cfg.EnableAoERotation && enemyCount >= cfg.AoEMinTargets;
        if (useAoE)
            return false;

        return WillAnyToolReturnWithin(context, ToolChargeWindowSeconds);
    }

    internal static bool WillAnyToolReturnWithin(IPrometheusContext context, float withinSeconds)
    {
        var level = context.Player.Level;
        var svc = context.ActionService;

        if (level >= MCHActions.AirAnchor.MinLevel)
        {
            var aa = MCHActions.GetAirAnchor((byte)level, svc);
            if (WillActionReturnWithin(svc, aa.ActionId, withinSeconds))
                return true;
        }

        if (level >= MCHActions.HotShot.MinLevel && level < MCHActions.AirAnchor.MinLevel)
        {
            if (WillActionReturnWithin(svc, MCHActions.HotShot.ActionId, withinSeconds))
                return true;
        }

        if (level >= MCHActions.Drill.MinLevel)
        {
            if (context.DrillCharges > 0)
                return true;
            if (WillActionReturnWithin(svc, MCHActions.Drill.ActionId, withinSeconds))
                return true;
        }

        if (level >= MCHActions.ChainSaw.MinLevel
            && WillActionReturnWithin(svc, MCHActions.ChainSaw.ActionId, withinSeconds))
            return true;

        return false;
    }

    internal static bool WillActionReturnWithin(IActionService actionService, uint actionId, float withinSeconds)
    {
        if (actionService.IsActionReady(actionId))
            return true;

        var remaining = actionService.GetCooldownRemaining(actionId);
        return remaining > 0f && remaining <= withinSeconds;
    }

    /// <summary>
    /// Wildfire recast within 15s — save Heat for burst (RSR IsPreBurst).
    /// </summary>
    internal static bool IsPreBurstWindow(IPrometheusContext context)
    {
        var level = context.Player.Level;
        if (level < MCHActions.Wildfire.MinLevel)
            return false;

        if (context.HasWildfire)
            return false;

        var wfRemaining = context.ActionService.GetCooldownRemaining(MCHActions.Wildfire.ActionId);
        return wfRemaining > 0f && wfRemaining <= 15f;
    }

    /// <summary>
    /// BMR-style heat dump before timeline phase transition (RSR BmrDumpBeforeDowntime).
    /// </summary>
    internal static bool ShouldDumpHeatBeforeDowntime(IPrometheusContext context)
    {
        if (!context.Configuration.Machinist.DumpHeatBeforeDowntime)
            return false;

        if (context.HasWildfire || context.IsOverheated)
            return false;

        if (context.Heat < 50 && !context.HasHypercharged)
            return false;

        if (IsPreBurstWindow(context))
            return false;

        return BurstHoldHelper.ShouldHoldForPhaseTransition(context.TimelineService, 15f);
    }

    /// <summary>
    /// Prefer Ricochet when recast elapsed is equal (RSR default).
    /// </summary>
    internal static bool PreferGaussRoundOverRicochet(IActionService actionService, uint gaussId, uint ricochetId)
    {
        var gaussElapsed = actionService.GetRecastTimeElapsed(gaussId);
        var ricochetElapsed = actionService.GetRecastTimeElapsed(ricochetId);
        return gaussElapsed > ricochetElapsed;
    }

    /// <summary>
    /// Kickstart Gauss/Ricochet recast timers when fully charged and idle (RSR AttackAbility lines 177–199).
    /// </summary>
    internal static bool ShouldKickstartCharge(IActionService actionService, uint actionId, int currentCharges)
    {
        if (currentCharges <= 0)
            return false;

        return actionService.GetCooldownRemaining(actionId) <= 0f
               && actionService.GetRecastTimeElapsed(actionId) <= 0f;
    }

    /// <summary>
    /// Hold Hypercharge while Full Metal Machinist proc is unspent — FMF must not be wasted inside Overheat.
    /// </summary>
    internal static bool ShouldHoldHyperchargeForFullMetalField(IPrometheusContext context)
    {
        if (!context.HasFullMetalMachinist || context.IsOverheated)
            return false;
        if (!context.Configuration.Machinist.EnableFullMetalField)
            return false;
        return context.ActionService.IsActionReady(MCHActions.FullMetalField.ActionId);
    }

    /// <summary>
    /// Trial/raid content uses RSR 14-step battery pairs; dungeons/trust use simple overcap rules.
    /// </summary>
    internal static bool UseRaidQueenStepPairs(IDutyContentService? dutyContentService, Configuration configuration)
    {
        if (dutyContentService == null)
            return false;

        if (configuration.EnableAutoDutyConfig)
        {
            return dutyContentService.EffectiveProfile is EffectiveDutyProfile.Trial
                or EffectiveDutyProfile.Raid
                or EffectiveDutyProfile.HighEndRaid;
        }

        return dutyContentService.CurrentDuty is DutyContentType.Trial or DutyContentType.Raid;
    }

    /// <summary>
    /// RSR opener: summon at 60 Battery immediately after Excavator in the first 15s of combat.
    /// </summary>
    internal static bool ShouldSummonQueenOpener(IPrometheusContext context)
    {
        if (context.Battery != 60)
            return false;
        if (context.CombatEventService.GetCombatDurationSeconds() >= 15f)
            return false;
        return context.ActionService.WasLastAction(MCHActions.Excavator.ActionId);
    }

    internal static bool ShouldOvercapSummonQueen(IPrometheusContext context, uint nextGcdId)
    {
        if (nextGcdId == 0)
            return false;

        if (IsComboFinisher(nextGcdId) && context.Battery > 90)
            return true;

        return IsBatteryTool(nextGcdId) && context.Battery > 80;
    }

    internal static bool IsComboFinisher(uint actionId) =>
        actionId == MCHActions.CleanShot.ActionId
        || actionId == MCHActions.HeatedCleanShot.ActionId;

    internal static bool IsBatteryTool(uint actionId) =>
        actionId == MCHActions.AirAnchor.ActionId
        || actionId == MCHActions.HotShot.ActionId
        || actionId == MCHActions.ChainSaw.ActionId
        || actionId == MCHActions.Excavator.ActionId;

    private static bool IsWildfireOnCooldown(IPrometheusContext context)
    {
        if (context.Player.Level < MCHActions.Wildfire.MinLevel)
            return false;
        return context.ActionService.GetCooldownRemaining(MCHActions.Wildfire.ActionId) > 0f;
    }

    private static bool StatusExpiringSoon(IBattleChara player, uint statusId, float withinSeconds)
    {
        if (!BaseStatusHelper.HasStatus(player, statusId, out var remaining))
            return false;
        return remaining > 0f && remaining <= withinSeconds;
    }
}
