using System.Collections.Generic;
using Dalamud.Plugin.Services;
using Daedalus.Services.Network;
using Moq;
using Xunit;

namespace Daedalus.Tests.Services.Network;

/// <summary>
/// RescueNeeded/RescueClaim wire plumbing (docs/rescue-plan.md Phase 0): payload round-trips,
/// receive-path event delivery via <c>CoordinationBus.InjectForTest</c>, and party-group
/// scoping — a rescue signal is strictly the sending party's business.
/// </summary>
public sealed class RescueCoordinationTests
{
    [Fact]
    public void RescueNeededPayload_RoundTrips_AllFields()
    {
        var payload = new LanRescueNeededPayload
        {
            EntityId = 268503433u,
            ActivationInMs = 1400,
            X = 100.5f,
            Y = -2.25f,
            Z = 87.125f,
            KnockbackImmune = true,
        };

        var parsed = LanRescueNeededPayload.FromJson(payload.ToJson());

        Assert.NotNull(parsed);
        Assert.Equal(268503433u, parsed!.EntityId);
        Assert.Equal(1400, parsed.ActivationInMs);
        Assert.Equal(100.5f, parsed.X, 3);
        Assert.Equal(-2.25f, parsed.Y, 3);
        Assert.Equal(87.125f, parsed.Z, 3);
        Assert.True(parsed.KnockbackImmune);

        Assert.Null(LanRescueNeededPayload.FromJson("not json {{{"));
    }

    [Fact]
    public void RescueNeeded_RaisesEvent_WithSenderAndPayload()
    {
        var bus = NewBus();
        var received = new List<(string Sender, LanRescueNeededPayload Payload)>();
        bus.OnRescueNeeded += (sender, payload) => received.Add((sender, payload));

        bus.InjectForTest(Remote(LanMessageType.RescueNeeded,
            new LanRescueNeededPayload { EntityId = 42u, ActivationInMs = 900 }.ToJson(), groupId: 0, ts: 1));
        bus.Update();

        var (sender, parsed) = Assert.Single(received);
        Assert.Equal("X@World", sender);
        Assert.Equal(42u, parsed.EntityId);
        Assert.Equal(900, parsed.ActivationInMs);
    }

    [Fact]
    public void RescueNeeded_FromAnotherParty_IsIgnored_OwnPartyDelivered()
    {
        var bus = NewGroupedBus("A@World", groupId: 100);
        var received = 0;
        bus.OnRescueNeeded += (_, _) => received++;

        bus.InjectForTest(Remote(LanMessageType.RescueNeeded,
            new LanRescueNeededPayload { EntityId = 42u }.ToJson(), groupId: 200, ts: 1));
        bus.Update();
        Assert.Equal(0, received);

        bus.InjectForTest(Remote(LanMessageType.RescueNeeded,
            new LanRescueNeededPayload { EntityId = 42u }.ToJson(), groupId: 100, ts: 2));
        bus.Update();
        Assert.Equal(1, received);
    }

    [Fact]
    public void RescueNeeded_MalformedOrZeroEntity_NeverRaises()
    {
        var bus = NewBus();
        var received = 0;
        bus.OnRescueNeeded += (_, _) => received++;

        bus.InjectForTest(Remote(LanMessageType.RescueNeeded, "garbage {{{", groupId: 0, ts: 1));
        bus.InjectForTest(Remote(LanMessageType.RescueNeeded,
            new LanRescueNeededPayload { EntityId = 0u }.ToJson(), groupId: 0, ts: 2));
        bus.Update();

        Assert.Equal(0, received);
    }

    [Fact]
    public void RescueClaim_RoundTripsAndRaises_SameGroupOnly()
    {
        var bus = NewGroupedBus("A@World", groupId: 100);
        var claims = new List<(string Sender, uint EntityId)>();
        bus.OnRescueClaim += (sender, payload) => claims.Add((sender, payload.EntityId));

        bus.InjectForTest(Remote(LanMessageType.RescueClaim,
            new LanRescueClaimPayload { EntityId = 42u }.ToJson(), groupId: 200, ts: 1));
        bus.InjectForTest(Remote(LanMessageType.RescueClaim,
            new LanRescueClaimPayload { EntityId = 42u }.ToJson(), groupId: 100, ts: 2));
        bus.Update();

        var (sender, entityId) = Assert.Single(claims);
        Assert.Equal("X@World", sender);
        Assert.Equal(42u, entityId);
    }

    [Fact]
    public void RescueNeeded_LegacyZeroGroupSender_ReachesGroupedReceiver()
    {
        // 0 matches everyone — a toon whose PartyId briefly reads 0 (zone-in blip) must not
        // have its cry for help dropped by its own party.
        var bus = NewGroupedBus("A@World", groupId: 100);
        var received = 0;
        bus.OnRescueNeeded += (_, _) => received++;

        bus.InjectForTest(Remote(LanMessageType.RescueNeeded,
            new LanRescueNeededPayload { EntityId = 42u }.ToJson(), groupId: 0, ts: 1));
        bus.Update();

        Assert.Equal(1, received);
    }

    private static LanMessage Remote(LanMessageType type, string payload, ulong groupId, long ts) => new()
    {
        SenderId = "X@World",
        MachineId = "machine-B",
        Type = type,
        Payload = payload,
        Timestamp = ts,
        PartyGroupId = groupId,
    };

    private static CoordinationBus NewBus()
    {
        var log = new Mock<IPluginLog>().Object;
        // Coordinator is never Start()ed, so Send() no-ops (no socket bind) — safe for unit tests.
        var lan = new LanCoordinator(log, "machine-A", 47200) { SenderId = "Self@World" };
        return new CoordinationBus(log, lan, partyService: null, localMachineId: "machine-A");
    }

    /// <summary>Bus whose local toon has self-registered with the given party group id.</summary>
    private static CoordinationBus NewGroupedBus(string senderId, ulong groupId)
    {
        var log = new Mock<IPluginLog>().Object;
        var lan = new LanCoordinator(log, "machine-A", 47200) { SenderId = senderId };
        var bus = new CoordinationBus(log, lan, partyService: null, localMachineId: "machine-A")
        {
            HeartbeatProvider = () => new LanHeartbeatPayload { CharacterName = senderId, PartyGroupId = groupId },
        };
        bus.Update(); // sends the heartbeat + self-registers the group id
        return bus;
    }
}
