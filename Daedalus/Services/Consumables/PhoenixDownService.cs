using System;
using System.Collections.Generic;
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

    /// <summary>ClientStructs <c>ActionType.Item</c> — what the cast bar reports while using an item.</summary>
    private const int ItemActionType = 2;

    /// <summary>Phoenix Down's Action row, which some cast bars report instead of the item id.</summary>
    private const uint PhoenixDownActionRow = 43336;

    private readonly PhoenixDownCastTracker _cast = new();
    private DateTime? _allHealersDownSince;

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
    private string _lastState = "idle";

    /// <summary>
    /// Why the safety net did or did not fire. Mirrored to the Revive tab on every assignment — this
    /// was computed once a second and displayed nowhere, so a toon quietly not using Phoenix Downs
    /// looked identical to one that had decided against it.
    /// </summary>
    public string LastState
    {
        get => _lastState;
        private set
        {
            _lastState = value;
            Daedalus.Services.Diagnostics.ReviveDiagnostics.Report(
                Daedalus.Services.Diagnostics.ReviveSource.PhoenixDown, value);
        }
    }

    /// <summary>Optional LAN bus — claim broadcast + foreign-claim hold-off. Null solo.</summary>
    public Daedalus.Services.Network.CoordinationBus? Bus { get; set; }

    /// <summary>Designated off-tank per the LAN tank-swap role — exempt from the tank hold.</summary>
    public Func<bool>? IsDesignatedOffTank { get; set; }

    /// <summary>
    /// Walks this toon to within range of a point (point, range, seconds) -- <c>Minerva.RequestStandNear</c> when
    /// Minerva is the engine. Null or false: nothing walks, and an out-of-range corpse stays out of range.
    /// </summary>
    public Func<Vector3, float, double, bool>? RequestApproach { get; set; }

    /// <summary>How long each walk request lasts. Re-asserted every check (1s), so it lapses soon after it stops being wanted.</summary>
    private const double ApproachRequestSeconds = 2.5;

    /// <summary>The corpse being walked to, kept so the request can be held through the cast.</summary>
    private Vector3? _approachPoint;

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

        // Resolve an attempt in flight every frame, before anything else, so turning the feature off
        // mid-cast still reports how it ended and a finished cast is recorded the frame it finishes.
        TrackAttempt(player, now);

        if (!_configuration.Consumables.EnablePhoenixDown)
            return;
        if ((now - _lastCheck).TotalSeconds < CheckIntervalSeconds)
            return;
        _lastCheck = now;

        // One attempt at a time; the tracker owns the status line until it resolves. Keep the walk request alive
        // through the cast, or Minerva's uptime goal comes back and pulls toward the boss.
        if (_cast.InFlight)
        {
            if (_approachPoint is { } holdAt)
                RequestApproach?.Invoke(holdAt, PhoenixDownPolicy.ApproachRangeYalms, ApproachRequestSeconds);
            return;
        }
        _approachPoint = null;

        if (partyList.Length == 0)
        {
            LastState = "no party";
            return;
        }

        var inCombat = (player.StatusFlags & Dalamud.Game.ClientState.Objects.Enums.StatusFlags.InCombat) != 0;

        var healers = 0;
        var deadHealers = 0;
        var livingOthers = 0;
        var livingNonTanks = new List<uint>();
        var livingNonTankSpots = new List<(uint Id, Vector3 Position)>();
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

            if (!isDead && !JobRegistry.IsTank(jobId))
            {
                livingNonTanks.Add(member.EntityId);
                livingNonTankSpots.Add((member.EntityId, member.Position));
            }

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

        // The clock every toon starts from: when the last healer went down.
        if (situation.AllHealersDead)
            _allHealersDownSince ??= now;
        else
            _allHealersDownSince = null;

        var (fire, reason) = PhoenixDownPolicy.Decide(in situation);
        LastState = reason;
        var sinceDown = _allHealersDownSince is { } down ? (now - down).TotalSeconds : 0d;

        // Walk to the corpse when it is our turn to: the nearest non-tank first, the next only if nobody has
        // claimed by then. Asked even once in range, so the uptime goal does not pull the toon back out of it
        // before the cast starts.
        if (target is not null && RequestApproach is { } approach && PhoenixDownPolicy.WantsApproach(in situation))
        {
            var corpseAt = target.Position;
            var others = new List<float>();
            foreach (var (id, position) in livingNonTankSpots)
            {
                if (id != player.EntityId)
                    others.Add(Vector3.Distance(position, corpseAt));
            }

            var walkRank = PhoenixDownStagger.ApproachRankOf(targetDistance, situation.SelfIsTank, others);
            var name = target.Name?.TextValue ?? "healer";
            if (PhoenixDownStagger.MayApproach(walkRank, sinceDown) && approach(corpseAt, PhoenixDownPolicy.ApproachRangeYalms, ApproachRequestSeconds))
            {
                _approachPoint = corpseAt;
                if (targetDistance > PhoenixDownPolicy.RangeYalms)
                    LastState = $"walking to {name} ({targetDistance:F0}y)";
            }
            else if (!fire)
            {
                LastState = $"{reason} — #{walkRank + 1} in line to walk to {name}";
            }
        }

        if (!fire)
            return;

        // Wait our turn, so two toons deciding in the same second don't both cast (see PhoenixDownStagger).
        var rank = PhoenixDownStagger.RankOf(player.EntityId, situation.SelfIsTank, livingNonTanks);
        if (!PhoenixDownStagger.MayFire(rank, sinceDown))
        {
            LastState = $"waiting my turn (#{rank + 1} in line)";
            return;
        }

        var targetName = target!.Name?.TextValue ?? "healer";
        _lastAttempt = now;
        _cast.BeginAttempt(targetName);

        // The return value is deliberately ignored: for an item it reads false even when the cast goes
        // through (field 2026-09-25). The tracker decides what happened from the cast bar instead.
        _actionService.ExecuteItem(ConsumableIds.PhoenixDown, preferHq: false, target.GameObjectId);
        LastState = $"starting on {targetName}";
    }

    /// <summary>Turns the cast bar into an outcome, and acts on it.</summary>
    private void TrackAttempt(IPlayerCharacter player, DateTime now)
    {
        var name = _cast.TargetName ?? "healer";
        switch (_cast.Observe(IsCastingPhoenixDown(player), player.CurrentCastTime, player.TotalCastTime))
        {
            case PhoenixDownAttemptOutcome.Started:
                // Claim the moment the cast is real — not on the call's return, which lies for items.
                LastState = $"casting on {name} (8s)";
                Bus?.BroadcastPhoenixDown(name);
                _log.Warning($"Phoenix Down: all healers down — casting on {name}");
                break;

            case PhoenixDownAttemptOutcome.Completed:
                _lastUse = now;
                LastState = $"used on {name}";
                break;

            case PhoenixDownAttemptOutcome.Cancelled:
                // Nothing was spent. A short pause, not the refusal backoff and never the item recast.
                _lastAttempt = now.AddSeconds(
                    PhoenixDownPolicy.CancelledRetrySeconds - PhoenixDownPolicy.RetryBackoffSeconds);
                LastState = $"cast on {name} was cancelled — retrying";
                _log.Information($"Phoenix Down: cast on {name} was cancelled before it finished");
                break;

            case PhoenixDownAttemptOutcome.Refused:
                LastState = "game refused the item (blocked duty?)";
                _log.Warning($"Phoenix Down: use on {name} refused by the game");
                break;
        }
    }

    private static bool IsCastingPhoenixDown(IPlayerCharacter player)
        => player.IsCasting
           && (Convert.ToInt32(player.CastActionType) == ItemActionType
               || player.CastActionId is ConsumableIds.PhoenixDown or PhoenixDownActionRow);

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
