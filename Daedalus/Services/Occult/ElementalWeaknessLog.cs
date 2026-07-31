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
    /// Boss-or-trash verdict from the recorded facts. The HP line is a starting threshold —
    /// MaxHp is persisted precisely so it can be re-tuned from real data instead of guessed
    /// at again.
    /// </summary>
    public OccultEnemyKind Kind =>
        MaxHp < ElementalWeaknessLog.BossHpThreshold ? OccultEnemyKind.Trash
        : SeenInCriticalEncounter ? OccultEnemyKind.CriticalEncounterBoss
        : OccultEnemyKind.Elite;
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
    /// Max-HP line between trash and a boss/elite. A first cut from field scale (Occult trash
    /// sits in the tens of thousands, critical-encounter bosses in the millions) — every
    /// entry stores its real MaxHp, so this is tunable from the collected table rather than
    /// permanently guessed.
    /// </summary>
    public const uint BossHpThreshold = 1_000_000;

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

    /// <summary>Everything learned so far, most-sighted first (Debug tab readout).</summary>
    public IReadOnlyList<OccultWeaknessEntry> Entries =>
        _entries.Values.OrderByDescending(e => e.Kind).ThenByDescending(e => e.MaxHp).ToList();

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
                $"occult weakness learned: {entry.Name} [{entry.Kind}] — {entry.Elements} (territory {territory})");
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
