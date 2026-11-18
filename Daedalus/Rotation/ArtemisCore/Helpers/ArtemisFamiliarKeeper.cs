using System;
using Daedalus.Data;

namespace Daedalus.Rotation.ArtemisCore.Helpers;

/// <summary>
/// Which Battlehorn to blow when no familiar is out. Pure, so the rules can be tested without a game.
/// <para>
/// A Beastmaster without a familiar has no Trick, no combos and no Tempered Release — and a death, a zone
/// change or a Parting Blow all leave it that way until someone presses a horn. Which beasts sit on the horns
/// stays the player's choice; this only puts one of them back out.
/// </para>
/// </summary>
public static class ArtemisFamiliarKeeper
{
    /// <summary>No horn to blow this frame.</summary>
    public const int None = -1;

    /// <summary>
    /// How long the familiar must be missing before it is called again. A freshly summoned beast takes a
    /// moment to appear, and the object table drops entries briefly around zone changes; neither is "gone".
    /// </summary>
    public const double GoneGraceSeconds = 1.5;

    /// <summary>
    /// Gap between tries. The horn is a one-second cast and the beast appears after it, so a try that is
    /// working looks exactly like one that failed for about that long.
    /// </summary>
    public const double RetrySeconds = 3.0;

    /// <summary>Gap after a press the game refused outright — long, because each refusal is an error toast.</summary>
    public const double RefusedRetrySeconds = 15.0;

    /// <summary>Nothing may have been pressed for this long before a horn goes out (an action's lock is about 0.6 s).</summary>
    public const double HornAfterActionSeconds = 0.7;

    /// <summary>
    /// The game will take a horn press now. A horn pressed while another press is still going out is refused, and each
    /// refusal used to cost fifteen seconds: of 62 refused horns in the logs (to 2026-11-08) nearly all had a weaponskill
    /// queued in the last half second of the GCD, or something pressed a few hundredths of a second before — and five
    /// times a second refusal followed the fifteen-second wait, half a minute with no beast out. So: no animation lock,
    /// nothing pressed in the last <see cref="HornAfterActionSeconds"/>, and not inside the GCD's queue window.
    /// </summary>
    public static bool CanPressHornNow(float animationLockRemaining, double secondsSinceLastAction, float gcdRemaining)
        => animationLockRemaining <= FFXIVConstants.WeaveWindowBuffer
           && secondsSinceLastAction >= HornAfterActionSeconds
           && !(gcdRemaining > 0f && gcdRemaining <= FFXIVTimings.QueueWindow);

    /// <summary>
    /// How long to wait after the game refused a horn: the usual gap the first time (it was most likely a press that
    /// collided with another), the long one from the second refusal in a row (something the rule cannot see, and each
    /// refusal is an error toast).
    /// </summary>
    public static double RefusedBackoffSeconds(int refusalsInARow)
        => refusalsInARow <= 1 ? RetrySeconds : RefusedRetrySeconds;

    /// <summary>What a wanted horn is waiting for, or null when nothing holds it. For the log, first reason only.</summary>
    public static string? HornWait(
        bool dead, bool moving, bool mounted, bool casting, bool bestiaryOpen, bool pressable, bool anyHornReady)
        => dead ? "you are down"
            : !anyHornReady ? "no horn is ready"
            : moving ? "you are moving (the horn is a cast)"
            : mounted ? "you are mounted"
            : casting ? "you are casting"
            : bestiaryOpen ? "the Bestiary is open"
            : !pressable ? "another press is still going out"
            : null;

    /// <summary>
    /// Gap between presses of the pre-fight Borrow sequence: long enough for a summoned beast to be in the world
    /// (a one-second cast, then it appears), and no more — the pull is waiting. <see cref="RetrySeconds"/> made the
    /// sequence 14.5-15 s, the whole of <see cref="PullHoldSeconds"/> (Korha, 2026-11-15: one pull ran out of time).
    /// </summary>
    public const double PreBorrowGapSeconds = 2.0;

    /// <summary>
    /// The horn of a beast that can be spent, to bring it out in place of the one that is being kept: the next ready
    /// horn after the out one whose beast no later battle needs, or <see cref="None"/>.
    /// </summary>
    public static int PickSpareHorn(
        bool wanted, bool canAct, double secondsSinceTry, int outSlot, ReadOnlySpan<bool> usable, ReadOnlySpan<bool> spare)
    {
        if (!wanted || !canAct || secondsSinceTry < RetrySeconds)
            return None;

        var start = outSlot >= 0 && outSlot < usable.Length ? outSlot + 1 : 0;
        for (var i = 0; i < usable.Length; i++)
        {
            var slot = (start + i) % usable.Length;
            if (slot != outSlot && usable[slot] && spare[slot])
                return slot;
        }

        return None;
    }

    /// <summary>The longest a boss pull waits for the first beast and the pre-fight Borrow.</summary>
    public const double PullHoldSeconds = 15.0;

