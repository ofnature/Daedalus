using Daedalus.Data;
using Daedalus.Services.Action;
using Daedalus.Rotation.Common;

namespace Daedalus.Rotation.Common.RoleActionHelpers;

/// <summary>
/// Pure-bool eligibility gates for rotation-integrated role actions.
/// Trigger logic stays in the rotation; the gate only checks level + cooldown.
///
/// Toggle delegates live on the per-rotation <see cref="Daedalus.Models.Action.AbilityBehavior"/>
/// and are consulted by the scheduler at dispatch time. Callers may also gate
/// on a per-job toggle before invoking these helpers.
/// </summary>
public static class RoleActionGates
{
    /// <summary>
    /// RSR CustomRotation_Ability: automatic True North only goes in the last weave slot (GCD remaining at or
    /// below this), so a charge isn't spent while the player still has time to walk to the positional.
    /// 1.35s is RSR's 0.63s clip guard plus one oGCD animation lock.
    /// </summary>
    public const float TrueNorthLastWeaveRemain = 1.35f;

    /// <summary>
    /// True iff Swiftcast is leveled and ready to fire (cooldown matured).
    /// </summary>
    public static bool SwiftcastReady(IRotationContext ctx)
    {
        if (!ActionAvailability.MeetsLevelAndLearned(ctx.Player.Level, ctx.ActionService, RoleActions.Swiftcast)) return false;
        if (!ctx.ActionService.IsActionReady(RoleActions.Swiftcast.ActionId)) return false;
        return true;
    }

    /// <summary>
    /// True iff True North is leveled and at least one charge is available.
    /// True North is a 2-charge ability with a 45s recast per charge;
    /// <see cref="Daedalus.Services.Action.IActionService.IsActionReady"/> already
    /// accounts for charge availability.
    /// </summary>
    public static bool TrueNorthReady(IRotationContext ctx)
    {
        if (!ActionAvailability.MeetsLevelAndLearned(ctx.Player.Level, ctx.ActionService, RoleActions.TrueNorth)) return false;
        if (!ctx.ActionService.IsActionReady(RoleActions.TrueNorth.ActionId)) return false;
        return true;
    }
}
