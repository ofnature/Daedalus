using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using Dalamud.Plugin.Services;

namespace Daedalus.Services.Beastmaster;

/// <summary>
/// How hard a beast is to capture, as the Gauge action phrases it.
///
/// <para>
/// Only tiers backed by a <b>real scan message</b> are ever assigned. The wording is learned from
/// samples, not guessed — the first guessed table used "catch"/"capture" and matched none of the
/// real messages, which all say "befriend". Values are append-only: they are persisted as numbers.
/// </para>
/// </summary>
public enum BeastCaptureDifficulty : byte
{
    /// <summary>No confirmed phrase matched. NOT the same as "impossible".</summary>
    Unknown = 0,

    /// <summary>"Befriending this beast should take no effort at all." — confirmed 2026-09-10.</summary>
    Trivial = 1,

    // Easy / Moderate / Hard / Extreme are placeholders: no real message has been seen for them yet,
    // so nothing maps to them. They exist so the real wording slots in without a schema change.
    Easy = 2,
    Moderate = 3,
    Hard = 4,
    Extreme = 5,

    /// <summary>
    /// "No pact can be forged with this target..." — confirmed 2026-09-10. A flat no: not a beast
    /// that can be tamed at all. Contrast <see cref="LevelGated"/>, which is "not yet".
    /// </summary>
    Impossible = 6,

    /// <summary>
    /// "You are not yet strong enough to befriend this beast..." — confirmed 2026-09-10. The beast is
    /// above the player's level. Deliberately NOT <see cref="Impossible"/>: "not yet" means it opens
    /// up with levels, and a rescan after levelling overwrites this with a real tier.
    /// </summary>
    LevelGated = 7,
}

/// <summary>One beast, as learned from Gauge scans. Keyed by name.</summary>
public sealed class BeastCaptureEntry
{
    /// <summary>Display name of the scanned target. The ledger's key.</summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// The beast's level, <b>read from the target object</b> at scan time — the scan text carries no
    /// level at all. 0 when unknown (rows logged before target-level reading existed).
    /// </summary>
    public int Level { get; set; }

    /// <summary>Most recent confirmed difficulty tier for this beast.</summary>
    public BeastCaptureDifficulty Difficulty { get; set; }

    /// <summary>
    /// Whether the beast can be captured. Null when no confirmed phrase has matched any scan — which
    /// is exactly what the debug tab's "unparsed" view lists.
    /// </summary>
    public bool? Capturable { get; set; }

    /// <summary>Territory the scan happened in.</summary>
    public ushort TerritoryId { get; set; }

    /// <summary>Territory name at scan time, for eyeballing the table.</summary>
    public string TerritoryName { get; set; } = "";

    /// <summary>
    /// <b>The PLAYER's position when the scan fired, not the beast's spawn point.</b> We have no
    /// signal for where a beast actually spawns, and the player is usually within a few yalms of
    /// the thing they just scanned, so this is a stand-in good enough to find the beast again —
    /// and nothing more. Do not present it as a spawn coordinate.
    /// </summary>
    public float PlayerX { get; set; }

    /// <summary>See <see cref="PlayerX"/> — player position, not spawn point.</summary>
    public float PlayerY { get; set; }

    /// <summary>See <see cref="PlayerX"/> — player position, not spawn point.</summary>
    public float PlayerZ { get; set; }

    /// <summary>
    /// Whether this beast is already in the Master's Bestiary — null when no scan has said. Every
    /// scan is logged regardless: a scan of an owned beast still confirms zone and level. The game
    /// answers this itself ("You have already befriended this beast."), so no sheet lookup is needed.
    /// </summary>
    public bool? AlreadyCaptured { get; set; }

    /// <summary>How many scans have landed on this beast.</summary>
    public int Scans { get; set; }

    public string FirstSeenUtc { get; set; } = "";
    public string LastSeenUtc { get; set; } = "";

    /// <summary>The most recent raw scan text, verbatim. Kept for compatibility and the tooltip.</summary>
    public string RawText { get; set; } = "";

