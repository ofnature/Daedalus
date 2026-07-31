using System.Collections.Generic;
using System.Globalization;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;

namespace Daedalus.Services.Positional.Navigation;

/// <summary>
/// Auto-manages BossMod Reborn's AI movement via a first-class BMR autorotation preset named
/// "Daedalus" (same mechanism AutoDuty uses): on enable it creates/refreshes the preset (movement
/// modules only — Daedalus fights) and activates it; the live per-GCD positional is fed through a
/// transient strategy on the GoToPositional module. You still enable BMR AI yourself (<c>/bmrai</c>).
///
/// On DISABLE the only action is clearing our own preset if it is still the active one — no raw
/// AIConfig writes ever happen at the off transition (field report 2026-07-26: the old
/// movement-only restore flipped <c>ForbidActions</c> at untick, which external preset managers
/// reacted to; the tick box must do NOTHING while off). A one-time legacy AIConfig cleanup runs at
/// ENABLE to unwind the old mode's ForbidActions/ManualTarget/DesiredPositional from earlier
/// versions. All IPC is fail-open; results surface in <see cref="LastPushResult"/>.
/// </summary>
public sealed class BmrAiConfigService
{
    private const double MinPushIntervalSeconds = 0.25;

    private readonly IDalamudPluginInterface _pi;
    private readonly IBossModSafetyService _bmr;
    private readonly IPluginLog? _log;
    private readonly Daedalus.Services.Debug.DebugLogService? _debugLog;
    private readonly Dalamud.Plugin.Services.IDtrBar? _dtrBar;

    private ICallGateSubscriber<List<string>, bool, List<string>>? _configIpc;
    private ICallGateSubscriber<bool, object>? _pauseMovementIpc;
    private ICallGateSubscriber<string, bool, bool>? _presetCreateIpc;
    private ICallGateSubscriber<string, bool>? _presetSetActiveIpc;
    private ICallGateSubscriber<string>? _presetGetActiveIpc;
    private ICallGateSubscriber<bool>? _presetClearActiveIpc;
    private ICallGateSubscriber<string, string, string, string, bool>? _presetAddTransientIpc;

    private string? _appliedPresetJson;
    private string? _lastPositional;
    private System.DateTime _lastPushUtc = System.DateTime.MinValue;
    private bool _legacyConfigCleaned;
    private bool _aiPresetNameApplied;
    private bool _wasEnabled;

    // Contested-slot backoff: another manager (field case: ADS's "passive - melee") re-taking
    // the preset slot after every reclaim is a 4Hz ping-pong that helps nobody. After
    // ContestedRetakeLimit foreign retakes we yield, surface who holds the slot, and stop
    // touching it until the user re-toggles the feature.
    private const int ContestedRetakeLimit = 3;
    private int _foreignRetakes;
    private bool _contested;

    /// <summary>Non-empty while we've yielded the preset slot to a re-asserting foreign manager.</summary>
    public string ContestedBy { get; private set; } = "";

    public BmrAiConfigService(IDalamudPluginInterface pi, IBossModSafetyService bmr, IPluginLog? log = null,
        Daedalus.Services.Debug.DebugLogService? debugLog = null,
        Dalamud.Plugin.Services.IDtrBar? dtrBar = null)
    {
        _pi = pi;
        _bmr = bmr;
        _log = log;
        _debugLog = debugLog;
        _dtrBar = dtrBar;
    }

    // ── AI mode (on/off) tracking ─────────────────────────────────────────────────────────────────────
    // BMR exposes NO IPC for "is AI mode enabled" (AI.GetPreset is the preset name only — "" both when
    // AI is off AND when it runs preset-less). The one place BMR publishes the real state is its server
    // info bar entry "bmr-ai" (DTRProvider: Text = "AI: On"/"AI: Off" from Beh != null), which Dalamud
    // lets other plugins read. Only populated while BMR's AI "Show DTR" toggle is on — hidden/absent
    // reads as Unknown, never as Off.

    public enum BmrAiMode { Unknown, On, Off }

