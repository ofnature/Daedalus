using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin.Services;
using Daedalus.Ipc;
using Daedalus.Services.Party;

namespace Daedalus.Services.Network;

/// <summary>One toon as seen through LAN heartbeats (local machine's toons included — uniform view).</summary>
public sealed class LanPeerInfo
{
    public string SenderId = "";
    public string MachineId = "";
    public string CharacterName = "";
    public uint JobId;
    public string JobAbbrev = "";
    public float HpPercent;
    public string Role = "";
    public string Status = "";
    /// <summary>Assigned slot after role negotiation: "Tank 1", "Healer 2", "DPS 3"...</summary>
    public string AssignedSlot = "";
    public DateTime LastSeenUtc;
    public double LatencyMs;

    /// <summary>Grey after 5s without a heartbeat (still listed until 30s).</summary>
    public bool IsStale(DateTime now) => (now - LastSeenUtc).TotalSeconds > 5;
}

/// <summary>
/// The single message bus both transports feed: Dalamud IPC (same machine, low latency) and the
/// LAN coordinator (cross machine, UDP broadcast). Rotation modules and UI subscribe HERE — never
/// to a transport directly.
///
/// Dedup model: same-machine toons deliver via BOTH transports (their LAN broadcasts loop back).
/// Mirrored IPC traffic from our own machine is therefore dropped (Dalamud IPC already delivered
/// it); everything else dedups on (SenderId, Timestamp). Socket-thread messages are queued and
/// drained on the framework thread via <see cref="Update"/> — game state is never touched from
/// the receive thread.
/// </summary>
public sealed class CoordinationBus : IDisposable
{
    private readonly IPluginLog _log;
    private readonly LanCoordinator _lan;
    private readonly PartyCoordinationService? _partyService;
    private readonly string _localMachineId;

    private readonly ConcurrentQueue<LanMessage> _inbox = new();
    private readonly Dictionary<string, LanPeerInfo> _roster = new();
    private readonly HashSet<string> _dedupKeys = new();
    private readonly Queue<string> _dedupOrder = new();
    private const int DedupWindow = 1024;

    private DateTime _lastHeartbeatSent = DateTime.MinValue;
    private long _newestRemoteTimestamp;

    // Burst coordination
    private readonly HashSet<string> _burstReadySenders = new();
    private DateTime _burstFireUntil = DateTime.MinValue;

    // Role negotiation
    private DateTime _roleCollectUntil = DateTime.MinValue;
    private readonly Dictionary<string, LanRolePayload> _roleResponses = new();

    /// <summary>Supplies the local toon's heartbeat payload each interval (null = not logged in).</summary>
    public Func<LanHeartbeatPayload?>? HeartbeatProvider { get; set; }

    public event System.Action<string /*sender*/, LanMessage>? OnHealerDown;
    public event System.Action<string /*sender*/, string /*targetName*/>? OnPhoenixDown;
    public event System.Action<string /*sender*/, LanMessage>? OnTankSwap;
    public event System.Action<string /*sender*/, LanMessage>? OnAddSpawn;
    public event System.Action<string /*sender*/, LanMessage>? OnBurnSignal;
    public event System.Action? OnBurstFire;
    public event System.Action<IReadOnlyDictionary<string, LanRolePayload>>? OnRolesAssigned;

