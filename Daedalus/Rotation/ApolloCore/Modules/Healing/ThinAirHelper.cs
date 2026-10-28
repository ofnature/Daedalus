using Daedalus.Data;
using Daedalus.Rotation.ApolloCore.Context;
using Daedalus.Services.Action;

namespace Daedalus.Rotation.ApolloCore.Modules.Healing;

/// <summary>
/// Shared helper for Thin Air optimization in healing handlers.
/// </summary>
internal static class ThinAirHelper
{
    /// <summary>
    /// Checks if we should wait for Thin Air before casting an expensive spell.
    /// </summary>
    /// <param name="context">The Apollo context.</param>
    /// <returns>True if Thin Air is available and we should wait for it.</returns>
    public static bool ShouldWaitForThinAir(IApolloContext context)
    {
        var config = context.Configuration;
        var player = context.Player;

        // Thin Air is quest-locked (Lv58 job quest) and can be synced out; IsActionReady reads true for an
        // unlearned action, so a raw level check here made every Cure II / Medica wait for a buff that never comes.
        if (!config.Buffs.EnableThinAir
            || !ActionAvailability.MeetsLevelAndLearned(player.Level, context.ActionService, WHMActions.ThinAir))
            return false;

        if (context.HasThinAir)
            return false;

        if (!context.ActionService.IsActionReady(WHMActions.ThinAir.ActionId))
            return false;

        return true;
    }
}
