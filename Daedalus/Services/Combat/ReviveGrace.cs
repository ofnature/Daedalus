using System;

namespace Daedalus.Services.Combat;

/// <summary>
/// Let the dodge go first after a raise, then hand the fight back to the rotation.
///
/// <para>
/// Transcendent does not lock actions — it makes the character immune to damage for 10 seconds
/// <b>until it acts</b>, and acting is what ends it. So a character raised inside an AoE was already
/// immune, and Daedalus threw that away on the very first frame: the death gate is a single
/// <c>CurrentHp == 0</c> check, so the frame HP came back the rotation submitted an action, the
/// immunity dropped, and the AoE killed it again where it stood (reported 2026-09-19).
/// </para>
///
/// <para>
/// Holding actions while the buff is up inverts that. Movement does not break it, so the character
/// keeps its immunity while Minerva/BossMod walk it clear — they steer from their own plugin and never
/// through this code. Daedalus's own max-melee approach runs inside the rotation, so holding the
/// rotation also stops it walking back toward the boss.
/// </para>
///
/// <para>
/// But the hold ends as soon as the character is actually clear, rather than running out the whole
/// immunity. Holding for the full window was safe and cost up to ten seconds of uptime per death, for
/// no benefit once the dodge had finished: the ask is "start dodging, then let the rotation take over",
/// not "stand still until the buff expires". <see cref="MinHoldSeconds"/> covers the gap where the
/// engine has not yet formed an opinion — releasing on the first frame because nothing looks wrong yet
/// is how the original bug worked.
/// </para>
///
/// <para>
/// RSR holds for the whole buff (<c>RSCommands_Actions</c> refuses every action while Transcendent is
/// up, with no timer either side). That is the conservative version of this and the behaviour to fall
/// back to if the early release ever proves too eager — raise <see cref="MinHoldSeconds"/> to the cap,
/// or stop passing a clear-of-danger signal.
/// </para>
/// </summary>
public sealed class ReviveGraceTracker
{
    /// <summary>
    /// The floor on the hold. The dodge engine needs a moment to see the mechanic and start steering;
    /// until then "nothing is steering and the spot looks fine" is indistinguishable from being clear,
    /// and acting on it reproduces the original bug on the first frame back.
    /// </summary>
    public const double MinHoldSeconds = 0.5;

    /// <summary>
    /// Has the dodge finished, so the rotation may take over? Both halves matter: a character mid-dodge
    /// is not clear no matter what the ground says, and a character standing still inside a telegraph is
    /// not clear either.
    /// <para>
    /// Pure, so the decision is testable — the caller only gathers the three readings. Fails open by
    /// construction: with no movement engine and no safety engine to ask, both arguments arrive as
    /// "nothing wrong" and the hold falls back to <see cref="MinHoldSeconds"/> rather than burning the
    /// whole immunity because a plugin is missing.
    /// </para>
    /// </summary>
    /// <param name="isMoving">The character is moving — something is repositioning it.</param>
    /// <param name="externalSteering">Minerva/BossMod is actively steering.</param>
    /// <param name="spotSafe">The engine says the ground underfoot stays safe for the lookahead.</param>
    public static bool IsClearOfDanger(bool isMoving, bool externalSteering, bool spotSafe)
        => !isMoving && !externalSteering && spotSafe;

    private readonly Func<DateTime> _utcNow;
    private DateTime? _holdStartedUtc;
    private DateTime _holdUntilUtc = DateTime.MinValue;

    /// <param name="utcNow">Injected clock; tests pass their own instead of moving a global one.</param>
    public ReviveGraceTracker(Func<DateTime>? utcNow = null)
        => _utcNow = utcNow ?? (() => DateTime.UtcNow);

    /// <summary>True while the rotation should stand still and let the dodge engine work.</summary>
    public bool IsActive => _utcNow() < _holdUntilUtc;

    /// <summary>Seconds of hold remaining (0 when inactive) — for a status line.</summary>
    public double RemainingSeconds => Math.Max(0d, (_holdUntilUtc - _utcNow()).TotalSeconds);

    /// <summary>Per-frame observation.</summary>
    /// <param name="postReviveInvulnerable">
    /// Transcendent (or duty support's Willful) is up: the character has just been brought back and is
    /// immune until it acts. The hold exists so that immunity is not spent on an attack.
    /// </param>
    /// <param name="clearOfDanger">
    /// Nothing is steering the character and the ground it stands on reads safe — the dodge has done its
    /// job, so the rotation may take over. Pass true when no engine is available to ask: a missing
    /// movement plugin should cost a moment, not the whole window.
    /// </param>
    /// <param name="maxHoldSeconds">
    /// Backstop, so the character is never frozen for longer than intended if it never reads clear.
    /// 0 or less disables the hold entirely. The buff ending always releases it regardless.
    /// </param>
    public void NoteFrame(bool postReviveInvulnerable, bool clearOfDanger, double maxHoldSeconds)
    {
        if (!postReviveInvulnerable)
        {
            // The buff is gone: it expired, or something acted and consumed it. Nothing left to
            // protect, so release now rather than run a timer down — which is also what lets a manual
            // action by the player hand control straight back on the next frame.
            _holdStartedUtc = null;
            _holdUntilUtc = DateTime.MinValue;
            return;
        }

        // The off switch needs no branch of its own and deliberately has none: a maxHoldSeconds of 0
        // puts the deadline at the start instant, so IsActive is false on the same frame and a hold in
        // progress is released the moment the setting is turned down.
        var startedUtc = _holdStartedUtc ??= _utcNow();
        var elapsed = (_utcNow() - startedUtc).TotalSeconds;

        // Clear, and the engine has had long enough to say otherwise: hand the fight back.
        if (clearOfDanger && elapsed >= MinHoldSeconds)
        {
            _holdUntilUtc = DateTime.MinValue;
            return;
        }

        _holdUntilUtc = startedUtc.AddSeconds(maxHoldSeconds);
    }

    /// <summary>Reload hygiene.</summary>
    public void Reset()
    {
        _holdStartedUtc = null;
        _holdUntilUtc = DateTime.MinValue;
    }
}

/// <summary>
/// The process-wide instance the rotation reads, because a transient flag the rotation stack reads
/// must never live on a config copy (the ExternalCombatOverride lesson).
///
/// <para>
/// The logic lives on <see cref="ReviveGraceTracker"/> rather than in here, so tests can exercise it
/// on their own instance. Arming this one from a test would hold the rotation inside every other test
/// class running in parallel — which is how the CastSpotSafety static made the suite flaky (2026-09-17).
/// </para>
/// </summary>
public static class ReviveGrace
{
    private static readonly ReviveGraceTracker Shared = new();

    /// <inheritdoc cref="ReviveGraceTracker.IsActive"/>
    public static bool IsActive => Shared.IsActive;

    /// <inheritdoc cref="ReviveGraceTracker.RemainingSeconds"/>
    public static double RemainingSeconds => Shared.RemainingSeconds;

    /// <inheritdoc cref="ReviveGraceTracker.NoteFrame"/>
    public static void NoteFrame(bool postReviveInvulnerable, bool clearOfDanger, double maxHoldSeconds)
        => Shared.NoteFrame(postReviveInvulnerable, clearOfDanger, maxHoldSeconds);
}
