using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;

namespace Daedalus.Services.Blu;

/// <summary>Death-family susceptibility verdict for one enemy species (BNpcName id).</summary>
public enum DeathImmunityVerdict
{
    Unknown = 0,
    Vulnerable = 1,
    Immune = 2,
}

/// <summary>One learned entry. NameId is the language-independent species key (same as farm mode).</summary>
public sealed class DeathLedgerEntry
{
    [JsonPropertyName("id")] public uint NameId { get; set; }
    [JsonPropertyName("n")] public string Name { get; set; } = "";
    [JsonPropertyName("z")] public string Zone { get; set; } = "";
    /// <summary>TerritoryType id — lets the Raid window show this duty's verdicts. Additive.</summary>
    [JsonPropertyName("tid")] public ushort TerritoryId { get; set; }
    [JsonPropertyName("v")] public DeathImmunityVerdict Verdict { get; set; }
    [JsonPropertyName("c")] public int Confirms { get; set; }
    [JsonPropertyName("hp")] public uint MaxHpSeen { get; set; }
    [JsonPropertyName("ts")] public long LastSeenUnix { get; set; }
}

public interface IDeathImmunityLedger
{
    DeathImmunityVerdict GetVerdict(uint bnpcNameId);

    /// <summary>A death-family spell (Missile etc.) was dispatched at this target — the ledger
    /// re-reads the target's HP after the cast resolves and records the verdict.</summary>
    void NotifyProbeCast(ulong targetGameObjectId, uint bnpcNameId, string name, uint maxHp, uint hpBefore);

    IReadOnlyList<DeathLedgerEntry> Entries { get; }

    /// <summary>This duty's learned verdicts (the Raid window's per-duty section).</summary>
    IReadOnlyList<DeathLedgerEntry> EntriesForTerritory(ushort territoryId);

    /// <summary>Per-frame: resolve pending probes, debounced save.</summary>
    void Update();

    void ClearAll();
}

/// <summary>
/// Auto-learned ledger of which enemies the DEATH-FAMILY spells work on (one shared immunity
/// flag: Missile, Tail Screw, Launcher, Level 5 Death, Ultravibration). No public list of
/// susceptible bosses exists — this builds the user's own, per real cast: probe dispatched →
/// target HP re-read ~3s later → Missile's 50%-of-current-HP signature (or the death) proves
/// vulnerability; an untouched HP bar proves immunity. Persists to death-immunity-ledger.json.
/// CAVEAT recorded per design: an invulnerability phase can false-mark Immune — a later
/// successful probe flips it back (Vulnerable evidence always wins; immunity flags don't change).
/// </summary>
public sealed class DeathImmunityLedger : IDeathImmunityLedger
{
    private const double ResolveDelaySeconds = 3.0;  // 2.0s cast + effect/HP-sync latency
    private const float VulnerableDropRatio = 0.65f; // hpNow ≤ 65% of before → the ~50% hit landed
    private const float ImmuneKeepRatio = 0.90f;     // hpNow ≥ 90% of before → nothing meaningful hit

    private readonly string _filePath;
    private readonly IObjectTable? _objectTable;
    private readonly Func<string>? _zoneNameProvider;
    private readonly Func<ushort>? _territoryIdProvider;
    private readonly IPluginLog? _log;

    private readonly Dictionary<uint, DeathLedgerEntry> _entries = new();
    private readonly List<PendingProbe> _pending = new();
    private bool _dirty;
    private DateTime _lastSaveUtc = DateTime.MinValue;

    /// <summary>Injectable clock (tests age probes/dedup without sleeping).</summary>
    internal Func<DateTime> UtcNow = () => DateTime.UtcNow;

    /// <summary>Exposed for tests: probes queued and not yet resolved.</summary>
    internal int PendingProbeCount => _pending.Count;

    /// <summary>
    /// One probe per target per window: a rotation-dispatched Missile is ALSO observed by the
    /// action-effect hook (manual-cast path), and both call NotifyProbeCast — without this the
    /// same cast would double-confirm. Longer than the 3s resolve delay so the duplicate can't
    /// slip in after the first probe resolves.
    /// </summary>
    private const double ProbeDedupSeconds = 4.0;

    private sealed class PendingProbe
    {
        public ulong TargetId;
        public uint NameId;
        public string Name = "";
        public uint MaxHp;
        public uint HpBefore;
        public DateTime CastUtc;
    }

    public DeathImmunityLedger(
        string configDirectory,
        IObjectTable? objectTable,
        Func<string>? zoneNameProvider = null,
        IPluginLog? log = null,
        Func<ushort>? territoryIdProvider = null)
    {
        _filePath = Path.Combine(configDirectory, "death-immunity-ledger.json");
        _objectTable = objectTable;
        _zoneNameProvider = zoneNameProvider;
        _territoryIdProvider = territoryIdProvider;
        _log = log;
        Load();
    }

    public IReadOnlyList<DeathLedgerEntry> EntriesForTerritory(ushort territoryId)
    {
        if (territoryId == 0)
            return Array.Empty<DeathLedgerEntry>();
        lock (_entries)
            return _entries.Values
                .Where(e => e.TerritoryId == territoryId)
                .OrderBy(e => e.Name, StringComparer.Ordinal)
                .ToList();
    }

