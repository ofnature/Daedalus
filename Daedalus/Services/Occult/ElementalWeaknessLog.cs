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

/// <summary>
/// What kind of enemy this is. The zone uses two different words for its two kinds of named
/// target, and they are not interchangeable: a critical encounter has a <b>boss</b>, a FATE has
/// an <b>elite</b>. Ordered by significance so the table can sort on it.
/// </summary>
public enum OccultEnemyKind : byte
{
    /// <summary>Ordinary field mob.</summary>
    Trash = 0,

    /// <summary>
    /// Present in the object table and recorded like anything else, but not an enemy — either an
    /// untargetable encounter mechanic (Pages, Spheres, Beacons, Plumes) or a targetable
    /// FRIENDLY (the Persistent Pot you escort, the treasure bunny). Never attackable, so no
    /// weakness can ever be revealed on it. Kept out of coverage counts.
    /// </summary>
    MechanicObject = 1,

    /// <summary>Big HP pool with no encounter attached — a field notorious spawn.</summary>
    FieldNotorious = 2,

    /// <summary>A FATE's named target. The zone calls these ELITES, not bosses.</summary>
    FateElite = 3,

    /// <summary>A critical encounter's named target. These are the BOSSES.</summary>
    CriticalEncounterBoss = 4,
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
    /// How many scan ticks this enemy has been seen on. Only meaningful next to
    /// <see cref="EverTargetable"/> — it is what says whether we have had a fair chance to
    /// observe targetability yet, so an enemy seen once is never judged.
    /// </summary>
    public int Sightings { get; set; }

    /// <summary>
    /// Ever observed as a targetable object. STICKY once true, because plenty of real bosses
    /// spend part of a fight untargetable — Company of Stone's Megaloknight cannot be hit until
    /// eight Occult Knights are dead. Only "seen many times, never once targetable" means a
    /// mechanic object, and only that can never be Libra'd.
    /// </summary>
    public bool EverTargetable { get; set; }

    /// <summary>
    /// Ever observed as something a damage action could be used on. STICKY once true.
    /// <para>
    /// ⚠ DIAGNOSTIC ONLY — do NOT classify on this. Measured 2026-08-11 against a live 266-row
    /// table: of the 139 rows carrying a revealed weakness (so provably attacked at some point),
    /// <b>134 read EverAttackable = false</b>. The probe behind it asks whether a damage action
    /// could be used on the target <i>right now</i>, which is range-limited, while this log scans
    /// the whole object table — so nearly everything is out of range when sampled. A 96% false
    /// negative rate makes it useless as evidence, and it briefly classified Nammu (738 sightings,
    /// Lightning weakness recorded) as "not an enemy".
    /// </para>
    /// </summary>
    public bool EverAttackable { get; set; }

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
    /// Scan ticks an enemy must be seen on before "never targetable" counts as evidence rather
    /// than luck. At a 2s scan interval this is under a minute of exposure, which any encounter
    /// member clears easily, while a mob glimpsed once in passing is never judged.
    /// </summary>
    public const int MinSightingsForTargetabilityVerdict = 20;

    /// <summary>
    /// Not an enemy: seen plenty of times, never once attackable, and with no weakness ever
    /// revealed. Two different things land here and both dilute the table the same way —
    /// untargetable encounter mechanics (the Forbidden Folios Pages, Tiny Terror's Spheres,
    /// Beacons, Plumes) and targetable FRIENDLIES (the Persistent Pot you escort in the pot
    /// FATEs, the treasure bunny). Neither can ever be Libra'd.
    /// <para>
    /// Evidence-based ON PURPOSE. Culling by name would delete real adds that ARE killed
    /// (Alabaster Golem, Long-dead Pirate, Tiny Apprentice all sit in the same HP band), so the
    /// game's own attackability probe decides rather than a hand-written list. Attackability
    /// subsumes targetability — <c>IsPlayerAttackable</c> already requires a targetable, living
    /// object — so it catches the friendlies that a targetable check would wave through.
    /// </para>
    /// </summary>
    public static bool IsMechanicObject(OccultWeaknessEntry entry) =>
        !entry.EverTargetable
        && entry.Elements == OccultElement.None
        && entry.Sightings >= MinSightingsForTargetabilityVerdict
        && WasTargetabilityActuallyRecorded(entry);