    /// <summary>
    /// Hold a Crucible boss pull until the first beast is out and the pre-fight Borrow is done (or cannot be), on a
    /// degree or Legendary run. Korha, First Board Third Degree, 2026-11-15: the pull opened about five seconds after
    /// the horns were set, the toon already walking to the boss, so the Borrow never had a still second and every
    /// fight began with no beast out. Released after <see cref="PullHoldSeconds"/> whatever has or has not happened.
    /// </summary>
    public static bool HoldsThePull(bool onBoard, bool degreeOrLegendary, bool keepFamiliarOut, bool settled, double secondsHeld)
        => onBoard && degreeOrLegendary && keepFamiliarOut && !settled && secondsHeld < PullHoldSeconds;

    /// <summary>What the pre-fight Borrow does this frame (<see cref="NextPreBorrowStep"/>).</summary>
    public enum PreBorrowStep
    {
        None,

        /// <summary>Blow the pre-borrow beast's horn.</summary>
        SummonBeast,

        /// <summary>Borrow from it.</summary>
        Borrow,

        /// <summary>Blow the first horn, so the fight opens on the planned beast.</summary>
        SummonOpener,
    }

    /// <summary>
    /// The horn to Borrow from before a board fight, or <see cref="None"/>: the first Beastkin (Beastskin) on a horn after
    /// the first, else the first Vilekin (Vileskin). The first horn opens the fight, so it is never used. Matches the
    /// BMRV/fuurie D3 pre-pull Borrow on every documented First Board team and Ymir (docs/crucible-research/
    /// bmrv-setups.md), and the Japanese Legendary opener's "Vileskin or Beastskin before the pull".
    /// </summary>
    public static int PickPreBorrowSlot(ReadOnlySpan<Daedalus.Data.BeastClassification> kins)
    {
        for (var i = 1; i < kins.Length; i++)
        {
            if (kins[i] == Daedalus.Data.BeastClassification.Beastkin)
                return i;
        }

        for (var i = 1; i < kins.Length; i++)
        {
            if (kins[i] == Daedalus.Data.BeastClassification.Vilekin)
                return i;
        }

        return None;
    }

    /// <summary>The card a pre-borrow from this kin puts on Beast Mode (0 for a kin it never picks).</summary>
    public static uint PreBorrowCard(Daedalus.Data.BeastClassification kin) => kin switch
    {
        Daedalus.Data.BeastClassification.Beastkin => Daedalus.Data.BSTActions.BeastskinId,
        Daedalus.Data.BeastClassification.Vilekin => Daedalus.Data.BSTActions.VileskinId,
        _ => 0,
    };

    /// <summary>
    /// A card already on Beast Mode that the pre-borrow must not replace: a defensive skin, or the Soul Crush / Quelling
    /// Wave kept for an interrupt or a dispel. Cloud Skim, Seedsower and Scouring Ash left over from the last fight go.
    /// </summary>
    public static bool HoldsCardWorthKeeping(uint beastModeId)
        => beastModeId is Daedalus.Data.BSTActions.BeastskinId or Daedalus.Data.BSTActions.VileskinId
            or Daedalus.Data.BSTActions.ScaleskinId or Daedalus.Data.BSTActions.SoulCrushId
            or Daedalus.Data.BSTActions.QuellingWaveId;

    /// <summary>
    /// The pre-fight Borrow, one step a frame, on a board between fights: call the pre-borrow beast, Borrow, then call
    /// the first horn back. Nothing once a card worth keeping is held and the first horn is out, or while Borrow is
    /// cooling (the beast already out goes back to the first horn then). <paramref name="outSlot"/> is the horn of
    /// the familiar out, <see cref="None"/> when none is.
    /// </summary>
    public static PreBorrowStep NextPreBorrowStep(
        bool wanted, bool canAct, int preSlot, int outSlot, bool cardHeld, bool borrowReady)
    {
        if (!wanted || !canAct || preSlot == None)
            return PreBorrowStep.None;
        // Borrowed from the first horn itself (the video plan's openers): it stays out — calling it again would
        // wipe the Kinship just taken.
        if (cardHeld || !borrowReady)
            return outSlot == preSlot && preSlot != 0 ? PreBorrowStep.SummonOpener : PreBorrowStep.None;
        return outSlot == preSlot ? PreBorrowStep.Borrow : PreBorrowStep.SummonBeast;
    }

    /// <summary>
    /// The horn last out, kept or forgotten: on a Crucible board it is forgotten between fights, because the horn
    /// setter reorders the horns per battle and the next fight must open on the first one. Remembering it made the
    /// Pas de Seul pull (a boss pull, which counts as combat before the game does) blow the Third Battlehorn — the
    /// Wespe's in the Ogre fight, Damselfly's by then — and open with poison on a poison-immune boss (Korha 2026-11-01).
    /// </summary>
    public static int LastOutBetweenFights(int lastOutSlot, bool onBoard, bool inCombat)
        => onBoard && !inCombat ? None : lastOutSlot;