    /// <summary>
    /// <b>Every distinct raw scan message seen for this beast</b>, oldest first, the most recently
    /// seen last. One string was not enough: scanning a beast you already own replies "already
    /// befriended", which would overwrite the one message that carried its difficulty. Keeping each
    /// distinct message is what lets <see cref="BeastCaptureLedger.Reparse"/> re-derive every field
    /// when the phrase table learns new wording.
    /// </summary>
    public List<string> RawSamples { get; set; } = [];
}

/// <summary>
/// The persisted beast-capture table: one row per beast name, accumulated from Gauge scans.
///
/// <para>
/// <b>Shared between the two halves and NOT debug-gated.</b> Collection (the chat watcher, the
/// parser and the debug tab) is <c>#if DEBUG</c>, but this store is also what the shipped
/// auto-capture rule reads, so its schema and its loader must exist in Release. In a Release build
/// nothing writes to it — it is a read-only lookup over whatever a debug build collected.
/// </para>
///
/// <para>
/// Modelled on <see cref="Daedalus.Services.Occult.ElementalWeaknessLog"/>: JSON in the plugin
/// config directory, debounced writes during play, a direct <see cref="Save"/> on dispose, and I/O
/// that disables itself rather than throwing every frame after a failure.
/// </para>
/// </summary>
public sealed class BeastCaptureLedger
{
    private const double SaveDebounceSeconds = 30.0;

    /// <summary>Distinct raw samples kept per beast. A beast has only a handful of possible replies.</summary>
    internal const int MaxRawSamples = 10;

    private readonly Dictionary<string, BeastCaptureEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly string? _filePath;
    private readonly string? _importPath;
    private readonly IPluginLog? _log;

    private bool _dirty;
    private bool _ioFaulted;
    private DateTime _lastSaveUtc = DateTime.MinValue;

    public BeastCaptureLedger(string? configDirectory, IPluginLog? log = null)
    {
        _log = log;
        _filePath = string.IsNullOrEmpty(configDirectory)
            ? null
            : Path.Combine(configDirectory, "beast-captures.json");
        _importPath = string.IsNullOrEmpty(configDirectory)
            ? null
            : Path.Combine(configDirectory, "beast-captures.import.json");
        Load();
        ApplyImport();
    }

    /// <summary>Every known beast, ordered by name.</summary>
    public IReadOnlyList<BeastCaptureEntry> Entries =>
        _entries.Values.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();

    public int Count => _entries.Count;

    /// <summary>Look a beast up by name, or null.</summary>
    public BeastCaptureEntry? Find(string? name) =>
        !string.IsNullOrWhiteSpace(name) && _entries.TryGetValue(name!, out var e) ? e : null;

    /// <summary>
    /// Whether this beast is known to be capturable. A fact about the beast — see
    /// <see cref="ShouldAutoCapture"/> for the auto-capture gate, which also excludes owned beasts.
    /// <para>
    /// Requires a positive <see cref="BeastCaptureEntry.Capturable"/>. An unscanned beast, or one
    /// whose scans matched no confirmed phrase, is NOT capturable as far as this table is concerned:
    /// the table is evidence, not assumption.
    /// </para>
    /// </summary>
    public bool IsKnownCapturable(string? name)
    {
        var entry = Find(name);
        return entry is { Capturable: true }
            && entry.Difficulty is not (BeastCaptureDifficulty.Impossible or BeastCaptureDifficulty.LevelGated);
    }

    /// <summary>
    /// The auto-capture gate: capturable AND not already in the Bestiary. Capturing a beast you own
    /// adds nothing, so firing Capture on one is a wasted GCD — and scanning owned beasts is routine
    /// while collecting data, so without this every owned beast would draw an attempt.
    /// </summary>
    public bool ShouldAutoCapture(string? name) =>
        IsKnownCapturable(name) && Find(name)?.AlreadyCaptured != true;

    /// <summary>
    /// Record a scan. Upserts by name and never discards information: a scan that matched nothing
    /// will not blank a level or tier an earlier one established.
    /// </summary>
    public BeastCaptureEntry Record(
        string name,
        int level,
        BeastCaptureDifficulty difficulty,
        bool? capturable,
        ushort territoryId,
        string territoryName,
        Vector3 playerPosition,
        bool? alreadyCaptured,
        string rawText)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A scan with no beast name cannot be keyed.", nameof(name));

        var nowUtc = DateTime.UtcNow.ToString("O");

