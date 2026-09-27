using System;

namespace Daedalus.Services;

/// <summary>
/// A short, owned hold on every action the rotation would submit, for another plugin that needs
/// the character to stop fighting for a moment without switching Daedalus off.
/// </summary>
/// <remarks>
/// <para>
/// The case it exists for: Odysseus running a quest that wants an item used on a mob "once it is
/// below half health" — 25 such steps in the Questionable path library. Daedalus fights for
/// Odysseus while it is busy, and at level 100 one GCD kills a level-65 overworld mob from full,
/// so the mob never sits below half long enough to take the item. With the rotation held, the
/// game's own auto-attack keeps swinging — a few percent a hit — which is exactly the controlled
/// damage the quest wants, and the item goes on in time.
/// </para>
/// <para>
/// Not <c>Daedalus.SetEnabled(false)</c>: that is the user's own switch, it reads as "the user
/// turned Daedalus off" to everything that asks, and a caller that crashed mid-fight would leave it
/// off for good.
/// </para>
/// <para>
/// So the hold is a <b>lease</b>: the holder re-asserts it and it lapses <see cref="Lease"/> after the
/// last time it did. A holder that unloads, crashes or simply forgets cannot leave the character
/// standing idle for more than a few seconds. And it is <b>owned</b>, like Minerva's preset slot:
/// while one plugin holds it, another cannot take it, and only the holder can release it.
/// </para>
/// </remarks>
public sealed class ExternalActionHold
{
    /// <summary>How long a hold lasts without being re-asserted.</summary>
    public static readonly TimeSpan Lease = TimeSpan.FromSeconds(3);

    private string? _owner;
    private DateTime _until;

    /// <summary>True while a hold is in force.</summary>
    public bool IsHeld(DateTime now) => _owner is not null && now < _until;

    /// <summary>Who holds it right now, or null.</summary>
    public string? HeldBy(DateTime now) => IsHeld(now) ? _owner : null;

    /// <summary>
    /// Take or renew the hold (<paramref name="hold"/> true), or give it back (false).
    /// </summary>
    /// <returns>
    /// False only when another plugin holds it. Releasing a hold nobody has is not an error — the
    /// caller's intent, "no hold from me", is already true.
    /// </returns>
    public bool Set(string owner, bool hold, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(owner))
            return false;

        if (IsHeld(now) && !string.Equals(_owner, owner, StringComparison.Ordinal))
            return false;

        if (!hold)
        {
            _owner = null;
            _until = default;
            return true;
        }

        _owner = owner;
        _until = now + Lease;
        return true;
    }
}
