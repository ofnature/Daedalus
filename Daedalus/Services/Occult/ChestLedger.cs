using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using Daedalus.Config;
using Daedalus.Services.Drawing;

namespace Daedalus.Services.Occult;

/// <summary>
/// Records every coffer seen in an Occult zone — position and tier — so the questions we can't
/// answer today become answerable from real samples: is a spawn point's tier fixed? do gold
/// coffers repeat in the same places? which spots are worth a detour?
/// <para>
/// Deliberately dumb. It collects and it counts; it predicts nothing. Any predictor built later
/// (see docs/occult-pot-treasure-predictor.md) reads this rather than guessing.
/// </para>
/// </summary>
public sealed class ChestLedger
{
    /// <summary>Two sightings closer than this are the same spawn point.</summary>
    public const float SameSpotYalms = 3f;

    /// <summary>
    /// Ceiling on stored spots, so a long-lived config can't grow without bound. Generous —
    /// a Horn has far fewer spawn points than this.
    /// </summary>
    public const int MaxEntries = 500;

    private readonly PhantomConfig? _config;
    private readonly IObjectTable? _objectTable;
    private readonly IClientState? _clientState;
    private readonly IDataManager? _dataManager;
    private readonly System.Action? _save;

    /// <summary>
    /// An EventObj coffer that vanishes while you're standing on it was opened. Beyond this
    /// distance a despawn is more likely to be you walking out of range.
    /// </summary>
    public const float OpenProximityYalms = 6f;

    private readonly Dictionary<uint, uint> _sceneryIdByBaseId = [];

    private bool _dirty;
    private DateTime _lastSaveUtc = DateTime.MinValue;

#if DEBUG
    /// <summary>
    /// Coffers currently in view, so an open can be spotted as a TRANSITION. Debug-only, like
    /// the collection it serves — Release never populates it.
    /// </summary>
    private readonly Dictionary<ulong, TrackedCoffer> _tracked = [];

    private sealed class TrackedCoffer
    {
        public Vector3 Position;
        public TreasureTier Tier;
        public bool WasOpened;
        public bool IsEventObj;
        public float LastDistance;
        public bool Counted;
    }
#endif

    /// <summary>Test seam.</summary>
    internal Func<DateTime> UtcNow { get; set; } = () => DateTime.UtcNow;

    public ChestLedger(
        PhantomConfig? config,
        IObjectTable? objectTable,
        IClientState? clientState,
        IDataManager? dataManager,
        System.Action? save)
    {
        _config = config;
        _objectTable = objectTable;
        _clientState = clientState;
        _dataManager = dataManager;
        _save = save;
    }

    /// <summary>How many distinct spawn points are on record for the zone you're standing in.</summary>
    public int EntriesForCurrentZone()
    {
        if (_config is null || _clientState is null)
            return 0;

        var zone = (ushort)_clientState.TerritoryType;
        var count = 0;
        foreach (var entry in _config.ChestLedger)
        {
            if (entry.Zone == zone)
                count++;
        }

        return count;
    }

