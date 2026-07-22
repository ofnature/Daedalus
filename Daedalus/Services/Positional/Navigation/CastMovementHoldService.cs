using System;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;

namespace Daedalus.Services.Positional.Navigation;

/// <summary>
/// Universal cast-vs-movement arbitration: while the player has a cast bar up and no danger lands
/// before the cast finishes, BMR's AI movement is paused (<c>BossMod.AI.PauseMovement</c>), then
/// released the instant the cast ends or danger approaches.
///
/// Field origin (2026-07-20, PCT): a toon outside BMR's configured stand distance but inside
/// spell range looped "start cast → BMR steps in → cast interrupted" all the way to the stand
/// ring. With the hold, each cast completes and BMR steps during the recast — stop-and-go walk-in
/// with zero wasted casts. Job-agnostic by construction: the trigger is the cast bar itself, so
/// caster/healer hardcasts, SAM Iaijutsu, and PLD Clemency are all covered; jobs without cast
/// bars never trigger it.
///
/// The paused flag is a PERSISTED BMR config value (AIConfig.ForbidMovement), so this service is
/// aggressive about failing open: release on cast end, on danger, on any IPC error, on dispose,
/// and via a watchdog that force-releases (and latches off for the rest of that cast) if a hold
/// somehow outlives the longest legitimate cast. Dodging always wins — when damage or a zone
/// activation falls inside the cast window (+ buffer), the hold releases and BMR moves (the cast
/// dies; a dead cast beats a dead toon).
/// </summary>
public sealed class CastMovementHoldService : IDisposable
{
    /// <summary>Force-release a hold older than this (longest legit cast ~5s teleport + slack).</summary>
    public const float MaxHoldSeconds = 8f;

    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly Configuration _configuration;
    private readonly IBossModSafetyService _bossModSafety;
    private readonly IObjectTable _objectTable;
    private readonly IPluginLog _log;

    private ICallGateSubscriber<bool, object>? _pauseMovement;

    private bool _wePaused;
    private DateTime _holdStartUtc;
    private bool _watchdogTrippedThisCast;

    /// <summary>Test seams.</summary>
    internal Func<DateTime> UtcNow = () => DateTime.UtcNow;
    internal Action<bool>? PauseInvokerOverride;

    /// <summary>Live status line for the Nav Control window.</summary>
    public string Status { get; private set; } = "idle";

    public CastMovementHoldService(
        IDalamudPluginInterface pluginInterface,
        Configuration configuration,
        IBossModSafetyService bossModSafety,
        IObjectTable objectTable,
        IPluginLog log)
    {
        _pluginInterface = pluginInterface;
        _configuration = configuration;
        _bossModSafety = bossModSafety;
        _objectTable = objectTable;
        _log = log;
    }

    /// <summary>Framework-thread tick.</summary>
    public void Update()
    {
        var player = _objectTable.LocalPlayer;
        var casting = player is { IsCasting: true };
        if (!casting)
            _watchdogTrippedThisCast = false;

        var castRemaining = casting
            ? MathF.Max(0f, player!.TotalCastTime - player.CurrentCastTime)
            : 0f;

        var hold = _configuration.Nav.HoldBmrMovementWhileCasting
            && casting
            && !_watchdogTrippedThisCast
            && _bossModSafety.IsAvailable
            && CastHoldRules.ShouldHold(
                castRemaining,
                _bossModSafety.NextDamageInSeconds,
                _bossModSafety.ForbiddenZoneActivationInSeconds,
                _bossModSafety.ForbiddenZonesCount);

        if (hold && _wePaused && (UtcNow() - _holdStartUtc).TotalSeconds > MaxHoldSeconds)
        {
            // Watchdog: never let a stuck cast bar keep BMR frozen. Latch off until this cast ends.
            _watchdogTrippedThisCast = true;
            hold = false;
            _log.Warning("[CastMovementHold] watchdog released a {0:F1}s hold — check cast detection.",
                (UtcNow() - _holdStartUtc).TotalSeconds);
        }

        if (hold && !_wePaused)
        {
            if (SetPaused(true))
            {
                _wePaused = true;
                _holdStartUtc = UtcNow();
            }
        }
        else if (!hold && _wePaused)
        {
            SetPaused(false);
            _wePaused = false;
        }

        Status = _wePaused
            ? $"holding BMR ({castRemaining:F1}s cast left)"
            : casting ? "casting (no hold needed)" : "idle";
    }

    private bool SetPaused(bool paused)
    {
        try
        {
            if (PauseInvokerOverride is { } seam)
            {
                seam(paused);
                return true;
            }

            _pauseMovement ??= _pluginInterface.GetIpcSubscriber<bool, object>("BossMod.AI.PauseMovement");
            _pauseMovement.InvokeAction(paused);
            return true;
        }
        catch (Exception ex)
        {
            // Fail open: if we can't talk to BMR, never consider ourselves holding.
            _log.Debug(ex, "[CastMovementHold] AI.PauseMovement({0}) failed.", paused);
            return false;
        }
    }

    public void Dispose()
    {
        if (_wePaused)
        {
            SetPaused(false);
            _wePaused = false;
        }
    }
}

/// <summary>
/// Pure hold/release decision for <see cref="CastMovementHoldService"/> — hold only when nothing
/// dangerous lands before the cast completes (plus a reaction buffer for BMR's pathfind latency).
/// </summary>
public static class CastHoldRules
{
    /// <summary>Extra calm required beyond the cast end — BMR needs time to path after release.</summary>
    public const float DangerBufferSeconds = 1.5f;

    public static bool ShouldHold(
        float castRemainingSeconds,
        float nextDamageInSeconds,
        float forbiddenZoneActivationInSeconds,
        int forbiddenZonesCount,
        float dangerBufferSeconds = DangerBufferSeconds)
    {
        var horizon = castRemainingSeconds + dangerBufferSeconds;

        // Incoming damage inside the cast window: let BMR do whatever it judges necessary.
        if (nextDamageInSeconds <= horizon)
            return false;

        // A danger zone activating (or already live) inside the window: BMR must be free to dodge.
        if (forbiddenZonesCount > 0 && forbiddenZoneActivationInSeconds <= horizon)
            return false;

        return true;
    }
}
