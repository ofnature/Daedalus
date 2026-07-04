using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;

namespace Daedalus.Services.Farm;

/// <summary>A mob that drops the looked-up item. NameId 0 = couldn't resolve to a BNpcName row.</summary>
public sealed record DropperCandidate(string Name, string LevelText, uint NameId, ulong GarlandId, string ZoneName);

/// <summary>A mob's representative spawn location from its Garland mob doc (map coordinates).</summary>
public sealed record MobLocation(string ZoneName, float MapX, float MapY);

/// <summary>
/// "Which mobs drop this item, and where" lookup via GarlandTools (same data source Monster Loot
/// Hunter uses — MLH itself has no IPC). Async fire-and-forget with in-memory caches; mob names
/// resolve to BNpcName row ids through Lumina (English sheet — Garland names are English).
/// Drop tables are server-side, so a web source is mandatory.
/// </summary>
public sealed class GarlandDropSource : IDisposable
{
    private const string ItemUrl = "https://www.garlandtools.org/db/doc/item/en/3/{0}.json";
    private const string MobUrl = "https://www.garlandtools.org/db/doc/mob/en/2/{0}.json";
    private const string CoreDataUrl = "https://www.garlandtools.org/db/doc/core/en/3/data.json";

    private readonly IDataManager _dataManager;
    private readonly IPluginLog _log;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly object _lock = new();

    private readonly Dictionary<uint, IReadOnlyList<DropperCandidate>> _dropperCache = new();
    private readonly Dictionary<ulong, MobLocation?> _locationCache = new();
    private Dictionary<uint, string>? _locationNames;
    private Dictionary<string, uint>? _bnpcNameLookup;

    public bool IsBusy { get; private set; }
    public string? LastError { get; private set; }

    public GarlandDropSource(IDataManager dataManager, IPluginLog log)
    {
        _dataManager = dataManager;
        _log = log;
    }

    /// <summary>Cached droppers for the item, or null when never looked up (or in flight).</summary>
    public IReadOnlyList<DropperCandidate>? TryGetCached(uint itemId)
    {
        lock (_lock)
            return _dropperCache.TryGetValue(itemId, out var result) ? result : null;
    }

    /// <summary>Cached spawn location for a Garland mob id. Outer null = not fetched yet; inner null = fetched, no coords.</summary>
    public bool TryGetMobLocation(ulong garlandId, out MobLocation? location)
    {
        lock (_lock)
            return _locationCache.TryGetValue(garlandId, out location);
    }

    /// <summary>Kicks off the item lookup; results appear via <see cref="TryGetCached"/>.</summary>
    public void BeginLookup(uint itemId)
    {
        lock (_lock)
        {
            if (IsBusy || _dropperCache.ContainsKey(itemId))
                return;
            IsBusy = true;
            LastError = null;
        }

        // Lumina index built on the caller's (framework) thread; background task never touches it.
        var nameLookup = GetBNpcNameLookup();

        _ = Task.Run(async () =>
        {
            try
            {
                var locations = await GetLocationNamesAsync().ConfigureAwait(false);
                var json = await _http.GetStringAsync(string.Format(ItemUrl, itemId)).ConfigureAwait(false);
                var droppers = ParseDroppers(json, nameLookup, locations);
                lock (_lock)
                    _dropperCache[itemId] = droppers;
                _log.Info("[Farm] GarlandTools: item {0} has {1} dropper(s).", itemId, droppers.Count);
            }
            catch (Exception ex)
            {
                lock (_lock)
                    LastError = $"lookup failed: {ex.Message}";
                _log.Warning(ex, "[Farm] GarlandTools item lookup failed for {0}.", itemId);
            }
            finally
            {
                lock (_lock)
                    IsBusy = false;
            }
        });
    }

