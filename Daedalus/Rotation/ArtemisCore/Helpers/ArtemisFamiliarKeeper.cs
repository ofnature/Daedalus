using System;

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
