using System;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;
using Dalamud.Game.ClientState.Objects;
using Daedalus.Services.Targeting;

namespace Daedalus.Ipc;

/// <summary>
/// Questionable kill-step bridge. When Questionable has no combat module configured it never
/// targets the kill mobs itself — its CombatController only engages through a module (RSR/Wrath/
/// BossMod), and without one the quest just waits at the Combat step. This bridge reads
/// Questionable's own read-side IPC (<c>Questionable.IsRunning</c> +
/// <c>Questionable.GetCurrentStepData</c>): while a Combat step is active it holds the
/// external-combat override AND acquires kill targets Henchman-style — enemies already fighting
/// us first, then the nearest enemy the game has flagged with a quest nameplate icon; the
/// automation engage opens on it, and when it dies the next poll targets the next mob until the
/// step advances.
/// </summary>
/// <remarks>
/// Questionable's step data does not expose the quest's kill-target data ids, but the game itself
/// marks objective mobs with a nameplate icon (the gold quest marker) — that is the targeting
/// filter, so unrelated ambient mobs are never pulled. If the user instead configures
/// Questionable's combat module to "Rotation Solver Reborn", Questionable does its own (exact)
/// targeting through <see cref="RsrCompatIpc"/> and this bridge simply re-asserts the same
/// override alongside it. Fail-open: Questionable missing or IPC errors read as no combat step.
/// </remarks>
public sealed class QuestionableIpc : IDisposable
{
    private const string PluginInternalName = "Questionable";
    private const float KillTargetScanRangeYalms = 30f;
    // Wider than the kill scan so a chasing mob that lags behind during navigation doesn't
    // flap the cleanup state on and off at the range boundary.
    private const float CleanupScanRangeYalms = 40f;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly Configuration _configuration;
    private readonly IPluginLog _log;
    private readonly ITargetManager _targetManager;
    private readonly ITargetingService _targetingService;
    private readonly IObjectTable _objectTable;
    private readonly AutomationOverrideTracker _tracker = new();
    private readonly FreshPullSettleGate _pullGate = new();
    private readonly Stopwatch _pollClock = Stopwatch.StartNew();

    private ICallGateSubscriber<bool>? _isRunning;
    private ICallGateSubscriber<StepData?>? _getStepData;
    private bool? _lastLoggedActive;
    private bool _questionableRunning;
    private string _currentQuestId = "";

    /// <summary>Quest id of the active Questionable combat step ("" when none) — for status display.</summary>
    public string CurrentQuestId => _currentQuestId;

    /// <summary>Fired when this bridge flips the external-combat override.</summary>
    public event Action<bool>? OverrideChanged;

    public QuestionableIpc(
        IDalamudPluginInterface pluginInterface,
        Configuration configuration,
        IPluginLog log,
        ITargetManager targetManager,
        ITargetingService targetingService,
        IObjectTable objectTable)
    {
        _pluginInterface = pluginInterface;
        _configuration = configuration;
        _log = log;
        _targetManager = targetManager;
        _targetingService = targetingService;
        _objectTable = objectTable;
    }

