using Daedalus.Services.Action;
using Daedalus.Data;
using Daedalus.Rotation.AstraeaCore.Abilities;
using Daedalus.Rotation.AstraeaCore.Context;
using Daedalus.Rotation.Common;
using Daedalus.Rotation.Common.Modules;

namespace Daedalus.Rotation.AstraeaCore.Modules;

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
            && ActionAvailability.MeetsLevelAndLearned(player.Level, context.ActionService, ASTActions.EarthlyStar)
            && actions.IsActionReady(ASTActions.EarthlyStar.ActionId))
        {
            return actions.ExecuteGroundTargetedOgcd(ASTActions.EarthlyStar, player.Position);
        }

        if (config.PrePullAstralDraw
            && ast.CardService.CanAstralDraw
            && ActionAvailability.MeetsLevelAndLearned(player.Level, context.ActionService, ASTActions.AstralDraw)
            && actions.IsActionReady(ASTActions.AstralDraw.ActionId))
        {
            return actions.ExecuteOgcd(ASTActions.AstralDraw, player.GameObjectId);
        }

        return false;
    }
}