    public CoordinationBus(
        IPluginLog log,
        LanCoordinator lan,
        PartyCoordinationService? partyService,
        string localMachineId)
    {
        _log = log;
        _lan = lan;
        _partyService = partyService;
        _localMachineId = localMachineId;

        _lan.OnMessageReceived += msg => _inbox.Enqueue(msg); // socket thread — queue only

        // Mirror every outgoing IPC message onto the LAN so cross-machine toons see the same
        // protocol traffic same-machine toons do.
        if (_partyService != null)
        {
            _partyService.OnHeartbeatReady += m => MirrorToLan("Heartbeat", m);
            _partyService.OnHealIntentReady += m => MirrorToLan("HealIntent", m);
            _partyService.OnHealLandedReady += m => MirrorToLan("HealLanded", m);
            _partyService.OnCooldownUsedReady += m => MirrorToLan("CooldownUsed", m);
            _partyService.OnAoEHealIntentReady += m => MirrorToLan("AoEHealIntent", m);
            _partyService.OnRaidBuffIntentReady += m => MirrorToLan("RaidBuffIntent", m);
            _partyService.OnBurstWindowStartReady += m => MirrorToLan("BurstWindowStart", m);
            _partyService.OnGaugeStateReady += m => MirrorToLan("GaugeState", m);
            _partyService.OnRoleDeclarationReady += m => MirrorToLan("RoleDeclaration", m);
            _partyService.OnGroundEffectPlacedReady += m => MirrorToLan("GroundEffectPlaced", m);
            _partyService.OnRaiseIntentReady += m => MirrorToLan("RaiseIntent", m);
            _partyService.OnCleanseIntentReady += m => MirrorToLan("CleanseIntent", m);
            _partyService.OnInterruptIntentReady += m => MirrorToLan("InterruptIntent", m);
            _partyService.OnTankSwapIntentReady += m => MirrorToLan("TankSwapIntent", m);
        }
    }

    #region Roster / status

    public IReadOnlyCollection<LanPeerInfo> Roster
    {
        get { lock (_roster) return _roster.Values.ToArray(); }
    }

    public string LocalMachineId => _localMachineId;

    /// <summary>Distinct REMOTE machines with a fresh heartbeat.</summary>
    public int PeerMachineCount
    {
        get
        {
            var now = DateTime.UtcNow;
            lock (_roster)
                return _roster.Values
                    .Where(p => p.MachineId != _localMachineId && !p.IsStale(now))
                    .Select(p => p.MachineId)
                    .Distinct()
                    .Count();
        }
    }

    /// <summary>Total fresh toons on remote machines (the window's "Peers" count).</summary>
    public int PeerToonCount
    {
        get
        {
            var now = DateTime.UtcNow;
            lock (_roster)
                return _roster.Values.Count(p => p.MachineId != _localMachineId && !p.IsStale(now));
        }
    }

    /// <summary>Best-effort latency to the newest remote machine (heartbeat echo round trip).</summary>
    public double RemoteLatencyMs
    {
        get
        {
            var now = DateTime.UtcNow;
            lock (_roster)
            {
                var fresh = _roster.Values
                    .Where(p => p.MachineId != _localMachineId && !p.IsStale(now) && p.LatencyMs > 0)
                    .ToArray();
                return fresh.Length == 0 ? 0 : fresh.Min(p => p.LatencyMs);
            }
        }
    }

    #endregion

    #region Framework-thread pump

    /// <summary>
    /// Called once per framework tick: drains the socket-thread inbox, sends the 2s heartbeat,
    /// ages the roster (grey 5s / drop 30s), and updates the peer status light.
    /// </summary>
    public void Update()
    {
        var now = DateTime.UtcNow;

        while (_inbox.TryDequeue(out var msg))
            Process(msg, now);

        if ((now - _lastHeartbeatSent).TotalSeconds >= 2)
        {
            _lastHeartbeatSent = now;
            var payload = HeartbeatProvider?.Invoke();
            if (payload != null)
            {
                payload.EchoTimestamp = _newestRemoteTimestamp;
                _lan.Send(new LanMessage { Type = LanMessageType.Heartbeat, Payload = payload.ToJson() });

                // Self-register directly: our own broadcast loops back but is dropped by the
                // sender filter, so the local toon would never appear in its own roster (the
                // window looked empty when solo). Same data, no loopback dependency.
                UpsertRosterEntry(_lan.SenderId, _localMachineId, payload, now, latencyMs: 0);
            }
        }

        lock (_roster)
        {
            var dead = _roster.Values.Where(p => (now - p.LastSeenUtc).TotalSeconds > 30).Select(p => p.SenderId).ToArray();
            foreach (var id in dead)
                _roster.Remove(id);
        }

        if (PeerMachineCount > 0) _lan.MarkPeerSeen();
        else _lan.MarkNoPeers();

        // Close the role-collection window and hand out slots.
        if (_roleCollectUntil != DateTime.MinValue && now >= _roleCollectUntil)
        {
            _roleCollectUntil = DateTime.MinValue;
            AssignRoleSlots();
        }
    }