    /// <summary>Whether BMR AI mode (/bmrai) is actually running, read from BMR's own status-bar entry.</summary>
    public BmrAiMode AiMode()
    {
        if (!_bmr.IsAvailable || _dtrBar == null)
            return BmrAiMode.Unknown;
        try
        {
            foreach (var entry in _dtrBar.Entries)
            {
                if (entry.Title != "bmr-ai")
                    continue;
                return ParseAiDtr(entry.Shown, entry.Text?.TextValue);
            }
        }
        catch (System.Exception ex)
        {
            _log?.Debug(ex, "[BmrAiConfigService] DTR read failed");
        }
        return BmrAiMode.Unknown;
    }

    /// <summary>Pure text→state mapping (tested): a hidden entry means "can't know", never "off".</summary>
    internal static BmrAiMode ParseAiDtr(bool shown, string? text)
    {
        if (!shown || string.IsNullOrEmpty(text))
            return BmrAiMode.Unknown;
        if (text.EndsWith("On", System.StringComparison.Ordinal)) return BmrAiMode.On;
        if (text.EndsWith("Off", System.StringComparison.Ordinal)) return BmrAiMode.Off;
        return BmrAiMode.Unknown;
    }

    public readonly record struct Request(
        bool Enabled,
        uint JobId,
        PositionalType? RequiredPositional,
        float RangedStandDistance,
        bool BoundaryCampingActive = false,
        /// <summary>BMR reports live forbidden zones — a positional goal would drag its
        /// pathfinder toward boss-centered AoEs; feed "Any" until the danger clears.</summary>
        bool ForbiddenZonesLive = false);

    // ── UI status (read by the Nav Control panel) ─────────────────────────────────────────────────────
    /// <summary>BossMod Reborn is installed and loaded.</summary>
    public bool BmrAvailable => _bmr.IsAvailable;
    /// <summary>Last result line from the BMR IPC (config push or preset op).</summary>
    public string LastPushResult { get; private set; } = "";

    public void Update(in Request req)
    {
        if (!req.Enabled)
        {
            if (_wasEnabled)
                RestoreAndReset();
            return;
        }

        if (!_bmr.IsAvailable)
        {
            LastPushResult = "BossMod Reborn not loaded";
            return;
        }

        EnsureSubscribers();
        _wasEnabled = true;

        // One-time migration off the old raw-AIConfig mode: earlier versions left
        // ForbidActions/ManualTarget/DesiredPositional set. Runs at ENABLE (never at
        // disable — the off transition must be side-effect free).
        if (!_legacyConfigCleaned)
        {
            PushConfig("ForbidActions", "false");
            PushConfig("ManualTarget", "false");
            PushConfig("DesiredPositional", "Any");
            _legacyConfigCleaned = true;
        }

        // BMR's AI enforces AIAutorotPresetName on EVERY AI engage (SwitchToFollow looks the
        // preset up by this persisted name — the mechanism behind the "locked to passive -
        // melee" saga: a leftover /bmrai setpresetname from an orchestrator re-applied that
        // preset with zero plugins running). Claim it for the Daedalus preset once per
        // enable session so BMR itself keeps our preset installed. Never touched at disable.
        if (!_aiPresetNameApplied)
        {
            PushConfig("AIAutorotPresetName", BmrAiConfigPolicy.PresetName);
            _aiPresetNameApplied = true;
        }

        // Rate cap: nothing changes value faster than a GCD, so a sub-0.25s change means oscillation — skip
        // this frame (the still-changed value pushes on the next eligible frame).
        var now = System.DateTime.UtcNow;
        if ((now - _lastPushUtc).TotalSeconds < MinPushIntervalSeconds)
            return;

        var pushed = false;

        // Create/refresh + activate the "Daedalus" preset whenever the role-shaped JSON changes
        // (job role swap, ranged-distance slider) or another manager replaced the active preset.
        var json = BmrAiConfigPolicy.BuildPresetJson(
            BmrAiConfigPolicy.IsBacklineJob(req.JobId), req.RangedStandDistance);
        if (_appliedPresetJson != json)
        {
            if (CreatePreset(json) && ActivatePreset())
            {
                _appliedPresetJson = json;
                _lastPositional = null; // fresh preset: transient strategies were reset
            }
            pushed = true;
        }
        else if (ActivePresetName() is var active && active != BmrAiConfigPolicy.PresetName)
        {
            // Someone else grabbed the slot (AutoDuty run start etc.) — take it back only
            // while the user has auto-manage ON. If a manager keeps re-taking it, yield
            // instead of ping-ponging and tell the user who holds the slot.
            if (_contested)
                return;

            // Empty active = the slot was CLEARED (zone change / BMR reload), not taken —
            // reclaim freely; only a named foreign preset counts toward the yield.
            if (BmrAiConfigPolicy.CountsAsForeignOwner(active) && ++_foreignRetakes >= ContestedRetakeLimit)
            {
                _contested = true;
                ContestedBy = active;
                LastPushResult = $"preset slot contested by \"{active}\" — yielded";
                _debugLog?.Log(Daedalus.Services.Debug.DebugLogCategory.Nav,
                    Daedalus.Services.Debug.DebugLogSeverity.Warning,
                    $"BMR preset slot contested by \"{active}\" after {_foreignRetakes} retakes — yielding until Auto-Manage is re-toggled");
                return;
            }

            if (ActivatePreset())
                _lastPositional = null;
            pushed = true;
        }

        // Live per-GCD positional via a transient strategy on the GoToPositional module
        // (raw AIConfig positional is ignored while any preset is active). Held while the user
        // is manually clicking targets — a fresh transient pulse against their pick makes BMR
        // micro-steer under their hands (the stutter-step report, 2026-07-30).
        var positional = Daedalus.Services.Targeting.ManualControlGrace.IsActive
            ? "Any"
            : BmrAiConfigPolicy.ResolveDesiredPositional(
                req.JobId, req.RequiredPositional, req.BoundaryCampingActive, req.ForbiddenZonesLive);
        if (!BmrAiConfigPolicy.IsBacklineJob(req.JobId) && _lastPositional != positional)
        {
            if (SetTransientPositional(positional))
                _lastPositional = positional;
            pushed = true;
        }

        if (pushed)
            _lastPushUtc = now;
    }