    /// <summary>
    /// When <see cref="OccultWeaknessEntry.EverTargetable"/> began being recorded (v0.1.58).
    /// </summary>
    public static readonly DateTime TargetabilityTrackedSinceUtc = new(2026, 8, 10, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// Has this row been seen since the targetability flag existed?
    /// <para>
    /// Without this the verdict is unsound in the one direction that matters. A row last seen
    /// BEFORE v0.1.58 reads <c>EverTargetable == false</c> because nothing ever wrote the field,
    /// not because the thing was unreachable — and plenty of those rows carry hundreds of
    /// sightings, so the evidence gate waves them straight through. Measured 2026-08-14: 129 of
    /// 273 rows had 20+ sightings while only 30 had been seen since the flag shipped, so the
    /// unguarded rule would have deleted real enemies on the strength of a field that was never
    /// populated. A row with no recorded targetability is UNKNOWN, and unknown is not a verdict.
    /// </para>
    /// </summary>
    public static bool WasTargetabilityActuallyRecorded(OccultWeaknessEntry entry)
        => DateTime.TryParse(
               entry.LastSeenUtc,
               System.Globalization.CultureInfo.InvariantCulture,
               System.Globalization.DateTimeStyles.RoundtripKind,
               out var seen)
           && seen.ToUniversalTime() >= TargetabilityTrackedSinceUtc;

    /// <summary>
    /// Anything that can never yield a weakness. Today that means untargetable mechanics only.
    /// <para>
    /// FRIENDLIES ARE NOT DETECTED AUTOMATICALLY. The escort pot is targetable, so only an
    /// attackability test could catch it, and that probe is range-limited to the point of
    /// uselessness here (see <see cref="OccultWeaknessEntry.EverAttackable"/>). Until a
    /// range-independent hostility signal is found, friendlies go in
    /// <see cref="NonCombatNameIds"/> by hand — a short, confirmed list beats a signal that is
    /// wrong 96% of the time.
    /// </para>
    /// </summary>
    public static bool IsNotAnEnemy(OccultWeaknessEntry entry) => IsMechanicObject(entry);

    /// <summary>
    /// Things the object table reports as hostile NPCs that are not enemies and can never carry
    /// an elemental weakness, so they only ever dilute the table. Field 2026-08-10, cleaning up
    /// South Horn: the Striking Dummy sat in the trash bucket at 4.7M HP — far above the ~700k
    /// the zone's real trash runs — while the traps and the treasure bunny sat below it at 188k.
    /// Both ends are noise; the dummy is the one that also distorts the "is this a boss" maths,
    /// since every classification here is RELATIVE to the zone median.
    /// </summary>
    public static readonly IReadOnlySet<uint> NonCombatNameIds = new HashSet<uint>
    {
        541,    // Striking Dummy — training dummy, both Horns
        7248,   // Happy Bunny — treasure-hunt bunny, both Horns
        7958,   // Hidden Trap — coffer trap, North Horn
        13967,  // Trap — coffer trap, South Horn
        13742,  // Persistent Pot — the pot you ESCORT in the pot FATEs (user-confirmed friendly).
                // Its encounter stamps are junk for the same reason: a friendly wandering about
                // picks up whichever CE/FATE happened to be running ("Flame of Dusk" in South,
                // "Imbalanced Diet" in North). The two other Persistent Pot NameIds (14770 in
                // Daylight Pottery, 14773 in In a Pot of Bother) are very likely the same escort
                // object at different scaling, but are NOT hardcoded here — the attackability
                // evidence will settle them on the next run of those FATEs rather than guessing.
    };

    /// <summary>
    /// Whether a stored row earns its place in the table. Applied when loading BOTH the shipped
    /// seed and the character's own file, so an existing file cleans itself up on the next
    /// launch rather than needing a manual scrub — which matters because the log rewrites that
    /// file wholesale and would other­wise put the junk straight back.
    /// <para>
    /// A row that carries a learned element is ALWAYS kept, whatever else is wrong with it:
    /// elements only appear when something reveals them (Occult Libra on Red Mage), so they are
    /// the expensive part of this table and must never be thrown away over a bad HP sample.
    /// </para>
    /// </summary>
    public static bool IsWorthKeeping(OccultWeaknessEntry entry)
    {
        if (entry.NameId == 0)
            return false;
        if (NonCombatNameIds.Contains(entry.NameId))
            return false;

        // Everything below is a data-quality judgement, so a known element overrides it.
        if (entry.Elements != OccultElement.None)
            return true;

        // Proven-unreachable mechanics: seen plenty of times SINCE targetability was recorded,
        // never once targetable, and carrying no element. Libra cannot reach them, so they can
        // never contribute anything — they only pad the table and drag the coverage figures
        // down. Dropped here rather than merely hidden, so the character's own file cleans
        // itself up on the next launch (the log rewrites that file wholesale and would
        // otherwise put them straight back).
        //
        // Self-correcting on purpose: nothing learned is lost, because the rule requires no
        // element. If one of these turns out to be a real add after all, the next sighting that
        // finds it targetable re-adds it and it is never trimmed again.
        if (IsNotAnEnemy(entry))
            return false;

        if (string.IsNullOrWhiteSpace(entry.Name))
            return false;

        // A STORED row with no credible max HP never got a real reading in the first place —
        // 0 included. New in-memory entries are built in Record, not loaded, so this cannot
        // discard an enemy that simply has not been measured yet.
        return IsCredibleMaxHp(entry.MaxHp);
    }

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
    /// What kind of enemy this is, from observed facts. The big-HP line is the zone's own median
    /// enemy HP times <see cref="BossHpMultipleOfZoneMedian"/> once the zone has enough samples;
    /// before that it falls back to <see cref="BossHpThresholdFallback"/>. Above that line the
    /// encounter it belongs to decides the WORD: critical encounters have bosses, FATEs have
    /// elites. Pure — the whole rule is here.
    /// </summary>
    /// <param name="isEncounterTopMember">
    /// This is the largest thing recorded in its named encounter — i.e. that encounter's target.
    /// </param>
    public static OccultEnemyKind Classify(
        uint maxHp, bool seenInCriticalEncounter, uint zoneMedianHp, int zoneSamples,
        bool seenInFate = false, bool isMechanicObject = false, bool isEncounterTopMember = false)
    {
        // Checked before anything else: an untargetable mechanic object can carry an
        // encounter-sized HP pool (the Forbidden Folios Pages are 74M apiece) and would
        // otherwise be filed as a boss.
        if (isMechanicObject)
            return OccultEnemyKind.MechanicObject;

        // An encounter's named target is a boss/elite BECAUSE it is the target, not because of
        // its HP. Field 2026-08-11: Nammu, the ELITE of Rough Waters, is 152,523 HP — a fifth of
        // ordinary South Horn trash — and the HP line filed it as trash.
        // PROMOTE-ONLY on purpose: this can raise a small named target, never demote anything.
        // Demoting non-target members would also stop fat encounter adds reading as bosses,
        // which is arguably more correct, but it would reclassify a great many rows on a rule
        // that has not been checked in the field — so the HP line below still has the last word
        // for everything that is not its encounter's target.
        if (isEncounterTopMember && (seenInCriticalEncounter || seenInFate))
        {
            return seenInCriticalEncounter
                ? OccultEnemyKind.CriticalEncounterBoss
                : OccultEnemyKind.FateElite;
        }

        var line = zoneSamples >= MinZoneSamplesForRelative && zoneMedianHp > 0
            ? zoneMedianHp * BossHpMultipleOfZoneMedian
            : BossHpThresholdFallback;

        if (maxHp < line)
            return OccultEnemyKind.Trash;
        if (seenInCriticalEncounter)
            return OccultEnemyKind.CriticalEncounterBoss;
        return seenInFate ? OccultEnemyKind.FateElite : OccultEnemyKind.FieldNotorious;
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

            // Biggest recorded member per named encounter — that is the boss/elite. Only
            // targetable members count, or an untargetable 74M Page would claim the title from
            // the boss it is a mechanic of.
            var encounterTop = new Dictionary<string, uint>();
            foreach (var e in all)
            {
                var key = EncounterKey(e);
                if (key is null || IsNotAnEnemy(e))
                    continue;
                if (!encounterTop.TryGetValue(key, out var best) || e.MaxHp > best)
                    encounterTop[key] = e.MaxHp;
            }

            foreach (var e in all)
            {
                var median = ZoneMedianHp(e.TerritoryId);
                var samples = _entries.Values.Count(x => x.TerritoryId == e.TerritoryId && x.MaxHp > 0);
                var key = EncounterKey(e);
                var isTop = key is not null
                    && encounterTop.TryGetValue(key, out var top)
                    && e.MaxHp == top
                    && !IsNotAnEnemy(e);

                e.Kind = Classify(e.MaxHp, e.SeenInCriticalEncounter, median, samples,
                    e.SeenInFate, IsNotAnEnemy(e), isTop);
                e.BelongsToCriticalEncounter =
                    IsCriticalEncounterParticipant(e.SeenInCriticalEncounter, e.MaxHp, median, samples);
            }

            return all.OrderByDescending(e => e.Kind).ThenByDescending(e => e.MaxHp).ToList();
        }
    }

