using System.Collections.Generic;
using System.Text.Json;
using Daedalus.Services.Farm;
using Xunit;

namespace Daedalus.Tests.Services.Farm;

public class GarlandDropSourceTests
{
    // Shape mirrors garlandtools.org/db/doc/item/en/3/{id}.json — verified live 2026-07-04:
    // levels are numbers ("6") OR range strings ("5 - 9"); z is Garland's location id.
    private const string SampleItemJson = """
        {
          "item": { "id": 5291, "name": "Animal Skin", "drops": [ 50000000005, 370000000037 ] },
          "partials": [
            { "type": "mob", "id": "50000000005", "obj": { "i": 5, "n": "Opo-opo", "l": "5 - 9", "z": 57 } },
            { "type": "mob", "id": "370000000037", "obj": { "i": 37, "n": "ground squirrel", "l": 2, "z": 57 } },
            { "type": "npc", "id": "1001", "obj": { "n": "some vendor" } },
            { "type": "mob", "id": "40300", "obj": { "l": 7 } }
          ]
        }
        """;

    private static readonly Dictionary<uint, string> Locations = new() { [57] = "North Shroud" };

    [Fact]
    public void ParseDroppers_ExtractsMobs_LevelsZonesAndNameIds()
    {
        var lookup = new Dictionary<string, uint> { ["opo-opo"] = 96 };

        var result = GarlandDropSource.ParseDroppers(SampleItemJson, lookup, Locations);

        Assert.Equal(2, result.Count); // vendor and nameless mob ignored

        Assert.Equal("Opo-opo", result[0].Name);
        Assert.Equal("5 - 9", result[0].LevelText); // range string preserved
        Assert.Equal(96u, result[0].NameId);        // case-insensitive resolution
        Assert.Equal(50000000005uL, result[0].GarlandId);
        Assert.Equal("North Shroud", result[0].ZoneName);

        Assert.Equal("ground squirrel", result[1].Name);
        Assert.Equal("2", result[1].LevelText);
        Assert.Equal(0u, result[1].NameId); // unresolved
    }

    [Fact]
    public void ParseDroppers_NoPartials_ReturnsEmpty()
    {
        var result = GarlandDropSource.ParseDroppers(
            """{ "item": { "id": 1 } }""", new Dictionary<string, uint>(), Locations);
        Assert.Empty(result);
    }

    [Fact]
    public void ParseMobLocation_ReadsCoordsAndZone()
    {
        // Shape verified live: mob doc has coords [mapX, mapY, ...] and zoneid.
        var json = """
            { "mob": { "name": "Opo-opo", "id": 50000000005, "coords": [27.4, 24.6, 21.15], "zoneid": 57, "lvl": "5 - 9" } }
            """;

        var location = GarlandDropSource.ParseMobLocation(json, Locations);

        Assert.NotNull(location);
        Assert.Equal(27.4f, location!.MapX, 3);
        Assert.Equal(24.6f, location.MapY, 3);
        Assert.Equal("North Shroud", location.ZoneName);
    }

    [Fact]
    public void ParseMobLocation_NoCoords_ReturnsNull()
    {
        var json = """{ "mob": { "name": "x", "zoneid": 57 } }""";
        Assert.Null(GarlandDropSource.ParseMobLocation(json, Locations));
    }

    [Fact]
    public void ReadLevelText_NumberAndString()
    {
        using var doc = JsonDocument.Parse("""{ "a": { "l": 6 }, "b": { "l": "5 - 9" }, "c": {} }""");
        Assert.Equal("6", GarlandDropSource.ReadLevelText(doc.RootElement.GetProperty("a")));
        Assert.Equal("5 - 9", GarlandDropSource.ReadLevelText(doc.RootElement.GetProperty("b")));
        Assert.Equal("", GarlandDropSource.ReadLevelText(doc.RootElement.GetProperty("c")));
    }

    [Theory]
    // Map center (21.5 on a 100-scale 2048-unit map) is world 0; forward transform is
    // map = 41/c * (c*(world+offset)+1024)/2048 + 1 (Dalamud MapUtil.WorldToMap).
    [InlineData(21.5f, (ushort)100, (short)0, 0f)]
    [InlineData(11.25f, (ushort)200, (short)0, 0f)]
    [InlineData(21.5f, (ushort)100, (short)100, -100f)]
    public void MapCoordToWorld_KnownAnchors(float mapCoord, ushort sizeFactor, short offset, float expectedWorld)
    {
        Assert.Equal(expectedWorld, FarmLocationHelper.MapCoordToWorld(mapCoord, sizeFactor, offset), 1);
    }

    [Fact]
    public void MapCoordToWorld_RoundTripsForwardTransform()
    {
        // Forward: map = 41/c * (c*(world+offset)+1024)/2048 + 1
        static float WorldToMap(float world, ushort sizeFactor, short offset)
        {
            var c = sizeFactor / 100f;
            return 41f / c * ((c * (world + offset) + 1024f) / 2048f) + 1f;
        }

        foreach (var (world, scale, offset) in new[] { (294.7f, (ushort)100, (short)0), (-512f, (ushort)200, (short)50), (801.3f, (ushort)95, (short)-20) })
        {
            var map = WorldToMap(world, scale, offset);
            Assert.Equal(world, FarmLocationHelper.MapCoordToWorld(map, scale, offset), 1);
        }
    }
}
