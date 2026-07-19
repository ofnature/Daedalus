using Daedalus.Data;
using Daedalus.Rotation.AsclepiusCore.Abilities;
using Daedalus.Rotation.AsclepiusCore.Context;
using Daedalus.Rotation.Common.Scheduling;

namespace Daedalus.Rotation.AsclepiusCore.Modules.Healing;

/// <summary>
/// Pops Swiftcast so the next emergency GCD heal (Diagnosis/Prognosis) lands instantly when a
/// party member is critically low. Without it a 1.5s Diagnosis cast can be too slow — or
/// impossible while moving — to save a spiking tank, which is how tanks die mid-pull.
/// </summary>
public sealed class SwiftcastEmergencyHandler : IHealingHandler
{
    private const int PushPriority = 4;

    public int Priority => 4;
    public string Name => "SwiftcastEmergency";

    public void CollectCandidates(IAsclepiusContext context, RotationScheduler scheduler, bool isMoving)
    {
        if (!context.Configuration.Healing.UseSwiftcastForEmergencyHeal) return;

        // Already instant — nothing to do.
        if (context.HasSwiftcast) { context.Debug.EmergencySwiftcastState = "Active"; return; }

        var player = context.Player;
        if (player.Level < RoleActions.Swiftcast.MinLevel) return;
        if (!context.ActionService.IsActionReady(RoleActions.Swiftcast.ActionId))
        {
            context.Debug.EmergencySwiftcastState = "On CD";
            return;
        }

        var critical = context.Configuration.Healing.GcdEmergencyThreshold;
        var target = context.Configuration.Healing.UseDamageIntakeTriage
            ? context.PartyHelper.FindMostEndangeredPartyMember(
                player, context.DamageIntakeService, 0, context.DamageTrendService, context.ShieldTrackingService)
            : context.PartyHelper.FindLowestHpPartyMember(player);
        if (target == null) { context.Debug.EmergencySwiftcastState = "No target"; return; }

        // Raise owns Swiftcast (alliance-raid field report 2026-07-19: a dead LAN bot never got
        // raised — under raid healing pressure this handler burned Swiftcast on emergency heals
        // the moment it came up, and with hardcast raise disabled the ResurrectionModule waited
        // for a Swiftcast that never survived to its turn). A corpse outranks a low-HP save.
        if (context.Configuration.Resurrection.EnableRaise
            && context.PartyHelper.FindDeadPartyMemberNeedingRaise(player) != null)
        {
            context.Debug.EmergencySwiftcastState = "Held (raise pending)";
            return;
        }

        var hpPercent = target.MaxHp > 0 ? (float)target.CurrentHp / target.MaxHp : 1f;
        if (hpPercent > critical)
        {
            context.Debug.EmergencySwiftcastState = $"{hpPercent:P0} > {critical:P0}";
            return;
        }

        scheduler.PushOgcd(AsclepiusAbilities.SwiftcastHeal, player.GameObjectId, priority: PushPriority,
            onDispatched: _ =>
            {
                context.Debug.PlannedAction = RoleActions.Swiftcast.Name;
                context.Debug.EmergencySwiftcastState = $"Emergency ({hpPercent:P0})";
            });
    }
}
