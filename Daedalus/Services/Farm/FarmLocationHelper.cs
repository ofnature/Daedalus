using System;
using Dalamud.Plugin.Services;

namespace Daedalus.Services.Farm;

/// <summary>Resolved game-side location for a Garland zone name + map coords.</summary>
public readonly record struct ResolvedZone(uint TerritoryId, uint MapId, ushort SizeFactor, short OffsetX, short OffsetY, string Name);

/// <summary>
/// Map-coordinate math and zone-name resolution for farm dropper locations.
/// Garland gives map coordinates (the "X: 27.4 Y: 24.6" numbers players see); converting to world
/// space needs the target map's SizeFactor and offsets from the Map sheet.
/// </summary>
public static class FarmLocationHelper
{
    /// <summary>
    /// Inverse of the game's world→map display transform (Dalamud MapUtil.WorldToMap):
    /// map = 41/c * (c*(world+offset) + 1024)/2048 + 1, c = SizeFactor/100 — verified against
    /// Dalamud's forward transform in tests.
    /// </summary>
    public static float MapCoordToWorld(float mapCoord, ushort sizeFactor, short offset)
    {
        var c = sizeFactor / 100f;
        return (mapCoord - 1f) * 2048f / 41f - 1024f / c - offset;
    }

    /// <summary>
    /// The game's world→map display transform: the "X: 27.4" numbers players read off the map.
    /// Exact inverse of <see cref="MapCoordToWorld"/>.
    /// <para>
    /// Useful as a CHECK on any world↔map work: print this next to the coordinates the game
    /// itself shows, and a mismatch localises the error immediately instead of leaving an
    /// overlay that merely looks about right.
    /// </para>
    /// </summary>
    public static float WorldToMapCoord(float world, ushort sizeFactor, short offset)
    {
        var c = sizeFactor / 100f;
        return (41f / c * ((c * (world + offset) + 1024f) / 2048f)) + 1f;
    }

    /// <summary>
    /// World coordinate to a pixel in the 2048px map texture — the inner term of
    /// <see cref="WorldToMapCoord"/>, which is what an overlay drawn on the map image needs.
    /// </summary>
    public static float WorldToMapPixel(float world, ushort sizeFactor, short offset)
        => (sizeFactor / 100f * (world + offset)) + 1024f;

    /// <summary>
    /// Finds the overworld territory whose place name matches a Garland zone name (English).
    /// Returns null when no territory matches (Garland names occasionally drift).
    /// </summary>
    public static ResolvedZone? ResolveZoneByName(IDataManager dataManager, string zoneName)
    {
        if (string.IsNullOrWhiteSpace(zoneName))
            return null;

        var territories = dataManager.GetExcelSheet<Lumina.Excel.Sheets.TerritoryType>(Dalamud.Game.ClientLanguage.English);
        if (territories == null)
            return null;

        foreach (var territory in territories)
        {
            var placeName = territory.PlaceName.ValueNullable?.Name.ExtractText();
            if (placeName == null || !placeName.Equals(zoneName, StringComparison.OrdinalIgnoreCase))
                continue;

            var map = territory.Map.ValueNullable;
            if (map == null || territory.Map.RowId == 0)
                continue;

            // Prefer the real overworld instance of the name (towns/duties share place names).
            if (territory.TerritoryIntendedUse.RowId != 1)
                continue;

            return new ResolvedZone(
                territory.RowId,
                territory.Map.RowId,
                map.Value.SizeFactor,
                map.Value.OffsetX,
                map.Value.OffsetY,
                placeName);
        }

        return null;
    }

    /// <summary>Sets the in-game map flag (the red pin) on the given zone's map at map coordinates.</summary>
    public static unsafe bool SetMapFlag(ResolvedZone zone, float mapX, float mapY)
    {
        try
        {
            var agent = FFXIVClientStructs.FFXIV.Client.UI.Agent.AgentMap.Instance();
            if (agent == null)
                return false;

            var worldX = MapCoordToWorld(mapX, zone.SizeFactor, zone.OffsetX);
            var worldZ = MapCoordToWorld(mapY, zone.SizeFactor, zone.OffsetY);
            agent->SetFlagMapMarker(zone.TerritoryId, zone.MapId, worldX, worldZ);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
