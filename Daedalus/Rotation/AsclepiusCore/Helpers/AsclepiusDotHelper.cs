using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Daedalus.Data;
using Daedalus.Models.Action;
using Daedalus.Rotation.AsclepiusCore.Context;
using Daedalus.Services.Targeting;

namespace Daedalus.Rotation.AsclepiusCore.Helpers;

/// <summary>
/// Eukrasian Dosis uptime checks for Sage. Scans all in-range enemies for the player's DoT
/// so pack fights do not re-apply when the enemy strategy flips to a different add.
/// </summary>
public static class AsclepiusDotHelper
{
    private static readonly uint[] EukrasianDosisStatusIds =
    [
        SGEActions.EukrasianDosisStatusId,
        SGEActions.EukrasianDosisIIStatusId,
        SGEActions.EukrasianDosisIIIStatusId,
    ];

    public static bool IsEnabled(IAsclepiusContext context) =>
        context.Configuration.EnableDoT && context.Configuration.Sage.EnableDot;

    public static float RefreshThreshold(IAsclepiusContext context) =>
        context.Configuration.Sage.DotRefreshThreshold;

    /// <summary>
    /// True when any in-range enemy still carries a healthy Eukrasian Dosis from this player
    /// (status scan, then recent-cast estimate when StatusList is stale).
    /// </summary>
    public static bool HasHealthyDotUptime(IAsclepiusContext context, float maxRange)
    {
        var threshold = RefreshThreshold(context);
        var player = context.Player;
        var sourceId = (uint)player.GameObjectId;

        var fromSource = context.TargetingService.GetBestStatusRemainingFromSourceOnAnyEnemy(
            EukrasianDosisStatusIds, sourceId, maxRange, player);
        if (fromSource >= threshold)
            return true;

        var anySource = context.TargetingService.GetBestStatusRemainingOnAnyEnemy(
            EukrasianDosisStatusIds, maxRange, player);
        if (anySource >= threshold)
            return true;

        return context.EukrasiaService.GetEstimatedDotRemainingSeconds() >= threshold;
    }

    /// <summary>
    /// Returns an enemy that needs Eukrasian Dosis, or null when uptime is already healthy.
    /// </summary>
    public static IBattleChara? FindEnemyNeedingEukrasianDosis(
        IAsclepiusContext context, ActionDefinition dotAction)
    {
        if (HasHealthyDotUptime(context, dotAction.Range))
            return null;

        var threshold = RefreshThreshold(context);
        var dotStatusId = SGEActions.GetDotStatusId(context.Player.Level);

        // No fallback to the plain enemy pick: once uptime is unhealthy, FindEnemyNeedingDot returns null only for
        // an enemy it skipped on purpose (about to die — the DoT time-to-kill gate), and Dosis beats the DoT there.
        return context.TargetingService.FindEnemyNeedingDot(
            dotStatusId, threshold, dotAction.Range, context.Player);
    }
}
