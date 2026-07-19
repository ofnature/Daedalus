using System;
using System.Collections.Generic;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using Daedalus.Data;
using Daedalus.Services.Party;

namespace Daedalus.Services;

/// <summary>
/// Detects raid buff burst windows by scanning the local player's status effects,
/// subscribing to party cast events for low-latency detection, and optionally
/// consulting IPC state from PartyCoordinationService.
///
/// Burst window = any of the coordinated party raid buffs is active on the player.
/// These buffs (Battle Litany, Technical Finish, Brotherhood, etc.) all have ~120s CDs
/// and ~15–20s durations, creating a predictable ~100s cycle.
///
/// Cast-event subscription removes the per-frame status-scan latency: when a party
/// member's raid buff resolves, we mark the burst window immediately rather than
/// waiting for the local status list to populate (typically 50-200ms later for
/// non-self casts due to network propagation + Dalamud refresh). The status scan
/// stays as a fallback to catch any missed events and to detect buff falloff.
/// </summary>
public sealed class BurstWindowService : IBurstWindowService, IDisposable
{
    private readonly IPartyCoordinationService? _partyCoordinationService;
    private readonly ICombatEventService? _combatEventService;
    private readonly IPartyList? _partyList;
    private readonly IObjectTable? _objectTable;
    private readonly Action<uint, uint>? _onAbilityUsedHandler;

    // Raid buff status IDs that appear on the LOCAL PLAYER when burst is active.
    // These are party-wide buffs applied by DPS/support jobs to the entire party.
    private static readonly HashSet<uint> RaidBuffStatusIds = new()
    {
        DRGActions.StatusIds.BattleLitany,      // 786  – +10% crit rate
        BRDActions.StatusIds.BattleVoice,        // 141  – +20% DH rate
        BRDActions.StatusIds.RadiantFinale,      // 2964 – +2-6% damage
        SMNActions.StatusIds.SearingLight,       // 2703 – +5% damage
        RDMActions.StatusIds.EmboldenParty,      // 1297 – +5% damage (party version)
        DNCActions.StatusIds.TechnicalFinish,   // 1822 – +5% damage
        RPRActions.StatusIds.ArcaneCircle,       // 2599 – +3% damage
        MNKActions.StatusIds.Brotherhood,        // 1182 – +5% damage
        PCTActions.StatusIds.StarryMuse,         // 3685 – +5% damage
        ASTActions.DivinationStatusId,           // 1878 – +6% damage
    };

    // Raid debuff status IDs that appear on the ENEMY TARGET during burst.
    // These are vulnerability/crit debuffs applied by support jobs that extend
    // the effective burst window on a specific target.
    private static readonly HashSet<uint> RaidDebuffStatusIds = new()
    {
        SCHActions.ChainStratagemStatusId,       // 1221 – +10% crit rate on target
        NINActions.StatusIds.Dokumori,           // 3849 – +5% damage taken
        NINActions.StatusIds.VulnerabilityUp,    // 638  – +5% damage taken (Trick Attack)
    };

    // Burst cycle approximation for timer-based prediction when IPC is unavailable.
    // Most raid buffs: 120s CD, ~20s duration → gap between windows ≈ 100s.
    private const float BurstCycleGapSeconds = 100f;

    /// <summary>
    /// After this many seconds in combat without coordinated IPC burst signals,
    /// stop holding for party burst and use local raid-buff detection only.
    /// Tuned for solo/trust and short dungeon pulls where 30s was too late.
    /// </summary>
    private const float SoloBurstFallbackCombatSeconds = 8f;

    // Cached per-frame state
    private bool _isInBurstWindow;
    private float _secondsRemainingInBurst;

    // Tracks when the most recent burst window ended for timer-based prediction
    private DateTime? _lastBurstWindowEnd;

    // Combat timing for solo burst fallback
    private DateTime? _combatStartUtc;
    private DateTime? _lastCoordinatedBurstSignalUtc;

    // Burst window history tracking
    private readonly List<(DateTime Start, DateTime End)> _burstWindowHistory = new();
    private DateTime _currentWindowStart;
    private bool _wasInBurst;

    public BurstWindowService(
        IPartyCoordinationService? partyCoordinationService = null,
        ICombatEventService? combatEventService = null,
        IPartyList? partyList = null,
        IObjectTable? objectTable = null)
    {
        _partyCoordinationService = partyCoordinationService;
        _combatEventService = combatEventService;
        _partyList = partyList;
        _objectTable = objectTable;

        if (_combatEventService != null)
        {
            _onAbilityUsedHandler = OnAbilityUsed;
            _combatEventService.OnAbilityUsed += _onAbilityUsedHandler;
        }
    }

    public void Dispose()
    {
        if (_combatEventService != null && _onAbilityUsedHandler != null)
            _combatEventService.OnAbilityUsed -= _onAbilityUsedHandler;
    }

