using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using Daedalus.Data;

namespace Daedalus.Services.Occult;

/// <summary>Elemental weaknesses an Occult Crescent enemy has been observed to carry.</summary>
[Flags]
public enum OccultElement : byte
{
    None = 0,
    Fire = 1,
    Ice = 2,
    Lightning = 4,
    Wind = 8,
}

/// <summary>How dangerous an Occult enemy is — the "boss or trash?" answer.</summary>
public enum OccultEnemyKind : byte
{
    /// <summary>Ordinary field mob.</summary>
    Trash = 0,

    /// <summary>Big HP pool outside a critical encounter — forts / notorious spawns.</summary>
    Elite = 1,

    /// <summary>Big HP pool seen while a critical encounter was running.</summary>
    CriticalEncounterBoss = 2,
}

/// <summary>One learned enemy → weakness mapping.</summary>
public sealed class OccultWeaknessEntry
{
    public uint NameId { get; set; }
    public string Name { get; set; } = "";
    public ushort TerritoryId { get; set; }
    public OccultElement Elements { get; set; }

    /// <summary>Largest max-HP ever observed — the raw signal behind <see cref="Kind"/>.</summary>
    public uint MaxHp { get; set; }

    /// <summary>Ever seen while a critical encounter was active.</summary>
    public bool SeenInCriticalEncounter { get; set; }

    public string LastSeenUtc { get; set; } = "";

    /// <summary>
    /// Boss-or-trash verdict, filled in by <see cref="ElementalWeaknessLog"/> — it depends on
    /// the whole zone's HP distribution, not on this row alone.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public OccultEnemyKind Kind { get; internal set; }
}

/// <summary>
/// Learns which Occult Crescent enemies are weak to which element by watching for the
/// weakness statuses the game applies when a weakness is revealed (Occult Libra and friends):
/// 5322 Fire, 5323 Ice, 5324 Lightning, 5325 Wind — "Elemental weakness has been made
/// apparent. Damage from &lt;element&gt;-aspected phantom actions is increased."
/// <para>
/// The table persists to <c>occult-weaknesses.json</c> in the plugin config directory, so a
/// few farming sessions build a real reference (e.g. which North/South Horn mobs take the
/// Necromancer Deep Freeze ice bonus: potency 300→390, or 400→520 under Drain Touch).
/// Purely observational — it records what the game showed, never guesses.
/// </para>
/// </summary>
public sealed class ElementalWeaknessLog
{
    private const double ScanIntervalSeconds = 2.0;
    private const double SaveDebounceSeconds = 30.0;

    /// <summary>
    /// Fallback max-HP line, used only until a zone has enough samples to speak for itself.
    /// </summary>
    public const uint BossHpThresholdFallback = 1_000_000;

    /// <summary>
    /// A boss dwarfs the trash around it, so the jump is a MULTIPLE of the zone's typical
    /// enemy rather than an absolute number — this is what removes the guessed threshold:
    /// once a zone has real samples, its own median sets the line.
    /// </summary>
    public const uint BossHpMultipleOfZoneMedian = 10;

    /// <summary>Samples needed in a zone before trusting its distribution over the fallback.</summary>
    public const int MinZoneSamplesForRelative = 5;

    private readonly IObjectTable _objectTable;
    private readonly IClientState _clientState;
    private readonly IPluginLog _log;
    private readonly Debug.DebugLogService? _debugLog;
    private readonly string? _filePath;

    private readonly Dictionary<uint, OccultWeaknessEntry> _entries = new();
    private DateTime _lastScanUtc = DateTime.MinValue;
    private DateTime _lastSaveUtc = DateTime.MinValue;
    private bool _dirty;
    private bool _ioFaulted;

    public ElementalWeaknessLog(
        IObjectTable objectTable,
        IClientState clientState,
        IPluginLog log,
        string? configDirectory,
        Debug.DebugLogService? debugLog = null)
    {
        _objectTable = objectTable;
        _clientState = clientState;
        _log = log;
        _debugLog = debugLog;
        _filePath = string.IsNullOrEmpty(configDirectory)
            ? null
            : Path.Combine(configDirectory, "occult-weaknesses.json");
        Load();
    }

    /// <summary>
    /// Boss-or-trash from observed facts. The line is the zone's own median enemy HP times
    /// <see cref="BossHpMultipleOfZoneMedian"/> once the zone has enough samples; before that
    /// it falls back to <see cref="BossHpThresholdFallback"/>. Pure — the whole rule is here.
    /// </summary>
    public static OccultEnemyKind Classify(uint maxHp, bool seenInCriticalEncounter, uint zoneMedianHp, int zoneSamples)
    {
        var line = zoneSamples >= MinZoneSamplesForRelative && zoneMedianHp > 0
            ? zoneMedianHp * BossHpMultipleOfZoneMedian
            : BossHpThresholdFallback;

        if (maxHp < line)
            return OccultEnemyKind.Trash;
        return seenInCriticalEncounter ? OccultEnemyKind.CriticalEncounterBoss : OccultEnemyKind.Elite;
    }

    /// <summary>Median observed max-HP for a territory (0 when nothing recorded there yet).</summary>
    public uint ZoneMedianHp(ushort territoryId)
    {
        var hps = _entries.Values.Where(e => e.TerritoryId == territoryId && e.MaxHp > 0)
            .Select(e => e.MaxHp).OrderBy(h => h).ToList();
        return hps.Count == 0 ? 0u : hps[hps.Count / 2];
    }