    /// <summary>
    /// On disable: clear OUR preset if it is still the active one — nothing else. The off
    /// transition must have no observable side effects beyond releasing our own slot.
    /// </summary>
    private void RestoreAndReset()
    {
        if (_bmr.IsAvailable && ActivePresetName() == BmrAiConfigPolicy.PresetName)
        {
            try
            {
                _presetClearActiveIpc?.InvokeFunc();
                LastPushResult = "preset released";
            }
            catch (System.Exception ex)
            {
                _log?.Debug(ex, "[BmrAiConfigService] Presets.ClearActive failed");
            }
        }
        _appliedPresetJson = null;
        _lastPositional = null;
        _lastPushUtc = System.DateTime.MinValue;
        _wasEnabled = false;
        _foreignRetakes = 0;
        _contested = false;
        ContestedBy = "";
        _aiPresetNameApplied = false; // flag only — the config write happens at next ENABLE
    }

    private bool CreatePreset(string json)
    {
        try
        {
            var ok = _presetCreateIpc?.InvokeFunc(json, true) ?? false; // overwrite: true
            LastPushResult = ok ? "preset created" : "Presets.Create rejected the Daedalus preset";
            if (!ok)
                _debugLog?.Log(Daedalus.Services.Debug.DebugLogCategory.Nav,
                    Daedalus.Services.Debug.DebugLogSeverity.Warning,
                    "BMR rejected the Daedalus preset JSON");
            return ok;
        }
        catch (System.Exception ex)
        {
            LastPushResult = $"Presets.Create threw ({ex.Message})";
            _log?.Debug(ex, "[BmrAiConfigService] Presets.Create failed");
            return false;
        }
    }

    private bool ActivatePreset()
    {
        try
        {
            var ok = _presetSetActiveIpc?.InvokeFunc(BmrAiConfigPolicy.PresetName) ?? false;
            if (ok)
                LastPushResult = "preset active";
            return ok;
        }
        catch (System.Exception ex)
        {
            LastPushResult = $"Presets.SetActive threw ({ex.Message})";
            _log?.Debug(ex, "[BmrAiConfigService] Presets.SetActive failed");
            return false;
        }
    }

