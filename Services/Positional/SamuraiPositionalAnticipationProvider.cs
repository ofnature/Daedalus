using Olympus.Data;
using Olympus.Services.Positional.Navigation;

namespace Olympus.Services.Positional;

/// <summary>
/// SAM positional anticipation mirrored from Avarice <c>IsSAMAnticipatedRear/Flank</c>.
/// Fires after Jinpu/Shifu (combo setup), Hakaze/Gyofu stage A/B (predicted step 2),
/// or during Meikyo Sen routing (~one GCD before finisher).
/// </summary>
public sealed class SamuraiPositionalAnticipationProvider : IPositionalAnticipationProvider
{
    /// <summary>Matches <see cref="NikeCore.Modules.DamageModule"/> Fugetsu/Fuka refresh threshold.</summary>
    private const float BuffRefreshThresholdSeconds = 10f;

    public PositionalAnticipation? GetAnticipatedPositional(in PositionalAnticipationContext context)
    {
        if (context.TargetHasPositionalImmunity || context.HasTrueNorth)
            return null;

        // Confirmed Jinpu/Shifu and Meikyo take precedence over Hakaze/Gyofu stage A/B.
        if (TryGetRear(in context, out var rear))
            return rear;

        if (TryGetFlank(in context, out var flank))
            return flank;

        return null;
    }

    private static bool TryGetRear(in PositionalAnticipationContext context, out PositionalAnticipation anticipation)
    {
        anticipation = default;
        if (context.PlayerLevel < SAMActions.Gekko.MinLevel)
            return false;

        if (context.LastComboAction == SAMActions.Jinpu.ActionId)
        {
            anticipation = new PositionalAnticipation(
                PositionalType.Rear,
                SAMActions.Gekko.ActionId,
                PositionalAnticipationReason.ComboSetup);
            return true;
        }

        if (TryGetEarlyRear(in context, out anticipation))
            return true;

        if (context.HasMeikyoShisui
            && !context.SuppressMeikyoAnticipation
            && !context.HasGetsuSen
            && context.HasKaSen)
        {
            anticipation = new PositionalAnticipation(
                PositionalType.Rear,
                SAMActions.Gekko.ActionId,
                PositionalAnticipationReason.MeikyoSen);
            return true;
        }

        return false;
    }

    private static bool TryGetFlank(in PositionalAnticipationContext context, out PositionalAnticipation anticipation)
    {
        anticipation = default;
        if (context.PlayerLevel < SAMActions.Kasha.MinLevel)
            return false;

        if (context.LastComboAction == SAMActions.Shifu.ActionId)
        {
            anticipation = new PositionalAnticipation(
                PositionalType.Flank,
                SAMActions.Kasha.ActionId,
                PositionalAnticipationReason.ComboSetup);
            return true;
        }

        if (TryGetEarlyFlank(in context, out anticipation))
            return true;

        // Meikyo flank: exactly missing Ka (HasGetsu, no Ka). Both or neither → no anticipation.
        if (context.HasMeikyoShisui
            && !context.SuppressMeikyoAnticipation
            && context.HasGetsuSen
            && !context.HasKaSen)
        {
            anticipation = new PositionalAnticipation(
                PositionalType.Flank,
                SAMActions.Kasha.ActionId,
                PositionalAnticipationReason.MeikyoSen);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Stage A: after Hakaze/Gyofu when Jinpu path is likely (Fugetsu missing or expiring soon).
    /// </summary>
    private static bool TryGetEarlyRear(in PositionalAnticipationContext context, out PositionalAnticipation anticipation)
    {
        anticipation = default;
        if (!IsComboStarter(context.LastComboAction))
            return false;

        if (context.PlayerLevel < SAMActions.Jinpu.MinLevel)
            return false;

        if (!NeedsFugetsuRefresh(in context))
            return false;

        anticipation = new PositionalAnticipation(
            PositionalType.Rear,
            SAMActions.Gekko.ActionId,
            PositionalAnticipationReason.ComboSetup);
        return true;
    }

    /// <summary>
    /// Stage B: after Hakaze/Gyofu when Shifu path is likely (Fuka missing or expiring, Fugetsu healthy).
    /// Skipped when stage A applies — mirrors DamageModule Jinpu-before-Shifu priority.
    /// </summary>
    private static bool TryGetEarlyFlank(in PositionalAnticipationContext context, out PositionalAnticipation anticipation)
    {
        anticipation = default;
        if (!IsComboStarter(context.LastComboAction))
            return false;

        if (context.PlayerLevel < SAMActions.Shifu.MinLevel)
            return false;

        if (NeedsFugetsuRefresh(in context))
            return false;

        if (!NeedsFukaRefresh(in context))
            return false;

        anticipation = new PositionalAnticipation(
            PositionalType.Flank,
            SAMActions.Kasha.ActionId,
            PositionalAnticipationReason.ComboSetup);
        return true;
    }

    private static bool IsComboStarter(uint lastComboAction)
        => lastComboAction == SAMActions.Hakaze.ActionId
           || lastComboAction == SAMActions.Gyofu.ActionId;

    private static bool NeedsFugetsuRefresh(in PositionalAnticipationContext context)
        => !context.HasFugetsu || context.FugetsuRemainingSeconds < BuffRefreshThresholdSeconds;

    private static bool NeedsFukaRefresh(in PositionalAnticipationContext context)
        => !context.HasFuka || context.FukaRemainingSeconds < BuffRefreshThresholdSeconds;
}
