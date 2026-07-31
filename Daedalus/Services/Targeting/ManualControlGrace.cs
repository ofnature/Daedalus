using System;

namespace Daedalus.Services.Targeting;

/// <summary>
/// Detects USER mouse/keyboard targeting and grants a short "hands off the wheel" grace.
/// Field 2026-07-30: left-clicking anything in the world changed the hard target, the
/// positional anchor + per-GCD BMR GoToPositional transient immediately re-pulsed against
/// it, and the toon stutter-stepped under the player's hands. Every Daedalus hard-target
/// write registers itself here; a target change nobody registered is a manual click, and
/// movement pulses (vNav anchor hops, BMR positional transients) hold for the grace window.
/// Static-backed on purpose — transient flags the rotation stack reads must never live on a
/// config copy (the ExternalCombatOverride lesson).
/// </summary>
public static class ManualControlGrace
{
    /// <summary>How long movement pulses stay suppressed after a manual target change.</summary>
    public const double GraceSeconds = 4.0;

    /// <summary>Own-write attribution window: a target change matching a write this recent is ours.</summary>
    private const double OwnWriteAttributionSeconds = 1.0;

    private static ulong _lastSeenTargetId;
    private static ulong _lastOwnWriteId;
    private static DateTime _lastOwnWriteUtc = DateTime.MinValue;
    private static DateTime _graceUntilUtc = DateTime.MinValue;

    /// <summary>True while movement pulses should hold because the user recently clicked a target.</summary>
    public static bool IsActive => UtcNow() < _graceUntilUtc;

    /// <summary>Seconds of grace remaining (0 when inactive) — for the Nav window status line.</summary>
    public static double RemainingSeconds => Math.Max(0d, (_graceUntilUtc - UtcNow()).TotalSeconds);

    /// <summary>Testable clock.</summary>
    internal static Func<DateTime> UtcNow { get; set; } = () => DateTime.UtcNow;

    /// <summary>Call from EVERY Daedalus code path that writes TargetManager.Target.</summary>
    public static void RecordOwnWrite(ulong targetId)
    {
        _lastOwnWriteId = targetId;
        _lastOwnWriteUtc = UtcNow();
    }

    /// <summary>
    /// Per-frame observation of the current hard target id (0 = none). A change that doesn't
    /// match a recent own-write is a manual click — arm the grace. Clearing the target (0) is
    /// never treated as manual; escape/clears shouldn't freeze movement.
    /// </summary>
    public static void NoteFrame(ulong currentTargetId)
    {
        if (currentTargetId == _lastSeenTargetId)
            return;

        var now = UtcNow();
        var isOwnWrite = currentTargetId != 0
            && currentTargetId == _lastOwnWriteId
            && (now - _lastOwnWriteUtc).TotalSeconds <= OwnWriteAttributionSeconds;

        if (currentTargetId != 0 && !isOwnWrite)
            _graceUntilUtc = now.AddSeconds(GraceSeconds);

        _lastSeenTargetId = currentTargetId;
    }

    /// <summary>Test/reload hygiene.</summary>
    internal static void Reset()
    {
        _lastSeenTargetId = 0;
        _lastOwnWriteId = 0;
        _lastOwnWriteUtc = DateTime.MinValue;
        _graceUntilUtc = DateTime.MinValue;
        UtcNow = () => DateTime.UtcNow;
    }
}