    /// <summary>Active BMR autorotation preset name ("" when none). Shown in Nav Control.</summary>
    public string ActivePresetName()
    {
        if (!_bmr.IsAvailable)
            return "";
        EnsureSubscribers();
        try { return _presetGetActiveIpc?.InvokeFunc() ?? ""; }
        catch { return ""; }
    }

    private bool SetTransientPositional(string positional)
    {
        try
        {
            return _presetAddTransientIpc?.InvokeFunc(
                BmrAiConfigPolicy.PresetName,
                BmrAiConfigPolicy.GoToPositionalModule,
                "Positional",
                positional) ?? false;
        }
        catch (System.Exception ex)
        {
            _log?.Debug(ex, "[BmrAiConfigService] Presets.AddTransientStrategy failed");
            return false;
        }
    }

    private void PushConfig(string field, string value)
    {
        try
        {
            var result = _configIpc?.InvokeFunc(new List<string> { "AIConfig", field, value }, false);
            // BMR returns lines on error (config/field not found, conversion failure); empty list = success.
            if (result is { Count: > 0 })
            {
                LastPushResult = $"{field}={value}: {string.Join("; ", result)}";
                _debugLog?.Log(Daedalus.Services.Debug.DebugLogCategory.Nav,
                    Daedalus.Services.Debug.DebugLogSeverity.Warning,
                    $"BMR config push rejected: {field}={value} — {string.Join("; ", result)}");
            }
            else
            {
                LastPushResult = $"{field}={value}: ok";
            }
        }
        catch (System.Exception ex)
        {
            LastPushResult = $"{field}={value}: IPC threw ({ex.Message})";
            _log?.Debug(ex, "[BmrAiConfigService] Failed to push AIConfig.{Field}={Value}", field, value);
            _debugLog?.Log(Daedalus.Services.Debug.DebugLogCategory.Nav,
                Daedalus.Services.Debug.DebugLogSeverity.Error,
                $"BMR config push failed: {field}={value} — IPC threw ({ex.Message})");
        }
    }

    private void EnsureSubscribers()
    {
        _configIpc ??= _pi.GetIpcSubscriber<List<string>, bool, List<string>>("BossMod.Configuration");
        _pauseMovementIpc ??= _pi.GetIpcSubscriber<bool, object>("BossMod.AI.PauseMovement");
        _presetCreateIpc ??= _pi.GetIpcSubscriber<string, bool, bool>("BossMod.Presets.Create");
        _presetSetActiveIpc ??= _pi.GetIpcSubscriber<string, bool>("BossMod.Presets.SetActive");
        _presetGetActiveIpc ??= _pi.GetIpcSubscriber<string>("BossMod.Presets.GetActive");
        _presetClearActiveIpc ??= _pi.GetIpcSubscriber<bool>("BossMod.Presets.ClearActive");
        _presetAddTransientIpc ??= _pi.GetIpcSubscriber<string, string, string, string, bool>("BossMod.Presets.AddTransientStrategy");
    }

    /// <summary>
    /// Pause/resume BMR AI movement (AIConfig.ForbidMovement via BossMod.AI.PauseMovement) —
    /// the hardcast-raise hold: BMR's constant micro-follow otherwise keeps isMoving true and
    /// an 8-10s raise cast can never start. Edge-toggled by the Plugin pump off
    /// <see cref="Daedalus.Services.Positional.RaiseCastHold"/>; fail-open when BMR is absent.
    /// </summary>
    public void SetAiMovementPaused(bool paused)
    {
        if (!_bmr.IsAvailable)
            return;
        EnsureSubscribers();
        try
        {
            _pauseMovementIpc?.InvokeAction(paused);
            _debugLog?.Log(Daedalus.Services.Debug.DebugLogCategory.Nav,
                Daedalus.Services.Debug.DebugLogSeverity.Info,
                paused ? "BMR AI movement PAUSED (hardcast raise hold)" : "BMR AI movement resumed");
        }
        catch (System.Exception ex)
        {
            _log?.Debug(ex, "[BmrAiConfigService] AI.PauseMovement failed");
        }
    }
}
