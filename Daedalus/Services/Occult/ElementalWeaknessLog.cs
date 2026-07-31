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

    /// <summary>
    /// Ever seen while a critical encounter was active. RAW fact — true for the encounter's
    /// own mobs AND for any field trash that merely stood in scan range at the time. Use
    /// <see cref="BelongsToCriticalEncounter"/> for "is actually part of the fight".
    /// </summary>
    public bool SeenInCriticalEncounter { get; set; }

    /// <summary>
    /// Actually part of the encounter, rather than a bystander. Filled in by
    /// <see cref="ElementalWeaknessLog"/> — it needs the zone's HP distribution to decide.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool BelongsToCriticalEncounter { get; internal set; }

    /// <summary>Name of the critical encounter it was seen in ("Quarried Away"), when known.</summary>
    public string CriticalEncounter { get; set; } = "";

    /// <summary>
    /// Ever seen carrying a FATE id. Unlike CE membership this needs no heuristic — the game
    /// stamps the fate on the object, so a FATE mob identifies itself.
    /// </summary>
    public bool SeenInFate { get; set; }

    /// <summary>Name of the FATE it was seen in ("Allure of the Occult"), when resolvable.</summary>
    public string Fate { get; set; } = "";

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
    /// Bootstrap max-HP line, used only until a zone has enough samples to speak for itself.
    /// <para>
    /// Field scale (2026-07-31): Occult critical encounters are NOT synced to the present
    /// player count — their HP pools are sized for a full 72-player field, so the Dark
    /// Artistry CE (the Necromancer soul-stone drop) carries ~450,000,000 HP against trash
    /// orders of magnitude smaller. The gap is enormous, which is exactly why the real rule
    /// below is RELATIVE: if Square ever syncs CE scaling those pools collapse overnight and
    /// any absolute number written here would silently misclassify every boss.
    /// </para>
    /// </summary>
    public const uint BossHpThresholdFallback = 10_000_000;

    /// <summary>
    /// A boss dwarfs the trash around it, so the jump is a MULTIPLE of the zone's typical
    /// enemy rather than an absolute number — this is what removes the guessed threshold:
    /// once a zone has real samples, its own median sets the line.
    /// </summary>
    public const uint BossHpMultipleOfZoneMedian = 10;

    /// <summary>Samples needed in a zone before trusting its distribution over the fallback.</summary>
    public const int MinZoneSamplesForRelative = 5;

    /// <summary>
    /// Multiple of the zone median an enemy must clear to count as part of a critical
    /// encounter rather than a bystander. Field 2026-07-31: encounter adds run 4-7M HP
    /// (Abductor's Plume, Alabaster Golem, Tiny Apprentice) while the ordinary "Crescent …"
    /// field mobs standing nearby run 780-850k — roughly the zone median. Anything merely
    /// visible while a CE ran was being filed under that CE; this is the separation.
    /// </summary>
    public const uint CriticalEncounterHpMultiple = 3;

    /// <summary>Is this enemy part of the encounter, or just standing near it?</summary>
    public static bool IsCriticalEncounterParticipant(
        bool seenInCe, uint maxHp, uint zoneMedianHp, int zoneSamples)
    {
        if (!seenInCe)
            return false;
        if (zoneSamples < MinZoneSamplesForRelative || zoneMedianHp == 0)
            return true; // not enough to judge — keep the raw sighting rather than hide it
        return maxHp >= zoneMedianHp * CriticalEncounterHpMultiple;
    }

    /// <summary>
    /// A newly observed max-HP at or below this fraction of the stored one means the encounter
    /// was RESCALED, not that we caught it mid-fight — replace the record instead of keeping
    /// the old maximum. North Horn shipped 2026-07-28 with critical encounters unsynced to
    /// player count (450M / 250M pools); South Horn got that fixed after launch, so the same
    /// correction is expected here within days. Without this the table would carry pre-patch
    /// numbers forever and drag every zone median with them.
    /// </summary>
    public const float RescaleDetectionFraction = 0.5f;

    /// <summary>
    /// Max-HP readings below this are garbage, not data. Field 2026-07-31: the Doubled Trouble
    /// CE boss (Conjured Calofisteri) was recorded at 44 HP — a transient spawn-time read that
    /// the rescale rule below then accepted as truth, overwriting its real multi-million pool
    /// and demoting the boss to "trash". Nothing in a level-100 zone has double-digit max HP.
    /// </summary>
    public const uint MinCredibleMaxHp = 1_000;

    /// <summary>
    /// Separate sightings that must agree before a collapsed max-HP is accepted as a real
    /// rescale. A patch is permanent and will read the same every time; spawn-time garbage
    /// will not survive a second look.
    /// </summary>
    public const int RescaleConfirmations = 2;

    /// <summary>Is this max-HP reading worth recording at all?</summary>
    public static bool IsCredibleMaxHp(uint observed) => observed >= MinCredibleMaxHp;

    /// <summary>
    /// Does this reading look like a genuine downward rescale (rather than a low reading we
    /// should ignore)? Credible magnitude AND a collapse against what we already hold.
    /// </summary>
    public static bool LooksLikeRescale(uint stored, uint observed) =>
        stored > 0 && IsCredibleMaxHp(observed) && observed <= stored * RescaleDetectionFraction;

    private readonly IObjectTable _objectTable;
    private readonly Dalamud.Plugin.Services.IFateTable? _fateTable;
    private readonly IClientState _clientState;
    private readonly IPluginLog _log;
    private readonly Debug.DebugLogService? _debugLog;
    private readonly string? _filePath;

    /// <summary>
    /// Keyed by ZONE + NameId, not NameId alone. The same enemy exists in both Horns with
    /// different stats — Persistent Pot is 883,127 HP in South Horn and 188,300 in North
    /// (field 2026-07-31) — so a NameId-only key made them overwrite each other, and the
    /// 4.7x gap between them even looked like a rescale.
    /// </summary>
    private readonly Dictionary<ulong, OccultWeaknessEntry> _entries = new();

    private static ulong Key(ushort territoryId, uint nameId) => ((ulong)territoryId << 32) | nameId;

    /// <summary>Unconfirmed rescale candidates: NameId → (observed value, agreeing sightings).</summary>
    private readonly Dictionary<ulong, (uint Value, int Count)> _pendingRescale = new();
    private DateTime _lastScanUtc = DateTime.MinValue;
    private DateTime _lastSaveUtc = DateTime.MinValue;
    private bool _dirty;
    private bool _ioFaulted;

    public ElementalWeaknessLog(
        IObjectTable objectTable,
        IClientState clientState,
        IPluginLog log,
        string? configDirectory,
        Debug.DebugLogService? debugLog = null,
        Dalamud.Plugin.Services.IFateTable? fateTable = null)
    {
        _objectTable = objectTable;
        _fateTable = fateTable;
        _clientState = clientState;
        _log = log;
        _debugLog = debugLog;
        _filePath = string.IsNullOrEmpty(configDirectory)
            ? null
            : Path.Combine(configDirectory, "occult-weaknesses.json");
        LoadSeed(); // shipped baseline first…
        Load();     // …then this character's own observations, which win

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
                e.BelongsToCriticalEncounter =
                    IsCriticalEncounterParticipant(e.SeenInCriticalEncounter, e.MaxHp, median, samples);
            }

            return all.OrderByDescending(e => e.Kind).ThenByDescending(e => e.MaxHp).ToList();
        }
    }

    /// <summary>Path of the persisted table (shown in the Debug tab so it can be opened).</summary>
    public string? FilePath => _filePath;

    /// <summary>True when this enemy has been observed weak to the given element.</summary>
    public bool IsWeakTo(uint nameId, OccultElement element) =>
        _entries.TryGetValue(Key((ushort)_clientState.TerritoryType, nameId), out var e)
        && (e.Elements & element) != 0;

    /// <summary>
    /// Every revealed weakness for an enemy, or null when nothing has been revealed. Null must
    /// be read as "unknown", never as "no weakness" — an unrevealed weakness is not evidence
    /// of its absence, and treating it as such would starve the nuke picker.
    /// </summary>
    public OccultElement? KnownWeakness(uint nameId) =>
        _entries.TryGetValue(Key((ushort)_clientState.TerritoryType, nameId), out var w)
        && w.Elements != OccultElement.None
            ? w.Elements
            : null;

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
            var (ceActive, ceName) = ReadCriticalEncounter();

            // EVERY hostile enemy is recorded, not just ones with a revealed weakness: the
            // boss/trash line is the zone MEDIAN, and a table containing only Libra'd mobs is a
            // biased sample (reveal the boss and nothing else, and the median becomes
            // boss-sized). Weakness flags are filled in as and when they get revealed.
            foreach (var obj in _objectTable)
            {
                if (obj.ObjectKind != ObjectKind.BattleNpc || obj is not IBattleNpc npc)
                    continue;
                if (npc.BattleNpcKind == BattleNpcSubKind.Pet)
                    continue;
                if (npc.MaxHp == 0)
                    continue;
                var (inFate, fateName) = ReadFate(npc);
                Record(npc, ReadRevealedElements(npc), territory, now, ceActive, ceName, inFate, fateName);
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
    private unsafe (bool Active, string Name) ReadCriticalEncounter()
    {
        try
        {
            var container = FFXIVClientStructs.FFXIV.Client.Game.InstanceContent.DynamicEventContainer.GetInstance();
            if (container == null || container->CurrentEventIndex < 0)
                return (false, string.Empty);

            var ev = container->GetCurrentEvent();
            var name = ev == null ? string.Empty : ev->Name.ToString();
            return (true, name);
        }
        catch
        {
            return (false, string.Empty); // unknown CE state just means "not marked as a boss"
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

    /// <summary>
    /// The FATE this enemy belongs to: its id straight off the object, resolved to a name via
    /// the fate table. Id 0 = not in a FATE. A live id with no matching table row still counts
    /// as a FATE (unnamed) — the stamp is the fact, the name is a convenience.
    /// </summary>
    private unsafe (bool InFate, string Name) ReadFate(IBattleNpc npc)
    {
        try
        {
            var obj = (FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)npc.Address;
            if (obj == null || obj->FateId == 0)
                return (false, string.Empty);

            var id = obj->FateId;
            if (_fateTable != null)
            {
                foreach (var fate in _fateTable)
                {
                    if (fate.FateId == id)
                        return (true, fate.Name.TextValue ?? string.Empty);
                }
            }

            return (true, string.Empty);
        }
        catch
        {
            return (false, string.Empty);
        }
    }

    private void Record(IBattleNpc npc, OccultElement element, ushort territory, DateTime now, bool ceActive, string ceName, bool inFate, string fateName)
    {
        var nameId = npc.NameId;
        if (nameId == 0)
            return;

        var key = Key(territory, nameId);
        if (!_entries.TryGetValue(key, out var entry))
        {
            entry = new OccultWeaknessEntry
            {
                NameId = nameId,
                Name = npc.Name?.TextValue ?? $"#{nameId}",
                TerritoryId = territory,
            };
            _entries[key] = entry;
        }

        var isNew = (entry.Elements & element) != element;
        entry.Elements |= element;

        // Max-HP upkeep. Normally keep the largest ever seen (we may meet an enemy mid-fight),
        // but a value that has COLLAPSED means the encounter was rescaled by a patch — take
        // the new number as truth so the table (and the zone median) stay current.
        if (!IsCredibleMaxHp(npc.MaxHp))
        {
            // Spawn-time / teardown garbage — never let it touch a recorded pool.
        }
        else if (npc.MaxHp > entry.MaxHp)
        {
            entry.MaxHp = npc.MaxHp;
            _pendingRescale.Remove(key);
        }
        else if (LooksLikeRescale(entry.MaxHp, npc.MaxHp))
        {
            // Collapsed pool: a patch re-syncing the encounter, or a bad frame? Only a value
            // that shows up again on a LATER sighting is allowed to replace the truth.
            var agreeing = _pendingRescale.TryGetValue(key, out var pending) && pending.Value == npc.MaxHp
                ? pending.Count + 1
                : 1;

            if (agreeing >= RescaleConfirmations)
            {
                _debugLog?.Log(Debug.DebugLogCategory.General, Debug.DebugLogSeverity.Info,
                    $"occult rescale confirmed: {entry.Name} max HP {entry.MaxHp:N0} -> {npc.MaxHp:N0}");
                entry.MaxHp = npc.MaxHp;
                _pendingRescale.Remove(key);
            }
            else
            {
                _pendingRescale[key] = (npc.MaxHp, agreeing);
            }
        }
        else
        {
            _pendingRescale.Remove(key); // a normal reading clears any half-formed suspicion
        }
        if (inFate)
        {
            entry.SeenInFate = true;
            if (!string.IsNullOrEmpty(fateName))
                entry.Fate = fateName;
        }
        if (ceActive)
        {
            entry.SeenInCriticalEncounter = true;
            if (!string.IsNullOrEmpty(ceName))
                entry.CriticalEncounter = ceName;
        }
        entry.LastSeenUtc = now.ToString("O");
        _dirty = true;

        if (isNew)
        {
            _debugLog?.Log(Debug.DebugLogCategory.General, Debug.DebugLogSeverity.Info,
                $"occult weakness learned: {entry.Name} — {entry.Elements} " +
                $"({entry.MaxHp:N0} HP, territory {territory})");
        }
    }

    /// <summary>
    /// The table shipped with the plugin (embedded <c>Data/OccultWeaknessSeed.json</c>),
    /// gathered on Debug builds and baked in so every toon starts with the reference rather
    /// than an empty file. Loaded FIRST so a character's own observations override it.
    /// </summary>
    private void LoadSeed()
    {
        try
        {
            var asm = typeof(ElementalWeaknessLog).Assembly;
            var resource = asm.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("OccultWeaknessSeed.json", StringComparison.Ordinal));
            if (resource is null)
                return;

            using var stream = asm.GetManifestResourceStream(resource);
            if (stream is null)
                return;
            using var reader = new StreamReader(stream);
            var list = JsonSerializer.Deserialize<List<OccultWeaknessEntry>>(reader.ReadToEnd());
            if (list is null)
                return;

            foreach (var e in list)
            {
                if (e.NameId != 0)
                    _entries[Key(e.TerritoryId, e.NameId)] = e;
            }

            _log.Information("[OccultWeakness] seeded {0} enemies from the shipped table", list.Count);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "[OccultWeakness] shipped seed unreadable — starting from the local file only");
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
                    _entries[Key(e.TerritoryId, e.NameId)] = e;
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
