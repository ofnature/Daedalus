using System;
using System.Diagnostics;
using System.Numerics;
using Dalamud.Plugin.Services;
using Dalamud.Game.ClientState.Objects;
using Daedalus.Services.Consumables;
using Daedalus.Services.Positional.Navigation;
using Daedalus.Services.Targeting;

namespace Daedalus.Services.Farm;

/// <summary>
/// Farm mode: kill the profile's mobs around user-placed spots until X of an item is in the bag.
/// Daedalus is the automation driver here (fourth source next to Henchman/AutoDuty/Questionable):
/// it holds the external-combat override (Source "Farm"), acquires targets (aggroed-on-us first,
/// then profile mobs leashed to the active spot), the shared automation engage opens on them, and
/// it roams between spots via vnavmesh when nothing is up. Single zone only — leaving the zone
/// stops the run (no teleporting by design). See docs/farm-mode.md.
/// </summary>
public sealed class FarmModeService : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);
    private const float AggroCleanupRangeYalms = 40f;
    private const float StallWarnMinutes = 5;

    private readonly Configuration _configuration;
    private readonly IObjectTable _objectTable;
    private readonly ITargetManager _targetManager;
    private readonly ITargetingService _targetingService;
    private readonly IVNavService _vNav;
    private readonly IInventoryProbe _inventory;
    private readonly IClientState _clientState;
    private readonly IPluginLog _log;

    private readonly Stopwatch _pollClock = Stopwatch.StartNew();
    private int _spotIndex;
    private bool _atSpot;
    private DateTime _arrivedAtSpotUtc;
    private DateTime _lastProgressUtc;
    private ulong _lastEngagedTargetId;

    /// <summary>User-facing progress/state notifications (Plugin routes these to chat).</summary>
    public event Action<string>? Notify;

    public FarmProfile Profile { get; } = new();
    public bool IsRunning { get; private set; }
    public string StatusLine { get; private set; } = "Idle";
    public int Kills { get; private set; }
    public uint CurrentItemCount { get; private set; }
    public int ActiveSpotIndex => _spotIndex;

    public FarmModeService(
        Configuration configuration,
        IObjectTable objectTable,
        ITargetManager targetManager,
        ITargetingService targetingService,
        IVNavService vNav,
        IInventoryProbe inventory,
        IClientState clientState,
        IPluginLog log)
    {
        _configuration = configuration;
        _objectTable = objectTable;
        _targetManager = targetManager;
        _targetingService = targetingService;
        _vNav = vNav;
        _inventory = inventory;
        _clientState = clientState;
        _log = log;
    }

    public string? Start()
    {
        if (IsRunning)
            return "already running";
        if (!Profile.IsValid)
            return "profile incomplete — needs an item, a target count, at least one mob, and at least one spot";
        if (_clientState.TerritoryType != Profile.TerritoryId)
            return "wrong zone — the spots were placed in a different zone";
        if (!_vNav.IsAvailable)
            return "vnavmesh is not loaded — farm mode needs it to move between spots";

        IsRunning = true;
        _spotIndex = 0;
        _atSpot = false;
        Kills = 0;
        _lastEngagedTargetId = 0;
        _lastProgressUtc = DateTime.UtcNow;
        CurrentItemCount = _inventory.GetItemCount(Profile.ItemId);
        Notify?.Invoke($"Farm started: {Profile.ItemName} ×{Profile.TargetCount} (have {CurrentItemCount}), {Profile.Mobs.Count} mob kind(s), {Profile.Spots.Count} spot(s).");
        _log.Info("[Farm] started: item {0} ({1}) x{2}, {3} mobs, {4} spots, territory {5}",
            Profile.ItemName, Profile.ItemId, Profile.TargetCount, Profile.Mobs.Count, Profile.Spots.Count, Profile.TerritoryId);
        return null;
    }

    public void Stop(string reason)
    {
        if (!IsRunning)
            return;

        IsRunning = false;
        StatusLine = "Idle";
        if (ExternalCombatOverrideState.Source == "Farm")
        {
            _configuration.ExternalCombatOverride = false;
            ExternalCombatOverrideState.Source = "";
        }
        try
        {
            if (_vNav.IsPathRunning || _vNav.IsPathfindInProgress)
                _vNav.Stop();
        }
        catch
        {
            // vnav unload race — nothing to clean up.
        }
        Notify?.Invoke($"Farm stopped — {reason}. {CurrentItemCount}× {Profile.ItemName} in bag, {Kills} kills this run.");
        _log.Info("[Farm] stopped: {0}", reason);
    }

    /// <summary>Framework-thread poll; internally throttled.</summary>
    public void Update()
    {
        if (!IsRunning)
            return;
        if (_pollClock.Elapsed < PollInterval)
            return;
        _pollClock.Restart();

        var player = _objectTable.LocalPlayer;
        if (player == null)
            return;

        if (player.CurrentHp == 0)
        {
            Stop("player died");
            return;
        }

        if (_clientState.TerritoryType != Profile.TerritoryId)
        {
            Stop("left the farm zone");
            return;
        }

        CurrentItemCount = _inventory.GetItemCount(Profile.ItemId);
        if (FarmRoamPolicy.IsComplete(CurrentItemCount, Profile.TargetCount))
        {
            Stop("target count reached");
            return;
        }

        // Hold the override for the whole run (level-triggered: another bridge's clear edge or the
        // dummy guard may drop it; re-assert like the other automation drivers do).
        if (!_configuration.ExternalCombatOverride)
        {
            _configuration.ExternalCombatOverride = true;
            ExternalCombatOverrideState.Source = "Farm";
        }

        var target = _targetingService.GetUserEnemyTarget();
        if (target != null)
        {
            _lastEngagedTargetId = target.GameObjectId;
            _lastProgressUtc = DateTime.UtcNow;
            ApproachTarget(player, target.Position, target.HitboxRadius);
            StatusLine = $"Fighting {target.Name}";
            return;
        }

        // Target just went away after we were engaging — count the kill.
        if (_lastEngagedTargetId != 0)
        {
            Kills++;
            _lastEngagedTargetId = 0;
            _lastProgressUtc = DateTime.UtcNow;
        }

        var spot = Profile.Spots[Math.Min(_spotIndex, Profile.Spots.Count - 1)];

        // Acquire: finish anything on us first, then the nearest profile mob leashed to the spot.
        var candidate = _targetingService.FindNearestAggroedEnemy(AggroCleanupRangeYalms, player)
            ?? _targetingService.FindNearestEnemyByNameIds(
                Profile.MobNameIds, Profile.LeashRadiusYalms, player, spot, Profile.LeashRadiusYalms);
        if (candidate != null)
        {
            _targetManager.Target = candidate;
            if (_vNav.IsPathRunning)
                _vNav.Stop();
            StatusLine = $"Targeting {candidate.Name}";
            return;
        }

        Roam(player.Position, spot);

        if ((DateTime.UtcNow - _lastProgressUtc).TotalMinutes >= StallWarnMinutes)
        {
            Notify?.Invoke("Farm: no kills in a while — check the spot placement and mob list.");
            _lastProgressUtc = DateTime.UtcNow;
        }
    }

    private void ApproachTarget(Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter player, Vector3 targetPosition, float targetHitbox)
    {
        // Solo positional movement is suppressed by design elsewhere, so farm does its own
        // approach: walk to the target's hitbox edge (melee reach for melee/tanks, cast range
        // otherwise), then let the rotation take over. Re-path only when nav is idle (vNav
        // stutter-step guard, same pattern as NinjaBurstApproachService).
        var stopDistance = FarmRoamPolicy.ApproachStopYalms(player.ClassJob.RowId);
        var edgeDistance = Vector3.Distance(player.Position, targetPosition) - targetHitbox - player.HitboxRadius;

        if (edgeDistance > stopDistance)
        {
            if (!_vNav.IsPathRunning && !_vNav.IsPathfindInProgress)
                _vNav.PathfindAndMoveCloseTo(targetPosition, Math.Max(stopDistance - 0.5f, 1.5f));
        }
        else if (_vNav.IsPathRunning)
        {
            _vNav.Stop();
        }
    }

    private void Roam(Vector3 playerPosition, Vector3 spot)
    {
        var distance = Vector3.Distance(playerPosition, spot);
        var wasAtSpot = _atSpot;
        _atSpot = distance <= FarmRoamPolicy.ArriveToleranceYalms;
        if (_atSpot && !wasAtSpot)
            _arrivedAtSpotUtc = DateTime.UtcNow;

        var idleSeconds = _atSpot ? (DateTime.UtcNow - _arrivedAtSpotUtc).TotalSeconds : 0;
        var navBusy = _vNav.IsPathRunning || _vNav.IsPathfindInProgress;

        switch (FarmRoamPolicy.Decide(navBusy, distance, idleSeconds, _configuration.Farm.RespawnWaitSeconds, Profile.Spots.Count))
        {
            case FarmRoamAction.MoveToSpot:
                _vNav.PathfindAndMoveTo(_vNav.SnapToFloor(spot));
                StatusLine = $"Moving to spot {_spotIndex + 1}/{Profile.Spots.Count}";
                break;

            case FarmRoamAction.AdvanceSpot:
                _spotIndex = FarmRoamPolicy.NextSpotIndex(_spotIndex, Profile.Spots.Count);
                _atSpot = false;
                StatusLine = $"Roaming to spot {_spotIndex + 1}/{Profile.Spots.Count}";
                break;

            default:
                StatusLine = _atSpot
                    ? $"Waiting for respawns at spot {_spotIndex + 1}/{Profile.Spots.Count}"
                    : StatusLine;
                break;
        }
    }

    public void Dispose()
    {
        Stop("plugin unloading");
    }
}
