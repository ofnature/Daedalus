namespace Daedalus.Config;

/// <summary>
/// Variant dungeon duty-action settings (docs/variant-actions-plan.md). Phase 1: only the
/// config UI reads it; the Phase 2 VariantActionLayer consumes it.
/// </summary>
public sealed class VariantConfig
{
    /// <summary>Master toggle for the variant duty-action layer (in variant territories only).</summary>
    public bool EnableVariantActions { get; set; } = true;

    /// <summary>Variant Cure fires when self HP falls below this fraction.</summary>
    public float CureHpPct { get; set; } = 0.60f;

    /// <summary>Maintain the Spirit Dart DoT (Sustained Damage) on the current target.</summary>
    public bool UseSpiritDart { get; set; } = true;

    /// <summary>Fire Eagle Eye Shot on cooldown at the current target.</summary>
    public bool UseEagleEyeShot { get; set; } = true;

    /// <summary>Keep Variant Rampart's 20% reduction up while in combat.</summary>
    public bool UseRampart { get; set; } = true;

    /// <summary>Recast Rampart on cooldown even while its buff is still up (RSR parity option).</summary>
    public bool RampartSpamOnCooldown { get; set; } = false;

    /// <summary>Hard-cast Variant Raise on dead party members (8s cast, own recast timer).</summary>
    public bool UseRaise { get; set; } = true;

    /// <summary>AoE provoke + stun. Default OFF — in a multibox party only the MT wants this.</summary>
    public bool UseUltimatum { get; set; } = false;
}
