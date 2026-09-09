using System;
using Daedalus.Data;

namespace Daedalus.Rotation.ArtemisCore.Helpers;

/// <summary>
/// Tracks the Inner Compass: which affinity was last spent, whether the 7s window is still open,
/// how long the chain has run, and whether a given affinity would complete an intentional combo.
///
/// <para>
/// Modelled directly on the in-game gauge description (read 2026-09-08): "When an instinctual skill
/// with one such affinity is executed, its corresponding symbol will light up. If either the
/// beastmaster <b>or familiar</b> executes another instinctual skill within seven seconds of the
/// previous one, an instinctual combo will be completed, dealing extra damage. Furthermore, if the
/// second instinctual skill performed runs <b>clockwise</b> on the Inner Compass, an intentional
/// combo will be completed."
/// </para>
///
/// <para>
/// Two consequences that are easy to get wrong. First, <b>the familiar advances the chain too</b> —
/// Trick is an instinctual skill executed by the familiar, so it counts, even though which Heart it
/// grants depends on the familiar and is not knowable from here; that is what
/// <see cref="RecordUnknown"/> is for. Second, <b>the chain length is not cosmetic</b>: "a higher
/// number increases the potency of extra damage dealt by combos", so keeping a chain alive is worth
/// more than any single well-chosen affinity.
/// </para>
/// </summary>
public sealed class ArtemisInstinctTracker
{
    /// <summary>
    /// Combo window in seconds. Confirmed twice: every Heart tooltip states "Duration: 7s", and the
    /// gauge description says "within seven seconds of the previous one".
    /// </summary>
    public const float WindowSeconds = BSTActions.HeartDurationSeconds;

    /// <summary>True: read off the tooltips and the gauge description, not assumed.</summary>
    public const bool WindowSecondsIsConfirmed = true;

    /// <summary>Test seam, matching the other trackers in this codebase.</summary>
    internal Func<DateTime> UtcNow = () => DateTime.UtcNow;

    private InstinctAffinity _last = InstinctAffinity.None;
    private DateTime _lastUtc = DateTime.MinValue;
    private int _chainLength;

    /// <summary>
    /// The affinity last spent, or None — which means either no chain is running, or the chain is
    /// running but the last link came from the familiar and its affinity is unknown.
    /// </summary>
    public InstinctAffinity LastAffinity => IsWindowOpen ? _last : InstinctAffinity.None;

    /// <summary>
    /// Whether a chain is still live. Note this does NOT require a known affinity: a chain advanced
    /// by the familiar is still a chain, and dropping it would forfeit the accumulated potency.
    /// </summary>
    public bool IsWindowOpen =>
        _lastUtc != DateTime.MinValue && SecondsSinceLast < WindowSeconds;

    /// <summary>
    /// How many instinctual skills are in the current chain. "A higher number increases the potency
    /// of extra damage dealt by combos" — this is the number rendered beneath the Inner Compass.
    /// Zero when no chain is running.
    /// </summary>
    public int ChainLength => IsWindowOpen ? _chainLength : 0;

    /// <summary>Seconds since the last spend; <see cref="float.MaxValue"/> when none.</summary>
    public float SecondsSinceLast => _lastUtc == DateTime.MinValue
        ? float.MaxValue
        : (float)(UtcNow() - _lastUtc).TotalSeconds;

    /// <summary>
    /// The affinity that would score an intentional combo next — the clockwise neighbour on the
    /// Compass. None when no chain is running, or when the last link's affinity is unknown.
    /// </summary>
    public InstinctAffinity PreferredNext => IsWindowOpen && _last != InstinctAffinity.None
        ? BSTActions.NextInCycle(_last)
        : InstinctAffinity.None;

    /// <summary>
    /// Would spending this affinity now score an <i>intentional</i> combo? True only when it is the
    /// clockwise successor. Opening a fresh chain is not an intentional combo — there is nothing to
    /// chain from.
    /// </summary>
    public bool WouldBeIntentional(InstinctAffinity affinity) =>
        affinity != InstinctAffinity.None
        && IsWindowOpen
        && _last != InstinctAffinity.None
        && affinity == PreferredNext;

    /// <summary>
    /// Would spending this affinity still combo, weakly? Any affinity continues a live chain
    /// off-order; only the clockwise successor is intentional.
    /// </summary>
    public bool WouldCombo(InstinctAffinity affinity) =>
        affinity != InstinctAffinity.None && IsWindowOpen;

    /// <summary>Record a spend whose affinity is known. Call from the dispatch callback.</summary>
    public void Record(InstinctAffinity affinity)
    {
        if (affinity == InstinctAffinity.None)
            return;

        Advance();
        _last = affinity;
    }

    /// <summary>
    /// Record a link in the chain whose affinity we cannot determine — the familiar's Trick, which
    /// grants one of the four Hearts depending on the beast. The chain advances (it must: the game
    /// counts it) but the picker stops claiming to know what comes next, so it will not chase a
    /// combo off a guess.
    /// </summary>
    public void RecordUnknown()
    {
        Advance();
        _last = InstinctAffinity.None;
    }

    private void Advance()
    {
        _chainLength = IsWindowOpen ? _chainLength + 1 : 1;
        _lastUtc = UtcNow();
    }

    /// <summary>Drop the chain — combat end, zone change, or a familiar swap that resets it.</summary>
    public void Reset()
    {
        _last = InstinctAffinity.None;
        _lastUtc = DateTime.MinValue;
        _chainLength = 0;
    }

    /// <summary>
    /// Which intentional combo spending this affinity would complete, or None. The two alternate
    /// around the Compass, and at tier 2 either direction completes Universality.
    /// </summary>
    public IntentionalCombo ComboFrom(InstinctAffinity affinity) =>
        IsWindowOpen && _last != InstinctAffinity.None
            ? BSTActions.ComboFor(_last, affinity)
            : IntentionalCombo.None;

    /// <summary>One-line readout for the debug panel.</summary>
    public string Describe()
    {
        if (!IsWindowOpen)
            return "no chain";

        var head = _last == InstinctAffinity.None ? "familiar (unknown)" : _last.ToString();
        var want = PreferredNext == InstinctAffinity.None ? "any" : PreferredNext.ToString();
        return $"{head} ×{_chainLength} → want {want} ({WindowSeconds - SecondsSinceLast:0.0}s left)";
    }
}
