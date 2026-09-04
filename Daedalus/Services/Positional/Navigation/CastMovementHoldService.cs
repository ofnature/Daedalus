using System;
using Daedalus.Config;
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

    /// <summary>Minerva's hold is asked for per frame; this much beyond the cast bar covers a dropped frame.</summary>
    private const double MinervaHoldSlackSeconds = 0.5d;

    private readonly Func<BossHandling> _engine;
    private readonly Func<double, bool> _minervaHold;

    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly Configuration _configuration;
    private readonly IBossModSafetyService _bossModSafety;
    private readonly IObjectTable _objectTable;
    private readonly IPluginLog _log;

    private ICallGateSubscriber<bool, object>? _pauseMovement;

    private bool _wePaused;
    private DateTime _holdStartUtc;
    private bool _watchdogTrippedThisCast;
    private bool _startupStaleHoldCleared;

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
        IPluginLog log,
        Func<BossHandling>? engine = null,
        Func<double, bool>? minervaHold = null)
    {
        _pluginInterface = pluginInterface;
        _configuration = configuration;
        _bossModSafety = bossModSafety;
        _objectTable = objectTable;
        _log = log;
        _engine = engine ?? (() => BossHandling.BossMod);
        _minervaHold = minervaHold ?? (_ => false);
    }

    private bool MinervaEngine => _engine() == BossHandling.Minerva;

    /// <summary>Framework-thread tick.</summary>
    public void Update()
    {
        // Stale-hold recovery (field report 2026-07-26: BMR computed dodge targets but the
        // toon stood in the AoE as it fired): ForbidMovement is a PERSISTED BMR config value,
        // so a crash / failed release during a plugin reload leaves BMR frozen across
        // sessions — navigating but never moving. Clear it once per plugin lifetime as soon
        // as BMR is reachable; our own holds re-assert within a frame when legitimate.
        // One attempt only: on BMR builds without the endpoint SetPaused fails forever, and
        // retrying every frame just spams the log (field: continuous 20ms failure lines).
        // BMR only: Minerva's hold is timed, so there is nothing persisted to clear.
        if (!_startupStaleHoldCleared && !MinervaEngine && _bossModSafety.IsAvailable)
        {
            SetPaused(false);
            _startupStaleHoldCleared = true;
        }

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

        if (MinervaEngine)
        {
            // Minerva's hold is timed and re-asserted rather than latched: ask for it every frame the cast
            // still needs it, and ask for nothing to release it. Only uptime steering yields -- danger
            // still moves the character, which is the mid-cast bail BMR needs a separate release for.
            // This used to call BMR's PauseMovement regardless of engine: 27,574 failed calls in one day
            // on a Minerva box, and no hold at all.
            if (hold)
            {
                if (!_wePaused)
                    _holdStartUtc = UtcNow();
                _wePaused = _minervaHold(castRemaining + MinervaHoldSlackSeconds);
            }
            else if (_wePaused)
            {
                _minervaHold(0d);
                _wePaused = false;
            }
        }
        else if (hold && !_wePaused)
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
            ? $"holding {(MinervaEngine ? "Minerva" : "BMR")} ({castRemaining:F1}s cast left)"
            : casting ? "casting (no hold needed)" : "idle";
    }

    private bool SetPaused(bool paused)
    {
        // Dispose and the stale-hold path come through here too; on Minerva they mean "release".
        if (MinervaEngine)
            return _minervaHold(paused ? MinervaHoldSlackSeconds : 0d);

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
