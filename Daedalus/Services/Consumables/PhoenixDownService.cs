using System;
using System.Numerics;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using Daedalus.Data;
using Daedalus.Services.Action;

namespace Daedalus.Services.Consumables;

/// <summary>
/// Phoenix Down safety net (lan-ipc-plan Phase 3): when every healer in the party is dead,
/// hardcast item 4570 (8s, 15y) on the nearest dead healer. Runs from the framework tick
/// beside the LAN healer-down detector, and works without LAN — the bus only adds the
/// claim broadcast so two toons never burn an item on the same corpse.
/// Ships dark: Consumables ▸ EnablePhoenixDown, default off.
/// </summary>
public sealed class PhoenixDownService
{
    /// <summary>Raise Pending — a raise is already incoming for this corpse.</summary>
    private const ushort RaisePendingStatusId = 148;

    /// <summary>Same threshold BaseRotation.UpdateMovement uses (~5cm per frame).</summary>
    private const float MovementThresholdSquared = 0.0025f;

    private const double CheckIntervalSeconds = 1.0;

    private readonly IActionService _actionService;
    private readonly IInventoryProbe _inventory;
    private readonly Configuration _configuration;
    private readonly IPluginLog _log;

    private DateTime _lastCheck = DateTime.MinValue;
    private DateTime _lastAttempt = DateTime.MinValue;
    private DateTime _lastUse = DateTime.MinValue;
    private DateTime _foreignClaim = DateTime.MinValue;
    private DateTime _lastMoved = DateTime.MinValue;
    private Vector3 _lastPosition;

    /// <summary>Why the last decision did (not) fire — surfaced for debug UI.</summary>
    public string LastState { get; private set; } = "idle";

    /// <summary>Optional LAN bus — claim broadcast + foreign-claim hold-off. Null solo.</summary>
    public Daedalus.Services.Network.CoordinationBus? Bus { get; set; }

    /// <summary>Designated off-tank per the LAN tank-swap role — exempt from the tank hold.</summary>
    public Func<bool>? IsDesignatedOffTank { get; set; }

    public PhoenixDownService(
        IActionService actionService,
        IInventoryProbe inventory,
        Configuration configuration,
        IPluginLog log)
    {
        _actionService = actionService;
        _inventory = inventory;
        _configuration = configuration;
        _log = log;
    }

    /// <summary>Another toon broadcast that it is casting one — hold off.</summary>
    public void OnForeignClaim(string sender, string targetName)
    {
        _foreignClaim = DateTime.UtcNow;
        _log.Information($"Phoenix Down: {sender} is casting on {targetName} — holding off");
    }

    /// <summary>Framework tick. Cheap movement sample every frame; full decision at 1s cadence.</summary>
    public void Update(IPlayerCharacter? player, IPartyList partyList)
    {
        if (player is null)
            return;

        // Movement must be sampled every frame — a 1s cadence would miss the stop windows.
        var now = DateTime.UtcNow;
        if (Vector3.DistanceSquared(player.Position, _lastPosition) > MovementThresholdSquared)
            _lastMoved = now;
        _lastPosition = player.Position;

        if (!_configuration.Consumables.EnablePhoenixDown)
            return;
        if ((now - _lastCheck).TotalSeconds < CheckIntervalSeconds)
            return;
        _lastCheck = now;

        if (partyList.Length == 0)
        {
            LastState = "no party";
            return;
        }

        var inCombat = (player.StatusFlags & Dalamud.Game.ClientState.Objects.Enums.StatusFlags.InCombat) != 0;

        var healers = 0;
        var deadHealers = 0;
        var livingOthers = 0;
        IBattleChara? target = null;
        var targetDistance = float.MaxValue;

        foreach (var member in partyList)
        {
            if (member?.ClassJob.RowId is not { } jobId)
                continue;

            var isSelf = member.GameObject?.GameObjectId == player.GameObjectId;
            var isDead = member.CurrentHP == 0;

            if (!isSelf && !isDead)
                livingOthers++;

            if (!JobRegistry.IsHealer(jobId))
                continue;

            healers++;
            if (!isDead)
                continue;
            deadHealers++;

            if (member.GameObject is not IBattleChara corpse || HasStatus(corpse, RaisePendingStatusId))
                continue;

            var distance = Vector3.Distance(player.Position, corpse.Position);
            if (distance < targetDistance)
            {
                target = corpse;
                targetDistance = distance;
            }
        }

        var isMoving = (now - _lastMoved).TotalSeconds < PhoenixDownPolicy.MovementGraceSeconds
            || Daedalus.Rotation.Base.RotationServices.VNav?.IsPathRunning == true
            || Daedalus.Rotation.Base.RotationServices.MovementArbiter?.IsExternalMovementActive == true;

        var situation = new PhoenixDownSituation(
            Enabled: true, // config checked above; kept in the record for the pure tests
            InCombat: inCombat,
            SelfAlive: player.CurrentHp > 0,
            SelfCasting: player.IsCasting,
            SelfIsTank: JobRegistry.IsTank(player.ClassJob.RowId),
            SelfIsDesignatedOffTank: IsDesignatedOffTank?.Invoke() == true,
            LivingOthers: livingOthers,
            HealersPresent: healers > 0,
            AllHealersDead: healers > 0 && healers == deadHealers,
            TargetFound: target is not null,
            TargetDistanceYalms: targetDistance,
            ItemCount: _inventory.GetItemCount(ConsumableIds.PhoenixDown),
            SecondsSinceOwnUse: (now - _lastUse).TotalSeconds,
            SecondsSinceOwnAttempt: (now - _lastAttempt).TotalSeconds,
            SecondsSinceForeignClaim: (now - _foreignClaim).TotalSeconds,
            IsMoving: isMoving);

        var (fire, reason) = PhoenixDownPolicy.Decide(in situation);
        LastState = reason;
        if (!fire)
            return;

        var targetName = target!.Name?.TextValue ?? "healer";
        _lastAttempt = now;
        if (_actionService.ExecuteItem(ConsumableIds.PhoenixDown, preferHq: false, target.GameObjectId))
        {
            _lastUse = now;
            LastState = $"casting on {targetName} (8s)";
            Bus?.BroadcastPhoenixDown(targetName);
            _log.Warning($"Phoenix Down: all healers down — casting on {targetName}");
        }
        else
        {
            // The game said no: blocked duty type, a recast we can't see, or a target the
            // item refuses. Back off (RetryBackoffSeconds) instead of hammering it.
            LastState = "game refused the item (blocked duty?)";
            _log.Warning($"Phoenix Down: use on {targetName} refused by the game");
        }
    }

    private static bool HasStatus(IBattleChara chara, uint statusId)
    {
        if (chara.StatusList == null)
            return false;

        foreach (var status in chara.StatusList)
        {
            if (status != null && status.StatusId == statusId)
                return true;
        }

        return false;
    }
}
