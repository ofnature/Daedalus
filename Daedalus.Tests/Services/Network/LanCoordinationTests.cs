using System;
using System.Linq;
using Daedalus.Services.Network;
using Xunit;

namespace Daedalus.Tests.Services.Network;

/// <summary>
/// LAN coordinator building blocks: message envelope round-trip, heartbeat/role payloads, and
/// peer-info staleness. Socket-level behavior (bind, broadcast, receive loop) needs two machines
/// and is covered by the live testing sequence in the spec, not unit tests.
/// </summary>
public class LanCoordinationTests
{
    [Fact]
    public void LanMessage_JsonRoundTrip_PreservesAllFields()
    {
        var msg = new LanMessage
        {
            SenderId = "Arthena@Coeurl",
            MachineId = "abc123",
            Type = LanMessageType.BurstFire,
            Payload = "{\"x\":1}",
            Timestamp = 638_000_000_000_000_000,
        };

        var parsed = LanMessage.FromJson(msg.ToJson());

        Assert.NotNull(parsed);
        Assert.Equal(msg.SenderId, parsed!.SenderId);
        Assert.Equal(msg.MachineId, parsed.MachineId);
        Assert.Equal(msg.Type, parsed.Type);
        Assert.Equal(msg.Payload, parsed.Payload);
        Assert.Equal(msg.Timestamp, parsed.Timestamp);
    }

    [Fact]
    public void LanMessage_MalformedOrWrongVersion_ReturnsNull_NeverThrows()
    {
        Assert.Null(LanMessage.FromJson("not json at all {{{"));
        Assert.Null(LanMessage.FromJson(""));
        Assert.Null(LanMessage.FromJson("{\"s\":\"x\",\"m\":\"y\",\"t\":0,\"ts\":1,\"v\":999}"));
    }

    [Fact]
    public void HeartbeatPayload_RoundTrip()
    {
        var hb = new LanHeartbeatPayload
        {
            CharacterName = "Kronos",
            JobId = 21,
            JobAbbrev = "WAR",
            HpPercent = 0.87f,
            Role = "Tank",
            Status = "OK",
            EchoTimestamp = 12345,
        };

        var parsed = LanHeartbeatPayload.FromJson(hb.ToJson());

        Assert.NotNull(parsed);
        Assert.Equal("Kronos", parsed!.CharacterName);
        Assert.Equal(21u, parsed.JobId);
        Assert.Equal("WAR", parsed.JobAbbrev);
        Assert.Equal(0.87f, parsed.HpPercent, 3);
        Assert.Equal("Tank", parsed.Role);
        Assert.Equal(12345, parsed.EchoTimestamp);
    }

    [Fact]
    public void RolePayload_RoundTrip()
    {
        var role = new LanRolePayload { CharacterName = "Lyria", JobId = 33, Role = "Healer" };
        var parsed = LanRolePayload.FromJson(role.ToJson());

        Assert.NotNull(parsed);
        Assert.Equal("Lyria", parsed!.CharacterName);
        Assert.Equal("Healer", parsed.Role);
    }

    [Fact]
    public void PeerInfo_StaleAfterFiveSeconds()
    {
        var now = DateTime.UtcNow;
        var peer = new LanPeerInfo { LastSeenUtc = now.AddSeconds(-4) };
        Assert.False(peer.IsStale(now));

        peer.LastSeenUtc = now.AddSeconds(-6);
        Assert.True(peer.IsStale(now));
    }

    [Fact]
    public void LanMessage_CompactJson_StaysSmall()
    {
        // Combat signals must fit comfortably in one datagram; the envelope overhead should be tiny.
        var msg = new LanMessage
        {
            SenderId = "Somecharacter Name@Behemoth",
            MachineId = Guid.NewGuid().ToString("N"),
            Type = LanMessageType.Heartbeat,
            Payload = new LanHeartbeatPayload
            {
                CharacterName = "Somecharacter Name",
                JobId = 42,
                JobAbbrev = "PCT",
                HpPercent = 1f,
                Role = "DPS",
                Status = "OK",
                EchoTimestamp = DateTime.UtcNow.Ticks,
            }.ToJson(),
            Timestamp = DateTime.UtcNow.Ticks,
        };

        Assert.True(msg.ToJson().Length < 512, $"heartbeat envelope was {msg.ToJson().Length} bytes");
    }
}
