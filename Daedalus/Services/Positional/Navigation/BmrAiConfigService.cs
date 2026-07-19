using System.Collections.Generic;
using System.Globalization;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;

namespace Daedalus.Services.Positional.Navigation;

/// <summary>
/// Auto-manages BossMod Reborn's AI movement config by role — for group content, where AutoDuty (which
/// only runs in Trust) isn't there to do it. When enabled, it pushes role-based <c>MaxDistanceToTarget</c>
/// and a live <c>DesiredPositional</c> into BMR via the <c>BossMod.Configuration</c> IPC, and puts BMR in
/// movement-only mode (<c>ForbidActions</c>/<c>ManualTarget</c>) so BMR positions while Daedalus keeps the
/// rotation + targeting. You still enable BMR AI yourself (<c>/bmrai</c>).
///
/// It does NOT touch your AI preset — but BMR's distance/positional movement only applies when NO preset
/// is loaded, so when one is active the UI warns you to clear it. Pushes are transient (save=false), only
/// sent on change, rate-capped, and fail-open. The <c>Configuration</c> IPC returns status/error strings,
/// surfaced in <see cref="LastPushResult"/> for the Nav Control panel.
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
    private ICallGateSubscriber<string>? _getPresetIpc;
    private ICallGateSubscriber<bool, object>? _pauseMovementIpc;

    private float? _lastDistance;
    private string? _lastPositional;
    private System.DateTime _lastPushUtc = System.DateTime.MinValue;
    private bool _movementOnlyApplied;
    private bool _wasEnabled;

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
        bool BoundaryCampingActive = false);

    // ── UI status (read by the Nav Control panel) ─────────────────────────────────────────────────────
    /// <summary>BossMod Reborn is installed and loaded.</summary>
    public bool BmrAvailable => _bmr.IsAvailable;
    /// <summary>Last result line returned by the BossMod.Configuration IPC (empty / "ok" / an error).</summary>
    public string LastPushResult { get; private set; } = "";
    /// <summary>The AI preset currently loaded in BMR ("" = none). A loaded preset blocks our movement config.</summary>
    public string CurrentAiPreset()
    {
        if (!_bmr.IsAvailable)
            return "";
        EnsureSubscribers();
        try { return _getPresetIpc?.InvokeFunc() ?? ""; }
        catch (System.Exception ex) { _log?.Debug(ex, "[BmrAiConfigService] AI.GetPreset failed"); return ""; }
    }

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

        // Movement-only, once per enable session: BMR positions, Daedalus fights + targets. We do NOT clear
        // the AI preset — that proved surprising; the UI warns instead when a preset is blocking us.
        if (!_movementOnlyApplied)
        {
            PushConfig("ForbidActions", "true");
            PushConfig("ManualTarget", "true");
            PushConfig("FollowTarget", "true");
            _movementOnlyApplied = true;
        }

        // Rate cap: nothing changes value faster than a GCD, so a sub-0.25s change means oscillation — skip
        // this frame (the still-changed value pushes on the next eligible frame).
        var now = System.DateTime.UtcNow;
        if ((now - _lastPushUtc).TotalSeconds < MinPushIntervalSeconds)
            return;

        var pushed = false;

        var distance = BmrAiConfigPolicy.ResolveMaxDistance(req.JobId, req.RangedStandDistance);
        if (_lastDistance != distance)
        {
            PushConfig("MaxDistanceToTarget", distance.ToString("0.0", CultureInfo.InvariantCulture));
            _lastDistance = distance;
            pushed = true;
        }

        var positional = BmrAiConfigPolicy.ResolveDesiredPositional(req.JobId, req.RequiredPositional, req.BoundaryCampingActive);
        if (_lastPositional != positional)
        {
            PushConfig("DesiredPositional", positional);
            _lastPositional = positional;
            pushed = true;
        }

        if (pushed)
            _lastPushUtc = now;
    }

    /// <summary>On disable, hand control back to BMR (drop movement-only) and clear the throttle cache.</summary>
    private void RestoreAndReset()
    {
        if (_bmr.IsAvailable && _movementOnlyApplied)
        {
            PushConfig("ForbidActions", "false");
            PushConfig("ManualTarget", "false");
        }
        _lastDistance = null;
        _lastPositional = null;
        _lastPushUtc = System.DateTime.MinValue;
        _movementOnlyApplied = false;
        _wasEnabled = false;
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
        _getPresetIpc ??= _pi.GetIpcSubscriber<string>("BossMod.AI.GetPreset");
        _pauseMovementIpc ??= _pi.GetIpcSubscriber<bool, object>("BossMod.AI.PauseMovement");
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
