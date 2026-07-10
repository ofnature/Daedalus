using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
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
    /// <summary>Current enemy target GameObjectId (0 = none) — for target-agreement display.</summary>
    public ulong TargetId;
    /// <summary>Whether this toon is currently in combat.</summary>
    public bool InCombat;
    /// <summary>In-game party id (0 = solo) — toons sharing a non-zero id are in the same party.</summary>
    public ulong PartyGroupId;
    /// <summary>Content id + home world — the native party-invite call's addressing.</summary>
    public ulong ContentId;
    public ushort HomeWorldId;
    /// <summary>World position (~2s stale) — used by the Split assigner's locality term.</summary>
    public Vector3 Position;
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
/// Transport model: Dalamud IPC call gates are PER-PROCESS, so the UDP loopback mirror is the only
/// path between two game clients on the same machine — IpcMirror frames are processed from every
/// machine including our own. A toon's own frames are filtered by SenderId in the receive loop,
/// every service handler ignores its own InstanceId, and replays dedup on (SenderId, Timestamp).
/// Socket-thread messages are queued and drained on the framework thread via <see cref="Update"/>
/// — game state is never touched from the receive thread.
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
    private DateTime _newestRemoteTimestampSeenUtc;

    // Burst coordination
    private readonly HashSet<string> _burstReadySenders = new();
    private DateTime _burstFireUntil = DateTime.MinValue;

    // Role negotiation
    private DateTime _roleCollectUntil = DateTime.MinValue;
    private readonly Dictionary<string, LanRolePayload> _roleResponses = new();

    // Party target mode (Focus / Split / Kill Adds). Sticky while its setter keeps refreshing it;
    // auto-clears if the setter goes silent so a crashed master never leaves toons locked.
    private PartyTargetMode _targetMode = PartyTargetMode.None;
    private ulong _focusTargetId;
    private string _offTankSenderId = "";
    private string _targetModeOwner = "";
    private DateTime _targetModeFreshUntil = DateTime.MinValue;
    private DateTime _lastTargetModeSent = DateTime.MinValue;
    // After switching to None the owner keeps rebroadcasting for a short tail — a single lost UDP
    // datagram must not leave remote boxes enforcing a dead mode until the 10s timeout.
    private DateTime _noneRebroadcastUntil = DateTime.MinValue;
    private const float TargetModeTimeoutSeconds = 10f;
    private const float TargetModeRebroadcastSeconds = 2f;
    private const float NoneRebroadcastTailSeconds = 6f;

    /// <summary>Supplies the local toon's heartbeat payload each interval (null = not logged in).</summary>
    public Func<LanHeartbeatPayload?>? HeartbeatProvider { get; set; }

    public event System.Action<string /*sender*/, LanMessage>? OnHealerDown;
    public event System.Action<string /*sender*/, string /*targetName*/>? OnPhoenixDown;
    public event System.Action<string /*sender*/, LanMessage>? OnAddSpawn;

    /// <summary>Real tank-swap traffic (request/confirm/manual command) for the window's alert feed.
    /// String = a short human description e.g. "requested", "confirmed", "manual".</summary>
    public event System.Action<string /*description*/>? OnTankSwapActivity;
    public event System.Action<string /*sender*/, LanMessage>? OnBurnSignal;
    public event System.Action? OnBurstFire;
    public event System.Action<IReadOnlyDictionary<string, LanRolePayload>>? OnRolesAssigned;

    /// <summary>Raised whenever the party target mode / focus / off-tank changes (local set or remote).</summary>
    public event System.Action? OnTargetModeChanged;

    /// <summary>A remote toon's DPS self-report (framework thread — raised from <see cref="Update"/>).</summary>
    public event System.Action<string /*sender*/, LanDpsReportPayload>? OnDpsReport;

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
            _partyService.OnTankSwapIntentReady += m =>
            {
                MirrorToLan("TankSwapIntent", m);
                RaiseTankSwapActivity(m.IsConfirmation ? "confirmed" : "requested");
            };
        }
    }

    private void RaiseTankSwapActivity(string description) => OnTankSwapActivity?.Invoke(description);

    #region Roster / status

    public IReadOnlyCollection<LanPeerInfo> Roster
    {
        get { lock (_roster) return _roster.Values.ToArray(); }
    }

    public string LocalMachineId => _localMachineId;

    /// <summary>The local toon's sender id ("Character@World").</summary>
    public string LocalSenderId => _lan.SenderId;

    /// <summary>The active party targeting mode (auto-clears to None if the setter goes silent).</summary>
    public PartyTargetMode CurrentTargetMode => _targetMode;

    /// <summary>Focus-mode chosen enemy GameObjectId (0 when not in Focus / none picked).</summary>
    public ulong FocusTargetId => _focusTargetId;

    /// <summary>SenderId of the designated off-tank (empty = none; every tank protected).</summary>
    public string OffTankSenderId => _offTankSenderId;

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
                payload.EchoHeldMs = _newestRemoteTimestamp > 0
                    ? (long)(now - _newestRemoteTimestampSeenUtc).TotalMilliseconds
                    : 0;
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

        UpdateTargetMode(now);
    }

    private void UpdateTargetMode(DateTime now)
    {
        if (_targetMode == PartyTargetMode.None)
        {
            // Off-tank designation outlives the target mode: while an off-tank is set, the owner keeps
            // the state alive indefinitely (refreshing freshness) so tank-swap roles don't evaporate
            // at the 10s timeout just because no target mode is active.
            if (_targetModeOwner == _lan.SenderId
                && _offTankSenderId.Length > 0
                && (now - _lastTargetModeSent).TotalSeconds >= TargetModeRebroadcastSeconds)
            {
                _targetModeFreshUntil = now.AddSeconds(TargetModeTimeoutSeconds);
                SendTargetMode();
                return;
            }

            // Owner tail: re-announce None a few times so one lost datagram can't strand remote
            // boxes on the old mode until their freshness timeout.
            if (_targetModeOwner == _lan.SenderId
                && now < _noneRebroadcastUntil
                && (now - _lastTargetModeSent).TotalSeconds >= TargetModeRebroadcastSeconds)
            {
                SendTargetMode();
            }

            return;
        }

        // We own the mode: keep it alive for the party (and refresh our own expiry).
        if (_targetModeOwner == _lan.SenderId)
        {
            if ((now - _lastTargetModeSent).TotalSeconds >= TargetModeRebroadcastSeconds)
            {
                _targetModeFreshUntil = now.AddSeconds(TargetModeTimeoutSeconds);
                SendTargetMode();
            }
            return;
        }

        // A remote owner set it — auto-clear if they've gone silent (crash / disconnect).
        if (now > _targetModeFreshUntil)
        {
            _targetMode = PartyTargetMode.None;
            _focusTargetId = 0;
            _offTankSenderId = "";
            _targetModeOwner = "";
            OnTargetModeChanged?.Invoke();
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
        {
            _newestRemoteTimestamp = msg.Timestamp;
            _newestRemoteTimestampSeenUtc = now;
        }

        switch (msg.Type)
        {
            case LanMessageType.Heartbeat:
                HandleHeartbeat(msg, now);
                break;

            case LanMessageType.IpcMirror:
                // Process mirrors from EVERY machine, including our own. The old same-machine drop
                // assumed Dalamud IPC had already delivered local traffic — but Dalamud call gates
                // are per-process, so two game clients on one PC never see each other's IPC. The
                // UDP loopback mirror IS the same-machine transport (it's how the roster works);
                // dropping it killed instance discovery (HasRemoteTank), tank-swap intents, and
                // every reservation channel between same-machine toons. Our own frames are already
                // filtered by SenderId in the receive loop, and every service handler additionally
                // ignores its own InstanceId, so there is no double-delivery.
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

            case LanMessageType.TankSwapCommand:
                // A tank box pressed "swap tanks": arm the manual swap on this box too (remote press).
                _partyService?.ArmManualSwap();
                RaiseTankSwapActivity("manual");
                break;

            case LanMessageType.AddSpawn:
                OnAddSpawn?.Invoke(msg.SenderId, msg);
                break;

            case LanMessageType.BurnSignal:
                OnBurnSignal?.Invoke(msg.SenderId, msg);
                break;

            case LanMessageType.DpsReport:
                var report = LanDpsReportPayload.FromJson(msg.Payload);
                if (report != null)
                    OnDpsReport?.Invoke(msg.SenderId, report);
                break;

            case LanMessageType.TargetMode:
                var tm = LanTargetModePayload.FromJson(msg.Payload);
                if (tm != null)
                    ApplyTargetModeState(tm.Mode, tm.FocusTargetId, tm.OffTankSenderId, owner: msg.SenderId);
                break;
        }
    }

    private void HandleHeartbeat(LanMessage msg, DateTime now)
    {
        var hb = LanHeartbeatPayload.FromJson(msg.Payload);
        if (hb is null) return;

        double latency = 0;
        // The remote echoed the newest timestamp it had seen from anyone; if that was one of OUR
        // ticks, now - echo - (how long the remote held it before this heartbeat) ≈ round trip.
        if (hb.EchoTimestamp > 0)
        {
            var rtt = (now - new DateTime(hb.EchoTimestamp, DateTimeKind.Utc)).TotalMilliseconds
                      - hb.EchoHeldMs;
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
            peer.TargetId = hb.TargetId;
            peer.InCombat = hb.InCombat;
            peer.PartyGroupId = hb.PartyGroupId;
            peer.ContentId = hb.ContentId;
            peer.HomeWorldId = hb.HomeWorldId;
            peer.Position = new Vector3(hb.PosX, hb.PosY, hb.PosZ);
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
            case "TankSwapIntent":
                if (parsed is TankSwapIntentMessage ts)
                {
                    _partyService.HandleRemoteTankSwapIntent(ts);
                    RaiseTankSwapActivity(ts.IsConfirmation ? "confirmed" : "requested");
                }
                break;
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

    /// <summary>
    /// Manual "swap tanks now" from the coordination window. Arms the swap locally (our own broadcast
    /// is filtered on loopback) and triple-sends with one shared Timestamp — the dedup ring
    /// (sender, timestamp, type) makes the redundant copies idempotent, so a single dropped datagram
    /// can't lose the command.
    /// </summary>
    public void BroadcastTankSwapCommand()
    {
        _partyService?.ArmManualSwap();
        RaiseTankSwapActivity("manual");

        var ts = DateTime.UtcNow.Ticks;
        for (var i = 0; i < 3; i++)
            _lan.Send(new LanMessage { Type = LanMessageType.TankSwapCommand, Timestamp = ts });
    }

    public void BroadcastAddSpawn(string payload)
        => _lan.Send(new LanMessage { Type = LanMessageType.AddSpawn, Payload = payload });

    /// <summary>Broadcast this toon's DPS self-report for the current encounter.</summary>
    public void BroadcastDpsReport(LanDpsReportPayload payload)
        => _lan.Send(new LanMessage { Type = LanMessageType.DpsReport, Payload = payload.ToJson() });

    /// <summary>
    /// Set + broadcast the party targeting mode from the coordination window. This toon becomes the
    /// mode's owner and keeps it alive via periodic rebroadcast (see <see cref="Update"/>).
    /// </summary>
    public void BroadcastTargetMode(PartyTargetMode mode, ulong focusTargetId, string offTankSenderId)
    {
        ApplyTargetModeState(mode, focusTargetId, offTankSenderId, owner: _lan.SenderId);
        SendTargetMode();
    }

    private void SendTargetMode()
    {
        _lastTargetModeSent = DateTime.UtcNow;
        _lan.Send(new LanMessage
        {
            Type = LanMessageType.TargetMode,
            Payload = new LanTargetModePayload
            {
                Mode = _targetMode,
                FocusTargetId = _focusTargetId,
                OffTankSenderId = _offTankSenderId,
            }.ToJson(),
        });
    }

    private void ApplyTargetModeState(PartyTargetMode mode, ulong focusTargetId, string offTankSenderId, string owner)
    {
        var changed = _targetMode != mode || _focusTargetId != focusTargetId || _offTankSenderId != offTankSenderId;
        if (changed && mode == PartyTargetMode.None && owner == _lan.SenderId)
            _noneRebroadcastUntil = DateTime.UtcNow.AddSeconds(NoneRebroadcastTailSeconds);
        _targetMode = mode;
        _focusTargetId = focusTargetId;
        _offTankSenderId = offTankSenderId ?? "";
        _targetModeOwner = owner;
        _targetModeFreshUntil = DateTime.UtcNow.AddSeconds(TargetModeTimeoutSeconds);
        if (changed)
            OnTargetModeChanged?.Invoke();
    }

    #endregion

    #region Burst coordination

    /// <summary>True while a coordinated burst window is open (3s after BurstFire).</summary>
    public bool IsBurstFireActive => DateTime.UtcNow < _burstFireUntil;

    /// <summary>
    /// Senders that have signaled burst-ready this cycle (drives the window's ⚡ readiness pips).
    /// All access is on the framework thread, so a plain snapshot is safe.
    /// </summary>
    public IReadOnlyCollection<string> BurstReadySenders => _burstReadySenders.ToArray();

    /// <summary>
    /// Operator-triggered manual burst: dump now regardless of who has reported ready, so a burst can
    /// be aligned on demand from the Party Coordination window. Mirrors the fire path in
    /// <see cref="TryFireBurst"/> without the all-ready gate.
    /// </summary>
    public void ForceBurstFire()
    {
        _lan.Send(new LanMessage { Type = LanMessageType.BurstFire });
        ActivateBurstFire(null);
    }

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