    /// <summary>
    /// Groups an entry by the named encounter it belongs to, CE taking precedence over FATE.
    /// Null for ordinary field enemies, which have no encounter to be the target of.
    /// </summary>
    private static string? EncounterKey(OccultWeaknessEntry e)
    {
        if (e.SeenInCriticalEncounter && !string.IsNullOrWhiteSpace(e.CriticalEncounter))
            return $"c{e.TerritoryId}|{e.CriticalEncounter}";
        if (e.SeenInFate && !string.IsNullOrWhiteSpace(e.Fate))
            return $"f{e.TerritoryId}|{e.Fate}";
        return null;
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
    public OccultElement? KnownWeakness(uint nameId) => KnownWeakness(nameId, null);

    /// <summary>
    /// As above, but falls back to another row with the SAME NAME in this zone when the exact
    /// NameId is unknown.
    /// <para>
    /// The game gives one enemy several NameIds — Crescent Void Viper is 13896 and 13907,
    /// Animated Doll 13893 and 13894 — and the weakness is a property of the creature, not of
    /// the id. Verified 2026-08-11: in every same-name pair where both ids are known they AGREE,
    /// and RSR's independent table gives the same element for the unknown twin. Without this a
    /// perfectly well-known enemy reads as unknown purely because this instance spawned under
    /// the other id, and the nuke picker falls back to Fire.
    /// </para>
    /// </summary>
    public OccultElement? KnownWeakness(uint nameId, string? name)
    {
        var territory = (ushort)_clientState.TerritoryType;

        if (_entries.TryGetValue(Key(territory, nameId), out var exact) && exact.Elements != OccultElement.None)
            return exact.Elements;

        if (string.IsNullOrWhiteSpace(name))
            return null;

        foreach (var e in _entries.Values)
        {
            if (e.TerritoryId == territory
                && e.Elements != OccultElement.None
                && string.Equals(e.Name, name, StringComparison.Ordinal))
            {
                return e.Elements;
            }
        }

        return null;
    }

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
        if (NonCombatNameIds.Contains(nameId))
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

        // Targetability evidence. Sticky once true: real bosses spend phases untargetable
        // (Company of Stone's Megaloknight until eight knights die), so only "seen many times,
        // never once targetable" identifies a mechanic object.
        entry.Sightings++;
        if (npc.IsTargetable)
            entry.EverTargetable = true;
        if (!entry.EverAttackable && Daedalus.Services.Targeting.EnemyAttackability.IsPlayerAttackable(npc))
            entry.EverAttackable = true;

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

            var kept = 0;
            foreach (var e in list)
            {
                if (!IsWorthKeeping(e))
                    continue;
                _entries[Key(e.TerritoryId, e.NameId)] = e;
                kept++;
            }

            _log.Information("[OccultWeakness] seeded {0} enemies from the shipped table", kept);
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

            // Filtering on the way IN is what makes the cleanup stick: Save() rewrites this file
            // wholesale from _entries, so anything dropped here is gone from disk after the next
            // save rather than reappearing forever.
            var dropped = 0;
            foreach (var e in list)
            {
                if (!IsWorthKeeping(e))
                {
                    dropped++;
                    continue;
                }

                _entries[Key(e.TerritoryId, e.NameId)] = e;
            }

            if (dropped > 0)
                _log.Information("[OccultWeakness] dropped {0} non-enemy/unusable row(s) from the local table", dropped);
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
