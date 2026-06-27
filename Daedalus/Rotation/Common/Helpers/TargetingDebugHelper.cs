using System;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Daedalus.Services.Positional.Navigation;
using Daedalus.Services.Targeting;

namespace Daedalus.Rotation.Common.Helpers;

/// <summary>
/// Shared formatting for combat target display in the debug window.
/// </summary>
public static class TargetingDebugHelper
{
    /// <summary>
    /// Builds the Overview tab <c>TargetInfo</c> string from the rotation context target
    /// and/or the player's selected enemy.
    /// </summary>
    public static string FormatTargetInfo(IBattleChara? currentTarget, ITargetingService targetingService)
        => currentTarget?.Name?.TextValue
           ?? targetingService.GetUserEnemyTarget()?.Name?.TextValue
           ?? "None";

    /// <summary>
    /// Resolves the best combat target for debug readouts: explicit rotation target,
    /// then the player's hard target, then the nearest valid enemy.
    /// </summary>
    public static IBattleChara? ResolveCombatTarget(
        IBattleChara? currentTarget,
        ITargetingService targetingService,
        IPlayerCharacter player)
        => currentTarget
           ?? targetingService.GetUserEnemyTarget()
           ?? targetingService.FindNearbyEnemy(25f, player);

    /// <summary>
    /// Horizontal edge-to-edge distance between two objects (ignores vertical offset).
    /// </summary>
    public static float GetHorizontalEdgeDistance(IGameObject from, IGameObject to)
    {
        var dx = from.Position.X - to.Position.X;
        var dz = from.Position.Z - to.Position.Z;
        return MathF.Max(0f, MathF.Sqrt(dx * dx + dz * dz) - from.HitboxRadius - to.HitboxRadius);
    }

    /// <summary>
    /// Formats edge distance for the Why Stuck tab, including melee reach context.
    /// </summary>
    public static string FormatTargetDistance(IPlayerCharacter? player, IBattleChara? target)
    {
        if (player == null || target == null)
            return "None";

        var edgeDistance = GetHorizontalEdgeDistance(player, target);
        var meleeRange = PositionalMovementConstants.MeleeActionRangeYalms;
        return edgeDistance <= meleeRange
            ? $"{edgeDistance:F1}y edge (in melee)"
            : $"{edgeDistance:F1}y edge (melee {meleeRange:F1}y)";
    }
}
