using System;
using Daedalus.Services.Targeting;

namespace Daedalus.Config;

/// <summary>
/// Configuration for targeting settings.
/// </summary>
public sealed class TargetingConfig
{
    /// <summary>
    /// Strategy for selecting enemy targets during combat.
    /// </summary>
    public EnemyTargetingStrategy EnemyStrategy { get; set; } = EnemyTargetingStrategy.LowestHp;

    /// <summary>
    /// When using TankAssist strategy, fall back to LowestHp if no tank target is found.
    /// </summary>
    public bool UseTankAssistFallback { get; set; } = true;

    /// <summary>
    /// How long to cache valid enemy list in milliseconds.
    /// Higher values improve performance but may delay target switching.
    /// </summary>
    public int TargetCacheTtlMs { get; set; } = 100;

    /// <summary>
    /// When true, all damage targeting is suppressed while the player has no selected target.
    /// This is the primary safeguard for gaze mechanics (drop target to look away) and for
    /// any case where the player wants Daedalus to stop attacking. Default ON.
    /// Does not pause when the player is in combat, the hard target is dead or missing,
    /// and live hostiles remain nearby — Daedalus auto-retargets instead.
    /// </summary>
    public bool PauseWhenNoTarget { get; set; } = true;

    /// <summary>
    /// When true, DoT applications are skipped on targets estimated to die within
    /// <see cref="DotTimeToKillThresholdSeconds"/> (RSR TimeToKill parity). Prevents wasting a
    /// 30s DoT plus its GCD on a mob two filler casts would finish. Fail-open: while TTK data is
    /// insufficient (fresh pulls read as unknown), DoTs are never skipped. Default ON.
    /// </summary>
    public bool EnableDotTimeToKillCheck { get; set; } = true;

    /// <summary>
    /// Targets estimated to die within this many seconds are skipped for DoT application.
    /// </summary>
    private float _dotTimeToKillThresholdSeconds = 10f;
    public float DotTimeToKillThresholdSeconds
    {
        get => _dotTimeToKillThresholdSeconds;
        set => _dotTimeToKillThresholdSeconds = Math.Clamp(value, 0f, 30f);
    }

    /// <summary>
    /// When true, damage module execution is suppressed while the player has any
    /// forced-movement debuff active (Forward/Backward/Left/Right March, Confusion).
    /// These debuffs interrupt cast-time GCDs, so retrying every frame produces log
    /// spam and can confuse the player. Instant GCDs and oGCDs still fire because
    /// other modules (buff, mitigation, healing) continue to run; only the damage
    /// module returns false. Default ON.
    /// </summary>
    public bool SuppressDamageOnForcedMovement { get; set; } = true;

    /// <summary>
    /// When true, all rotation and healing module execution is suppressed while the
    /// player has a Pyretic-style "any action kills you" debuff. Unlike forced-movement
    /// suppression (damage only), this halts healing, mitigation, buffs, and oGCDs as well
    /// — pressing anything during Pyretic applies a lethal vuln stack. Default ON.
    /// </summary>
    public bool PauseAllOnStandStillPunisher { get; set; } = true;

    /// <summary>
    /// When true, all rotation execution is suppressed while the player has an active
    /// channel/stance that would be cancelled by any other action (Passage of Arms,
    /// Flamethrower, Meditate, Collective Unconscious, Improvisation). The player pressed
    /// these deliberately to trade damage for an effect; the bot must not interfere.
    /// Resumes the frame the status drops. Default ON.
    /// </summary>
    public bool PauseOnPlayerChannel { get; set; } = true;

    /// <summary>
    /// When true, the fallback that retargets to LowestHp when CurrentTarget/FocusTarget
    /// strategies fail is disabled — a missing current target simply stops damage. This
    /// makes "drop target" a hard pause for players using explicit-target strategies.
    /// Relaxed automatically when the hard target dies mid-combat, live hostiles remain,
    /// and <see cref="EnemyStrategy"/> is an aggregate strategy (LowestHp, Nearest, etc.).
    /// Default ON.
    /// </summary>
    public bool StrictCurrentTargetStrategy { get; set; } = true;

    /// <summary>
    /// Master toggle for gap closer safety heuristics. When ON:
    ///  - Gap closers will only fire on the enemy the player has explicitly targeted.
    ///  - Gap closers are blocked if the player has been moving away from the target recently.
    /// Default ON.
    /// </summary>
    public bool SafeGapCloser { get; set; } = true;

    /// <summary>
    /// How far back (milliseconds) to track player movement when deciding whether they are
    /// actively moving away from the current target. 400ms is roughly a server tick and
    /// catches intentional repositioning without being noisy on small jitters.
    /// </summary>
    public int GapCloserMovementLookbackMs { get; set; } = 400;

    /// <summary>
    /// Minimum distance the player must have gained from the target within the lookback
    /// window to be considered "moving away". Expressed in yalms. 1.0y is small enough
    /// to trigger on deliberate movement but large enough to ignore GCD-stutter jitter.
    /// </summary>
    public float GapCloserMovementAwayThresholdY { get; set; } = 1.0f;

    /// <summary>
    /// When true, auto-targeting filters out enemies that are behind walls or
    /// other geometry using a BGCollision raycast. Prevents the rotation from
    /// trying to cast through pillars in dungeons and raids.
    /// </summary>
    public bool EnableLineOfSightFiltering { get; set; } = true;

    /// <summary>
    /// When true, auto-targeting skips enemies that have known invulnerability status
    /// effects (boss phase transitions, invulnerable adds, untouchable objects).
    /// Prevents the rotation from wasting actions on immune targets.
    /// Only affects aggregate strategies (LowestHp, HighestHp, Nearest, TankAssist) —
    /// explicit CurrentTarget/FocusTarget selections are never filtered.
    /// </summary>
    public bool EnableInvulnerabilityFiltering { get; set; } = true;

    /// <summary>
    /// When true, aggregate targeting includes valid hostiles that are not flagged in combat
    /// with you personally. Needed in alliance raids where other parties tag mobs first —
    /// those targets still accept contribution damage but lack your InCombat flag until hit.
    /// Only applies while you (or your group, with party-combat assist) are effectively fighting.
    /// </summary>
    public bool IncludeHostilesWithoutPersonalCombatFlag { get; set; } = false;

    /// <summary>
    /// When true, if the followed target (strict Current/Focus Target, or Tank Assist without
    /// fallback) is a valid living enemy that is simply OUT OF REACH — e.g. a boss split into an
    /// elevated "upper" part melee cannot hit and a grounded "lower" part — and another attackable
    /// hostile IS in range, Daedalus switches to the reachable enemy (and makes it the hard target
    /// so auto-face turns to it) instead of standing idle. Only triggers after the held target has
    /// been unreachable for a short grace and only when a reachable alternative actually exists, so
    /// normal gap-closing to a single far target is never interrupted. Default ON.
    /// </summary>
    public bool RetargetUnreachableTarget { get; set; } = true;
}
