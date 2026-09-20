using System;

namespace Daedalus.Services.Combat;

/// <summary>
/// Don't attack out of the post-raise invulnerability. Let the dodge spend it instead.
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
/// keeps its immunity for the whole window while Minerva/BossMod walk it clear — they steer from
/// their own plugin and never through this code. Daedalus's own max-melee approach runs inside the
/// rotation, so holding the rotation also stops it walking back toward the boss. By the time the buff
/// ends the character is out of the AoE: "start dodging, then attack once safe", rather than "attack,
/// then die".
/// </para>
///
/// <para>
/// This is RSR parity — <c>RSCommands_Actions</c> refuses outright to submit any action while
/// Transcendent is up, with no timer on either side of it. Nothing here waits for the buff to CLEAR
/// before starting a delay. An earlier version of this class did, on the mistaken belief that the buff
/// locked actions; because it never acted, the buff always ran its full 10s and the character then sat
/// still for a further delay on top of it.
/// </para>
/// </summary>
public sealed class ReviveGraceTracker
{
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
    /// <param name="maxHoldSeconds">
    /// Upper bound on the hold, so the character is never frozen for longer than intended even if the
    /// buff outlives expectations, and so the wait can be shortened to trade safety for damage. 0 or
    /// less disables the feature and restores the immediate re-engage. The buff ending always releases
    /// the hold regardless of this.
    /// </param>
    public void NoteFrame(bool postReviveInvulnerable, double maxHoldSeconds)
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

        // Held from the first frame the buff is seen, for at most maxHoldSeconds.
        //
        // The off switch needs no branch of its own and deliberately has none: a maxHoldSeconds of 0
        // puts the deadline at the start instant, so IsActive is false on the same frame and a hold in
        // progress is released the moment the setting is turned down. A guard clause for it looked
        // prudent and was unreachable — no test could tell it apart from its absence.
        _holdStartedUtc ??= _utcNow();
        _holdUntilUtc = _holdStartedUtc.Value.AddSeconds(maxHoldSeconds);
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
    public static void NoteFrame(bool postReviveInvulnerable, double maxHoldSeconds)
        => Shared.NoteFrame(postReviveInvulnerable, maxHoldSeconds);
}
