using Daedalus.Config.DPS;
using Daedalus.Rotation.Common.Scheduling;
using Daedalus.Rotation.ProteusCore.Abilities;
using Daedalus.Rotation.ProteusCore.Context;

namespace Daedalus.Rotation.ProteusCore.Modules;

/// <summary>
/// Tank-role survival: Diamondback (~90% mitigation, 10s, locks all actions) below the configured
/// HP threshold. Deliberately blunt for v1 — a timeline/tankbuster-aware trigger is a later pass.
/// </summary>
public sealed class MitigationModule : IProteusModule
{
    public int Priority => 10;
    public string Name => "Mitigation";

    public bool TryExecute(IProteusContext context, bool isMoving) => false;
    public void UpdateDebugState(IProteusContext context) { }

    public void CollectCandidates(IProteusContext context, RotationScheduler scheduler, bool isMoving)
    {
        if (!context.InCombat) { context.Debug.MitigationState = "Not in combat"; return; }
        if (context.HasDiamondback) { context.Debug.MitigationState = "Diamondback (locked)"; return; }
        if (context.Role != BluRole.Tank) { context.Debug.MitigationState = "Not tank role"; return; }
        if (!context.Configuration.BlueMage.EnableDiamondback) return;

        var player = context.Player;
        var hpPercent = player.MaxHp > 0 ? (float)player.CurrentHp / player.MaxHp * 100f : 100f;
        if (hpPercent > context.Configuration.BlueMage.DiamondbackHpPercent)
        {
            context.Debug.MitigationState = $"Monitoring ({hpPercent:F0}% HP)";
            return;
        }
        if (context.CurrentMp < Daedalus.Data.BLUActions.Diamondback.MpCost) return;

        scheduler.PushGcd(ProteusAbilities.Diamondback, player.GameObjectId, priority: 1,
            onDispatched: _ =>
            {
                context.Debug.PlannedAction = Daedalus.Data.BLUActions.Diamondback.Name;
                context.Debug.MitigationState = $"Diamondback ({hpPercent:F0}% HP)";
            });
    }
}