    /// <summary>
    /// Cast-event handler. Fires when any nearby actor's action effect resolves.
    /// Filters to coordinated raid buffs cast by the local player or a party member,
    /// then opens the burst window with the buff's known duration.
    /// </summary>
    private void OnAbilityUsed(uint casterEntityId, uint actionId)
    {
        if (!CoordinatedRaidBuffs.IsCoordinatedRaidBuff(actionId))
            return;

        if (!IsCasterInParty(casterEntityId))
            return;

        var duration = CoordinatedRaidBuffs.GetBuffDuration(actionId);

        if (!_isInBurstWindow)
        {
            _isInBurstWindow = true;
            _currentWindowStart = DateTime.UtcNow;
            _wasInBurst = true;
        }
        if (duration > _secondsRemainingInBurst)
            _secondsRemainingInBurst = duration;
    }

    private bool IsCasterInParty(uint casterEntityId)
    {
        // Self-cast always counts: the local player's own raid buff applies to them.
        if (_objectTable?.LocalPlayer?.EntityId == casterEntityId)
            return true;

        // Party member cast: their raid buff applies to the local player.
        if (_partyList == null)
            return false;
        foreach (var member in _partyList)
        {
            if (member?.GameObject?.EntityId == casterEntityId)
                return true;
        }
        return false;
    }

    #region IBurstWindowService

    public bool IsInBurstWindow => _isInBurstWindow;

    public float SecondsRemainingInBurst => _secondsRemainingInBurst;

    /// <inheritdoc />
    /// <remarks>
    /// Computed on read from live combat elapsed (not cached at <see cref="Update"/>)
    /// so rotation modules and burst scanning agree within the same frame.
    /// </remarks>
    public bool UseSoloBurstFallback => ComputeUseSoloBurstFallback();

    public IReadOnlyList<(DateTime Start, DateTime End)> BurstWindowHistory => _burstWindowHistory;

    /// <inheritdoc />
    public float SecondsSinceLastBurstStart
    {
        get
        {
            if (_isInBurstWindow)
                return 0f;
            if (_burstWindowHistory.Count > 0)
                return (float)(DateTime.UtcNow - _burstWindowHistory[^1].Start).TotalSeconds;
            return -1f;
        }
    }

    public bool IsBurstImminent(float thresholdSeconds = 5f)
    {
        if (_isInBurstWindow)
            return false;

        // Solo/trust: do not hold spenders waiting for party IPC or NPC debuff windows.
        if (UseSoloBurstFallback)
            return false;

        // IPC: another Daedalus instance announced incoming raid buff
        if (_partyCoordinationService?.HasPendingRaidBuffIntent(thresholdSeconds) == true)
            return true;

        // IPC: general burst state from party coordination
        var ipcState = _partyCoordinationService?.GetBurstWindowState();
        if (ipcState is { IsImminent: true, SecondsUntilBurst: >= 0 } state &&
            state.SecondsUntilBurst <= thresholdSeconds)
            return true;

        // Timer-based fallback: use the ~100s burst cycle
        var until = TimerBasedSecondsUntilBurst;
        return until >= 0f && until <= thresholdSeconds;
    }

    public float SecondsUntilNextBurst
    {
        get
        {
            if (_isInBurstWindow)
                return 0f;

            // IPC estimate
            if (_partyCoordinationService != null)
            {
                var ipcSeconds = _partyCoordinationService.GetSecondsUntilBurst();
                if (ipcSeconds >= 0f)
                    return ipcSeconds;
            }

            return TimerBasedSecondsUntilBurst;
        }
    }

    public void Update(IPlayerCharacter player, IBattleChara? currentTarget = null, bool inCombat = true)
    {
        UpdateCombatAndFallbackState(inCombat);

        // Scan player status list for active party raid buffs
        _isInBurstWindow = false;
        _secondsRemainingInBurst = 0f;

        if (player.StatusList != null)
        {
            foreach (var status in player.StatusList)
            {
                if (!RaidBuffStatusIds.Contains(status.StatusId))
                    continue;

                _isInBurstWindow = true;
                if (status.RemainingTime > _secondsRemainingInBurst)
                    _secondsRemainingInBurst = status.RemainingTime;
            }
        }

        // Trust/NPC debuffs are not the player's burst cycle — skip in solo fallback so
        // analytics and pooling align with locally cast raid buffs (Embolden, Litany, etc.).
        if (!UseSoloBurstFallback && currentTarget?.StatusList != null)
        {
            foreach (var status in currentTarget.StatusList)
            {
                if (!RaidDebuffStatusIds.Contains(status.StatusId))
                    continue;

                _isInBurstWindow = true;
                if (status.RemainingTime > _secondsRemainingInBurst)
                    _secondsRemainingInBurst = status.RemainingTime;
            }
        }

        // Also check IPC burst state (multi-instance scenario)
        if (!UseSoloBurstFallback && !_isInBurstWindow && _partyCoordinationService?.IsInBurstWindow() == true)
        {
            _isInBurstWindow = true;
            _secondsRemainingInBurst = _partyCoordinationService?.GetBurstWindowRemaining() ?? 0f;
        }

        // Record when the burst window ends for timer-based cycle prediction
        if (_wasInBurst && !_isInBurstWindow)
            _lastBurstWindowEnd = DateTime.UtcNow;

        // Track burst window history transitions
        if (_isInBurstWindow && !_wasInBurst)
            _currentWindowStart = DateTime.UtcNow;
        else if (!_isInBurstWindow && _wasInBurst)
            _burstWindowHistory.Add((_currentWindowStart, DateTime.UtcNow));
        _wasInBurst = _isInBurstWindow;
    }

