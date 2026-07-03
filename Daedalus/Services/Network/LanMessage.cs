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

    /// <summary>Provoke/Shirk coordination signal.</summary>
    TankSwap = 2,

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

    public string ToJson() => JsonSerializer.Serialize(this);

    public static LanHeartbeatPayload? FromJson(string json)
    {
        try { return JsonSerializer.Deserialize<LanHeartbeatPayload>(json); }
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

    /// <summary>UTC ticks of the sender's encounter start — receivers match ±15s.</summary>
    [JsonPropertyName("es")]
    public long EncounterStartTicks { get; set; }

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
