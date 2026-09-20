using System;

namespace Daedalus.Config;

/// <summary>
/// Global navigation / vNav tuning, surfaced in the Nav Control window. Applies across all jobs
/// (not per-job) and persists with the rest of the plugin configuration.
/// </summary>
public sealed class NavConfig
{
    private float _vNavFlex = 0.5f;

    /// <summary>
    /// "Grace" dead-band (yalms) around the max-melee stand distance before vNav is called to correct
    /// position. The character only repaths when its distance to the target leaves the
    /// <c>[standDistance − VNavFlex, standDistance + VNavFlex]</c> band — inside it the vNav call is
    /// suppressed entirely, which is what stops the move-in/move-out twitching. Range 0.0–2.0, default 0.5.
    /// </summary>
    public float VNavFlex
    {
        get => _vNavFlex;
        set => _vNavFlex = Math.Clamp(value, 0.0f, 2.0f);
    }

    /// <summary>
    /// Master switch for boundary-camping positional movement (the vNav flank/rear hop arcs + the
    /// BMR DesiredPositional=Any handoff). EXPERIMENTAL — default OFF: field testing 2026-07-05 still
    /// showed movement misbehavior with it live, so it ships opt-in until the cadence is proven.
    /// </summary>
    public bool EnableBoundaryCamping { get; set; } = false;

    private float _positionalBoundaryBiasDegrees = 10f;

    /// <summary>
    /// Degrees inside the required positional arc from the ±135° flank/rear boundary where melee stands
    /// ("boundary camping") — a flank↔rear swap becomes a short chord hop (2·r·sin(bias)) instead of a
    /// quarter-circle run. 10° (not smaller) absorbs server↔client facing desync so the finisher still
    /// lands inside the arc. 0 = stand at arc centers (pre-camping behavior). Range 0–30, default 10.
    /// </summary>
    public float PositionalBoundaryBiasDegrees
    {
        get => _positionalBoundaryBiasDegrees;
        set => _positionalBoundaryBiasDegrees = Math.Clamp(value, 0f, 30f);
    }

    /// <summary>
    /// Yield movement to the boss engine: while it is dodging or danger zones are live/imminent, Daedalus
    /// stops its own vNav pathing and stays hands-off until the danger clears plus a short cooldown. Both
    /// engines steer by input injection and defer to vNav whenever a path runs — without this, the two
    /// systems tug-of-war and stutter. Fail-open when the selected engine isn't loaded. Default true
    /// (kill-switch only).
    /// <para>The property name is historical: this is routed through <see cref="Config.BossHandling"/>, so
    /// under Minerva it yields to Minerva's steering, not to BossMod Reborn's.</para>
    /// </summary>
    public bool YieldToBmrMovement { get; set; } = true;

    /// <summary>
    /// Hold the boss engine's movement while the player has a cast bar up (and no danger lands before the
    /// cast finishes). Fixes the walk-in loop: a toon outside its stand distance but inside spell range
    /// would start a cast, the engine would step, the cast died — repeating all the way in. With the hold,
    /// casts complete and it steps between them. Releases instantly when danger approaches (dodging always
    /// wins). Default true (kill-switch only).
    /// <para>The property name is historical: under BossMod Reborn this pauses its AI movement, and under
    /// Minerva it asks Minerva to hold instead (<c>Minerva.RequestHold</c>).</para>
    /// </summary>
    public bool HoldBmrMovementWhileCasting { get; set; } = true;

    /// <summary>
    /// Disable max-melee positioning while solo (in a solo duty or with no party members present).
    /// Default false.
    /// </summary>
    public bool SoloPositionLock { get; set; } = false;

    /// <summary>
    /// Draw the max-melee debug rings (enemy hitbox / combined / max-melee + grace band) around the
    /// current target. Default false.
    /// </summary>
    public bool MaxMeleeDebugRings { get; set; } = false;

    /// <summary>
    /// Tank feature: ranged-pull adds to the camp. Behavior is stubbed for now; this only persists the
    /// toggle. Default false.
    /// </summary>
    public bool AddPull { get; set; } = false;

    /// <summary>
    /// Tank feature: toggle between boss-anchor mode and add-puller mode. Behavior is stubbed for now;
    /// this only persists the toggle. Default false.
    /// </summary>
    public bool TankMode { get; set; } = false;

    private float _bmrRangedStandDistance = 15f;

    /// <summary>
    /// Auto-manage BossMod Reborn's AI movement config by role (for group content, where AutoDuty isn't
    /// running its own BMR management). When on and BMR is loaded, Daedalus feeds BMR a role-based stand
    /// distance + the live next-GCD positional and puts BMR in movement-only mode so it positions while
    /// Daedalus keeps the rotation. You still enable BMR AI yourself (<c>/bmrai</c>). Default OFF.
    /// </summary>
    public bool AutoManageBmrAi { get; set; } = false;

    /// <summary>
    /// Distance (yalms) backline jobs (healers/ranged/casters) stand from the target when auto-managing
    /// BMR AI. 15y sits inside the 25y cast range but out of most melee/AoE. Range 8–24, default 15.
    /// </summary>
    public float BmrRangedStandDistance
    {
        get => _bmrRangedStandDistance;
        set => _bmrRangedStandDistance = Math.Clamp(value, 8f, 24f);
    }

    private float _bmrRangedMinDistance = 1f;

    /// <summary>
    /// Distance (yalms) backline jobs keep OFF the target's hitbox when auto-managing BMR AI. Without a floor BMR's
    /// band starts at the hitbox, so a caster pushed under the boss stays there and micro-adjusts instead of casting.
    /// Range 0-3, default 1: beside the boss rather than inside it.
    /// </summary>
    public float BmrRangedMinDistance
    {
        get => _bmrRangedMinDistance;
        set => _bmrRangedMinDistance = Math.Clamp(value, 0f, 3f);
    }
}
