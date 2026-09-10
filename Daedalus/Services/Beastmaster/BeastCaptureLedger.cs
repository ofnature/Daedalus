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
/// <b>The wording is not confirmed.</b> Beastmaster shipped in 7.56 and no reliable capture data
/// exists yet, so the phrase-to-tier table in <c>GaugeScanParser</c> is seeded with plausible
/// guesses and will be corrected from real samples. The tiers themselves are the stable part —
/// they are what the ledger stores and what the auto-capture rule reads — so getting the wording
/// wrong costs a re-parse, not a re-design.
/// </para>
/// </summary>
public enum BeastCaptureDifficulty : byte
{
    /// <summary>The scan text did not match any known phrase. NOT the same as "impossible".</summary>
    Unknown = 0,

    Trivial = 1,
    Easy = 2,
    Moderate = 3,
    Hard = 4,
    Extreme = 5,

    /// <summary>The scan said outright that this cannot be captured (e.g. above your level).</summary>
    Impossible = 6,
}

/// <summary>One beast, as learned from Gauge scans. Keyed by name.</summary>
public sealed class BeastCaptureEntry
{
    /// <summary>Display name, as the scan reported it. The ledger's key.</summary>
    public string Name { get; set; } = "";

    /// <summary>Level the scan reported, or 0 when it did not.</summary>
    public int Level { get; set; }

    /// <summary>Best difficulty tier seen for this beast.</summary>
    public BeastCaptureDifficulty Difficulty { get; set; }

    /// <summary>
    /// Whether the scan indicated this beast can be captured at all. Distinct from
    /// <see cref="Difficulty"/> being Unknown — an unparsed scan says nothing either way.
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
    /// Whether this beast was already in the Master's Bestiary at scan time — <b>null when we
    /// could not tell</b>. Logged for every scan regardless of capture state: a scan of an
    /// already-captured beast still confirms zone and level, which is most of what this table is
    /// for, so nothing here is gated on "not yet captured".
    /// <para>
    /// Null is the honest default today. <c>XBMManager.IsPetUnlocked(petId)</c> answers this, but
    /// it needs a beast-name → XBMPet-RowId map, and the only sheet carrying that mapping
    /// (<c>Lumina.Excel.Sheets.Experimental.XBMPet</c>) is marked evaluation-only and subject to
    /// change. See <c>GaugeScanWatcher.CapturedResolver</c> for the seam that fills this in.
    /// </para>
    /// </summary>
    public bool? AlreadyCaptured { get; set; }

    /// <summary>How many scans have landed on this beast.</summary>
    public int Scans { get; set; }

    public string FirstSeenUtc { get; set; } = "";
    public string LastSeenUtc { get; set; } = "";

    /// <summary>
    /// <b>The raw scan text, verbatim.</b> This is the single most important field in the record.
    /// The parser is guessing at wording nobody has confirmed yet; keeping the original text means
    /// a corrected parser can re-derive every field from scans already collected, instead of the
    /// early samples being lost to an early guess. Never overwrite it with a parsed summary.
    /// </summary>
    public string RawText { get; set; } = "";
}

/// <summary>
/// The persisted beast-capture table: one row per beast name, accumulated from Gauge scans.
///
/// <para>
/// <b>Shared between the two halves and NOT debug-gated.</b> Collection (the chat watcher and its
/// debug tab) is <c>#if DEBUG</c>, but this store is also what the shipped auto-capture rule reads,
/// so its schema and its loader must exist in Release. In a Release build nothing writes to it —
/// it is a read-only lookup over whatever the debug build collected.
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

    private readonly Dictionary<string, BeastCaptureEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly string? _filePath;
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
        Load();
    }

    /// <summary>Every known beast, ordered by name.</summary>
    public IReadOnlyList<BeastCaptureEntry> Entries =>
        _entries.Values.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();

    public int Count => _entries.Count;

    /// <summary>Look a beast up by name, or null.</summary>
    public BeastCaptureEntry? Find(string? name) =>
        !string.IsNullOrWhiteSpace(name) && _entries.TryGetValue(name!, out var e) ? e : null;

    /// <summary>
    /// Whether this beast is known to be capturable — the gate the shipped auto-capture rule uses.
    /// <para>
    /// Requires a positive <see cref="BeastCaptureEntry.Capturable"/>. A beast we have never
    /// scanned, or one whose scan text did not parse, is NOT treated as capturable: acting on an
    /// unparsed record would spend a GCD on a guess, and the whole point of the ledger is that
    /// this table is evidence rather than assumption.
    /// </para>
    /// </summary>
    public bool IsKnownCapturable(string? name)
    {
        var entry = Find(name);
        return entry is { Capturable: true } && entry.Difficulty != BeastCaptureDifficulty.Impossible;
    }

    /// <summary>
    /// Record a scan. Upserts by name and never discards information: a later scan that fails to
    /// parse will not blank a level or difficulty an earlier one established.
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

        // Never let a worse-informed scan erase a better-informed one.
        if (level > 0) entry.Level = level;
        if (difficulty != BeastCaptureDifficulty.Unknown) entry.Difficulty = difficulty;
        if (capturable.HasValue) entry.Capturable = capturable;
        if (!string.IsNullOrEmpty(rawText)) entry.RawText = rawText;

        entry.TerritoryId = territoryId;
        entry.TerritoryName = territoryName;
        entry.PlayerX = playerPosition.X;
        entry.PlayerY = playerPosition.Y;
        entry.PlayerZ = playerPosition.Z;
        if (alreadyCaptured.HasValue) entry.AlreadyCaptured = alreadyCaptured;

        _dirty = true;
        return entry;
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
        sb.AppendLine("| name | lv | difficulty | capturable | captured | territory | x,y,z | scans |");
        sb.AppendLine("| --- | --- | --- | --- | --- | --- | --- | --- |");
        foreach (var e in rows)
        {
            var cap = e.Capturable is null ? "?" : e.Capturable.Value ? "yes" : "no";
            var got = e.AlreadyCaptured is null ? "?" : e.AlreadyCaptured.Value ? "yes" : "no";
            sb.AppendLine($"| {e.Name} | {e.Level} | {e.Difficulty} | {cap} | {got} "
                + $"| {e.TerritoryName} ({e.TerritoryId}) | {e.PlayerX:0.0},{e.PlayerY:0.0},{e.PlayerZ:0.0} | {e.Scans} |");
        }

        sb.AppendLine();
        sb.AppendLine("Raw scan text (verbatim — this is what a corrected parser would re-read):");
        foreach (var e in rows.Where(r => !string.IsNullOrWhiteSpace(r.RawText)))
        {
            sb.AppendLine();
            sb.AppendLine($"{e.Name}:");
            sb.AppendLine($"  {e.RawText}");
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
                _entries[e.Name] = e;
        }
        catch (Exception ex)
        {
            _log?.Warning(ex, "[BeastCapture] load failed — starting empty");
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
