using System;
using Daedalus.Data;

namespace Daedalus.Rotation.ArtemisCore.Helpers;

/// <summary>
/// Tracks the instinctual combo clock: which affinity was last spent, whether the 7s window is
/// still open, and whether a given affinity would complete an intentional combo.
///
/// <para>
/// This is the one piece of Beastmaster that is fully knowable without the action data — the
/// cycle order and the window are mechanics, not numbers off a sheet — so it is implemented for
/// real and unit-tested rather than stubbed. When the action ids land, the rotation asks this
/// which affinity it wants next and picks the matching action.
/// </para>
///
/// <para>
/// The affinities are NAMED — Volant, Rampant, Durant, Eldritch — not colour-coded; the original
/// scaffold's yellow/green/blue/red model was wrong and is corrected. The cycle is a four-cycle
/// as expected, and the 7s window is now confirmed from the tooltips rather than assumed.
/// </para>
/// </summary>
public sealed class ArtemisInstinctTracker
{
    /// <summary>
    /// Combo window in seconds. **CONFIRMED 2026-09-08** from the client's own sheets: every
    /// Heart tooltip states "Duration: 7s". The scaffold guessed 7s and the guess happened to be
    /// right, which is not the same as having been right — this now comes from the data.
    /// </summary>
    public const float WindowSeconds = BSTActions.HeartDurationSeconds;

    /// <summary>True: read off the tooltips, not assumed.</summary>
    public const bool WindowSecondsIsConfirmed = true;

    /// <summary>Test seam, matching the other trackers in this codebase.</summary>
    internal Func<DateTime> UtcNow = () => DateTime.UtcNow;

    private InstinctAffinity _last = InstinctAffinity.None;
    private DateTime _lastUtc = DateTime.MinValue;

    /// <summary>The affinity last spent, or None when no chain is running.</summary>
    public InstinctAffinity LastAffinity => IsWindowOpen ? _last : InstinctAffinity.None;

    /// <summary>Whether a chain is still live.</summary>
    public bool IsWindowOpen =>
        _last != InstinctAffinity.None && SecondsSinceLast < WindowSeconds;

    /// <summary>Seconds since the last spend; <see cref="float.MaxValue"/> when none.</summary>
    public float SecondsSinceLast => _lastUtc == DateTime.MinValue
        ? float.MaxValue
        : (float)(UtcNow() - _lastUtc).TotalSeconds;

    /// <summary>
    /// The affinity that would score an intentional combo next, or None when no chain is running
    /// (in which case any affinity opens one).
    /// </summary>
    public InstinctAffinity PreferredNext => IsWindowOpen
        ? BSTActions.NextInCycle(_last)
        : InstinctAffinity.None;

    /// <summary>
    /// Would spending this affinity now score an intentional combo? True when it is the next in
    /// the cycle. Opening a fresh chain is not an intentional combo — there is nothing to chain
    /// from — so this is false with the window closed.
    /// </summary>
    public bool WouldBeIntentional(InstinctAffinity affinity) =>
        affinity != InstinctAffinity.None && IsWindowOpen && affinity == PreferredNext;

    /// <summary>
    /// Would spending this affinity still combo, weakly? Any affinity continues a live chain
    /// off-order; only the next in the cycle is intentional.
    /// </summary>
    public bool WouldCombo(InstinctAffinity affinity) =>
        affinity != InstinctAffinity.None && IsWindowOpen;

    /// <summary>Record a spend. Call from the dispatch callback, not from the decision.</summary>
    public void Record(InstinctAffinity affinity)
    {
        if (affinity == InstinctAffinity.None)
            return;

        _last = affinity;
        _lastUtc = UtcNow();
    }

    /// <summary>Drop the chain — combat end, zone change, or a familiar swap that resets it.</summary>
    public void Reset()
    {
        _last = InstinctAffinity.None;
        _lastUtc = DateTime.MinValue;
    }

    /// <summary>
    /// Which intentional combo spending this affinity would complete, or None. The two combos
    /// alternate around the cycle, so the picker can name what it is going for.
    /// </summary>
    public IntentionalCombo ComboFrom(InstinctAffinity affinity) =>
        IsWindowOpen ? BSTActions.ComboFor(_last, affinity) : IntentionalCombo.None;

    /// <summary>One-line readout for the debug panel.</summary>
    public string Describe() => !IsWindowOpen
        ? "no chain"
        : $"{_last} → want {PreferredNext} ({WindowSeconds - SecondsSinceLast:0.0}s left)";
}
