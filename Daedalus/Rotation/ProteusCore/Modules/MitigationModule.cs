using Daedalus.Config.DPS;
using Daedalus.Rotation.Common.Scheduling;
using Daedalus.Rotation.ProteusCore.Abilities;
using Daedalus.Rotation.ProteusCore.Context;

namespace Daedalus.Rotation.ProteusCore.Modules;

/// <summary>
/// Tank-role survival: Diamondback (~90% mitigation, 10s, locks all actions) below the configured
/// HP threshold. Deliberately blunt for v1 — a timeline/tankbuster-aware trigger is a later pass.
/// v3.3 adds Cactguard onto the party tank when BossMod forecasts a tankbuster (one designated
/// non-tank caster per fleet).
/// </summary>
public sealed class MitigationModule : IProteusModule
{
    public int Priority => 10;
    public string Name => "Mitigation";

    public bool TryExecute(IProteusContext context, bool isMoving) => false;
    public void UpdateDebugState(IProteusContext context) { }

    /// <summary>Injectable clock so tests can age the Cactguard latch.</summary>
    internal System.Func<System.DateTime> UtcNow = () => System.DateTime.UtcNow;

    /// <summary>Cactguard fires when BMR's tankbuster forecast is inside this lead (buff lasts 6s).</summary>
    private const float CactguardLeadSeconds = 5f;

    /// <summary>One Cactguard per buster window — the forecast keeps reading "soon" while it counts down.</summary>
    private const float CactguardRelatchSeconds = 10f;

    private System.DateTime _cactguardLatchUtc = System.DateTime.MinValue;

    public void CollectCandidates(IProteusContext context, RotationScheduler scheduler, bool isMoving)
    {
        if (!context.InCombat) { context.Debug.MitigationState = "Not in combat"; return; }
        if (context.HasDiamondback) { context.Debug.MitigationState = "Diamondback (locked)"; return; }
        if (context.HasWaningNocturne) { context.Debug.MitigationState = "Waning Nocturne (locked out)"; return; }

        TryPushCactguard(context, scheduler, isMoving);

        if (context.Role != BluRole.Tank && context.Role != BluRole.Solo)
        { context.Debug.MitigationState = "Not tank/solo role"; return; }
        if (!context.Configuration.BlueMage.EnableDiamondback) return;
        if (!context.IsSpellUsable(Daedalus.Data.BLUActions.Diamondback.ActionId))
        {
            context.Debug.MitigationState = "Diamondback not slotted";
            return;
        }
        // 2.0s hardcast: while moving the cast would start-cancel loop at exactly the moment it
        // matters — hold until stationary (movement is AutoDuty/mechanic-driven and brief).
        if (isMoving) { context.Debug.MitigationState = "Diamondback (waiting: moving)"; return; }

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

    /// <summary>
    /// v3.3: Cactguard (5% DR, 15% when the caster carries tank mimicry) onto the party tank when
    /// BossMod forecasts a tankbuster. ONE designated non-tank per fleet (capability election);
    /// a lone BLU in a mixed party self-designates. Deliberately NOT MechanicCastGate-gated —
    /// the forecast that opens this push is the same mechanic the gate would block on.
    /// </summary>
    private void TryPushCactguard(IProteusContext context, RotationScheduler scheduler, bool isMoving)
    {
        var cfg = context.Configuration.BlueMage;
        if (!cfg.EnableCactguard) return;
        if (context.Role == BluRole.Tank) return; // the tank is the TARGET, never the caster
        if (Daedalus.Services.Blu.BluCoordinationState.CoordinationActive
            && !Daedalus.Services.Blu.BluCoordinationState.IsCactguardOwner)
            return;
        if (!context.IsSpellUsable(Daedalus.Data.BLUActions.Cactguard.ActionId)) return;

        var busterIn = Daedalus.Services.Blu.BluCoordinationState.NextTankbusterInSeconds;
        if (busterIn <= 0f || busterIn > CactguardLeadSeconds) return;
        if (isMoving) return; // 1.0s cast

        var now = UtcNow();
        if ((now - _cactguardLatchUtc).TotalSeconds < CactguardRelatchSeconds) return;

        var player = context.Player;
        Dalamud.Game.ClientState.Objects.Types.IBattleChara? tank = null;
        foreach (var member in context.PartyHelper.GetAllPartyMembers(player))
        {
            if (member.EntityId == player.EntityId) continue;
            if (member.CurrentHp == 0) continue;
            // A real tank job, or the tank-mimic BLU in an all-BLU party (status 2124).
            var jobId = Daedalus.Rotation.Common.Helpers.TrustPartyRoleHelper.ResolveJobId(member, context.PartyList);
            var isTank = Daedalus.Data.JobRegistry.IsTank(jobId)
                         || Daedalus.Rotation.Common.Helpers.BaseStatusHelper.HasStatus(
                             member, Daedalus.Data.BLUActions.StatusIds.AethericMimicryTank);
            if (!isTank) continue;
            if (System.Numerics.Vector3.Distance(player.Position, member.Position)
                > Daedalus.Data.BLUActions.Cactguard.Range) continue;
            tank = member;
            break;
        }
        if (tank == null) return;

        var capturedTank = tank;
        var capturedBusterIn = busterIn;
        scheduler.PushGcd(ProteusAbilities.Cactguard, tank.GameObjectId, priority: 2,
            onDispatched: _ =>
            {
                _cactguardLatchUtc = UtcNow();
                context.Debug.PlannedAction = Daedalus.Data.BLUActions.Cactguard.Name;
                context.Debug.MitigationState =
                    $"Cactguard → {capturedTank.Name?.TextValue ?? "tank"} (buster in {capturedBusterIn:F1}s)";
            });
    }
}
