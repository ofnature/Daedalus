using System.Collections.Generic;
using Daedalus.Services.Farm;
using Xunit;

namespace Daedalus.Tests.Services.Farm;

public class GarlandDropSourceTests
{
    // Shape mirrors garlandtools.org/db/doc/item/en/3/{id}.json: mob partials carry
    // n (name, lowercase singular), l (level); other partial types must be ignored.
    private const string SampleJson = """
        {
          "item": { "id": 5291, "name": "Animal Skin", "drops": [ 40105, 40201 ] },
          "partials": [
            { "type": "mob", "id": "40105", "obj": { "i": 105, "n": "wild dodo", "l": 4 } },
            { "type": "mob", "id": "40201", "obj": { "i": 201, "n": "forest funguar", "l": 5.5 } },
            { "type": "npc", "id": "1001", "obj": { "n": "some vendor" } },
            { "type": "mob", "id": "40300", "obj": { "l": 7 } }
          ]
        }
        """;

    [Fact]
    public void ParseDroppers_ExtractsMobsOnly_ResolvesNameIds()
    {
        var lookup = new Dictionary<string, uint> { ["wild dodo"] = 47 };

        var result = GarlandDropSource.ParseDroppers(SampleJson, lookup);

        Assert.Equal(2, result.Count); // vendor ignored, nameless mob ignored
        Assert.Equal("wild dodo", result[0].Name);
        Assert.Equal(4, result[0].Level);
        Assert.Equal(47u, result[0].NameId);

        Assert.Equal("forest funguar", result[1].Name);
        Assert.Equal(5, result[1].Level); // fractional garland levels truncate
        Assert.Equal(0u, result[1].NameId); // unresolved
    }

    [Fact]
    public void ParseDroppers_NoPartials_ReturnsEmpty()
    {
        var result = GarlandDropSource.ParseDroppers("""{ "item": { "id": 1 } }""", new Dictionary<string, uint>());
        Assert.Empty(result);
    }

    [Fact]
    public void ParseDroppers_NameMatchingIsCaseInsensitiveViaNormalization()
    {
        var lookup = new Dictionary<string, uint> { ["wild dodo"] = 47 };
        var json = """
            { "partials": [ { "type": "mob", "id": "1", "obj": { "n": "Wild Dodo", "l": 4 } } ] }
            """;

        var result = GarlandDropSource.ParseDroppers(json, lookup);

        Assert.Single(result);
        Assert.Equal(47u, result[0].NameId);
    }
}
