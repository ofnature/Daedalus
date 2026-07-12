using System;
using System.Linq;
using Dalamud.Plugin.Services;
using Daedalus.Services.Network;
using Moq;
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
    public void HeartbeatPayload_TargetAndCombatFields_RoundTrip_AndDefault()
    {
        var hb = new LanHeartbeatPayload { TargetId = 0x4000_1234u, InCombat = true };
        var parsed = LanHeartbeatPayload.FromJson(hb.ToJson());
        Assert.NotNull(parsed);
        Assert.Equal(0x4000_1234u, parsed!.TargetId);
        Assert.True(parsed.InCombat);

        // Older clients omit the fields — must default (0 / false), never fail.
        var legacy = LanHeartbeatPayload.FromJson("{\"n\":\"X\",\"hp\":1.0}");
        Assert.NotNull(legacy);
        Assert.Equal(0u, legacy!.TargetId);
        Assert.False(legacy.InCombat);
    }

    [Fact]
    public void HeartbeatPayload_EntityId_RoundTrips_AndDefaultsToZero()
    {
        // eid feeds Charon's Heal Watch (object-table resolution without name collisions).
        var hb = new LanHeartbeatPayload { PlayerEntityId = 268503433u };
        var parsed = LanHeartbeatPayload.FromJson(hb.ToJson());
        Assert.NotNull(parsed);
        Assert.Equal(268503433u, parsed!.PlayerEntityId);

        // Pre-eid clients omit the field — must default to 0, never fail.
        var legacy = LanHeartbeatPayload.FromJson("{\"n\":\"X\",\"hp\":1.0}");
        Assert.NotNull(legacy);
        Assert.Equal(0u, legacy!.PlayerEntityId);
    }

    [Fact]
    public void PluginRelayPayload_RoundTrip_AndMalformedIsNull()
    {
        var relay = new LanPluginRelayPayload { Channel = "charon.pillion", Data = "{\"seat\":2}" };
        var parsed = LanPluginRelayPayload.FromJson(relay.ToJson());
        Assert.NotNull(parsed);
        Assert.Equal("charon.pillion", parsed!.Channel);
        Assert.Equal("{\"seat\":2}", parsed.Data);

        Assert.Null(LanPluginRelayPayload.FromJson("not json {{{"));
    }

    [Fact]
    public void Heartbeat_SelfRegistration_CarriesVitalsIntoRoster()
    {
        // The roster IPC serves hp/entityId straight from LanPeerInfo — the heartbeat must plumb
        // both through UpsertRosterEntry (self-registration path exercises the same code remote
        // heartbeats use).
        var bus = NewBus();
        bus.HeartbeatProvider = () => new LanHeartbeatPayload
        {
            CharacterName = "Kronos",
            HpPercent = 0.42f,
            PlayerEntityId = 268503433u,
        };

        bus.Update(); // sends the heartbeat + self-registers

        var self = Assert.Single(bus.Roster);
        Assert.Equal(0.42f, self.HpPercent, 3);
        Assert.Equal(268503433u, self.PlayerEntityId);
    }

    [Fact]
    public void PublishPluginRelay_EmptyChannel_IsRejected_NonEmptyDoesNotThrow()
    {
        var bus = NewBus();
        bus.PublishPluginRelay("", "{}");             // rejected silently
        bus.PublishPluginRelay("charon.rally", "{}"); // coordinator not started → Send no-ops
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
    public void MachineId_IsPerMachine_NotPerInstance()
    {
        // Regression (2026-07-03): a random per-config GUID gave each multibox game instance
        // its own "machine", so two toons on one PC showed as Local + fake Remote machines.
        var a = new Daedalus.Config.PartyCoordinationConfig();
        var b = new Daedalus.Config.PartyCoordinationConfig();
        Assert.Equal(a.GetOrCreateMachineId(), b.GetOrCreateMachineId());
        Assert.Equal(System.Environment.MachineName, a.GetOrCreateMachineId());
    }

    [Fact]
    public void MachineId_ReplacesLegacyGuid()
    {
        var cfg = new Daedalus.Config.PartyCoordinationConfig
        {
            LanMachineId = "0123456789abcdef0123456789abcdef"
        };
        Assert.Equal(System.Environment.MachineName, cfg.GetOrCreateMachineId());
    }

    [Fact]
    public void HeartbeatPayload_EchoHeldMs_RoundTrips_AndDefaultsToZero()
    {
        var payload = new LanHeartbeatPayload { EchoTimestamp = 123456789, EchoHeldMs = 1800 };
        var parsed = LanHeartbeatPayload.FromJson(payload.ToJson());
        Assert.NotNull(parsed);
        Assert.Equal(1800, parsed!.EchoHeldMs);

        // Older clients don't send the field — must parse as 0, not fail.
        var legacy = LanHeartbeatPayload.FromJson("{\"e\":123}");
        Assert.NotNull(legacy);
        Assert.Equal(0, legacy!.EchoHeldMs);
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

    private static CoordinationBus NewBus()
    {
        var log = new Mock<IPluginLog>().Object;
        // Coordinator is never Start()ed, so Send() no-ops (no socket bind) — safe for unit tests.
        var lan = new LanCoordinator(log, "machine-A", 47200) { SenderId = "Self@World" };
        return new CoordinationBus(log, lan, partyService: null, localMachineId: "machine-A");
    }

    [Fact]
    public void ForceBurstFire_OpensBurstWindow_AndRaisesOnBurstFire()
    {
        var bus = NewBus();
        var fired = false;
        bus.OnBurstFire += () => fired = true;

        Assert.False(bus.IsBurstFireActive);
        bus.ForceBurstFire();

        Assert.True(fired);
        Assert.True(bus.IsBurstFireActive);
        // Force clears the readiness set, same as a normal fire.
        Assert.Empty(bus.BurstReadySenders);
    }

    [Fact]
    public void BurstReadySenders_EmptyByDefault_AndSnapshotIsDetached()
    {
        var bus = NewBus();

        // Fresh bus: nothing ready.
        Assert.Empty(bus.BurstReadySenders);

        // Snapshot must be a detached copy, not a live view that could mutate under the UI.
        var snapshot = bus.BurstReadySenders;
        bus.ForceBurstFire();
        Assert.Empty(snapshot);
    }

    [Fact]
    public void BroadcastBurstReady_Solo_FiresImmediately()
    {
        // With a one-toon roster the local sender is both the coordinator and the only required
        // ready signal, so readiness resolves to an immediate fire (and clears the ready set).
        var bus = NewBus();
        var fired = false;
        bus.OnBurstFire += () => fired = true;

        bus.BroadcastBurstReady();

        Assert.True(fired);
        Assert.True(bus.IsBurstFireActive);
        Assert.Empty(bus.BurstReadySenders);
    }

    [Fact]
    public void TargetModePayload_RoundTrip()
    {
        var payload = new LanTargetModePayload
        {
            Mode = PartyTargetMode.Focus,
            FocusTargetId = 0x4000_5678u,
            OffTankSenderId = "OffTank@World",
        };

        var parsed = LanTargetModePayload.FromJson(payload.ToJson());

        Assert.NotNull(parsed);
        Assert.Equal(PartyTargetMode.Focus, parsed!.Mode);
        Assert.Equal(0x4000_5678u, parsed.FocusTargetId);
        Assert.Equal("OffTank@World", parsed.OffTankSenderId);
    }

    [Fact]
    public void BroadcastTargetMode_SetsState_AndRaisesChangedEvent()
    {
        var bus = NewBus();
        var changed = 0;
        bus.OnTargetModeChanged += () => changed++;

        Assert.Equal(PartyTargetMode.None, bus.CurrentTargetMode);

        bus.BroadcastTargetMode(PartyTargetMode.KillAdds, focusTargetId: 0, offTankSenderId: "OT@W");

        Assert.Equal(PartyTargetMode.KillAdds, bus.CurrentTargetMode);
        Assert.Equal("OT@W", bus.OffTankSenderId);
        Assert.Equal(1, changed);

        // Re-broadcasting the same state must not re-raise the change event.
        bus.BroadcastTargetMode(PartyTargetMode.KillAdds, focusTargetId: 0, offTankSenderId: "OT@W");
        Assert.Equal(1, changed);
    }

    [Fact]
    public void BroadcastTankSwapCommand_ArmsLocalSwap_AndRaisesActivity()
    {
        var log = new Mock<IPluginLog>().Object;
        var lan = new LanCoordinator(log, "machine-A", 47200) { SenderId = "Self@World" };
        var svc = new Daedalus.Services.Party.PartyCoordinationService(
            new Daedalus.Config.PartyCoordinationConfig
            {
                EnablePartyCoordination = true,
                EnableTankSwapCoordination = true,
            },
            log);
        var bus = new CoordinationBus(log, lan, svc, "machine-A");
        var activity = 0;
        bus.OnTankSwapActivity += _ => activity++;

        bus.BroadcastTankSwapCommand();

        // Our own broadcast is filtered on loopback, so the presser's box arms itself directly.
        Assert.True(svc.IsManualSwapArmed());
        Assert.True(activity > 0);
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