    /// <summary>
    /// Framework tick — cheap, and only inside an Occult zone.
    /// <para>
    /// DEBUG-ONLY collection, same as the elemental weakness census: this is dev-time data
    /// gathering, so shipped builds have no business scanning the object table or growing the
    /// config. Release still READS the ledger (see <see cref="EntriesForCurrentZone"/>), so once
    /// the data is collected and baked into a seed the fleet gets the benefit without the cost.
    /// </para>
    /// </summary>
    public void Update()
    {
#if !DEBUG
        return;
#else
        if (_config is null || _objectTable is null || _clientState is null)
            return;

        var zone = (ushort)_clientState.TerritoryType;
        if (!Data.PhantomJobData.OccultTerritoryIds.Contains(zone))
            return;

        var now = UtcNow();
        var player = _objectTable.LocalPlayer;
        var seen = new HashSet<ulong>();

        foreach (var obj in _objectTable)
        {
            // Deliberately NOT the guide-line filter: that requires IsTargetable, and an opened
            // coffer stops being targetable at the exact moment we care about.
            var isEventObj = obj.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.EventObj;
            var isCoffer = obj.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Treasure
                || (isEventObj && IsCofferName(obj.Name.TextValue));
            if (!isCoffer)
                continue;

            seen.Add(obj.GameObjectId);

            // EventObj coffers carry the tier in their NAME ("Gold Coffer"); Treasure objects
            // carry it in their scenery model.
            var tier = isEventObj
                ? TierFromCofferName(obj.Name.TextValue)
                : WorldLineSelector.TierFromSceneryId(ResolveSceneryId(obj.BaseId));

            if (!_tracked.TryGetValue(obj.GameObjectId, out var tracked))
            {
                tracked = new TrackedCoffer();
                _tracked[obj.GameObjectId] = tracked;
            }

            var nowOpened = !isEventObj && IsOpened(obj);
            var justOpened = BecameOpened(tracked.WasOpened, nowOpened);

            tracked.Position = obj.Position;
            tracked.Tier = tier;
            tracked.WasOpened = nowOpened;
            tracked.IsEventObj = isEventObj;
            tracked.LastDistance = player is null ? float.MaxValue : Vector3.Distance(player.Position, obj.Position);

            if (justOpened)
                tracked.Counted = true;

            if (Record(_config.ChestLedger, zone, obj.Position, tier, now, opened: justOpened))
                _dirty = true;
        }

        // Despawn pass: an EventObj coffer has no Opened flag, so vanishing next to you is the
        // only evidence it was looted.
        if (_tracked.Count > 0)
        {
            var gone = new List<ulong>();
            foreach (var kv in _tracked)
            {
                if (seen.Contains(kv.Key))
                    continue;

                gone.Add(kv.Key);
                var coffer = kv.Value;
                if (!coffer.IsEventObj || coffer.Counted || !DespawnedIntoPickup(coffer.LastDistance))
                    continue;

                if (Record(_config.ChestLedger, zone, coffer.Position, coffer.Tier, now, opened: true))
                    _dirty = true;
            }

            foreach (var id in gone)
                _tracked.Remove(id);
        }

        FlushIfDue();
#endif
    }

    /// <summary>
    /// Merge one sighting into the ledger. Returns true when something changed.
    /// <para>
    /// Pure and static so the merge rules are testable without the game: same zone within
    /// <see cref="SameSpotYalms"/> is the same spot, and a spot that later reports a BETTER tier
    /// is upgraded — an unrecognised model reads as Unknown, and Unknown should never overwrite
    /// a tier we actually identified.
    /// </para>
    /// </summary>
    /// <summary>
    /// Pot coffers are EventObj and name themselves — "Gold Coffer", "Silver Coffer". That is a
    /// far better handle than their BaseIds, only one of which we have ever observed.
    /// </summary>
    public static bool IsCofferName(string? name) =>
        !string.IsNullOrWhiteSpace(name) && name.Contains("Coffer", StringComparison.OrdinalIgnoreCase);

    /// <summary>Tier from a coffer's display name.</summary>
    public static TreasureTier TierFromCofferName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return TreasureTier.Unknown;
        if (name.Contains("Gold", StringComparison.OrdinalIgnoreCase))
            return TreasureTier.Gold;
        if (name.Contains("Silver", StringComparison.OrdinalIgnoreCase))
            return TreasureTier.Silver;
        if (name.Contains("Bronze", StringComparison.OrdinalIgnoreCase))
            return TreasureTier.Bronze;

