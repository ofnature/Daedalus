using Daedalus.Data;
using Daedalus.Rotation.HermesCore.Context;
using Daedalus.Services.Positional;

namespace Daedalus.Rotation.HermesCore.Helpers;

internal static class HermesPositionalHelper
{
    public static bool CanPushPositionalFinisher(IHermesContext context, bool correctPositional)
    {
        if (correctPositional)
            return true;

        if (context.TargetHasPositionalImmunity || context.HasTrueNorth)
            return true;

        if (!PositionalRequirementHelper.ShouldApply(context.Debug.EngagedEnemies))
            return true;

        if (!context.Configuration.Ninja.EnforcePositionals)
            return true;

        return context.Configuration.Ninja.AllowPositionalLoss;
    }

    public static bool NeedsTrueNorthForUpcomingFinisher(IHermesContext context)
    {
        if (context.HasTrueNorth || context.TargetHasPositionalImmunity)
            return false;

        if (context.ComboStep != 2 || context.LastComboAction != NINActions.GustSlash.ActionId)
            return false;

        var finisher = HermesKazematoiRules.GetFinisherPositional(context.Kazematoi);
        return finisher switch
        {
            PositionalType.Rear => !context.IsAtRear,
            PositionalType.Flank => !context.IsAtFlank,
            _ => false,
        };
    }
}
