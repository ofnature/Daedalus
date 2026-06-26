using Daedalus.Data;
using Daedalus.Rotation.Common;
using Daedalus.Rotation.Common.Modules;
using Daedalus.Rotation.PrometheusCore.Context;
using Daedalus.Services;
using Daedalus.Services.Action;

namespace Daedalus.Rotation.PrometheusCore.Modules;

/// <summary>
/// Pre-pull MCH opener: Reassemble (~4.75s) then Air Anchor at pull (RSR CountDownAction).
/// Uses pull intent when duty countdown is unavailable.
/// </summary>
public sealed class PrometheusPrePullCandidate : IPrePullCandidate
{
    public bool TryDispatch(uint jobId, IRotationContext context)
    {
        if (jobId != JobRegistry.Machinist) return false;
        if (context is not IPrometheusContext mch) return false;
        if (context.InCombat) return false;

        var cfg = context.Configuration.Machinist;
        if (!cfg.EnablePrePullOpener) return false;

        var player = mch.Player;
        var actions = mch.ActionService;

        if (!actions.CanExecuteOgcd)
            return false;

        return TryReassemble(mch, actions, player.GameObjectId);
    }

    /// <summary>
    /// GCD leg of the opener (Air Anchor at pull). Called from <see cref="Prometheus"/> when pre-pull GCD is ready.
    /// </summary>
    internal static bool TryDispatchPrePullGcd(IPrometheusContext context)
    {
        if (context.InCombat) return false;
        if (!context.Configuration.Machinist.EnablePrePullOpener) return false;
        if (!context.ActionService.CanExecuteGcd) return false;

        var target = context.TargetingService.FindEnemy(
            context.Configuration.Targeting.EnemyStrategy,
            FFXIVConstants.RangedTargetingRange,
            context.Player);
        if (target is null) return false;

        return TryAirAnchor(context, context.ActionService, target.GameObjectId);
    }

    private static bool TryReassemble(IPrometheusContext context, IActionService actions, ulong targetId)
    {
        if (!context.Configuration.Machinist.EnableReassemble) return false;
        if (context.Player.Level < MCHActions.Reassemble.MinLevel) return false;
        if (context.HasReassemble || context.ReassembleCharges <= 0) return false;
        if (!actions.IsActionReady(MCHActions.Reassemble.ActionId)) return false;

        var countdown = SafeGameAccess.TryGetCountdownRemaining();
        if (countdown.HasValue && countdown.Value > 4.75f)
            return false;

        return actions.ExecuteOgcd(MCHActions.Reassemble, targetId);
    }

    private static bool TryAirAnchor(IPrometheusContext context, IActionService actions, ulong targetId)
    {
        if (!context.Configuration.Machinist.EnableAirAnchor) return false;
        var level = (byte)context.Player.Level;
        var aa = MCHActions.GetAirAnchor(level, actions);
        if (context.Player.Level < aa.MinLevel) return false;
        if (!actions.IsActionReady(aa.ActionId)) return false;

        var countdown = SafeGameAccess.TryGetCountdownRemaining();
        if (countdown.HasValue)
        {
            var threshold = context.Configuration.Machinist.PrePullAirAnchorAtOneSecond ? 1f : 0.1f;
            if (countdown.Value > threshold)
                return false;
        }
        else if (!actions.WasLastAction(MCHActions.Reassemble.ActionId))
        {
            return false;
        }

        return actions.ExecuteGcd(aa, targetId);
    }
}
