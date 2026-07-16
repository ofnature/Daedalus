using Daedalus.Config.DPS;
using Daedalus.Data;
using Daedalus.Rotation.Common.Scheduling;
using Daedalus.Rotation.ProteusCore.Abilities;
using Daedalus.Rotation.ProteusCore.Context;

namespace Daedalus.Rotation.ProteusCore.Modules;

/// <summary>
/// Healer-role kit: White Wind (party emergency, heals = caster's current HP), Pom Cure (ST heal —
/// only worth casting under Mimicry:Healer, 100p without it), Exuviation (6y self AoE cleanse — the
/// BLU esuna), and Gobskin barrier upkeep. Tank role keeps White Wind self-sustain. All healer-kit
/// potencies are mimicry-gated by the game, so Pom Cure hard-requires the healer mimicry status.
/// </summary>
public sealed class HealingModule : IProteusModule
{
    public int Priority => 15;
    public string Name => "Healing";

    public bool TryExecute(IProteusContext context, bool isMoving) => false;
    public void UpdateDebugState(IProteusContext context) { }

    public void CollectCandidates(IProteusContext context, RotationScheduler scheduler, bool isMoving)
    {
        if (!context.InCombat) { context.Debug.HealingState = "Not in combat"; return; }
        if (context.HasDiamondback) return;
        if (context.HasWaningNocturne) return;
        if (isMoving) return; // every heal here is a hardcast

        var cfg = context.Configuration.BlueMage;
        var player = context.Player;
        var (_, lowest, injured) = context.PartyHealthMetrics;

        TryPushWhiteWind(context, scheduler, cfg, player, lowest, injured);

        if (context.Role != BluRole.Healer)
        {
            if (context.Debug.HealingState.Length == 0)
                context.Debug.HealingState = "Not healer role";
            return;
        }

        TryPushPomCure(context, scheduler, cfg, player);
        TryPushExuviation(context, scheduler, cfg, player);
        TryPushGobskin(context, scheduler, cfg, player, injured);
    }

    private void TryPushWhiteWind(
        IProteusContext context, RotationScheduler scheduler, BlueMageConfig cfg,
        Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter player,
        float lowest, int injured)
    {
        if (!cfg.EnableWhiteWind) return;
        if (!context.IsSpellUsable(BLUActions.WhiteWind.ActionId))
        {
            context.Debug.HealingState = "White Wind not slotted";
            return;
        }
        if (context.CurrentMp < BLUActions.WhiteWind.MpCost) return;

        var selfHpPercent = player.MaxHp > 0 ? (float)player.CurrentHp / player.MaxHp * 100f : 100f;
        // Heal amount == caster's current HP: below ~30% own HP the cast is wasted.
        if (selfHpPercent < 30f) { context.Debug.HealingState = "White Wind: own HP too low"; return; }

        var threshold = cfg.WhiteWindHpPercent / 100f;
        var healerCall = context.Role == BluRole.Healer && injured >= 2 && lowest <= threshold;
        var tankSelfCall = (context.Role == BluRole.Tank || context.Role == BluRole.Solo)
                           && selfHpPercent <= cfg.WhiteWindHpPercent;
        if (!healerCall && !tankSelfCall)
        {
            context.Debug.HealingState = $"Monitoring (lowest {lowest:P0}, {injured} injured)";
            return;
        }

        scheduler.PushGcd(ProteusAbilities.WhiteWind, player.GameObjectId, priority: 2,
            onDispatched: _ =>
            {
                context.Debug.PlannedAction = BLUActions.WhiteWind.Name;
                context.Debug.HealingState = healerCall
                    ? $"White Wind ({injured} injured)"
                    : $"White Wind (self {selfHpPercent:F0}%)";
            });
    }

    private void TryPushPomCure(
        IProteusContext context, RotationScheduler scheduler, BlueMageConfig cfg,
        Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter player)
    {
        if (!cfg.EnablePomCure) return;
        if (!context.IsSpellUsable(BLUActions.PomCure.ActionId)) return;
        // 100p without the healer mimicry — a wasted GCD. The mimicry is the potency gate.
        if (!context.HasHealerMimicry)
        {
            // Don't stomp White Wind's report — this hold is chronic until mimicry is grabbed.
            if (context.Debug.HealingState.Length == 0)
                context.Debug.HealingState = "Pom Cure: no healer mimicry";
            return;
        }
        if (context.CurrentMp < BLUActions.PomCure.MpCost) return;

        var lowest = context.PartyHelper.GetLowestHpMember(player);
        if (lowest == null) return;
        var hpPercent = context.PartyHelper.GetHpPercent(lowest) * 100f;
        if (hpPercent > cfg.PomCureHpPercent) return;
        if (System.Numerics.Vector3.Distance(player.Position, lowest.Position) > BLUActions.PomCure.Range)
            return;

        scheduler.PushGcd(ProteusAbilities.PomCure, lowest.GameObjectId, priority: 3,
            onDispatched: _ =>
            {
                context.Debug.PlannedAction = BLUActions.PomCure.Name;
                context.Debug.HealingState = $"Pom Cure ({hpPercent:F0}%)";
            });
    }

    private void TryPushExuviation(
        IProteusContext context, RotationScheduler scheduler, BlueMageConfig cfg,
        Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter player)
    {
        if (!cfg.EnableExuviation) return;
        if (!context.IsSpellUsable(BLUActions.Exuviation.ActionId)) return;
        if (context.CurrentMp < BLUActions.Exuviation.MpCost) return;

        // Self-centered 6y — only party members standing on us are cleansable.
        foreach (var member in context.PartyHelper.GetAllPartyMembers(player))
        {
            if (System.Numerics.Vector3.Distance(player.Position, member.Position) > BLUActions.Exuviation.Radius)
                continue;
            var (statusId, _, _) = context.DebuffDetectionService.FindHighestPriorityDebuff(member);
            if (statusId == 0) continue;

            scheduler.PushGcd(ProteusAbilities.Exuviation, player.GameObjectId, priority: 4,
                onDispatched: _ =>
                {
                    context.Debug.PlannedAction = BLUActions.Exuviation.Name;
                    context.Debug.HealingState = "Exuviation (cleanse)";
                });
            return;
        }
    }

    private void TryPushGobskin(
        IProteusContext context, RotationScheduler scheduler, BlueMageConfig cfg,
        Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter player,
        int injured)
    {
        if (!cfg.EnableGobskin) return;
        if (!context.IsSpellUsable(BLUActions.Gobskin.ActionId)) return;
        // Refresh when our barrier is consumed/expired. Cheap (200 MP) — keep it rolling while
        // anything is taking damage. Does NOT stack with SCH/SGE shields (multi-heal-comp is v3).
        if (context.HasGobskin) return;
        if (injured == 0) return;
        if (context.CurrentMp < BLUActions.Gobskin.MpCost) return;

        scheduler.PushGcd(ProteusAbilities.Gobskin, player.GameObjectId, priority: 5,
            onDispatched: _ =>
            {
                context.Debug.PlannedAction = BLUActions.Gobskin.Name;
                context.Debug.HealingState = "Gobskin (barrier)";
            });
    }
}