    /// <summary>Kicks off a mob-location lookup; results appear via <see cref="TryGetMobLocation"/>.</summary>
    public void BeginMobLocationLookup(ulong garlandId)
    {
        lock (_lock)
        {
            if (_locationCache.ContainsKey(garlandId))
                return;
        }

        _ = Task.Run(async () =>
        {
            MobLocation? location = null;
            try
            {
                var locations = await GetLocationNamesAsync().ConfigureAwait(false);
                var json = await _http.GetStringAsync(string.Format(MobUrl, garlandId)).ConfigureAwait(false);
                location = ParseMobLocation(json, locations);
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "[Farm] GarlandTools mob lookup failed for {0}.", garlandId);
            }

            lock (_lock)
                _locationCache[garlandId] = location;
        });
    }

    /// <summary>Parses Garland's item doc: partials of type "mob" are the droppers.</summary>
    internal static List<DropperCandidate> ParseDroppers(
        string json,
        IReadOnlyDictionary<string, uint> nameLookup,
        IReadOnlyDictionary<uint, string> locationNames)
    {
        var results = new List<DropperCandidate>();
        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("partials", out var partials) || partials.ValueKind != JsonValueKind.Array)
            return results;

        foreach (var partial in partials.EnumerateArray())
        {
            if (!partial.TryGetProperty("type", out var type) || type.GetString() != "mob")
                continue;
            if (!partial.TryGetProperty("obj", out var obj))
                continue;

            var name = obj.TryGetProperty("n", out var n) ? n.GetString() ?? "" : "";
            if (name.Length == 0)
                continue;

            var garlandId = 0uL;
            if (partial.TryGetProperty("id", out var id))
            {
                if (id.ValueKind == JsonValueKind.String)
                    ulong.TryParse(id.GetString(), out garlandId);
                else if (id.ValueKind == JsonValueKind.Number)
                    garlandId = (ulong)id.GetDouble();
            }

            var zoneName = "";
            if (obj.TryGetProperty("z", out var z) && z.ValueKind == JsonValueKind.Number)
                locationNames.TryGetValue((uint)z.GetDouble(), out zoneName!);

            nameLookup.TryGetValue(Normalize(name), out var nameId);
            results.Add(new DropperCandidate(name, ReadLevelText(obj), nameId, garlandId, zoneName ?? ""));
        }

        return results;
    }

    /// <summary>Garland levels are numbers ("6") or range strings ("5 - 9").</summary>
    internal static string ReadLevelText(JsonElement obj)
    {
        if (!obj.TryGetProperty("l", out var l))
            return "";
        return l.ValueKind switch
        {
            JsonValueKind.Number => l.GetDouble().ToString("0.#", CultureInfo.InvariantCulture),
            JsonValueKind.String => l.GetString() ?? "",
            _ => "",
        };
    }

    /// <summary>Parses a Garland mob doc into its representative spawn location (map coords).</summary>
    internal static MobLocation? ParseMobLocation(string json, IReadOnlyDictionary<uint, string> locationNames)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("mob", out var mob))
            return null;

        if (!mob.TryGetProperty("coords", out var coords)
            || coords.ValueKind != JsonValueKind.Array
            || coords.GetArrayLength() < 2)
            return null;

        var mapX = (float)coords[0].GetDouble();
        var mapY = (float)coords[1].GetDouble();

        var zoneName = "";
        if (mob.TryGetProperty("zoneid", out var zoneId) && zoneId.ValueKind == JsonValueKind.Number)
            locationNames.TryGetValue((uint)zoneId.GetDouble(), out zoneName!);

        return new MobLocation(zoneName ?? "", mapX, mapY);
    }

    private async Task<IReadOnlyDictionary<uint, string>> GetLocationNamesAsync()
    {
        lock (_lock)
        {
            if (_locationNames != null)
                return _locationNames;
        }

        var names = new Dictionary<uint, string>();
        try
        {
            var json = await _http.GetStringAsync(CoreDataUrl).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("locationIndex", out var index))
            {
                foreach (var entry in index.EnumerateObject())
                {
                    if (!uint.TryParse(entry.Name, out var locId))
                        continue;
                    if (entry.Value.TryGetProperty("name", out var name))
                        names[locId] = name.GetString() ?? "";
                }
            }
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "[Farm] GarlandTools location index fetch failed — zone names unavailable.");
        }

        lock (_lock)
            _locationNames ??= names;
        return names;
    }

    private IReadOnlyDictionary<string, uint> GetBNpcNameLookup()
    {
        if (_bnpcNameLookup != null)
            return _bnpcNameLookup;

        var lookup = new Dictionary<string, uint>();
        var sheet = _dataManager.GetExcelSheet<Lumina.Excel.Sheets.BNpcName>(Dalamud.Game.ClientLanguage.English);
        if (sheet != null)
        {
            foreach (var row in sheet)
            {
                var singular = row.Singular.ExtractText();
                if (singular.Length == 0)
                    continue;
                lookup.TryAdd(Normalize(singular), row.RowId);
            }
        }

        _bnpcNameLookup = lookup;
        return lookup;
    }

    private static string Normalize(string name) => name.Trim().ToLowerInvariant();

    public void Dispose()
    {
        _http.Dispose();
    }
}
