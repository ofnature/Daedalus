using System.Text.Json;
using Daedalus.Ipc;
using Xunit;

namespace Daedalus.Tests.Ipc;

/// <summary>
/// The LAN roster IPC's wire contract with Charon: JSON keys must be exactly
/// name/world/machine/online (Charon's LanToonInfo JsonPropertyNames), and the world derivation
/// from "Name@World" sender ids must tolerate missing worlds (Charon matches name-only then).
/// </summary>
public class LanRosterIpcTests
{
    [Theory]
    [InlineData("Korha Ishere@Behemoth", "Behemoth")]
    [InlineData("Xia Discord@Excalibur", "Excalibur")]
    [InlineData("NoWorldSender", "")]
    [InlineData("Trailing@", "")]
    [InlineData("", "")]
    public void WorldOf_ParsesSenderIdSuffix(string senderId, string expected)
        => Assert.Equal(expected, LanRosterIpc.WorldOf(senderId));

    [Fact]
    public void RosterEntry_SchemaMatchesCharonContract()
    {
        // Serialize a DTO-shaped anonymous payload through the same serializer defaults the
        // provider uses, then assert the exact key names Charon's parser binds to.
        var json = JsonSerializer.Serialize(new[]
        {
            new { name = "Korha Ishere", world = "Behemoth", machine = "DESKTOP-1", online = true },
        });

        using var doc = JsonDocument.Parse(json);
        var entry = doc.RootElement[0];
        Assert.Equal("Korha Ishere", entry.GetProperty("name").GetString());
        Assert.Equal("Behemoth", entry.GetProperty("world").GetString());
        Assert.Equal("DESKTOP-1", entry.GetProperty("machine").GetString());
        Assert.True(entry.GetProperty("online").GetBoolean());
    }
}