    #endregion

    #region Incoming

    private void Process(LanMessage msg, DateTime now)
    {
        // Dedup (replays / double-delivery), bounded window.
        var key = $"{msg.SenderId}|{msg.Timestamp}|{(int)msg.Type}";
        if (!_dedupKeys.Add(key)) return;
        _dedupOrder.Enqueue(key);
        while (_dedupOrder.Count > DedupWindow)
            _dedupKeys.Remove(_dedupOrder.Dequeue());

        if (msg.Timestamp > _newestRemoteTimestamp && msg.MachineId != _localMachineId)
            _newestRemoteTimestamp = msg.Timestamp;

        switch (msg.Type)
        {
            case LanMessageType.Heartbeat:
                HandleHeartbeat(msg, now);
                break;

            case LanMessageType.IpcMirror:
                // Same-machine mirrored traffic already arrived via Dalamud IPC — drop it here.
                if (msg.MachineId != _localMachineId)
                    HandleIpcMirror(msg);
                break;

            case LanMessageType.RoleAssignment:
                var role = LanRolePayload.FromJson(msg.Payload);
                if (role != null)
                {
                    _roleResponses[msg.SenderId] = role;
                    // A remote zone-in opens/extends our collection window too.
                    if (_roleCollectUntil == DateTime.MinValue)
                        _roleCollectUntil = now.AddSeconds(3);
                }
                break;

            case LanMessageType.BurstReady:
                _burstReadySenders.Add(msg.SenderId);
                TryFireBurst();
                break;

            case LanMessageType.BurstFire:
                ActivateBurstFire(msg);
                break;

            case LanMessageType.HealerDown:
                OnHealerDown?.Invoke(msg.SenderId, msg);
                break;

            case LanMessageType.PhoenixDown:
                OnPhoenixDown?.Invoke(msg.SenderId, msg.Payload);
                break;

            case LanMessageType.TankSwap:
                OnTankSwap?.Invoke(msg.SenderId, msg);
                break;

            case LanMessageType.AddSpawn:
                OnAddSpawn?.Invoke(msg.SenderId, msg);
                break;

            case LanMessageType.BurnSignal:
                OnBurnSignal?.Invoke(msg.SenderId, msg);
                break;
        }
    }

    private void HandleHeartbeat(LanMessage msg, DateTime now)
    {
        var hb = LanHeartbeatPayload.FromJson(msg.Payload);
        if (hb is null) return;

        double latency = 0;
        // The remote echoed the newest timestamp it had seen from anyone; if that was one of OUR
        // ticks, now - echo ≈ round trip.
        if (hb.EchoTimestamp > 0)
        {
            var rtt = (now - new DateTime(hb.EchoTimestamp, DateTimeKind.Utc)).TotalMilliseconds;
            if (rtt is > 0 and < 10_000) latency = rtt;
        }

        UpsertRosterEntry(msg.SenderId, msg.MachineId, hb, now, latency);
    }

    private void UpsertRosterEntry(string senderId, string machineId, LanHeartbeatPayload hb, DateTime now, double latencyMs)
    {
        if (senderId.Length == 0) return;

        lock (_roster)
        {
            if (!_roster.TryGetValue(senderId, out var peer))
            {
                peer = new LanPeerInfo { SenderId = senderId, MachineId = machineId };
                _roster[senderId] = peer;
            }

            peer.MachineId = machineId;
            peer.CharacterName = hb.CharacterName;
            peer.JobId = hb.JobId;
            peer.JobAbbrev = hb.JobAbbrev;
            peer.HpPercent = hb.HpPercent;
            peer.Role = hb.Role;
            peer.Status = hb.Status;
            peer.LastSeenUtc = now;
            if (latencyMs > 0) peer.LatencyMs = latencyMs;
        }
    }

