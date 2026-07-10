using Daedalus.Config.DPS;
using Daedalus.Rotation.Common.Scheduling;
using Daedalus.Rotation.ProteusCore.Abilities;
using Daedalus.Rotation.ProteusCore.Context;

namespace Daedalus.Rotation.ProteusCore.Modules;

/// <summary>
/// White Wind: healer-role party heal (2+ injured with the lowest at/below threshold) and tank-role
/// self-sustain. 15y SELF-centered — the party metrics approximate the radius for v1 (dungeon
/// parties stack). White Wind is MP-expensive and its heal scales with the caster's CURRENT HP, so
/// it is never cast while the BLU itself is nearly dead (heal would be tiny).
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
        if (!context.Configuration.BlueMage.EnableWhiteWind) return;
        if (!context.IsSpellUsable(Daedalus.Data.BLUActions.WhiteWind.ActionId))
        {
            context.Debug.HealingState = "White Wind not slotted";
            return;
        }
        if (isMoving) return; // 2.0s cast

        var cfg = context.Configuration.BlueMage;
        var player = context.Player;
        if (context.CurrentMp < Daedalus.Data.BLUActions.WhiteWind.MpCost) return;

        var selfHpPercent = player.MaxHp > 0 ? (float)player.CurrentHp / player.MaxHp * 100f : 100f;
        // Heal amount == caster's current HP: below ~30% own HP the cast is wasted.
        if (selfHpPercent < 30f) { context.Debug.HealingState = "White Wind: own HP too low"; return; }

        var (_, lowest, injured) = context.PartyHealthMetrics;
        var threshold = cfg.WhiteWindHpPercent / 100f;

        var healerCall = context.Role == BluRole.Healer && injured >= 2 && lowest <= threshold;
        var tankSelfCall = context.Role == BluRole.Tank && selfHpPercent <= cfg.WhiteWindHpPercent;
        if (!healerCall && !tankSelfCall)
        {
            context.Debug.HealingState = $"Monitoring (lowest {lowest:P0}, {injured} injured)";
            return;
        }

        scheduler.PushGcd(ProteusAbilities.WhiteWind, player.GameObjectId, priority: 2,
            onDispatched: _ =>
            {
                context.Debug.PlannedAction = Daedalus.Data.BLUActions.WhiteWind.Name;
                context.Debug.HealingState = healerCall
                    ? $"White Wind ({injured} injured)"
                    : $"White Wind (self {selfHpPercent:F0}%)";
            });
    }
}
