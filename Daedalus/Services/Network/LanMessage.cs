using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Daedalus.Services.Network;

/// <summary>
/// LAN coordination message types. The first block mirrors the plan in CLAUDE.md; IpcMirror wraps
/// any existing Dalamud-IPC <see cref="Daedalus.Ipc.PartyMessage"/> so every same-machine protocol
/// message automatically crosses the LAN too (CoordinationBus handles routing + dedup).
/// </summary>
public enum LanMessageType
{
    /// <summary>IPC burn coordination — hold burst until signal.</summary>
    BurnSignal = 0,

    /// <summary>Tank/healer/DPS role negotiation on zone-in.</summary>
    RoleAssignment = 1,

    /// <summary>RETIRED (value reserved so wire ids don't shift). The raw Provoke/Shirk signal was
    /// never wired to a sender; coordinated swaps ride TankSwapIntent (IpcMirror) + TankSwapCommand.</summary>
    TankSwap_Retired = 2,

    /// <summary>New hostile detected — designated add tank responds.</summary>
    AddSpawn = 3,

    /// <summary>One or both healers dead — trigger Phoenix Down logic.</summary>
    HealerDown = 4,

    /// <summary>Designated carrier is executing Phoenix Down on target.</summary>
    PhoenixDown = 5,

    /// <summary>Toon signals burst cooldowns are ready.</summary>
    BurstReady = 6,

    /// <summary>Coordinator signals all toons to dump burst simultaneously.</summary>
    BurstFire = 7,

    /// <summary>Lightweight roster heartbeat (job, HP%, role, status) every ~2s.</summary>
    Heartbeat = 8,

    /// <summary>A mirrored Dalamud-IPC PartyMessage; Payload = "{channel}\n{json}".</summary>
    IpcMirror = 9,

    /// <summary>Per-toon DPS self-report for the parser (~2s cadence in combat + final on end).</summary>
    DpsReport = 10,

    /// <summary>Party-wide targeting mode (Focus / Split / Kill Adds) set from the coordination window.</summary>
    TargetMode = 11,

    /// <summary>Manual "swap tanks now" from the coordination window — arms a swap on every tank box,
    /// which then run their normal request/confirm handshake per live aggro.</summary>
    TankSwapCommand = 12,

    /// <summary>Generic companion-plugin relay (Charon↔Charon etc.): opaque {channel, json} ferried
    /// across machines AND same-machine siblings (loopback mirror). The bus never inspects the data.</summary>
    PluginRelay = 13,

    /// <summary>BLU v3.4 fleet Final Sting: ordered stingers execute staggered on the target.</summary>
    ExecuteSting = 14,

    /// <summary>Fleet-wide BLU mimicry command from the coordination window (mimic role / remove).</summary>
    BluMimicry = 15,

    /// <summary>LAN Phase 2: pre-pull countdown T0 mirror for toons that can't see the local
    /// countdown agent (unpartied fleet members). T0Ticks=0 = countdown cancelled.</summary>
    CountdownStart = 16,
}

/// <summary>
/// Party-wide targeting intent set from the Party Coordination window and enforced by
/// <c>PartyTargetingCoordinator</c> on every eligible toon. <see cref="None"/> = normal per-job
/// targeting.
/// </summary>
public enum PartyTargetMode
{
    /// <summary>No override — each toon targets normally.</summary>
    None = 0,

    /// <summary>Everyone converges on one chosen enemy and ignores adds (single-target burn).</summary>
    Focus = 1,

    /// <summary>DPS spread across mobs, TTK-balanced so the pack dies together.</summary>
    Split = 2,

    /// <summary>Eligible toons (DPS + off-tank) prioritize adds while the MT holds the boss.</summary>
    KillAdds = 3,
}

/// <summary>
/// The UDP envelope. Compact JSON (short property names) — combat signals should stay well under
/// a single MTU. <see cref="SenderId"/> is unique per toon (character@world), <see cref="MachineId"/>
/// unique per machine (GUID persisted in config) — the pair drives self-filtering and IPC/LAN dedup.
/// </summary>
public sealed class LanMessage
{
    [JsonPropertyName("s")]
    public string SenderId { get; set; } = "";

    [JsonPropertyName("m")]
    public string MachineId { get; set; } = "";

    [JsonPropertyName("t")]
    public LanMessageType Type { get; set; }

    [JsonPropertyName("p")]
    public string Payload { get; set; } = "";

    /// <summary>UTC ticks at send time — ordering + dedup key component.</summary>
    [JsonPropertyName("ts")]
    public long Timestamp { get; set; }

    /// <summary>Protocol version — bump on incompatible schema changes.</summary>
    [JsonPropertyName("v")]
    public int Version { get; set; } = CurrentVersion;

