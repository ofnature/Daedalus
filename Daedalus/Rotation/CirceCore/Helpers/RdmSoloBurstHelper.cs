using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using System.Numerics;
using Daedalus.Data;
using Daedalus.Rotation.CirceCore.Context;
using Daedalus.Services;

namespace Daedalus.Rotation.CirceCore.Helpers;

/// <summary>
/// Solo/trust burst sequencing for Red Mage: Manafication → Embolden → melee combo.
/// Active when <see cref="IBurstWindowService.UseSoloBurstFallback"/> is true
/// (computed on read — no frame lag vs <see cref="IBurstWindowService.Update"/>).
/// </summary>
public static class RdmSoloBurstHelper
{
    public static bool IsSoloBurstMode(IBurstWindowService? burstWindowService) =>
        burstWindowService?.UseSoloBurstFallback == true;

    /// <summary>
    /// Target HP and pack size gate — avoids wasting raid buffs on dying packs.
    /// </summary>
    public static bool IsBurstPackViable(ICirceContext context, IBattleChara? target, IPlayerCharacter player)
    {
        if (target is not IBattleNpc battleNpc)
            return false;

        var cfg = context.Configuration.RedMage;
        if (battleNpc.MaxHp > 0)
        {
            var hpPercent = (float)battleNpc.CurrentHp / battleNpc.MaxHp;
            if (hpPercent < cfg.SoloBurstMinTargetHpPercent)
                return false;
        }

        var enemyCount = context.TargetingService.CountEnemiesInRangeOfTarget(5f, battleNpc, player);
        if (enemyCount >= cfg.SoloBurstMinEnemies)
            return true;

        // Lone target (boss / big single trash): burst is justified whenever the target will
        // outlive the burst window at the current kill rate. EstimateTimeToDeath returns
        // MaxValue when the target isn't dying (e.g. fight just started) — that passes.
        if (cfg.SoloBurstMinSingleTargetTtkSeconds <= 0f)
            return false;

        var ttk = context.DamageTrendService?.EstimateTimeToDeath(battleNpc.EntityId, battleNpc.CurrentHp)
                  ?? float.MaxValue;
        return ttk >= cfg.SoloBurstMinSingleTargetTtkSeconds;
    }

    /// <summary>Manafication participates in the solo chain: enabled and level-met.</summary>
    private static bool IsManaficationChainCandidate(ICirceContext context) =>
        context.Configuration.RedMage.EnableManafication
        && context.Player.Level >= RDMActions.Manafication.MinLevel;

    /// <summary>Embolden participates in the solo chain: enabled and level-met.</summary>
    private static bool IsEmboldenChainCandidate(ICirceContext context) =>
        context.Configuration.RedMage.EnableEmbolden
        && context.Player.Level >= RDMActions.Embolden.MinLevel;

    /// <summary>
    /// Manafication is ready now or comes off cooldown within the pair window. False when the
    /// action is disabled or under-leveled, so nothing in the chain ever waits on a Manafication
    /// that cannot fire.
    /// </summary>
    public static bool IsManaficationImminent(ICirceContext context, float windowSeconds)
    {
        if (!IsManaficationChainCandidate(context))
            return false;
        if (context.ManaficationReady)
            return true;

        var cd = context.ActionService.GetCooldownRemaining(RDMActions.Manafication.ActionId);
        return cd > 0f && cd <= windowSeconds;
    }

    /// <summary>
    /// Embolden is ready now or comes off cooldown within the pair window. False when the action
    /// is disabled or under-leveled.
    /// </summary>
    public static bool IsEmboldenImminent(ICirceContext context, float windowSeconds)
    {
        if (!IsEmboldenChainCandidate(context))
            return false;
        if (context.EmboldenReady)
            return true;

        var cd = context.ActionService.GetCooldownRemaining(RDMActions.Embolden.ActionId);
        return cd > 0f && cd <= windowSeconds;
    }

    /// <summary>
    /// Both major burst oGCDs ready, or one ready with the other coming off CD within the window.
    /// </summary>
    public static bool AreBurstCooldownsPaired(ICirceContext context, float windowSeconds) =>
        (context.EmboldenReady || context.ManaficationReady)
        && IsEmboldenImminent(context, windowSeconds)
        && IsManaficationImminent(context, windowSeconds);

