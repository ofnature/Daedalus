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
/// external-combat override AND acquires kill targets Henchman-style — nearest attackable enemy
/// (engaged first, then idle mobs) becomes the hard target, the automation engage opens on it,
/// and when it dies the next poll targets the next mob until the step advances.
/// </summary>
/// <remarks>
/// Questionable's step data does not expose the quest's kill-target data ids, so target selection
/// is a nearest-hostile heuristic — Questionable navigates the player into the objective area, so
/// the surrounding mobs are the quest mobs. If the user instead configures Questionable's combat
/// module to "Rotation Solver Reborn", Questionable does its own (exact) targeting through
/// <see cref="RsrCompatIpc"/> and this bridge simply re-asserts the same override alongside it.
/// Fail-open: Questionable missing or IPC errors read as no combat step.
/// </remarks>
public sealed class QuestionableIpc : IDisposable
{
    private const string PluginInternalName = "Questionable";
    private const float KillTargetScanRangeYalms = 30f;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly Configuration _configuration;
    private readonly IPluginLog _log;
    private readonly ITargetManager _targetManager;
    private readonly ITargetingService _targetingService;
    private readonly IObjectTable _objectTable;
    private readonly AutomationOverrideTracker _tracker = new();
    private readonly Stopwatch _pollClock = Stopwatch.StartNew();

    private ICallGateSubscriber<bool>? _isRunning;
    private ICallGateSubscriber<StepData?>? _getStepData;
    private bool? _lastLoggedCombatStep;

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
        if (combatStep != _lastLoggedCombatStep)
        {
            _log.Debug("[AutomationBridge:Questionable] combat step active={0}", combatStep);
            _lastLoggedCombatStep = combatStep;
        }

        switch (_tracker.Observe(combatStep))
        {
            case AutomationOverrideTracker.OverrideAction.Assert:
                if (!_configuration.ExternalCombatOverride)
                {
                    _configuration.ExternalCombatOverride = true;
                    ExternalCombatOverrideState.Source = "Questionable";
                    _log.Info("Questionable combat step — external-combat override on.");
                    OverrideChanged?.Invoke(true);
                }
                break;

            case AutomationOverrideTracker.OverrideAction.Clear:
                if (_configuration.ExternalCombatOverride)
                {
                    _configuration.ExternalCombatOverride = false;
                    ExternalCombatOverrideState.Source = "";
                    _log.Info("Questionable combat step done — external-combat override off.");
                    OverrideChanged?.Invoke(false);
                }
                break;
        }

        if (combatStep)
            TryAcquireKillTarget();
    }

    /// <summary>
    /// Henchman-style targeting: if the player has no live enemy target during a Questionable
    /// combat step, hard-target the nearest attackable enemy so the automation engage opens on it.
    /// Engaged enemies (something already attacking us) win over idle quest mobs.
    /// </summary>
    private void TryAcquireKillTarget()
    {
        var player = _objectTable.LocalPlayer;
        if (player == null || player.CurrentHp == 0)
            return;

        if (_targetingService.GetUserEnemyTarget() != null)
            return;

        var candidate = _targetingService.FindNearbyEnemy(KillTargetScanRangeYalms, player)
            ?? _targetingService.FindNearestTaggableEnemy(KillTargetScanRangeYalms, player);
        if (candidate == null)
            return;

        _targetManager.Target = candidate;
        _log.Debug("[AutomationBridge:Questionable] targeted kill candidate {0} (NameId {1})",
            candidate.Name, candidate.NameId);
    }

    private bool ReadCombatStepActive()
    {
        if (!IsQuestionableLoaded())
            return false;

        try
        {
            _isRunning ??= _pluginInterface.GetIpcSubscriber<bool>("Questionable.IsRunning");
            if (!_isRunning.InvokeFunc())
                return false;

            _getStepData ??= _pluginInterface.GetIpcSubscriber<StepData?>("Questionable.GetCurrentStepData");
            var step = _getStepData.InvokeFunc();
            return string.Equals(step?.InteractionType, "Combat", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            if (_lastLoggedCombatStep != false)
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