        if (!_entries.TryGetValue(name, out var entry))
        {
            entry = new BeastCaptureEntry { Name = name, FirstSeenUtc = nowUtc };
            _entries[name] = entry;
        }

        entry.Scans++;
        entry.LastSeenUtc = nowUtc;

        if (level > 0) entry.Level = level;
        Apply(entry, difficulty, capturable, alreadyCaptured);

        if (!string.IsNullOrEmpty(rawText))
        {
            entry.RawText = rawText;
            AddSample(entry, rawText);
        }

        entry.TerritoryId = territoryId;
        entry.TerritoryName = territoryName;
        entry.PlayerX = playerPosition.X;
        entry.PlayerY = playerPosition.Y;
        entry.PlayerZ = playerPosition.Z;

        _dirty = true;
        return entry;
    }

    /// <summary>
    /// Re-derive difficulty, capturability and capture state for every beast from its stored raw
    /// samples, using <paramref name="derive"/> (the current phrase table). This is what makes the
    /// raw-text design pay off: rows logged while the table was wrong are corrected in place, without
    /// rescanning anything.
    /// <para>
    /// Samples are replayed oldest-first so the most recent message wins, the same rule
    /// <see cref="Record"/> uses. A sample the table does not recognise contributes nothing — it is
    /// kept, not deleted, in case a later table learns it. Returns how many rows changed.
    /// </para>
    /// </summary>
    public int Reparse(Func<string, (BeastCaptureDifficulty Tier, bool? Capturable, bool? AlreadyCaptured)?> derive)
    {
        var changed = 0;
        foreach (var entry in _entries.Values)
        {
            var before = (entry.Difficulty, entry.Capturable, entry.AlreadyCaptured);

            foreach (var sample in entry.RawSamples)
            {
                if (derive(sample) is { } d)
                    Apply(entry, d.Tier, d.Capturable, d.AlreadyCaptured);
            }

            if ((entry.Difficulty, entry.Capturable, entry.AlreadyCaptured) != before)
                changed++;
        }

        if (changed > 0)
            _dirty = true;
        return changed;
    }

    /// <summary>Later information wins; absent information never erases present information.</summary>
    private static void Apply(
        BeastCaptureEntry entry, BeastCaptureDifficulty difficulty, bool? capturable, bool? alreadyCaptured)
    {
        if (difficulty != BeastCaptureDifficulty.Unknown) entry.Difficulty = difficulty;
        if (capturable.HasValue) entry.Capturable = capturable;
        if (alreadyCaptured.HasValue) entry.AlreadyCaptured = alreadyCaptured;
    }

    /// <summary>
    /// Keep each distinct message once, moved to the end when seen again so the list stays in
    /// last-seen order — that ordering is what lets <see cref="Reparse"/> apply the newest last.
    /// </summary>
    private static void AddSample(BeastCaptureEntry entry, string rawText)
    {
        entry.RawSamples.RemoveAll(s => string.Equals(s, rawText, StringComparison.Ordinal));
        entry.RawSamples.Add(rawText);

        if (entry.RawSamples.Count > MaxRawSamples)
            entry.RawSamples.RemoveRange(0, entry.RawSamples.Count - MaxRawSamples);
    }

    /// <summary>Debounced save; call once per frame from the owner.</summary>
    public void Tick()
    {
        if (!_dirty || _ioFaulted)
            return;

        if ((DateTime.UtcNow - _lastSaveUtc).TotalSeconds >= SaveDebounceSeconds)
            Save();
    }

    /// <summary>Paste-ready dump of everything collected, for handing samples over wholesale.</summary>
    public string Export()
    {
        var rows = Entries;
        if (rows.Count == 0)
            return "No beast scans recorded yet.";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Beastmaster capture ledger — {rows.Count} beast(s)");
        sb.AppendLine();
        sb.AppendLine("| name | lv | difficulty | capturable | captured | territory | x,y,z (player) | scans |");
        sb.AppendLine("| --- | --- | --- | --- | --- | --- | --- | --- |");
        foreach (var e in rows)
        {
            var cap = e.Capturable is null ? "?" : e.Capturable.Value ? "yes" : "no";
            var got = e.AlreadyCaptured is null ? "?" : e.AlreadyCaptured.Value ? "yes" : "no";
            sb.AppendLine($"| {e.Name} | {(e.Level > 0 ? e.Level.ToString() : "?")} | {e.Difficulty} | {cap} | {got} "
                + $"| {e.TerritoryName} ({e.TerritoryId}) | {e.PlayerX:0.0},{e.PlayerY:0.0},{e.PlayerZ:0.0} | {e.Scans} |");
        }

        sb.AppendLine();
        sb.AppendLine("Raw scan text (verbatim, every distinct message — what a corrected parser re-reads):");
        foreach (var e in rows.Where(r => r.RawSamples.Count > 0))
        {
            sb.AppendLine();
            sb.AppendLine($"{e.Name}:");
            foreach (var s in e.RawSamples)
                sb.AppendLine($"  {s}");
        }

        return sb.ToString();
    }

    private void Load()
    {
        if (_filePath is null || !File.Exists(_filePath))
            return;

        try
        {
            var list = JsonSerializer.Deserialize<List<BeastCaptureEntry>>(File.ReadAllText(_filePath));
            if (list is null)
                return;

            foreach (var e in list.Where(e => !string.IsNullOrWhiteSpace(e.Name)))
            {
                // Rows written before RawSamples existed carry their only sample in RawText.
                e.RawSamples ??= [];
                if (e.RawSamples.Count == 0 && !string.IsNullOrEmpty(e.RawText))
                    e.RawSamples.Add(e.RawText);

                _entries[e.Name] = e;
            }
        }
        catch (Exception ex)
        {
            _log?.Warning(ex, "[BeastCapture] load failed — starting empty");
        }
    }

    /// <summary>
    /// One-shot merge of hand-supplied facts from <c>beast-captures.import.json</c>, renamed to
    /// <c>.import.applied-&lt;utc&gt;.json</c> afterwards so it applies exactly once and leaves a trail.
    ///
    /// <para>
    /// <b>Why an import file rather than editing the table directly:</b> the running plugin holds the
    /// table in memory and writes it back on its next save and on every reload, so a direct edit to
    /// <c>beast-captures.json</c> is silently overwritten. An import is read by the plugin itself, after
    /// it loads, which is the only point at which a hand edit cannot be clobbered.
    /// </para>
    ///
    /// <para>
    /// <b>Level only, by design.</b> Level is informational. Difficulty and capturability decide what
    /// the shipped auto-capture rule presses, and those must stay evidence from real scans — a
    /// hand-typed "capturable" must never be able to make the plugin fire Capture. Rows are never
    /// invented either: a name with no scanned row is logged and skipped.
    /// </para>
    /// </summary>
    private void ApplyImport()
    {
        if (_importPath is null || !File.Exists(_importPath))
            return;

        try
        {
            var list = JsonSerializer.Deserialize<List<BeastCaptureEntry>>(File.ReadAllText(_importPath)) ?? [];
            var applied = 0;
            foreach (var imported in list)
            {
                var entry = Find(imported.Name);
                if (entry is null)
                {
                    _log?.Information("[BeastCapture] import: no scanned row named {Name} - skipped", imported.Name);
                    continue;
                }

                if (imported.Level > 0 && imported.Level != entry.Level)
                {
                    entry.Level = imported.Level;
                    applied++;
                }
            }

            if (applied > 0)
                _dirty = true;

            File.Move(_importPath, Path.Combine(
                Path.GetDirectoryName(_importPath)!,
                $"beast-captures.import.applied-{DateTime.UtcNow:yyyyMMddHHmmss}.json"));
            _log?.Information("[BeastCapture] import applied: {Count} level(s) set", applied);
        }
        catch (Exception ex)
        {
            _log?.Warning(ex, "[BeastCapture] import failed - left in place for the next load");
        }
    }

    /// <summary>Writes the table. Debounced during play; call directly on dispose.</summary>
    public void Save()
    {
        if (_filePath is null || _ioFaulted || _entries.Count == 0)
            return;

        try
        {
            File.WriteAllText(
                _filePath,
                JsonSerializer.Serialize(Entries, new JsonSerializerOptions { WriteIndented = true }));
            _dirty = false;
            _lastSaveUtc = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            _ioFaulted = true;
            _log?.Warning(ex, "[BeastCapture] save failed — persistence disabled this session");
        }
    }
}