    private void HandleIpcMirror(LanMessage msg)
    {
        if (_partyService is null) return;

        var split = msg.Payload.IndexOf('\n');
        if (split <= 0) return;
        var channel = msg.Payload[..split];
        var json = msg.Payload[(split + 1)..];

        var parsed = PartyMessage.FromJson(json);
        if (parsed is null) return; // malformed / version mismatch — skip

        // Route into the exact same handlers Dalamud IPC uses — the service can't tell the
        // transports apart, which is the whole point of the bus.
        switch (channel)
        {
            case "Heartbeat": if (parsed is HeartbeatMessage hb) _partyService.HandleRemoteHeartbeat(hb); break;
            case "HealIntent": if (parsed is HealIntentMessage hi) _partyService.HandleRemoteHealIntent(hi); break;
            case "HealLanded": if (parsed is HealLandedMessage hl) _partyService.HandleRemoteHealLanded(hl); break;
            case "CooldownUsed": if (parsed is CooldownUsedMessage cu) _partyService.HandleRemoteCooldownUsed(cu); break;
            case "AoEHealIntent": if (parsed is AoEHealIntentMessage ah) _partyService.HandleRemoteAoEHealIntent(ah); break;
            case "RaidBuffIntent": if (parsed is RaidBuffIntentMessage rb) _partyService.HandleRemoteRaidBuffIntent(rb); break;
            case "BurstWindowStart": if (parsed is BurstWindowStartMessage bw) _partyService.HandleRemoteBurstWindowStart(bw); break;
            case "GaugeState": if (parsed is GaugeStateMessage gs) _partyService.HandleRemoteGaugeState(gs); break;
            case "RoleDeclaration": if (parsed is RoleDeclarationMessage rd) _partyService.HandleRemoteRoleDeclaration(rd); break;
            case "GroundEffectPlaced": if (parsed is GroundEffectPlacedMessage ge) _partyService.HandleRemoteGroundEffectPlaced(ge); break;
            case "RaiseIntent": if (parsed is RaiseIntentMessage ri) _partyService.HandleRemoteRaiseIntent(ri); break;
            case "CleanseIntent": if (parsed is CleanseIntentMessage ci) _partyService.HandleRemoteCleanseIntent(ci); break;
            case "InterruptIntent": if (parsed is InterruptIntentMessage ii) _partyService.HandleRemoteInterruptIntent(ii); break;
            case "TankSwapIntent": if (parsed is TankSwapIntentMessage ts) _partyService.HandleRemoteTankSwapIntent(ts); break;
            default:
                _log.Debug($"LAN mirror: unknown channel '{channel}'");
                break;
        }
    }

    #endregion

    #region Outgoing

    private void MirrorToLan(string channel, PartyMessage message)
    {
        _lan.Send(new LanMessage
        {
            Type = LanMessageType.IpcMirror,
            Payload = channel + "\n" + message.ToJson(),
        });
    }

    /// <summary>Zone-in: broadcast our job/role and open the 3s collection window.</summary>
    public void BroadcastRoleAssignment(LanRolePayload payload)
    {
        _roleResponses.Clear();
        _roleResponses[_lan.SenderId] = payload;
        _roleCollectUntil = DateTime.UtcNow.AddSeconds(3);
        _lan.Send(new LanMessage { Type = LanMessageType.RoleAssignment, Payload = payload.ToJson() });
    }

    /// <summary>Signal our burst cooldowns are up. Fires BurstFire when every rostered toon is ready.</summary>
    public void BroadcastBurstReady()
    {
        _burstReadySenders.Add(_lan.SenderId);
        _lan.Send(new LanMessage { Type = LanMessageType.BurstReady });
        TryFireBurst();
    }

