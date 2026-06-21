using Olympus.Data;
using Olympus.Rotation.AstraeaCore.Abilities;
using Olympus.Rotation.AstraeaCore.Context;
using Olympus.Rotation.Common;
using Olympus.Rotation.Common.Modules;

namespace Olympus.Rotation.AstraeaCore.Modules;

/// <summary>
/// Pre-pull Astral Draw and Earthly Star when pull intent is detected.
/// </summary>
public sealed class AstraeaPrePullCandidate : IPrePullCandidate
{
    public bool TryDispatch(uint jobId, IRotationContext context)
    {
        if (jobId != JobRegistry.Astrologian) return false;
        if (context is not IAstraeaContext ast) return false;

        var config = context.Configuration.Astrologian;
        var player = ast.Player;
        var actions = ast.ActionService;

        if (config.PrePullEarthlyStar
            && !ast.IsStarPlaced
            && player.Level >= ASTActions.EarthlyStar.MinLevel
            && actions.IsActionReady(ASTActions.EarthlyStar.ActionId))
        {
            return actions.ExecuteGroundTargetedOgcd(ASTActions.EarthlyStar, player.Position);
        }

        if (config.PrePullAstralDraw
            && ast.CardService.CanAstralDraw
            && player.Level >= ASTActions.AstralDraw.MinLevel
            && actions.IsActionReady(ASTActions.AstralDraw.ActionId))
        {
            return actions.ExecuteOgcd(ASTActions.AstralDraw, player.GameObjectId);
        }

        return false;
    }
}