    /// <summary>
    /// Filler oGCDs (Fleche / Contre Sixte / Prefulgence) must leave the weave slot free while
    /// Embolden is the pending chain follower: hardcast-heavy stretches have ~one weave slot per
    /// dualcast, and priority-1 fillers taking them delayed Embolden 15s behind Manafication
    /// (Mistwake log 2026-07-02). Prefulgence additionally WANTS to land inside Embolden.
    /// </summary>
    public static bool ShouldHoldFillerOgcdsForEmbolden(ICirceContext context, IBurstWindowService? burstWindowService)
    {
        if (!IsSoloBurstMode(burstWindowService))
            return false;

        return context.HasManafication
               && !context.HasEmbolden
               && context.EmboldenReady;
    }

    /// <summary>
    /// Chain order is Manafication → Embolden: hold Embolden only while an unfired Manafication
    /// is imminent. Once Manafication is active (or can't fire soon), Embolden goes out.
    /// </summary>
    public static bool ShouldHoldEmboldenForManafication(ICirceContext context, float windowSeconds)
    {
        if (context.HasManafication)
            return false;

        return IsManaficationImminent(context, windowSeconds);
    }

    /// <summary>
    /// Mana threshold to begin the solo burst pair (Manafication first).
    /// </summary>
    public static bool IsSoloBurstManaReadyForPairStart(ICirceContext context)
    {
        var cfg = context.Configuration.RedMage;
        if (context.LowerMana >= cfg.SoloBurstIdealMinMana)
            return true;

        return context.LowerMana >= cfg.MeleeComboMinMana
               && context.EmboldenReady
               && context.ManaficationReady;
    }

    /// <summary>
    /// Hold Riposte/Moulinet while the solo burst chain is still assembling. A buff that is
    /// active never causes a hold by itself — holding through a live Embolden/Manafication
    /// window pushes the combo OUT of the buffs. The combo waits only for buffs that are
    /// actually about to fire (imminent and not yet active).
    /// </summary>
    public static bool ShouldHoldMeleeForSoloBurstChain(ICirceContext context, IBurstWindowService? burstWindowService)
    {
        if (!IsSoloBurstMode(burstWindowService))
            return false;

        var window = context.Configuration.RedMage.SoloBurstPairCooldownSeconds;
        var manaficationPending = !context.HasManafication && IsManaficationImminent(context, window);
        var emboldenPending = !context.HasEmbolden && IsEmboldenImminent(context, window);

        // One buff already running: wait only for the other if it fires within the window.
        if (context.HasManafication || context.HasEmbolden)
            return manaficationPending || emboldenPending;

        // Nothing active: park the combo only when the full pair is queued to launch.
        return manaficationPending && emboldenPending;
    }

    /// <summary>
    /// True when player is outside the 3y melee entry range after accounting for both hitbox radii.
    /// </summary>
    public static bool IsOutsideMeleeEntryRange(IPlayerCharacter player, IBattleChara? target)
    {
        if (target == null)
            return false;

        var centerDistance = Vector3.Distance(player.Position, target.Position);
        var edgeDistance = centerDistance - player.HitboxRadius - target.HitboxRadius;
        return edgeDistance > 3f;
    }

    /// <summary>
    /// Whether solo burst should use Corps-a-corps first to enter melee range before Riposte.
    /// </summary>
    public static bool ShouldGapCloseForMeleeEntry(
        ICirceContext context,
        IBurstWindowService? burstWindowService,
        IBattleChara? target)
    {
        if (!IsSoloBurstMode(burstWindowService))
            return false;

        if (ShouldHoldMeleeForSoloBurstChain(context, burstWindowService))
            return false;

        // Manafication extends the enchanted melee GCDs to 25y — no dash needed to start.
        if (context.HasManafication)
            return false;

        if (!context.CanStartMeleeCombo || context.IsInMeleeCombo || context.IsInMoulinetCombo)
            return false;

        if (context.CorpsACorpsCharges <= 0)
            return false;

        var hpPercent = context.Player.MaxHp > 0
            ? (float)context.Player.CurrentHp / context.Player.MaxHp
            : 1f;
        if (hpPercent < context.Configuration.RedMage.MeleeDashMinHpPercent)
            return false;

        return IsOutsideMeleeEntryRange(context.Player, target);
    }
}
