using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;

namespace Daedalus.Services.Farm;

/// <summary>A mob that drops the looked-up item. NameId 0 = couldn't resolve to a BNpcName row.</summary>
public sealed record DropperCandidate(string Name, int Level, uint NameId);

/// <summary>
/// "Which mobs drop this item" lookup via GarlandTools (same data source Monster Loot Hunter
/// uses — MLH itself has no IPC). Async fire-and-forget with an in-memory per-item cache;
/// mob names resolve to BNpcName row ids through Lumina (English sheet — Garland names are
/// English lowercase singulars). Drop tables are server-side, so a web source is mandatory.
/// </summary>
public sealed class GarlandDropSource : IDisposable
{
    private const string ItemUrl = "https://www.garlandtools.org/db/doc/item/en/3/{0}.json";

    private readonly IDataManager _dataManager;
    private readonly IPluginLog _log;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly Dictionary<uint, IReadOnlyList<DropperCandidate>> _cache = new();
    private readonly object _lock = new();

    private Dictionary<string, uint>? _bnpcNameLookup;

    public bool IsBusy { get; private set; }
    public string? LastError { get; private set; }

    public GarlandDropSource(IDataManager dataManager, IPluginLog log)
    {
        _dataManager = dataManager;
        _log = log;
    }

    /// <summary>Cached result for the item, or null when never looked up (or lookup in flight).</summary>
    public IReadOnlyList<DropperCandidate>? TryGetCached(uint itemId)
    {
        lock (_lock)
            return _cache.TryGetValue(itemId, out var result) ? result : null;
    }

    /// <summary>Kicks off an async lookup; results appear via <see cref="TryGetCached"/>.</summary>
    public void BeginLookup(uint itemId)
    {
        lock (_lock)
        {
            if (IsBusy || _cache.ContainsKey(itemId))
                return;
            IsBusy = true;
            LastError = null;
        }

        // BNpcName index is built lazily on the caller's (framework) thread so the background
        // task never touches Lumina.
        var nameLookup = GetBNpcNameLookup();

        _ = Task.Run(async () =>
        {
            try
            {
                var json = await _http.GetStringAsync(string.Format(ItemUrl, itemId)).ConfigureAwait(false);
                var droppers = ParseDroppers(json, nameLookup);
                lock (_lock)
                    _cache[itemId] = droppers;
                _log.Info("[Farm] GarlandTools: item {0} has {1} dropper(s).", itemId, droppers.Count);
            }
            catch (Exception ex)
            {
                lock (_lock)
                    LastError = $"lookup failed: {ex.Message}";
                _log.Warning(ex, "[Farm] GarlandTools lookup failed for item {0}.", itemId);
            }
            finally
            {
                lock (_lock)
                    IsBusy = false;
            }
        });
    }

    /// <summary>Parses Garland's item doc: partials of type "mob" are the entities the item's drop list references.</summary>
    internal static List<DropperCandidate> ParseDroppers(string json, IReadOnlyDictionary<string, uint> nameLookup)
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

            var level = 0;
            if (obj.TryGetProperty("l", out var l) && l.ValueKind == JsonValueKind.Number)
                level = (int)l.GetDouble();

            nameLookup.TryGetValue(Normalize(name), out var nameId);
            results.Add(new DropperCandidate(name, level, nameId));
        }

        return results;
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
