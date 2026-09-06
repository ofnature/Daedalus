using System;
using System.Numerics;
using Daedalus.Timeline;

namespace Daedalus.Rotation.Common.Helpers;

/// <summary>
/// Shared decision logic for blocking cast-time damage GCDs when a raidwide
/// or tank buster is predicted to hit before the cast would complete.
/// Any rotation (healer, caster DPS, physical ranged, tank) can call
/// <see cref="ShouldBlock"/> before executing a cast-time GCD.
///
/// Instant GCDs (castTime &lt;= 0) always return false -- wrapping every
/// GCD call site is safe.
///
/// Caveat: <c>castTime</c> must reflect the effective cast time considering
/// current buffs. For buff-instant cases (BLM Firestarter, PLD Divine Might,
/// Swiftcast, etc.) pass <c>0</c> or skip the gate in the calling rotation.
/// Passing the base action cast time when a buff has made the cast instant
/// produces a false-positive block.
/// </summary>
public static class MechanicCastGate
{
    /// <summary>
    /// "Can I stand here and finish this cast?", answered by whichever mechanics engine is
    /// selected — supplied by Plugin through BossHandlingRouter, so it is BossMod's forbidden
    /// zones or Minerva's MaxCastTime depending on Settings → General → Boss handling.
    /// <para>
    /// This is the half the timeline below cannot see. The timeline knows the FIGHT — a raidwide
    /// is coming, a buster is coming — and knows nothing about the ground under this particular
    /// character. The engine knows the ground and nothing about what the cast is worth. Both
    /// block, for different reasons.
    /// </para>
    /// <para>
    /// Null when no engine is wired, which reads as safe: a rotation that stops casting because a
    /// movement plugin is missing is worse than one that eats an avoidable puddle. An engine that
    /// throws is treated the same way.
    /// </para>
    /// </summary>
    public static Func<Vector3, float, bool>? CastSpotSafety { get; set; }

    /// <summary>
    /// Slack on top of the cast, matching the timeline half below: finishing a cast exactly as
    /// something lands is not "safe", and the animation lock outlasts the cast bar.
    /// </summary>
    private const float SafetyMarginSeconds = 0.5f;

    /// <summary>A standard GCD, for the debug string when the caller did not say how long the cast is.</summary>
    private const float NominalCastSeconds = 2.5f;

    public static bool ShouldBlock(IRotationContext context, float castTime)
    {
        if (castTime <= 0f) return false;

        // Movement gate (unconditional): a hard-cast can't complete while the character is moving — in
        // AutoDuty, vNav walks the toon to the mob / out of AoE, interrupting every Stone IV/Glare so the
        // GCD never finishes and the game rejects everything ("not ready", status 582). Hold the hard-cast
        // and let instants (castTime<=0, returned above) carry the rotation while moving. This mirrors the
        // base damage module's `CanSingleTarget => !isMoving`; scheduler-based casters that bypass that flow
        // (WHM/BLM/RDM …) only reach this gate, so it must live here too.
        if (context.IsMoving) return true;

        var cfg = context.Configuration.Timeline;
        if (!cfg.EnableMechanicAwareCasting) return false;

        // The engine's answer about THIS spot comes before the timeline's answer about the fight,
        // and is not gated on EnableTimelinePredictions — that switch governs our own timeline
        // data and its confidence score, neither of which this asks about.
        if (IsSpotUnsafe(context, castTime + SafetyMarginSeconds)) return true;

        if (!cfg.EnableTimelinePredictions) return false;

        var timeline = context.TimelineService;
        if (timeline == null || !timeline.IsActive) return false;
        if (timeline.Confidence < cfg.TimelineConfidenceThreshold) return false;

        var deadline = castTime + 0.5f;

        var raidwide = timeline.NextRaidwide;
        if (raidwide.HasValue && raidwide.Value.SecondsUntil > 0f && raidwide.Value.SecondsUntil <= deadline)
            return true;

        var tankbuster = timeline.NextTankBuster;
        if (tankbuster.HasValue && tankbuster.Value.SecondsUntil > 0f && tankbuster.Value.SecondsUntil <= deadline)
            return true;

        return false;
    }

    /// <summary>
    /// Produces a human-readable debug string describing why the gate would block,
    /// for surfacing in a module's debug state field.
    /// </summary>
    public static string FormatBlockedState(IRotationContext context, float castTime = 0f)
    {
        if (context.IsMoving) return "Held cast (moving — instants only)";

        var window = (castTime > 0f ? castTime : NominalCastSeconds) + SafetyMarginSeconds;
        if (context.Configuration.Timeline.EnableMechanicAwareCasting && IsSpotUnsafe(context, window))
            return "Held cast (this spot is not safe for the cast)";

        var timeline = context.TimelineService;
        if (timeline == null) return "Held cast (mechanic)";

        var rw = timeline.NextRaidwide;
        var tb = timeline.NextTankBuster;

        bool rwCloser = rw.HasValue && (!tb.HasValue || rw.Value.SecondsUntil <= tb.Value.SecondsUntil);
        if (rwCloser && rw.HasValue)
            return $"Held cast (raidwide in {rw.Value.SecondsUntil:F1}s)";
        if (tb.HasValue)
            return $"Held cast (tank buster in {tb.Value.SecondsUntil:F1}s)";
        return "Held cast (mechanic)";
    }

    /// <summary>
    /// Fail-open in every direction: no engine wired, no player to ask about, or an engine that
    /// throws all mean "no reason not to cast".
    /// </summary>
    private static bool IsSpotUnsafe(IRotationContext context, float window)
    {
        if (CastSpotSafety is not { } safe)
            return false;

        try
        {
            var player = context.Player;
            return player != null && !safe(player.Position, window);
        }
        catch
        {
            return false;
        }
    }
}
