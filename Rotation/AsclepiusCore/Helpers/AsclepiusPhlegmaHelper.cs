using Olympus.Data;
using Olympus.Rotation.AsclepiusCore.Abilities;
using Olympus.Rotation.AsclepiusCore.Context;
using Olympus.Rotation.Common.Scheduling;

namespace Olympus.Rotation.AsclepiusCore.Helpers;

/// <summary>
/// Phlegma charge pacing for Sage. RSR parity: spend at cap off-burst, one charge per party
/// burst window, hold spare charges for the next window (BeirutaSGE / SGE_Reborn usedUp semantics).
/// </summary>
internal static class AsclepiusPhlegmaHelper
{
    /// <summary>Seconds before recharge completes when a single remaining charge should be spent.</summary>
    private const float CapDumpRechargeThreshold = 5f;

    public static bool IsBurstWindowActive(IAsclepiusContext ctx)
    {
        var config = ctx.Configuration.PartyCoordination;
        if (!config.EnableHealerBurstAwareness || ctx.PartyCoordinationService is not { } coord)
            return false;

        return coord.GetBurstWindowState().IsActive;
    }

    public static bool ShouldPushPhlegma(
        IAsclepiusContext ctx,
        bool isMoving,
        uint phlegmaActionId,
        byte playerLevel,
        out string debugState)
    {
        var charges = ctx.ActionService.GetCurrentCharges(phlegmaActionId);
        if (charges < 1)
        {
            debugState = "No charges";
            return false;
        }

        var maxCharges = ctx.ActionService.GetMaxCharges(phlegmaActionId, playerLevel);
        if (maxCharges < 1)
            maxCharges = 2;

        var rechargeRemaining = ctx.ActionService.GetCooldownRemaining(phlegmaActionId);
        var nearCap = charges >= maxCharges
            || (charges == maxCharges - 1 && rechargeRemaining < CapDumpRechargeThreshold);

        // SGE_Reborn: moving may spend when about to overcap.
        if (isMoving && nearCap)
        {
            debugState = $"Moving cap dump ({charges}/{maxCharges})";
            return true;
        }

        if (nearCap && !IsBurstWindowActive(ctx))
        {
            debugState = $"Cap dump ({charges}/{maxCharges})";
            return true;
        }

        if (IsBurstWindowActive(ctx))
        {
            if (charges >= maxCharges)
            {
                debugState = $"Burst ({charges}/{maxCharges})";
                return true;
            }

            debugState = $"Holding spare ({charges}/{maxCharges}) for next burst";
            return false;
        }

        debugState = $"Holding for burst ({charges}/{maxCharges})";
        return false;
    }

    public static AbilityBehavior GetBehaviorForLevel(byte level) =>
        level >= SGEActions.PhlegmaIII.MinLevel ? AsclepiusAbilities.PhlegmaIII
        : level >= SGEActions.PhlegmaII.MinLevel ? AsclepiusAbilities.PhlegmaII
        : AsclepiusAbilities.Phlegma;
}
