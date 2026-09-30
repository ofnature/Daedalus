using System;
using Daedalus.Data;

namespace Daedalus.Rotation.ArtemisCore.Helpers;

/// <summary>
/// Pairs the familiar's Trick with the beastmaster's clockwise axe — the job's combo.
///
/// <para>
/// A combo is one of your axes and the familiar's Trick inside the 7 s window. <b>Trick first, then the
/// axe</b> is the default: finishing a combo with your own instinctual skill grants Mastered Instinct,
/// which Rally spends, which feeds the level 50 finisher. The axe must be the pet's colour's clockwise
/// neighbour (Rampant → Durant → Eldritch → Volant → Rampant) for an intentional combo.
/// </para>
///
/// <para>
/// The trap: the familiar does not act the instant Trick is pressed. Press the axe straight after and
/// your axe often resolves first, so the combo comes out backwards (Natural Instinct, the wrong
/// diamond). So the answer waits for the familiar's own action to resolve — seen on the action-effect
/// hook — or, if that is never seen, for <see cref="FallbackAnswerSeconds"/>, the guides' "weave one
/// ability between them".
/// </para>
/// </summary>
public sealed class ArtemisTrickPairing
{
    /// <summary>Answer anyway this long after Trick if the familiar's action was not seen resolving.</summary>
    public const float FallbackAnswerSeconds = 1.5f;

    /// <summary>
    /// Stop waiting to answer this long after Trick. The Heart the Trick granted lasts 7 s; the margin
    /// keeps a late answer from landing after it has fallen off.
    /// </summary>
    public const float AnswerDeadlineSeconds = BSTActions.HeartDurationSeconds - 0.5f;

    /// <summary>
    /// The longest a ready axe waits for the familiar's Trick before going out on its own. Both bars
    /// normally reach 100 at about the same pace (combo TP vs auto-attacks), but a familiar chasing a
    /// moving target swings less — and TP held at the 250 cap is TP thrown away.
    /// </summary>
    public const float MaxHoldForTrickSeconds = 12f;

    /// <summary>Test seam, matching the other trackers here.</summary>
    internal Func<DateTime> UtcNow = () => DateTime.UtcNow;

    private DateTime _trickAt = DateTime.MinValue;
    private bool _familiarActed;
    private DateTime _holdingSince = DateTime.MinValue;

    /// <summary>The colour of the Trick being answered; None when not waiting.</summary>
    public InstinctAffinity PetAffinity { get; private set; } = InstinctAffinity.None;

    /// <summary>A Trick went out and has not been answered, and its window is still open.</summary>
    public bool IsAwaitingAnswer =>
        PetAffinity != InstinctAffinity.None && SecondsSinceTrick < AnswerDeadlineSeconds;

    /// <summary>The axe colour that completes the intentional combo with the pending Trick.</summary>
    public InstinctAffinity AnswerAffinity =>
        IsAwaitingAnswer ? BSTActions.NextInCycle(PetAffinity) : InstinctAffinity.None;

    private float SecondsSinceTrick => _trickAt == DateTime.MinValue
        ? float.MaxValue
        : (float)(UtcNow() - _trickAt).TotalSeconds;

    /// <summary>
    /// Time to press the answering axe: the familiar has acted, or the fallback delay has passed —
    /// and the window is still open.
    /// </summary>
    public bool ReadyToAnswer =>
        IsAwaitingAnswer && (_familiarActed || SecondsSinceTrick >= FallbackAnswerSeconds);

    /// <summary>Trick was sent for a familiar whose Trick colour is known.</summary>
    public void OnTrickDispatched(InstinctAffinity petAffinity)
    {
        PetAffinity = petAffinity;
        _trickAt = UtcNow();
        _familiarActed = false;
        _holdingSince = DateTime.MinValue;
    }

    /// <summary>
    /// An axe is ready but the Trick is not. Returns true once it has waited
    /// <see cref="MaxHoldForTrickSeconds"/> — time to fire it unpaired.
    /// </summary>
    public bool HoldExpired()
    {
        var now = UtcNow();
        if (_holdingSince == DateTime.MinValue)
            _holdingSince = now;
        return (now - _holdingSince).TotalSeconds >= MaxHoldForTrickSeconds;
    }

    /// <summary>Nothing is being held any more (an axe went out, or none is ready).</summary>
    public void ClearHold() => _holdingSince = DateTime.MinValue;

    /// <summary>
    /// The familiar's action resolved (action-effect hook). Only counts after a Trick was sent — its
    /// auto-attacks are filtered out by the caller.
    /// </summary>
    public void OnFamiliarAction()
    {
        if (IsAwaitingAnswer)
            _familiarActed = true;
    }

    /// <summary>The answering axe went out.</summary>
    public void OnAnswered()
    {
        PetAffinity = InstinctAffinity.None;
        _trickAt = DateTime.MinValue;
        _familiarActed = false;
    }

    /// <summary>Combat end, familiar gone.</summary>
    public void Reset()
    {
        PetAffinity = InstinctAffinity.None;
        _trickAt = DateTime.MinValue;
        _familiarActed = false;
        _holdingSince = DateTime.MinValue;
    }

    /// <summary>One-line readout for the debug panel.</summary>
    public string Describe() => !IsAwaitingAnswer
        ? "no Trick pending"
        : $"{PetAffinity} Trick → answer {AnswerAffinity} " +
          (ReadyToAnswer ? "now" : "once the familiar acts") +
          $" ({AnswerDeadlineSeconds - SecondsSinceTrick:0.0}s left)";
}