    /// <summary>Everything learned so far, bosses first (Debug tab readout).</summary>
    public IReadOnlyList<OccultWeaknessEntry> Entries
    {
        get
        {
            var all = _entries.Values.ToList();
            foreach (var e in all)
            {
                var median = ZoneMedianHp(e.TerritoryId);
                var samples = _entries.Values.Count(x => x.TerritoryId == e.TerritoryId && x.MaxHp > 0);
                e.Kind = Classify(e.MaxHp, e.SeenInCriticalEncounter, median, samples);
            }

            return all.OrderByDescending(e => e.Kind).ThenByDescending(e => e.MaxHp).ToList();
        }
    }

    /// <summary>Path of the persisted table (shown in the Debug tab so it can be opened).</summary>
    public string? FilePath => _filePath;

    /// <summary>True when this enemy has been observed weak to the given element.</summary>
    public bool IsWeakTo(uint nameId, OccultElement element) =>
        _entries.TryGetValue(nameId, out var e) && (e.Elements & element) != 0;

    /// <summary>Framework tick — throttled scan of nearby enemies for revealed weaknesses.</summary>
    public void Update()
    {
        try
        {
            var territory = (ushort)_clientState.TerritoryType;
            if (!PhantomJobData.OccultTerritoryIds.Contains(territory))
                return;

            var now = DateTime.UtcNow;
            if ((now - _lastScanUtc).TotalSeconds < ScanIntervalSeconds)
                return;
            _lastScanUtc = now;
            var ceActive = IsCriticalEncounterActive();

            foreach (var obj in _objectTable)
            {
                if (obj.ObjectKind != ObjectKind.BattleNpc || obj is not IBattleNpc npc)
                    continue;
                var element = ReadRevealedElements(npc);
                if (element == OccultElement.None)
                    continue;
                Record(npc, element, territory, now, ceActive);
            }

            if (_dirty && (now - _lastSaveUtc).TotalSeconds >= SaveDebounceSeconds)
                Save();
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "[OccultWeakness] scan failed");
        }
    }

    /// <summary>
    /// True while a critical encounter is running. Occult CEs are dynamic events, and the
    /// container reports the active one — this is what separates a CE boss from an elite
    /// field spawn with a similar HP pool.
    /// </summary>
    private unsafe bool IsCriticalEncounterActive()
    {
        try
        {
            var container = FFXIVClientStructs.FFXIV.Client.Game.InstanceContent.DynamicEventContainer.GetInstance();
            return container != null && container->CurrentEventIndex >= 0;
        }
        catch
        {
            return false; // fail open: an unknown CE state just means "not marked as a boss"
        }
    }

    private static OccultElement ReadRevealedElements(IBattleNpc npc)
    {
        if (npc.StatusList == null)
            return OccultElement.None;

        var found = OccultElement.None;
        foreach (var status in npc.StatusList)
        {
            found |= status.StatusId switch
            {
                5322 => OccultElement.Fire,
                5323 => OccultElement.Ice,
                5324 => OccultElement.Lightning,
                5325 => OccultElement.Wind,
                _ => OccultElement.None,
            };
        }

        return found;
    }

    private void Record(IBattleNpc npc, OccultElement element, ushort territory, DateTime now, bool ceActive)
    {
        var nameId = npc.NameId;
        if (nameId == 0)
            return;

        if (!_entries.TryGetValue(nameId, out var entry))
        {
            entry = new OccultWeaknessEntry
            {
                NameId = nameId,
                Name = npc.Name?.TextValue ?? $"#{nameId}",
                TerritoryId = territory,
            };
            _entries[nameId] = entry;
        }

        var isNew = (entry.Elements & element) != element;
        entry.Elements |= element;
        if (npc.MaxHp > entry.MaxHp)
            entry.MaxHp = (uint)System.Math.Min(npc.MaxHp, uint.MaxValue);
        if (ceActive)
            entry.SeenInCriticalEncounter = true;
        entry.LastSeenUtc = now.ToString("O");
        _dirty = true;

        if (isNew)
        {
            _debugLog?.Log(Debug.DebugLogCategory.General, Debug.DebugLogSeverity.Info,
                $"occult weakness learned: {entry.Name} — {entry.Elements} " +
                $"({entry.MaxHp:N0} HP, territory {territory})");
        }
    }

    private void Load()
    {
        if (_filePath is null || !File.Exists(_filePath))
            return;
        try
        {
            var json = File.ReadAllText(_filePath);
            var list = JsonSerializer.Deserialize<List<OccultWeaknessEntry>>(json);
            if (list is null)
                return;
            foreach (var e in list)
            {
                if (e.NameId != 0)
                    _entries[e.NameId] = e;
            }
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "[OccultWeakness] load failed — starting empty");
        }
    }

    /// <summary>Writes the learned table (debounced during play; call directly on dispose).</summary>
    public void Save()
    {
        if (_filePath is null || _ioFaulted || _entries.Count == 0)
            return;
        try
        {
            var json = JsonSerializer.Serialize(
                _entries.Values.OrderBy(e => e.Name).ToList(),
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_filePath, json);
            _dirty = false;
            _lastSaveUtc = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            _ioFaulted = true;
            _log.Warning(ex, "[OccultWeakness] save failed — persistence disabled this session");
        }
    }
}
