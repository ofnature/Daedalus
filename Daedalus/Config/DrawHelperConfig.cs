namespace Daedalus.Config;

/// <summary>
/// Configuration for the Draw Helper feature — world-space visual overlays.
/// </summary>
public sealed class DrawHelperConfig
{
    // Master toggle
    public bool DrawingEnabled { get; set; } = false;

    // Pictomancy backend
    public bool UsePictomancy { get; set; } = true;
    public float PictomancyMaxAlpha { get; set; } = 0.5f;
    /// <summary>
    /// Clip overlays behind native UI (cast bar, etc.). Requires Pictomancy struct parity; off by default.
    /// </summary>
    public bool PictomancyClipNativeUI { get; set; } = false;

    // Enemy hitboxes
    public bool ShowEnemyHitboxes { get; set; } = false;
    public uint EnemyHitboxColor { get; set; } = 0x500000FFu; // semi-transparent red (ABGR)

    // Melee range indicator
    public bool ShowMeleeRange { get; set; } = false;
    public bool MeleeRangeFade { get; set; } = true;
    public uint MeleeRangeColor { get; set; } = 0xC000FF00u; // green
    public uint MeleeRangeOutOfRangeColor { get; set; } = 0xC000FFFFu; // yellow

    // Ranged range indicator (auto-detects 25y for all ranged/caster jobs)
    public bool ShowRangedRange { get; set; } = false;
    public uint RangedRangeColor { get; set; } = 0xC0FF8000u; // blue-ish
    public uint RangedRangeOutOfRangeColor { get; set; } = 0xC000FFFFu; // yellow

    // Positionals
    public bool ShowPositionals { get; set; } = false;
    public uint PositionalRearColor { get; set; } = 0x5000FF00u; // green
    public uint PositionalFlankColor { get; set; } = 0x50CFCF51u; // cyan

    // Treasure chest lines — a line from the player to every treasure coffer in range,
    // coloured by tier. Defaults match BOCCHI so the two plugins read the same at a glance.
    public bool ShowTreasureLines { get; set; } = false;
    public uint BronzeChestLineColor { get; set; } = 0xFF327FCDu; // bronze (ABGR)
    public uint SilverChestLineColor { get; set; } = 0xFFC0C0C0u; // silver
    public uint GoldChestLineColor { get; set; } = 0xFF00D7FFu; // gold
    public uint UnknownChestLineColor { get; set; } = 0xFFCC3399u; // purple — tier not recognised
    public float TreasureLineMaxDistance { get; set; } = 100f;

    // Occult Crescent carrot spots — use a Fortune Carrot on one to raise a chest.
    public bool ShowCarrotLines { get; set; } = false;
    public uint CarrotLineColor { get; set; } = 0xFF33CC33u; // green (ABGR)
    public float CarrotLineMaxDistance { get; set; } = 100f;

    /// <summary>
    /// Diagnostic: label every nearby non-combat world object with its ObjectKind and name.
    /// Exists to confirm which ObjectKind a given chest actually reports.
    /// </summary>
    public bool LabelWorldObjects { get; set; } = false;

    // Astrologian card range (30y on player — Balance, Spear, Bole, etc.)
    public bool ShowAstCardRange { get; set; } = false;
    public uint AstCardRangeColor { get; set; } = 0xC0D4A017u; // gold ring
    public uint AstCardRangeFillColor { get; set; } = 0x30D4A017u;
    public uint AstCardAllyInRangeColor { get; set; } = 0xC000FF00u;
    public uint AstCardAllyOutOfRangeColor { get; set; } = 0xC00000FFu;
}
