using System;

namespace Daedalus.Services.Targeting;

/// <summary>
/// The boss engine's authored opinion on targets, applied on top of whatever strategy is running: never an
/// enemy it forbids (a phase-invulnerable boss, the wrong half of Shinryu Paradox seen from the other
/// floor), and its priority list first (Alexander's adds during Perfect Defense, 2026-09-06: Daedalus
/// found them on its own, but only after forty seconds of trying the boss). Pure, so it is testable
/// without a game.
/// </summary>
public static class FightTargetHints
{
    public static bool IsForbidden(ulong id, ulong[]? forbidden)
    {
        if (id == 0 || forbidden == null)
            return false;
        foreach (var f in forbidden)
            if (f == id)
                return true;
        return false;
    }

    /// <summary>
    /// The hard target to put back once the engine stops forbidding it: the one dropped for being forbidden
    /// (<paramref name="droppedId"/>), when nothing has been targeted since (<paramref name="hardId"/> 0) and it is
    /// no longer on the list. Field 2026-10-31 (Korha, Drake Piece): a reflecting enemy left as the hard target kept
    /// eating auto-attacks; dropped, it has to come back when the reflect ends or auto-attacks never resume.
    /// </summary>
    public static bool ShouldRestore(ulong hardId, ulong droppedId, ulong[]? forbidden)
        => hardId == 0 && droppedId != 0 && !IsForbidden(droppedId, forbidden);

    /// <summary>The Bone Bishop of the First Board's first battle (BNpcName).</summary>
    public const uint BoneBishopNameId = 14532;

    /// <summary>
    /// The engine's priority list with <paramref name="firstId"/> put at its head (0: the list as it is). The Bone
    /// Bishop dies before the Bone Knight (user, 2026-11-15, and every Third Degree source): the fight engine makes the
    /// Knight its first priority whenever the beastmaster is behind his Forward Guard, and on 2026-11-15 the target
    /// went Bishop, Knight, Bishop, Knight, Bishop, Knight, Bishop inside two minutes.
    /// </summary>
    public static ulong[] KillFirst(ulong[] priority, ulong firstId)
    {
        if (firstId == 0 || (priority.Length > 0 && priority[0] == firstId))
            return priority;

        var others = 0;
        foreach (var id in priority)
        {
            if (id != firstId)
                others++;
        }

        var ordered = new ulong[1 + others];
        ordered[0] = firstId;
        var at = 1;
        foreach (var id in priority)
        {
            if (id != firstId)
                ordered[at++] = id;
        }

        return ordered;
    }

    /// <summary>The first priority id that <paramref name="resolve"/> turns into a usable target, in the engine's order.</summary>
    public static T? FirstPreferred<T>(ulong[]? priority, Func<ulong, T?> resolve) where T : class
    {
        if (priority == null)
            return null;
        foreach (var id in priority)
            if (resolve(id) is { } t)
                return t;
        return null;
    }
}
