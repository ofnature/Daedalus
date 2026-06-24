using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Olympus.Data;
using Olympus.Models.Action;
using Olympus.Rotation.AsclepiusCore.Context;
using Olympus.Services.Targeting;

namespace Olympus.Rotation.AsclepiusCore.Helpers;

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

        var needing = context.TargetingService.FindEnemyNeedingDot(
            dotStatusId, threshold, dotAction.Range, context.Player);
        if (needing != null)
            return needing;

        return context.TargetingService.FindEnemy(
            context.Configuration.Targeting.EnemyStrategy, dotAction.Range, context.Player);
    }
}
