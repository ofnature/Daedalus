using System.Numerics;
using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace Daedalus.Rotation.ApolloCore.Helpers;

/// <summary>
/// Helper methods for distance calculations in FFXIV.
/// Uses squared distance internally for performance (avoids sqrt).
/// </summary>
public static class DistanceHelper
{
    /// <summary>
    /// Checks if two positions are within the specified range.
    /// </summary>
    /// <param name="from">Source position.</param>
    /// <param name="to">Target position.</param>
    /// <param name="range">Maximum range in yalms.</param>
    /// <returns>True if within range, false otherwise.</returns>
    public static bool IsInRange(Vector3 from, Vector3 to, float range)
        => Vector3.DistanceSquared(from, to) <= range * range;

    /// <summary>
    /// Hit test for a target-circle AoE. The circle extends <paramref name="radius"/> from the
    /// anchor's CENTER and hits anything whose own hitbox ring intersects it — the anchor's
    /// hitbox does not enlarge the circle (game rule; RSR ActionTargetInfo parity). Including
    /// the anchor hitbox overcounts spread packs around large-hitbox mobs.
    /// </summary>
    /// <param name="anchorCenter">Position of the enemy the AoE is centered on.</param>
    /// <param name="candidate">Position of the enemy being tested.</param>
    /// <param name="radius">The action's effect radius in yalms.</param>
    /// <param name="candidateHitboxRadius">Hitbox radius of the enemy being tested.</param>
    public static bool IsWithinTargetCircleAoE(
        Vector3 anchorCenter, Vector3 candidate, float radius, float candidateHitboxRadius)
    {
        var effectiveRange = radius + candidateHitboxRadius;
        return Vector3.DistanceSquared(anchorCenter, candidate) <= effectiveRange * effectiveRange;
    }

    /// <summary>
    /// Checks if two game objects are within the specified range.
    /// </summary>
    /// <param name="from">Source object.</param>
    /// <param name="to">Target object.</param>
    /// <param name="range">Maximum range in yalms.</param>
    /// <returns>True if within range, false otherwise.</returns>
    public static bool IsInRange(IGameObject from, IGameObject to, float range)
        => IsInRange(from.Position, to.Position, range);

    /// <summary>
    /// Gets the squared distance between two positions.
    /// Useful when comparing distances without needing the actual value.
    /// </summary>
    public static float DistanceSquared(Vector3 from, Vector3 to)
        => Vector3.DistanceSquared(from, to);

    /// <summary>
    /// Checks whether an action can reach a target using the game's native range check.
    /// Uses ActionManager.GetActionInRangeOrLoS which accounts for both player and enemy hitbox radii,
    /// exactly as the game does — more accurate than manual distance math.
    /// Returns true when result is 0 (in range + LoS) or 565 (in range, wrong facing).
    /// </summary>
    public static unsafe bool IsActionInRange(uint actionId, IGameObject player, IGameObject target)
    {
        try
        {
            var playerStruct = (FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)player.Address;
            var targetStruct = (FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)target.Address;
            if (playerStruct == null || targetStruct == null) return false;
            var result = ActionManager.GetActionInRangeOrLoS(actionId, playerStruct, targetStruct);
            return result is 0 or 565;
        }
        catch
        {
            return false;
        }
    }
}
