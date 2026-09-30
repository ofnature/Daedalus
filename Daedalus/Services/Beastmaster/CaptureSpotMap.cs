using System.Collections.Generic;
using Daedalus.Data;
using Daedalus.Localization;
using Daedalus.Services.Farm;

namespace Daedalus.Services.Beastmaster;

/// <summary>Drops the map flag on a beast's capture spot and opens the map there.</summary>
public static class CaptureSpotMap
{
    private static readonly Dictionary<string, ResolvedZone?> Zones = new();

    /// <summary>Flag the spot and open its map. Returns an error to show, or null on success.</summary>
    public static unsafe string? FlagAndOpen(BstCaptureSpot spot)
    {
        if (spot.Zone is not { } zoneName)
            return "No overworld spot for this beast.";

        if (!Zones.TryGetValue(zoneName, out var zone))
        {
            var data = GameDataLocalizer.Instance?.DataManager;
            if (data == null)
                return "Game data is not loaded yet.";
            zone = FarmLocationHelper.ResolveZoneByName(data, zoneName);
            Zones[zoneName] = zone;
        }

        if (zone is not { } z)
            return $"Could not find the {zoneName} map.";
        if (!FarmLocationHelper.SetMapFlag(z, spot.MapX, spot.MapY))
            return "The game did not take the map flag.";

        var agent = FFXIVClientStructs.FFXIV.Client.UI.Agent.AgentMap.Instance();
        if (agent != null)
            agent->OpenMapByMapId(z.MapId, z.TerritoryId);
        return null;
    }
}
