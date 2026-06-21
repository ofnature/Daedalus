namespace Olympus.Services.Content;

/// <summary>
/// Runtime tuning profile applied on top of saved configuration when auto duty config is enabled.
/// </summary>
public enum EffectiveDutyProfile : byte
{
    /// <summary>No overlay — use saved configuration only.</summary>
    None = 0,

    /// <summary>4-player dungeon / trust — aggressive DPS, reactive healing.</summary>
    Dungeon,

    /// <summary>8-player trial — raid-like with lighter preemptive healing.</summary>
    Trial,

    /// <summary>8-player raid — co-healer aware, burst coordination.</summary>
    Raid,

    /// <summary>Savage / extreme / ultimate — proactive timeline and party coordination.</summary>
    HighEndRaid,
}