    /// <summary>Framework-thread poll; internally throttled to once per second.</summary>
    public void Update()
    {
        if (_pollClock.Elapsed < PollInterval)
            return;
        _pollClock.Restart();

        var combatStep = ReadCombatStepActive();

        // Aggro cleanup (Henchman "HandleHaters" parity): while Questionable is running, anything
        // engaged on us gets killed before we release — leftovers after a kill objective completes,
        // and mobs aggroed walking through camps between steps. Without this the override dropped
        // the instant the step advanced and the toon ran on with a train chewing on it.
        var cleanup = !combatStep && IsAggroCleanupNeeded();

        // Flagged-mob hunt: kill-objective mobs (gold quest icon) don't always line up with an
        // InteractionType == "Combat" step — Questionable can sit at "Step completed"/Wait with
        // 4/8 slugs still flagged around the player. While Questionable is running, any flagged
        // mob in range is a standing kill order: take them one at a time until no icons remain.
        var flaggedHunt = !combatStep && !cleanup && HasQuestFlaggedMobNearby();
        var active = combatStep || cleanup || flaggedHunt;

        if (active != _lastLoggedActive)
        {
            _log.Debug("[AutomationBridge:Questionable] active={0} (combatStep={1}, cleanup={2}, flaggedHunt={3}, quest {4})",
                active, combatStep, cleanup, flaggedHunt, _currentQuestId);
            _lastLoggedActive = active;
        }

        switch (_tracker.Observe(active))
        {
            case AutomationOverrideTracker.OverrideAction.Assert:
                if (!_configuration.ExternalCombatOverride)
                {
                    _configuration.ExternalCombatOverride = true;
                    ExternalCombatOverrideState.Source = "Questionable";
                    _log.Info("Questionable {0} — external-combat override on.",
                        combatStep ? "combat step" : cleanup ? "aggro cleanup" : "flagged objective mobs");
                    OverrideChanged?.Invoke(true);
                }
                break;

            case AutomationOverrideTracker.OverrideAction.Clear:
                if (_configuration.ExternalCombatOverride)
                {
                    _configuration.ExternalCombatOverride = false;
                    ExternalCombatOverrideState.Source = "";
                    _log.Info("Questionable combat done — external-combat override off.");
                    OverrideChanged?.Invoke(false);
                }
                break;
        }

        if (active)
            TryAcquireKillTarget(allowQuestFlagged: combatStep || flaggedHunt);
    }

    /// <summary>True when a quest-flagged (gold icon) attackable mob is within kill range.</summary>
    private bool HasQuestFlaggedMobNearby()
    {
        if (!_questionableRunning)
            return false;

        var player = _objectTable.LocalPlayer;
        if (player == null || player.CurrentHp == 0)
            return false;

        return _targetingService.FindNearestQuestFlaggedEnemy(KillTargetScanRangeYalms, player) != null;
    }

    /// <summary>
    /// True while Questionable is running and hostiles are engaged on us — the rotation must
    /// clean up the pull before Questionable's navigation drags a train across the map.
    /// </summary>
    private bool IsAggroCleanupNeeded()
    {
        if (!_questionableRunning)
            return false;

        var player = _objectTable.LocalPlayer;
        if (player == null || player.CurrentHp == 0)
            return false;

        if ((player.StatusFlags & Dalamud.Game.ClientState.Objects.Enums.StatusFlags.InCombat) == 0)
            return false;

        return _targetingService.FindNearestAggroedEnemy(CleanupScanRangeYalms, player) != null;
    }

    /// <summary>
    /// Henchman-style targeting: if the player has no live enemy target while this bridge is
    /// driving, hard-target the next kill candidate so the rotation opens on it. Priority:
    /// nearest enemy aggroed on us (finish the pull — never someone else's mobs, and always
    /// immediately); then — only during an actual Combat step — enemies the game has flagged
    /// with a quest nameplate icon, the authoritative "counts for the objective" marker. Fresh
    /// flagged pulls go one deliberate mob at a time: they wait for the post-combat settle gate,
    /// so the pull cadence is kill → clear everything aggroed → brief pause → next pull, never a
    /// chain-pull the same second the last mob dies. Idle unflagged mobs are never pulled (a
    /// nearest-anything fallback aggroed whole camps).
    /// </summary>
    private void TryAcquireKillTarget(bool allowQuestFlagged)
    {
        var player = _objectTable.LocalPlayer;
        if (player == null || player.CurrentHp == 0)
            return;

        // Feed the settle gate every poll: any in-combat frame counts as combat contact, so the
        // "brief pause before the next fresh pull" clock only starts once the pull is fully over.
        if ((player.StatusFlags & Dalamud.Game.ClientState.Objects.Enums.StatusFlags.InCombat) != 0)
            _pullGate.ReportCombatContact();

        if (_targetingService.GetUserEnemyTarget() != null)
            return;

        var candidate = _targetingService.FindNearestAggroedEnemy(CleanupScanRangeYalms, player);
        if (candidate != null)
        {
            // Finishing an existing pull is combat contact too (covers the gap frames where the
            // mob is aggroed but the InCombat flag hasn't caught up).
            _pullGate.ReportCombatContact();
        }
        else if (allowQuestFlagged && _pullGate.CanFreshPull())
        {
            candidate = _targetingService.FindNearestQuestFlaggedEnemy(KillTargetScanRangeYalms, player);
        }

        if (candidate == null)
            return;

        _targetManager.Target = candidate;
        _log.Debug("[AutomationBridge:Questionable] targeted kill candidate {0} (NameId {1})",
            candidate.Name, candidate.NameId);
    }

