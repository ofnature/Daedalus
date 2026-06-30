using System;
using System.Collections.Generic;
using System.IO;
using Dalamud.Plugin.Services;

namespace Daedalus.Services.Debug;

/// <summary>Category of a debug-log event, used for filtering and colouring.</summary>
public enum DebugLogCategory
{
    Action,
    Nav,
    Targeting,
    General,
}

/// <summary>Severity of a debug-log event.</summary>
public enum DebugLogSeverity
{
    Info,
    Warning,
    Error,
}

/// <summary>A single coalesced debug-log line.</summary>
public sealed class DebugLogEntry
{
    public DateTime FirstTimestamp { get; init; }
    public DateTime LastTimestamp { get; set; }
    public DebugLogCategory Category { get; init; }
    public DebugLogSeverity Severity { get; init; }
    public string Message { get; init; } = "";
    /// <summary>How many times this identical event has fired within the coalescing window.</summary>
    public int Count { get; set; } = 1;
}

/// <summary>
/// A curated, low-noise diagnostic log — separate from the Dalamud log. Surfaces meaningful failures
/// (an action the game refused to cast, a failed BossMod config push, etc.), NOT the per-frame rotation
/// chatter. Identical events inside a short window are coalesced into one line with a running count, so a
/// genuine stall shows as "Unable to cast Fast Blade ×42" instead of 42 lines.
///
/// Entries are held in a ring buffer for the in-game "Debug Log" tab and, when enabled, appended to
/// <c>daedalus-debug.log</c> in the plugin config directory for after-the-fact inspection. Fail-open: any
/// file error is swallowed (reported once to the Dalamud log) and never affects the rotation.
/// </summary>
public sealed class DebugLogService
{
    /// <summary>Identical (category + message) events within this window coalesce instead of adding a line.</summary>
    private static readonly TimeSpan CoalesceWindow = TimeSpan.FromSeconds(5);
    private const int MaxEntries = 300;
    private const long MaxFileBytes = 5 * 1024 * 1024;

    private readonly object _gate = new();
    private readonly LinkedList<DebugLogEntry> _entries = new();
    private readonly Dictionary<string, DebugLogEntry> _recent = new();

    private readonly Configuration? _configuration;
    private readonly IPluginLog? _log;
    private readonly string? _filePath;
    private bool _fileErrorReported;
    private bool _sessionHeaderWritten;

    public DebugLogService(Configuration? configuration = null, string? logDirectory = null, IPluginLog? log = null)
    {
        _configuration = configuration;
        _log = log;
        _filePath = string.IsNullOrEmpty(logDirectory)
            ? null
            : Path.Combine(logDirectory, "daedalus-debug.log");
    }

    /// <summary>Absolute path of the on-disk log, or null when no config directory is available.</summary>
    public string? FilePath => _filePath;

    /// <summary>Records a diagnostic event. Identical events are coalesced within a 5s window.</summary>
    public void Log(DebugLogCategory category, DebugLogSeverity severity, string message)
    {
        if (string.IsNullOrEmpty(message))
            return;

        var now = DateTime.UtcNow;
        var key = $"{(int)category}|{message}";
        bool isNewLine;

        lock (_gate)
        {
            PruneRecent(now);

            if (_recent.TryGetValue(key, out var existing) && now - existing.LastTimestamp < CoalesceWindow)
            {
                existing.Count++;
                existing.LastTimestamp = now;
                isNewLine = false;
            }
            else
            {
                var entry = new DebugLogEntry
                {
                    FirstTimestamp = now,
                    LastTimestamp = now,
                    Category = category,
                    Severity = severity,
                    Message = message,
                };
                _entries.AddLast(entry);
                while (_entries.Count > MaxEntries)
                    _entries.RemoveFirst();
                _recent[key] = entry;
                isNewLine = true;
            }
        }

        // Only the first occurrence of a coalesced burst is written to disk (keeps the file readable).
        if (isNewLine)
            AppendToFile(now, category, severity, message);
    }

    /// <summary>Newest-first snapshot of the current log entries for the UI.</summary>
    public IReadOnlyList<DebugLogEntry> GetSnapshot()
    {
        lock (_gate)
        {
            var result = new List<DebugLogEntry>(_entries.Count);
            for (var node = _entries.Last; node is not null; node = node.Previous)
                result.Add(node.Value);
            return result;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
            _recent.Clear();
        }
    }

    private void PruneRecent(DateTime now)
    {
        if (_recent.Count == 0)
            return;

        List<string>? stale = null;
        foreach (var kvp in _recent)
        {
            if (now - kvp.Value.LastTimestamp >= CoalesceWindow)
                (stale ??= new List<string>()).Add(kvp.Key);
        }
        if (stale is null)
            return;
        foreach (var k in stale)
            _recent.Remove(k);
    }

    private void AppendToFile(DateTime now, DebugLogCategory category, DebugLogSeverity severity, string message)
    {
        if (_filePath is null || _configuration?.Debug.EnableDebugLogFile != true)
            return;

        try
        {
            if (!_sessionHeaderWritten)
            {
                RollIfTooLarge();
                File.AppendAllText(_filePath,
                    $"{Environment.NewLine}===== Daedalus debug log — session start {DateTime.Now:yyyy-MM-dd HH:mm:ss} ====={Environment.NewLine}");
                _sessionHeaderWritten = true;
            }

            var line = $"{now.ToLocalTime():HH:mm:ss.fff} [{severity}] [{category}] {message}{Environment.NewLine}";
            File.AppendAllText(_filePath, line);
        }
        catch (Exception ex)
        {
            if (!_fileErrorReported)
            {
                _fileErrorReported = true;
                _log?.Warning(ex, "[DebugLogService] Failed to write {Path} — file logging disabled for this session", _filePath);
            }
        }
    }

    private void RollIfTooLarge()
    {
        try
        {
            if (_filePath is not null && File.Exists(_filePath) && new FileInfo(_filePath).Length > MaxFileBytes)
            {
                var old = _filePath + ".old";
                File.Delete(old);
                File.Move(_filePath, old);
            }
        }
        catch
        {
            // Best effort — if rolling fails we just keep appending.
        }
    }
}