    public IReadOnlyList<DeathLedgerEntry> Entries
    {
        get { lock (_entries) return _entries.Values.OrderBy(e => e.Name, StringComparer.Ordinal).ToList(); }
    }

    public DeathImmunityVerdict GetVerdict(uint bnpcNameId)
    {
        lock (_entries)
            return _entries.TryGetValue(bnpcNameId, out var e) ? e.Verdict : DeathImmunityVerdict.Unknown;
    }

    public void NotifyProbeCast(ulong targetGameObjectId, uint bnpcNameId, string name, uint maxHp, uint hpBefore)
    {
        if (bnpcNameId == 0 || hpBefore == 0)
            return;

        var now = UtcNow();
        foreach (var existing in _pending)
        {
            if (existing.TargetId == targetGameObjectId
                && (now - existing.CastUtc).TotalSeconds < ProbeDedupSeconds)
                return; // same cast seen by both the dispatch path and the effect hook
        }

        _pending.Add(new PendingProbe
        {
            TargetId = targetGameObjectId,
            NameId = bnpcNameId,
            Name = name,
            MaxHp = maxHp,
            HpBefore = hpBefore,
            CastUtc = now,
        });
    }

    /// <summary>
    /// Pure verdict math (tested): compare HP before the probe vs after it resolved.
    /// Dead/gone counts as Vulnerable (the probe finished it or the pull collapsed with it —
    /// either way the spell wasn't refused). The middle band (heavy unrelated damage but not a
    /// halving) is inconclusive: record nothing rather than guess.
    /// </summary>
    public static DeathImmunityVerdict ResolveProbe(float hpBefore, float hpNow, bool deadOrGone)
    {
        if (deadOrGone)
            return DeathImmunityVerdict.Vulnerable;
        if (hpBefore <= 0f)
            return DeathImmunityVerdict.Unknown;
        var ratio = hpNow / hpBefore;
        if (ratio <= VulnerableDropRatio)
            return DeathImmunityVerdict.Vulnerable;
        if (ratio >= ImmuneKeepRatio)
            return DeathImmunityVerdict.Immune;
        return DeathImmunityVerdict.Unknown;
    }

    public void Update()
    {
        var now = UtcNow();

        for (var i = _pending.Count - 1; i >= 0; i--)
        {
            var probe = _pending[i];
            if ((now - probe.CastUtc).TotalSeconds < ResolveDelaySeconds)
                continue;
            _pending.RemoveAt(i);

            uint hpNow = 0;
            var deadOrGone = true;
            var obj = _objectTable?.SearchById(probe.TargetId);
            if (obj is IBattleChara chara && !chara.IsDead)
            {
                deadOrGone = false;
                hpNow = chara.CurrentHp;
            }

            var verdict = ResolveProbe(probe.HpBefore, hpNow, deadOrGone);
            if (verdict == DeathImmunityVerdict.Unknown)
                continue; // inconclusive — try again on a future cast

            Record(probe, verdict);
        }

        if (_dirty && (now - _lastSaveUtc).TotalSeconds > 3)
            Save();
    }

    private void Record(PendingProbe probe, DeathImmunityVerdict verdict)
    {
        lock (_entries)
        {
            if (!_entries.TryGetValue(probe.NameId, out var entry))
            {
                entry = new DeathLedgerEntry
                {
                    NameId = probe.NameId,
                    Name = probe.Name,
                    Zone = _zoneNameProvider?.Invoke() ?? "",
                    TerritoryId = _territoryIdProvider?.Invoke() ?? 0,
                };
                _entries[probe.NameId] = entry;
            }

            // Vulnerable evidence always wins: immunity flags never change, but an invuln PHASE
            // can fake an Immune reading — one real hit corrects it permanently.
            if (entry.Verdict == DeathImmunityVerdict.Vulnerable && verdict == DeathImmunityVerdict.Immune)
            {
                entry.LastSeenUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                _dirty = true;
                return;
            }

            if (entry.Verdict == verdict) entry.Confirms++;
            else { entry.Verdict = verdict; entry.Confirms = 1; }

            entry.MaxHpSeen = Math.Max(entry.MaxHpSeen, probe.MaxHp);
            entry.LastSeenUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            _dirty = true;

            _log?.Information($"[BLU] Death ledger: {probe.Name} ({probe.NameId}) = {verdict} (×{entry.Confirms})");
        }
    }

    public void ClearAll()
    {
        lock (_entries) _entries.Clear();
        _dirty = true;
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_filePath)) return;
            var list = JsonSerializer.Deserialize<List<DeathLedgerEntry>>(File.ReadAllText(_filePath));
            if (list == null) return;
            lock (_entries)
                foreach (var e in list.Where(e => e.NameId != 0))
                    _entries[e.NameId] = e;
        }
        catch (Exception ex)
        {
            _log?.Warning(ex, "[BLU] Death ledger load failed — starting empty");
        }
    }

    private void Save()
    {
        try
        {
            List<DeathLedgerEntry> snapshot;
            lock (_entries) snapshot = _entries.Values.ToList();
            File.WriteAllText(_filePath, JsonSerializer.Serialize(snapshot));
            _dirty = false;
            _lastSaveUtc = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            _log?.Warning(ex, "[BLU] Death ledger save failed");
        }
    }
}
