namespace Daedalus.Rotation.Common.Modules;

/// <summary>
/// Whether a raise-state change is worth a Debug Log line. Pure, because the bug it exists to
/// prevent was an ordering mistake that is invisible by inspection and obvious in a test.
/// </summary>
public static class RaiseStateLogPolicy
{
    /// <summary>
    /// States that mean "nobody needs a raise". Two of these are written every frame from two
    /// different places — the execute path sets "No target"/"Disabled", then the debug-state pass
    /// sets "None needed" — so they alternate constantly and none of them is news.
    /// </summary>
    public static bool IsResting(string? state)
        => string.IsNullOrEmpty(state) || state is "No target" or "Disabled" or "None needed";

    /// <summary>
    /// Decide whether to log <paramref name="state"/> given the last logged state, and what the
    /// dedupe key should become.
    /// <para>
    /// A resting state CLEARS the key rather than storing it. Storing it was the bug: with two
    /// resting states alternating every frame, every frame looked like a transition and re-logged
    /// forever — field 2026-08-14, "Raise: None needed ×5314" in a single session. Clearing
    /// instead of storing also means a genuine raise need that returns after a lull still logs,
    /// rather than being swallowed as a duplicate of the last one.
    /// </para>
    /// </summary>
    public static (bool ShouldLog, string? NextKey) Decide(string? state, string? lastLogged)
    {
        if (IsResting(state))
            return (false, null);

        if (string.Equals(state, lastLogged, System.StringComparison.Ordinal))
            return (false, lastLogged);

        return (true, state);
    }
}
