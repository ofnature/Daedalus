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