    public const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
    };

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    /// <summary>Deserializes; returns null on malformed input or version mismatch (never throws).</summary>
    public static LanMessage? FromJson(string json)
    {
        try
        {
            var msg = JsonSerializer.Deserialize<LanMessage>(json, Options);
            if (msg is null || msg.Version != CurrentVersion)
                return null;
            return msg;
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>Heartbeat payload — everything the LAN Party window needs per toon.</summary>
public sealed class LanHeartbeatPayload
{
    [JsonPropertyName("n")]
    public string CharacterName { get; set; } = "";

    [JsonPropertyName("j")]
    public uint JobId { get; set; }

    [JsonPropertyName("jn")]
    public string JobAbbrev { get; set; } = "";

    [JsonPropertyName("hp")]
    public float HpPercent { get; set; }

    [JsonPropertyName("r")]
    public string Role { get; set; } = "";

    [JsonPropertyName("st")]
    public string Status { get; set; } = "";

    /// <summary>Current enemy target GameObjectId (0 = none). Shared across clients in one instance,
    /// so the window can resolve/compare it for target-agreement display. Additive/back-compat.</summary>
    [JsonPropertyName("tg")]
    public ulong TargetId { get; set; }

    /// <summary>Whether this toon is currently in combat. Additive/back-compat (defaults false).</summary>
    [JsonPropertyName("ic")]
    public bool InCombat { get; set; }

    /// <summary>The toon's in-game party id (0 = solo). Toons sharing a non-zero id are in the same
    /// actual party — drives the roster's group indicator. Additive/back-compat.</summary>
    [JsonPropertyName("pg")]
    public ulong PartyGroupId { get; set; }

    /// <summary>The toon's content id — required by the native party-invite call. Additive/back-compat.</summary>
    [JsonPropertyName("cid")]
    public ulong ContentId { get; set; }

    /// <summary>The toon's EntityId — lets companion plugins (Charon Heal Watch) resolve this toon
    /// in their local object table without name collisions. Additive/back-compat.</summary>
    [JsonPropertyName("eid")]
    public uint PlayerEntityId { get; set; }

    /// <summary>The toon's home world row id — required by the native party-invite call.</summary>
    [JsonPropertyName("wid")]
    public ushort HomeWorldId { get; set; }

    /// <summary>World position — used by the Split assigner's locality term. Additive/back-compat.</summary>
    [JsonPropertyName("px")]
    public float PosX { get; set; }

    [JsonPropertyName("py")]
    public float PosY { get; set; }

    [JsonPropertyName("pz")]
    public float PosZ { get; set; }

    /// <summary>
    /// BLU capability bitfield (<see cref="Daedalus.Services.Blu.BluCapabilities"/>): which
    /// coordination-relevant spells are slotted + role hints. 0 off-BLU and from older clients —
    /// consumers treat missing bits as "cannot be assigned". Additive/back-compat.
    /// </summary>
    [JsonPropertyName("cap")]
    public uint BluCapabilities { get; set; }

    /// <summary>Echo of the newest remote timestamp we've seen — latency = now - echo (one-way-ish).</summary>
    [JsonPropertyName("e")]
    public long EchoTimestamp { get; set; }

    /// <summary>
    /// How long (ms) the echoed timestamp sat waiting for this heartbeat to go out. Without it,
    /// "RTT" included up to a full 2s heartbeat interval of dwell (981 ms shown on loopback).
    /// Receivers subtract this; absent/0 from older clients degrades to the old behavior.
    /// </summary>
    [JsonPropertyName("eh")]
    public long EchoHeldMs { get; set; }

    // Skip default-valued fields on the wire (a non-BLU heartbeat has no cap, an idle toon no
    // target...) — every reader already defaults missing fields, so this only shrinks datagrams.
    private static readonly JsonSerializerOptions CompactOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
    };

    public string ToJson() => JsonSerializer.Serialize(this, CompactOptions);

    public static LanHeartbeatPayload? FromJson(string json)
    {
        try { return JsonSerializer.Deserialize<LanHeartbeatPayload>(json, CompactOptions); }
        catch { return null; }
    }
}

/// <summary>
/// DpsReport payload — a toon's own parse for the current encounter. Self-observed damage is
/// exact (own action effects always arrive), so receivers treat these as authoritative and
/// override their locally-observed row for this character.
/// </summary>
public sealed class LanDpsReportPayload
{
    [JsonPropertyName("n")]
    public string CharacterName { get; set; } = "";

    [JsonPropertyName("j")]
    public string JobAbbrev { get; set; } = "";

    /// <summary>UTC ticks of the sender's encounter start.</summary>
    [JsonPropertyName("es")]
    public long EncounterStartTicks { get; set; }

    /// <summary>Sender's territory id — receivers in a different zone drop the report.</summary>
    [JsonPropertyName("t")]
    public ushort TerritoryId { get; set; }

    [JsonPropertyName("d")]
    public long TotalDamage { get; set; }

    [JsonPropertyName("dur")]
    public float DurationSeconds { get; set; }

    [JsonPropertyName("c")]
    public float CritPercent { get; set; }

    [JsonPropertyName("dh")]
    public float DirectHitPercent { get; set; }

    /// <summary>True on the last report of an encounter (combat ended on the sender).</summary>
    [JsonPropertyName("f")]
    public bool IsFinal { get; set; }

    public string ToJson() => JsonSerializer.Serialize(this);

    public static LanDpsReportPayload? FromJson(string json)
    {
        try { return JsonSerializer.Deserialize<LanDpsReportPayload>(json); }
        catch { return null; }
    }
}

/// <summary>TargetMode payload — the party-wide targeting intent set from the coordination window.</summary>
public sealed class LanTargetModePayload
{
    [JsonPropertyName("md")]
    public PartyTargetMode Mode { get; set; }

    /// <summary>Focus mode: the chosen enemy's GameObjectId (0 otherwise).</summary>
    [JsonPropertyName("f")]
    public ulong FocusTargetId { get; set; }

    /// <summary>SenderId of the designated off-tank (empty = none; all tanks protected).</summary>
    [JsonPropertyName("ot")]
    public string OffTankSenderId { get; set; } = "";

    public string ToJson() => JsonSerializer.Serialize(this);

    public static LanTargetModePayload? FromJson(string json)
    {
        try { return JsonSerializer.Deserialize<LanTargetModePayload>(json); }
        catch { return null; }
    }
}

/// <summary>
/// PluginRelay payload — an opaque companion-plugin message. The bus ferries it verbatim
/// (cross-machine + same-machine loopback) and never inspects <see cref="Data"/>.
/// </summary>
public sealed class LanPluginRelayPayload
{
    /// <summary>Consumer-defined channel, e.g. "charon.pillion".</summary>
    [JsonPropertyName("ch")]
    public string Channel { get; set; } = "";

    /// <summary>Opaque JSON payload owned by the publishing plugin.</summary>
    [JsonPropertyName("data")]
    public string Data { get; set; } = "";

    public string ToJson() => JsonSerializer.Serialize(this);

    public static LanPluginRelayPayload? FromJson(string json)
    {
        try { return JsonSerializer.Deserialize<LanPluginRelayPayload>(json); }
        catch { return null; }
    }
}

/// <summary>ExecuteSting payload — the fleet Final Sting order (v3.4).</summary>
public sealed class LanExecuteStingPayload
{
    /// <summary>The boss's GameObjectId (shared across clients in one instance).</summary>
    [JsonPropertyName("t")]
    public ulong TargetId { get; set; }

    /// <summary>Ordered stinger SenderIds — index × 3s = each toon's stagger offset.</summary>
    [JsonPropertyName("s")]
    public string[] Stingers { get; set; } = [];

    public string ToJson() => JsonSerializer.Serialize(this);

    public static LanExecuteStingPayload? FromJson(string json)
    {
        try { return JsonSerializer.Deserialize<LanExecuteStingPayload>(json); }
        catch { return null; }
    }
}

/// <summary>BluMimicry payload — fleet-wide mimicry command (role buttons / remove).</summary>
public sealed class LanBluMimicryPayload
{
    /// <summary>BluRole enum value to mimic; ignored when <see cref="Remove"/> is set.</summary>
    [JsonPropertyName("r")]
    public int Role { get; set; }

    [JsonPropertyName("x")]
    public bool Remove { get; set; }

    public string ToJson() => JsonSerializer.Serialize(this);

    public static LanBluMimicryPayload? FromJson(string json)
    {
        try { return JsonSerializer.Deserialize<LanBluMimicryPayload>(json); }
        catch { return null; }
    }
}

/// <summary>CountdownStart payload — shared pull T0 (UTC ticks; 0 = cancelled).</summary>
public sealed class LanCountdownPayload
{
    [JsonPropertyName("t0")]
    public long T0Ticks { get; set; }

    public string ToJson() => JsonSerializer.Serialize(this);

    public static LanCountdownPayload? FromJson(string json)
    {
        try { return JsonSerializer.Deserialize<LanCountdownPayload>(json); }
        catch { return null; }
    }
}

/// <summary>RoleAssignment payload — broadcast on zone-in, collected for ~3s.</summary>
public sealed class LanRolePayload
{
    [JsonPropertyName("n")]
    public string CharacterName { get; set; } = "";

    [JsonPropertyName("j")]
    public uint JobId { get; set; }

    [JsonPropertyName("r")]
    public string Role { get; set; } = "";

    public string ToJson() => JsonSerializer.Serialize(this);

    public static LanRolePayload? FromJson(string json)
    {
        try { return JsonSerializer.Deserialize<LanRolePayload>(json); }
        catch { return null; }
    }
}
