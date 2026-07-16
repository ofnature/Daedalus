using System.Numerics;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;

namespace Daedalus.Rotation.ProteusCore.Helpers;

/// <summary>
/// Facing check for SELF-anchored cone spells (Breath of Magic 10y, Bad Breath 8y). These are
/// dispatched on self, so the game NEVER auto-faces the enemy for them — a toon parked facing
/// the wrong way fires the cone into nothing forever (first BLU field run: 12 consecutive
/// Breath of Magic casts that all missed). Targeted spells don't care about facing; only the
/// cones need this gate.
/// </summary>
public static class ConeFacingHelper
{
    /// <summary>cos(60°) — conservative vs the real cone arcs (~90°+), so a pass here always hits.</summary>
    private const float MinFacingDot = 0.5f;

    public static bool IsFacing(IPlayerCharacter player, IGameObject target)
    {
        var dir = target.Position - player.Position;
        dir.Y = 0f;
        if (dir.LengthSquared() < 0.04f)
            return true; // standing on top of it — any facing hits

        // FFXIV facing: rotation 0 = +Z; forward = (sin, 0, cos). Same math as TargetingService.
        var forward = new Vector3(System.MathF.Sin(player.Rotation), 0f, System.MathF.Cos(player.Rotation));
        return Vector3.Dot(Vector3.Normalize(dir), forward) >= MinFacingDot;
    }
}