        return TreasureTier.Unknown;
    }

    /// <summary>
    /// An open is the TRANSITION, not the state — a coffer reads Opened for as long as it lingers,
    /// so counting the state would count it once per frame.
    /// </summary>
    public static bool BecameOpened(bool previouslyOpened, bool nowOpened) => !previouslyOpened && nowOpened;

    /// <summary>An EventObj coffer vanishing this close to you was looted, not walked away from.</summary>
    public static bool DespawnedIntoPickup(float lastDistanceYalms) =>
        lastDistanceYalms <= OpenProximityYalms;

    public static bool Record(
        List<ChestLedgerEntry> ledger, ushort zone, Vector3 position, TreasureTier tier, DateTime nowUtc,
        bool opened = false)
    {
        if (ledger is null)
            return false;

        var stamp = new DateTimeOffset(DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc)).ToUnixTimeSeconds();
        var tierName = tier.ToString();

        foreach (var entry in ledger)
        {
            if (entry.Zone != zone)
                continue;
            if (Vector3.DistanceSquared(new Vector3(entry.X, entry.Y, entry.Z), position) > SameSpotYalms * SameSpotYalms)
                continue;

            // Same spot, already counted this visit? Only re-count once the sighting is a
            // genuinely new one — a chest sits there for minutes and Update runs every frame.
            var changed = false;
            if (stamp - entry.LastSeenUnixSeconds >= 60)
            {
                entry.TimesSeen++;
                changed = true;
            }

            if (opened)
            {
                entry.TimesOpened++;
                changed = true;
            }

            if (tier != TreasureTier.Unknown && entry.Tier != tierName)
            {
                entry.Tier = tierName;
                changed = true;
            }

            if (changed)
                entry.LastSeenUnixSeconds = stamp;

            return changed;
        }

        if (ledger.Count >= MaxEntries)
            return false;

        ledger.Add(new ChestLedgerEntry
        {
            Zone = zone,
            X = position.X,
            Y = position.Y,
            Z = position.Z,
            Tier = tierName,
            TimesSeen = 1,
            TimesOpened = opened ? 1 : 0,
            FirstSeenUnixSeconds = stamp,
            LastSeenUnixSeconds = stamp,
        });

        return true;
    }

    /// <summary>
    /// Dump the ledger as JSON for baking into a shipped seed — the same route the elemental
    /// weakness table took (collect in Debug, commit the file, Release loads it). Returns the
    /// path written, or null when there's nothing to write.
    /// </summary>
    public string? ExportSeed(string directory)
    {
        if (_config is null || _config.ChestLedger.Count == 0 || string.IsNullOrWhiteSpace(directory))
            return null;

        var path = System.IO.Path.Combine(directory, "occult-chests.json");
        var json = System.Text.Json.JsonSerializer.Serialize(
            _config.ChestLedger,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        System.IO.File.WriteAllText(path, json);
        return path;
    }

    /// <summary>Batch writes — the config file must not be rewritten every frame.</summary>
    private void FlushIfDue()
    {
        if (!_dirty)
            return;

        var now = UtcNow();
        if ((now - _lastSaveUtc).TotalSeconds < 30)
            return;

        _dirty = false;
        _lastSaveUtc = now;
        _save?.Invoke();
    }

    /// <summary>
    /// The coffer's Opened flag, straight off the ClientStructs Treasure struct — the same read
    /// BOCCHI uses. Fails closed: an unreadable struct reads as "not opened", which loses a
    /// sample rather than inventing one.
    /// </summary>
    private static unsafe bool IsOpened(IGameObject obj)
    {
        try
        {
            if (obj.Address == nint.Zero)
                return false;

            var treasure = (FFXIVClientStructs.FFXIV.Client.Game.Object.Treasure*)obj.Address;
            return treasure != null
                && treasure->Flags.HasFlag(FFXIVClientStructs.FFXIV.Client.Game.Object.Treasure.TreasureFlags.Opened);
        }
        catch
        {
            return false;
        }
    }

    private uint ResolveSceneryId(uint baseId)
    {
        if (_sceneryIdByBaseId.TryGetValue(baseId, out var cached))
            return cached;

        var sceneryId = _dataManager?.GetExcelSheet<Lumina.Excel.Sheets.Treasure>()?
            .GetRowOrDefault(baseId)?.SGB.RowId ?? 0u;
        _sceneryIdByBaseId[baseId] = sceneryId;
        return sceneryId;
    }
}