    public void BroadcastHealerDown(string deadHealerNames)
        => _lan.Send(new LanMessage { Type = LanMessageType.HealerDown, Payload = deadHealerNames });

    public void BroadcastPhoenixDown(string targetCharacterName)
        => _lan.Send(new LanMessage { Type = LanMessageType.PhoenixDown, Payload = targetCharacterName });

    public void BroadcastTankSwap(string payload)
        => _lan.Send(new LanMessage { Type = LanMessageType.TankSwap, Payload = payload });

    public void BroadcastAddSpawn(string payload)
        => _lan.Send(new LanMessage { Type = LanMessageType.AddSpawn, Payload = payload });

    #endregion

    #region Burst coordination

    /// <summary>True while a coordinated burst window is open (3s after BurstFire).</summary>
    public bool IsBurstFireActive => DateTime.UtcNow < _burstFireUntil;

    private void TryFireBurst()
    {
        // Coordinator election without a protocol: the alphabetically-first fresh toon fires the
        // signal once everyone in the roster (plus us) has reported ready. Deterministic on every
        // machine, so exactly one toon broadcasts BurstFire.
        var now = DateTime.UtcNow;
        List<string> fresh;
        lock (_roster)
            fresh = _roster.Values.Where(p => !p.IsStale(now)).Select(p => p.SenderId).ToList();
        fresh.Add(_lan.SenderId);
        fresh.Sort(StringComparer.Ordinal);

        if (fresh.Count == 0 || fresh[0] != _lan.SenderId) return;   // not the coordinator
        if (fresh.Any(s => !_burstReadySenders.Contains(s))) return; // someone not ready yet

        _lan.Send(new LanMessage { Type = LanMessageType.BurstFire });
        ActivateBurstFire(null);
    }

    private void ActivateBurstFire(LanMessage? _)
    {
        _burstFireUntil = DateTime.UtcNow.AddSeconds(3);
        _burstReadySenders.Clear();
        OnBurstFire?.Invoke();

        // Light up the EXISTING burst window state machine so every rotation's
        // GetBurstWindowState().IsActive gate opens without job-by-job changes.
        if (_partyService != null)
        {
            var start = new BurstWindowStartMessage
            {
                InstanceId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            };
            try { _partyService.HandleRemoteBurstWindowStart(start); }
            catch (Exception ex) { _log.Warning(ex, "BurstFire -> burst window bridge failed"); }
        }
    }

    #endregion

    #region Role slots

    private void AssignRoleSlots()
    {
        // Deterministic slotting: tanks, then healers, then DPS — each ordered by sender id, so
        // every machine computes identical assignments with no extra negotiation round.
        var ordered = _roleResponses.OrderBy(kv => kv.Key, StringComparer.Ordinal).ToArray();
        int tank = 0, healer = 0, dps = 0;
        var slots = new Dictionary<string, string>();
        foreach (var (sender, payload) in ordered)
        {
            var slot = payload.Role switch
            {
                "Tank" => $"Tank {++tank}",
                "Healer" => $"Healer {++healer}",
                _ => $"DPS {++dps}",
            };
            slots[sender] = slot;
        }

        lock (_roster)
        {
            foreach (var (sender, slot) in slots)
                if (_roster.TryGetValue(sender, out var peer))
                    peer.AssignedSlot = slot;
        }

        OnRolesAssigned?.Invoke(_roleResponses);
        _log.Info($"LAN role slots assigned: {string.Join(", ", slots.Select(kv => $"{kv.Key}={kv.Value}"))}");
    }

    /// <summary>The local toon's assigned slot after negotiation ("" until assigned).</summary>
    public string LocalAssignedSlot
    {
        get
        {
            lock (_roster)
                return _roster.TryGetValue(_lan.SenderId, out var self) ? self.AssignedSlot : "";
        }
    }

    #endregion

    public void Dispose()
    {
        // LanCoordinator is owned/disposed by the plugin; nothing to release here beyond events.
    }
}
