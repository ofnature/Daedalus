using System;
using System.Diagnostics;
using System.Numerics;
using Dalamud.Plugin.Services;
using Dalamud.Game.ClientState.Objects;
using Daedalus.Config;
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
    private readonly IFarmMountHelper _mount;
    private readonly IPluginLog _log;

    private readonly Stopwatch _pollClock = Stopwatch.StartNew();
    private int _spotIndex;
    private bool _atSpot;
    private DateTime _arrivedAtSpotUtc;
    private DateTime _lastProgressUtc;
    private ulong _lastEngagedTargetId;
    private ulong _approachTargetId;
    private DateTime? _atTagRangeSinceUtc;

    // ---- v4 mounted travel state ----
    private const double MountCastTimeoutSeconds = 3.0;
    private const double MountGiveUpSeconds = 30.0;
    private DateTime? _mountCastRequestedUtc;
    private bool _mountCastRetried;
    private DateTime _suppressMountUntilUtc = DateTime.MinValue;
    private bool _flyFailedThisLeg;
    private bool _warnedSpecificMountFallback;

    /// <summary>User-facing progress/state notifications (Plugin routes these to chat).</summary>
    public event Action<string>? Notify;

    public FarmProfile Profile { get; } = new();
    public bool IsRunning { get; private set; }

    /// <summary>Live bag count for any item — the setup UI shows this before a run starts
    /// (<see cref="CurrentItemCount"/> only refreshes while running).</summary>
    public uint PeekItemCount(uint itemId) => itemId == 0 ? 0 : _inventory.GetItemCount(itemId);
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
        IFarmMountHelper mountHelper,
        IPluginLog log)
    {
        _configuration = configuration;
        _objectTable = objectTable;
        _targetManager = targetManager;
        _targetingService = targetingService;
        _vNav = vNav;
        _inventory = inventory;
        _clientState = clientState;
        _mount = mountHelper;
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
        _mountCastRequestedUtc = null;
        _mountCastRetried = false;
        _suppressMountUntilUtc = DateTime.MinValue;
        _flyFailedThisLeg = false;
        _warnedSpecificMountFallback = false;
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

        var newCount = _inventory.GetItemCount(Profile.ItemId);
        if (newCount != CurrentItemCount)
        {
            _log.Debug("[Farm] bag count {0} -> {1} (target {2})", CurrentItemCount, newCount, Profile.TargetCount);
            _lastProgressUtc = DateTime.UtcNow;
        }
        CurrentItemCount = newCount;
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

        // Raw hard-target check, deliberately NOT GetUserEnemyTarget(): that runs the
        // attackability probe, which fails while the target is still out of range — the farm
        // then never approached and only BMR AI (when enabled) happened to walk the toon in.
        // The farm set this target itself; a live, targetable BattleNpc is enough to walk to.
        if (_targetManager.Target is Dalamud.Game.ClientState.Objects.Types.IBattleNpc { IsDead: false, IsTargetable: true } target)
        {
            _lastEngagedTargetId = target.GameObjectId;
            _lastProgressUtc = DateTime.UtcNow;
            ApproachTarget(player, target);
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

        // Acquire: finish anything on us first, then the nearest profile mob. The player-centered
        // scan runs out to the v4 acquisition radius (spot leash still applies — the widened
        // funnel must not pull the toon outside the patrol area).
        var scanRadius = Math.Clamp(_configuration.Farm.ScanRadiusYalms, 10f, 100f);
        var candidate = _targetingService.FindNearestAggroedEnemy(AggroCleanupRangeYalms, player)
            ?? _targetingService.FindNearestEnemyByNameIds(
                Profile.MobNameIds, Math.Max(scanRadius, Profile.LeashRadiusYalms), player, spot, Profile.LeashRadiusYalms);
        if (candidate != null)
        {
            _targetManager.Target = candidate;
            // Latch the kill at ACQUISITION: high-level toons one-shot farm mobs inside a single
            // poll, so the "fighting" branch may never see the target alive — without this the
            // kill counter reads 0 on every fast-kill run.
            _lastEngagedTargetId = candidate.GameObjectId;
            _flyFailedThisLeg = false; // new travel leg
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

    /// <summary>
    /// Farm-only pull tactic (deliberately not shared with any other movement system):
    /// walk to ranged tag distance, stand still while the rotation tags the mob, let it run to
    /// us and die on the way in (melee finishes when it arrives). If the tag window passes with
    /// no engagement (kits without a ranged attack, e.g. MNK), walk to melee reach instead.
    /// Uses only SimpleMove.PathfindAndMoveTo — the one vnavmesh endpoint proven on this install.
    /// </summary>
    private void ApproachTarget(
        Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter player,
        Dalamud.Game.ClientState.Objects.Types.IBattleNpc target)
    {
        // New target: reset the tag-window tracking.
        if (target.GameObjectId != _approachTargetId)
        {
            _approachTargetId = target.GameObjectId;
            _atTagRangeSinceUtc = null;
        }

        var toPlayer = player.Position - target.Position;
        var distance = toPlayer.Length();
        var edgeDistance = distance - target.HitboxRadius - player.HitboxRadius;

        // Mounted travel owns the long legs: mount up when the mob is far, ride/fly in, land and
        // dismount at DismountRange, then the normal tag/approach logic takes over on foot.
        if (HandleMountedTravel(target.Position, edgeDistance, destinationIsMob: true, target.Name?.TextValue ?? "target"))
            return;

        if (_atTagRangeSinceUtc == null && edgeDistance <= FarmRoamPolicy.TagRangeYalms)
            _atTagRangeSinceUtc = DateTime.UtcNow;

        // "Tagged" = the mob has turned on us (it targets the player) — from here it closes the
        // distance itself while the rotation shoots it.
        var engagedOnUs = target.TargetObjectId == player.GameObjectId;
        var secondsAtTagRange = _atTagRangeSinceUtc.HasValue
            ? (DateTime.UtcNow - _atTagRangeSinceUtc.Value).TotalSeconds
            : 0;

        switch (FarmRoamPolicy.DecideApproach(engagedOnUs, edgeDistance, secondsAtTagRange))
        {
            case FarmApproachAction.MoveToTagRange:
                MoveTowardTarget(target.Position, toPlayer, distance, target.HitboxRadius, player.HitboxRadius, FarmRoamPolicy.TagRangeYalms * 0.85f);
                StatusLine = $"Approaching {target.Name} (to tag range)";
                break;

            case FarmApproachAction.MoveToMelee:
                MoveTowardTarget(target.Position, toPlayer, distance, target.HitboxRadius, player.HitboxRadius, FarmRoamPolicy.MeleeStopYalms * 0.6f);
                StatusLine = $"Walking to {target.Name} (no ranged tag)";
                break;

            default:
                if (_vNav.IsPathRunning)
                    _vNav.Stop();
                StatusLine = engagedOnUs ? $"Fighting {target.Name}" : $"Tagging {target.Name}";
                break;
        }
    }

    private void MoveTowardTarget(Vector3 targetPosition, Vector3 toPlayer, float distance, float targetHitbox, float playerHitbox, float stopEdgeYalms)
    {
        // Re-path only when nav is idle (vNav stutter-step guard).
        if (_vNav.IsPathRunning || _vNav.IsPathfindInProgress)
            return;

        var stopPoint = distance > 0.5f
            ? targetPosition + toPlayer / distance * (targetHitbox + playerHitbox + stopEdgeYalms)
            : targetPosition;
        var result = _vNav.PathfindAndMoveTo(_vNav.SnapToFloor(stopPoint));
        if (result != VNavMoveResult.Queued)
            _log.Debug("[Farm] approach path not queued: {0}", result);
    }

    /// <summary>
    /// v4 mounted travel (docs/farm-mode.md §v4): one policy decision per tick, executed against
    /// vNav + the mount helper. Returns true when travel consumed the tick (casting the mount,
    /// riding, landing, dismounting); false hands the tick back to the normal ground logic.
    /// </summary>
    private bool HandleMountedTravel(Vector3 destination, float distanceYalms, bool destinationIsMob, string label)
    {
        var cfg = _configuration.Farm;
        var mounted = _mount.IsMounted;
        var castPending = _mountCastRequestedUtc.HasValue && !mounted;
        var suppressed = DateTime.UtcNow < _suppressMountUntilUtc;

        if (mounted && _mountCastRequestedUtc.HasValue)
        {
            // Cast landed — clear the wait state.
            _mountCastRequestedUtc = null;
            _mountCastRetried = false;
        }

        var action = FarmMountPolicy.Decide(
            mounted,
            _mount.IsInFlight,
            _mount.IsInCombat,
            castPending,
            suppressed,
            distanceYalms,
            cfg.MountDistanceThresholdYalms,
            cfg.DismountRangeYalms,
            destinationIsMob);

        switch (action)
        {
            case FarmTravelAction.CastMount:
                // Mount cast is ~1s and breaks on movement: halt nav this tick, cast on the next.
                if (_vNav.IsPathRunning || _vNav.IsPathfindInProgress)
                {
                    _vNav.Stop();
                    StatusLine = "Stopping to mount";
                    return true;
                }
                CastMount();
                StatusLine = "Calling mount";
                return true;

            case FarmTravelAction.AwaitMount:
                var waited = (DateTime.UtcNow - _mountCastRequestedUtc!.Value).TotalSeconds;
                if (waited >= MountCastTimeoutSeconds)
                {
                    if (!_mountCastRetried)
                    {
                        _mountCastRetried = true;
                        CastMount();
                    }
                    else
                    {
                        // Never loop-cast: give up on mounting for a while and walk this leg.
                        _mountCastRequestedUtc = null;
                        _mountCastRetried = false;
                        _suppressMountUntilUtc = DateTime.UtcNow.AddSeconds(MountGiveUpSeconds);
                        _log.Info("[Farm] mount cast never landed — walking for the next {0}s", MountGiveUpSeconds);
                        return false;
                    }
                }
                StatusLine = "Mounting...";
                return true;

            case FarmTravelAction.Ride:
                RideToward(destination, label);
                return true;

            case FarmTravelAction.LandThenDismount:
                // Airborne at the mob: descend on a ground path first — a mid-air dismount is
                // fall damage or a stuck float. fly=false forces the landing.
                if (!_vNav.IsPathRunning && !_vNav.IsPathfindInProgress)
                    _vNav.PathfindAndMoveTo(_vNav.SnapToFloor(destination), fly: false);
                StatusLine = $"Landing near {label}";
                return true;

            case FarmTravelAction.Dismount:
                if (_vNav.IsPathRunning || _vNav.IsPathfindInProgress)
                    _vNav.Stop();
                _mount.TryDismount();
                StatusLine = "Dismounting";
                return true;

            default:
                return false;
        }
    }

    private void CastMount()
    {
        var cfg = _configuration.Farm;
        var useSpecific = FarmMountPolicy.UseSpecificMount(
            cfg.MountMode, cfg.SpecificMountId, _mount.IsMountUnlocked(cfg.SpecificMountId));

        if (cfg.MountMode == FarmMountMode.Specific && cfg.SpecificMountId != 0
            && !useSpecific && !_warnedSpecificMountFallback)
        {
            _warnedSpecificMountFallback = true;
            Notify?.Invoke("Farm: selected mount is not unlocked on this character — using Mount Roulette.");
        }

        if (_mount.TryMount(useSpecific, cfg.SpecificMountId, out var detail))
        {
            _mountCastRequestedUtc = DateTime.UtcNow;
            _log.Debug("[Farm] mount cast issued ({0})", detail);
        }
        else
        {
            // Cast refused (status gate / combat re-check) — don't spin on it this leg.
            _suppressMountUntilUtc = DateTime.UtcNow.AddSeconds(MountGiveUpSeconds);
            _log.Debug("[Farm] mount cast refused: {0}", detail);
        }
    }

    private void RideToward(Vector3 destination, string label)
    {
        if (_vNav.IsPathRunning || _vNav.IsPathfindInProgress)
        {
            StatusLine = $"Riding to {label}";
            return;
        }

        var fly = cfgFlyAllowed();
        var result = _vNav.PathfindAndMoveTo(fly ? destination : _vNav.SnapToFloor(destination), fly);
        if (result != VNavMoveResult.Queued && fly)
        {
            // Fly pathing failed (mesh/attunement edge) — fall back to ground for this leg.
            _flyFailedThisLeg = true;
            result = _vNav.PathfindAndMoveTo(_vNav.SnapToFloor(destination), fly: false);
        }

        if (result != VNavMoveResult.Queued)
            _log.Debug("[Farm] mounted path not queued: {0}", result);

        StatusLine = fly && !_flyFailedThisLeg ? $"Flying to {label}" : $"Riding to {label}";

        bool cfgFlyAllowed() =>
            _configuration.Farm.FlyWhenPossible && !_flyFailedThisLeg && _mount.CanFlyInCurrentZone();
    }

    private void Roam(Vector3 playerPosition, Vector3 spot)
    {
        var distance = Vector3.Distance(playerPosition, spot);
        var wasAtSpot = _atSpot;
        _atSpot = distance <= FarmRoamPolicy.ArriveToleranceYalms;
        if (_atSpot && !wasAtSpot)
            _arrivedAtSpotUtc = DateTime.UtcNow;

        var idleSeconds = _atSpot ? (DateTime.UtcNow - _arrivedAtSpotUtc).TotalSeconds : 0;

        // Long spot legs travel mounted (fly where legal); short hops walk as before.
        if (HandleMountedTravel(spot, distance, destinationIsMob: false, $"spot {_spotIndex + 1}/{Profile.Spots.Count}"))
            return;

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
                _flyFailedThisLeg = false; // new travel leg
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
