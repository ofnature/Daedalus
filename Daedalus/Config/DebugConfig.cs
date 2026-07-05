using System.Collections.Generic;

namespace Daedalus.Config;

/// <summary>
/// Configuration for debug settings.
/// </summary>
public sealed class DebugConfig
{
    public int ActionHistorySize { get; set; } = 100;

    /// <summary>
    /// Enable verbose logging of healing decisions to Dalamud log.
    /// When enabled, logs each heal/oGCD/defensive decision with target, HP%, spell, and reason.
    /// Useful for understanding why specific healing choices are made.
    /// Default false to avoid log spam.
    /// </summary>
    public bool EnableVerboseLogging { get; set; } = false;

    /// <summary>
    /// Mirror the in-game Debug Log tab to <c>daedalus-debug.log</c> in the plugin config directory.
    /// Captures curated diagnostic events (refused casts, failed BossMod pushes) — not per-frame chatter.
    /// </summary>
    public bool EnableDebugLogFile { get; set; } = true;

    /// <summary>
    /// Dump raw combat packets ([ActorControl] / [ScreenLog] fly-text lines) to the Debug Log and
    /// Dalamud log. Diagnostic firehose for re-deriving packet layouts after a game patch (this is
    /// how the cat-1541 DoT tick channel was found) — noisy, keep off in normal play.
    /// </summary>
    public bool DumpRawCombatPackets { get; set; } = false;

    /// <summary>
    /// Visibility settings for debug window sections.
    /// Key is section name, value is whether it's visible.
    /// </summary>
    public Dictionary<string, bool> DebugSectionVisibility { get; set; } = new()
    {
        // Overview tab
        ["GcdPlanning"] = true,
        ["QuickStats"] = true,

        // Healing tab
        ["SpellStatus"] = true,
        ["SpellSelection"] = true,
        ["HpPrediction"] = true,
        ["AoEHealing"] = true,
        ["RecentHeals"] = true,
        ["Kardia"] = true,
        ["ShadowHp"] = true,

        // Damage tab
        ["DpsRotationState"] = true,

        // Mitigation tab
        ["MitigationState"] = true,

        // Overheal tab
        ["OverhealSummary"] = true,
        ["OverhealBySpell"] = true,
        ["OverhealByTarget"] = true,
        ["OverhealTimeline"] = true,
        ["OverhealControls"] = true,

        // Actions tab
        ["GcdDetails"] = true,
        ["SpellUsage"] = true,
        ["ActionHistory"] = true,

        // Performance tab
        ["Statistics"] = true,
        ["Downtime"] = true,
    };
}