    /// <summary>
    /// The horn to blow, or <see cref="None"/>.
    /// </summary>
    /// <param name="enabled">The setting.</param>
    /// <param name="hasFamiliar">A familiar is in the world.</param>
    /// <param name="canAct">Alive, standing still, on foot and not already casting — the horn is a cast.</param>
    /// <param name="secondsGone">How long there has been no familiar.</param>
    /// <param name="secondsSinceTry">How long since a horn was last tried.</param>
    /// <param name="lastOutSlot">The horn whose beast was out last, or <see cref="None"/> when unknown.</param>
    /// <param name="usable">Per horn: a beast is assigned, the horn is learned, and the game says it is ready.</param>
    public static int PickHorn(
        bool enabled, bool hasFamiliar, bool canAct, double secondsGone, double secondsSinceTry,
        int lastOutSlot, ReadOnlySpan<bool> usable)
    {
        if (!enabled || hasFamiliar || !canAct
            || secondsGone < GoneGraceSeconds || secondsSinceTry < RetrySeconds)
            return None;

        // The beast that was out comes back if its horn allows it. In combat a horn's cooldown starts when
        // its beast retreats, so after a Parting Blow that horn is not ready and the next one in line goes —
        // which is the swap the job is built around.
        var first = lastOutSlot >= 0 && lastOutSlot < usable.Length ? lastOutSlot : 0;
        for (var i = 0; i < usable.Length; i++)
        {
            var slot = (first + i) % usable.Length;
            if (usable[slot])
                return slot;
        }

        return None;
    }

    /// <summary>
    /// The horn to blow to switch a falling familiar out (it is still out; blowing another horn swaps it), or
    /// <see cref="None"/>: the next ready horn after the out one (<paramref name="outSlot"/>), in horn order. Used when
    /// Parting Blow, the usual retreat, is on its 10 s cooldown (user, 2026-11-01: a familiar died waiting for it).
    /// </summary>
    public static int PickSwapHorn(bool wanted, bool canAct, double secondsSinceTry, int outSlot, ReadOnlySpan<bool> usable)
    {
        if (!wanted || !canAct || secondsSinceTry < RetrySeconds)
            return None;

        var start = outSlot >= 0 && outSlot < usable.Length ? outSlot + 1 : 0;
        for (var i = 0; i < usable.Length; i++)
        {
            var slot = (start + i) % usable.Length;
            if (slot != outSlot && usable[slot])
                return slot;
        }

        return None;
    }

    /// <summary>
    /// True when the roster is known and a Wespe sits in a horn other than the out one, ready or not: the Mantis holds
    /// its Soundwave for it. Not "ready" (as <see cref="WespeWaiting"/>): a horn's cooldown starts when its beast
    /// retreats, so a Wespe pulled out in an earlier fight could still be cooling at a boss's pull, and the Mantis let
    /// go at once (roommate's run, 2026-11-01).
    /// </summary>
    public static bool WespeInRoster(bool rosterKnown, int outSlot, ReadOnlySpan<uint> beasts)
    {
        if (!rosterKnown)
            return false;
        for (var i = 0; i < beasts.Length; i++)
        {
            if (i != outSlot && beasts[i] == Daedalus.Rotation.ArtemisCore.Modules.DamageModule.WespeBestiaryNo)
                return true;
        }

        return false;
    }

    /// <summary>
    /// True when the roster is known and a Wespe sits in a ready horn other than the out one (<paramref name="outSlot"/>):
    /// its Final Sting is still to come, so the beasts ahead of it are cycled out (Release, then Parting Blow) to bring
    /// it. <paramref name="beasts"/> are the horns' Bestiary numbers.
    /// </summary>
    public static bool WespeWaiting(bool rosterKnown, int outSlot, ReadOnlySpan<uint> beasts, ReadOnlySpan<bool> usable)
    {
        if (!rosterKnown)
            return false;
        for (var i = 0; i < beasts.Length && i < usable.Length; i++)
        {
            if (i != outSlot && usable[i] && beasts[i] == Daedalus.Rotation.ArtemisCore.Modules.DamageModule.WespeBestiaryNo)
                return true;
        }

        return false;
    }

    /// <summary>
    /// True when the roster is known and no horn but the out beast's own (<paramref name="outSlot"/>) is ready: a
    /// retreat now would leave the beastmaster with no familiar until a horn comes back. In combat a horn's cooldown
    /// starts when its beast retreats; Korha 2026-10-27 (Board 1 boss): three falling retreats in 40s put all three
    /// horns down, and the boss had her alone for 70s, 58% to 6%. False while the roster is unread (no evidence).
    /// </summary>
    public static bool NoReplacementHorn(bool rosterKnown, int outSlot, ReadOnlySpan<bool> usable)
    {
        if (!rosterKnown)
            return false;
        for (var i = 0; i < usable.Length; i++)
        {
            if (i != outSlot && usable[i])
                return false;
        }

        return true;
    }
}