    public void ResetHistory()
    {
        _burstWindowHistory.Clear();
        _wasInBurst = false;
        _combatStartUtc = null;
        _lastCoordinatedBurstSignalUtc = null;
        _lastBurstWindowEnd = null;
    }

    #endregion

    #region Private Helpers

    private void UpdateCombatAndFallbackState(bool inCombat)
    {
        if (!inCombat)
        {
            _combatStartUtc = null;
            _lastCoordinatedBurstSignalUtc = null;
            return;
        }

        _combatStartUtc ??= DateTime.UtcNow;
        RefreshCoordinatedBurstSignalTimestamp();
    }

    /// <summary>
    /// Shared combat elapsed for solo-burst fallback. Uses CombatEventService when the
    /// local timer is unset so rotation reads match Plugin combat duration before the
    /// first <see cref="Update"/> in a pull.
    /// </summary>
    private float GetCombatElapsedSeconds()
    {
        var serviceCombatDuration = _combatEventService?.GetCombatDurationSeconds() ?? 0f;
        if (!_combatStartUtc.HasValue)
            return serviceCombatDuration;

        var localCombatElapsed = (float)(DateTime.UtcNow - _combatStartUtc.Value).TotalSeconds;
        return Math.Max(serviceCombatDuration, localCombatElapsed);
    }

    private bool ComputeUseSoloBurstFallback()
    {
        var combatElapsed = GetCombatElapsedSeconds();
        if (combatElapsed < SoloBurstFallbackCombatSeconds)
            return false;

        if (_combatStartUtc.HasValue || (_combatEventService?.IsInCombat ?? false))
            RefreshCoordinatedBurstSignalTimestamp();

        return !IsPartyBurstCoordinationActive();
    }

    /// <summary>
    /// True while another Daedalus DPS is actively coordinating burst via IPC
    /// (or we saw a coordinated signal within the fallback grace window).
    /// </summary>
    private bool IsPartyBurstCoordinationActive()
    {
        var coord = _partyCoordinationService;
        if (coord is not { IsPartyCoordinationEnabled: true })
            return false;

        if (!coord.HasRemoteDps)
            return false;

        if (coord.HasPendingRaidBuffIntent(SoloBurstFallbackCombatSeconds))
            return true;

        if (coord.IsInBurstWindow())
            return true;

        var state = coord.GetBurstWindowState();
        if (state is { HasBurstInfo: true, IsActive: true })
            return true;

        if (state is { HasBurstInfo: true, IsImminent: true })
            return true;

        return _lastCoordinatedBurstSignalUtc.HasValue
               && (DateTime.UtcNow - _lastCoordinatedBurstSignalUtc.Value).TotalSeconds
               < SoloBurstFallbackCombatSeconds;
    }

    private void RefreshCoordinatedBurstSignalTimestamp()
    {
        var coord = _partyCoordinationService;
        if (coord is not { IsPartyCoordinationEnabled: true, HasRemoteDps: true })
            return;

        if (coord.HasPendingRaidBuffIntent(SoloBurstFallbackCombatSeconds)
            || coord.IsInBurstWindow())
        {
            _lastCoordinatedBurstSignalUtc = DateTime.UtcNow;
            return;
        }

        var state = coord.GetBurstWindowState();
        if (state is { HasBurstInfo: true, IsActive: true }
            or { HasBurstInfo: true, IsImminent: true })
            _lastCoordinatedBurstSignalUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Estimates seconds until the next burst using the ~100s cycle gap from the last known end.
    /// Returns -1 if no timing data is available.
    /// </summary>
    private float TimerBasedSecondsUntilBurst
    {
        get
        {
            if (!_lastBurstWindowEnd.HasValue)
                return -1f;

            var elapsed = (float)(DateTime.UtcNow - _lastBurstWindowEnd.Value).TotalSeconds;
            var remaining = BurstCycleGapSeconds - elapsed;
            return remaining >= 0f ? remaining : -1f;
        }
    }

    #endregion
}
