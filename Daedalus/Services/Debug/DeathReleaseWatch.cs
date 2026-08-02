using System;
using System.Numerics;
using Dalamud.Plugin.Services;

namespace Daedalus.Services.Debug;

/// <summary>How a death ended.</summary>
public enum RevivalKind
{
    /// <summary>Picked up where you fell — someone raised you.</summary>
    Raised,

    /// <summary>Moved a long way on standing up — you returned to a spawn point.</summary>
    Released,
}

/// <summary>
/// Times every death and says how it ended, because "the toons release while waiting on a rez"
/// has two completely different causes and they are indistinguishable by eye.
/// <para>
/// If the gap is a second or two, something is clicking the return prompt — one of the automation
/// plugins, not Daedalus (our only SelectYesno handler is inert while you are in a party and
/// parses the prompt for a rostered inviter first). If the gap is the full raise window, nothing
/// is misbehaving and the game's own death timer simply expired.
/// </para>
/// </summary>
public sealed class DeathReleaseWatch
{
    /// <summary>
    /// Standing up more than this far from where you fell means you were returned to a spawn
    /// point rather than raised in place. Generous — a raise leaves you exactly where you died,
    /// so anything beyond a few yalms is a relocation.
    /// </summary>
    public const float ReleaseDistanceYalms = 30f;

    private readonly IObjectTable? _objectTable;
    private readonly DebugLogService? _debugLog;

    private bool _wasDead;
    private DateTime _diedAtUtc;
    private Vector3 _diedAt;

    /// <summary>Test seam.</summary>
    internal Func<DateTime> UtcNow { get; set; } = () => DateTime.UtcNow;

    public DeathReleaseWatch(IObjectTable? objectTable, DebugLogService? debugLog)
    {
        _objectTable = objectTable;
        _debugLog = debugLog;
    }

    /// <summary>Seconds spent dead on the last death, or null before the first one.</summary>
    public double? LastDeathSeconds { get; private set; }

    /// <summary>How the last death ended.</summary>
    public RevivalKind? LastRevival { get; private set; }

    /// <summary>
    /// A release is a relocation; a raise leaves you where you fell. Distance separates them far
    /// more reliably than timing, which varies with who is raising and how far away they are.
    /// </summary>
    public static RevivalKind Classify(float movedYalms)
        => movedYalms > ReleaseDistanceYalms ? RevivalKind.Released : RevivalKind.Raised;

    /// <summary>Framework tick — two field reads.</summary>
    public void Update()
    {
        var player = _objectTable?.LocalPlayer;
        if (player is null)
            return;

        var dead = player.IsDead;

        if (dead && !_wasDead)
        {
            _wasDead = true;
            _diedAtUtc = UtcNow();
            _diedAt = player.Position;
            _debugLog?.Log(DebugLogCategory.General, DebugLogSeverity.Warning,
                $"DIED at ({_diedAt.X:F1}, {_diedAt.Z:F1}) — timing until raise or release");
            return;
        }

        if (dead || !_wasDead)
            return;

        _wasDead = false;

        var seconds = (UtcNow() - _diedAtUtc).TotalSeconds;
        var moved = Vector3.Distance(_diedAt, player.Position);
        var kind = Classify(moved);

        LastDeathSeconds = seconds;
        LastRevival = kind;

        var verdict = kind == RevivalKind.Released
            ? seconds < 10
                ? "RELEASED almost immediately — something is clicking the return prompt"
                : "RELEASED — check whether this matches the raise window expiring"
            : "RAISED in place";

        _debugLog?.Log(DebugLogCategory.General,
            kind == RevivalKind.Released ? DebugLogSeverity.Warning : DebugLogSeverity.Info,
            $"{verdict}: dead for {seconds:F1}s, moved {moved:F0}y");
    }
}