    private bool ReadCombatStepActive()
    {
        _currentQuestId = "";
        _questionableRunning = false;

        if (!IsQuestionableLoaded())
            return false;

        try
        {
            _isRunning ??= _pluginInterface.GetIpcSubscriber<bool>("Questionable.IsRunning");
            if (!_isRunning.InvokeFunc())
                return false;
            _questionableRunning = true;

            _getStepData ??= _pluginInterface.GetIpcSubscriber<StepData?>("Questionable.GetCurrentStepData");
            var step = _getStepData.InvokeFunc();
            var combat = string.Equals(step?.InteractionType, "Combat", StringComparison.OrdinalIgnoreCase);
            _currentQuestId = combat ? step!.QuestId : "";
            return combat;
        }
        catch (Exception ex)
        {
            if (_lastLoggedActive != false)
                _log.Debug("[AutomationBridge:Questionable] IPC unavailable ({0}) — reading idle.", ex.GetType().Name);
            return false;
        }
    }

    private bool IsQuestionableLoaded()
    {
        try
        {
            return _pluginInterface.InstalledPlugins.Any(p =>
                p.InternalName.Equals(PluginInternalName, StringComparison.OrdinalIgnoreCase) && p.IsLoaded);
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        // Only release the override if this bridge is the one currently holding it.
        if (_tracker.Observe(false) == AutomationOverrideTracker.OverrideAction.Clear)
        {
            _configuration.ExternalCombatOverride = false;
            ExternalCombatOverrideState.Source = "";
        }
    }

    /// <summary>
    /// Pull cadence for automation kill loops: a FRESH (not-yet-aggroed) objective pull is allowed
    /// only after a brief calm window with no combat contact. Aggroed mobs are exempt — anything
    /// already on us is finished immediately, whatever the clock says. This keeps quest/hunt
    /// farming to one deliberate pull at a time: kill, clear the leftovers, breathe, pull the
    /// next — instead of hard-targeting the next flagged mob the same second the last one dies
    /// (which snowballed camps when flagged mobs stood close together).
    /// </summary>
    public sealed class FreshPullSettleGate
    {
        /// <summary>Calm seconds required after the last combat contact before a fresh pull.</summary>
        public const float SettleSeconds = 3f;

        /// <summary>Test seam.</summary>
        internal Func<DateTime> UtcNow = () => DateTime.UtcNow;

        private DateTime _lastCombatContactUtc = DateTime.MinValue;

        /// <summary>Report combat contact (in-combat frame, or an aggroed mob being finished).</summary>
        public void ReportCombatContact() => _lastCombatContactUtc = UtcNow();

        /// <summary>True once the calm window has fully elapsed since the last combat contact.</summary>
        public bool CanFreshPull() => (UtcNow() - _lastCombatContactUtc).TotalSeconds >= SettleSeconds;
    }

    /// <summary>
    /// Shape-matched copy of Questionable's IPC StepData (Questionable/External/QuestionableIpc.cs).
    /// Dalamud converts mismatched IPC types by serialization, so field names/types must match.
    /// </summary>
    public sealed class StepData
    {
        public string QuestId { get; set; } = "";
        public byte Sequence { get; set; }
        public int Step { get; set; }
        public string InteractionType { get; set; } = "";
        public Vector3? Position { get; set; }
        public uint TerritoryId { get; set; }
    }
}
