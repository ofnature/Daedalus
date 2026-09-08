using System;
using Daedalus.Data;

namespace Daedalus.Rotation.ArtemisCore.Helpers;

/// <summary>
/// Tracks the Instinctual Combo clock: which colour was last spent, whether the window is still
/// open, and whether the next colour would score the Intentional (clockwise) combo.
///
/// <para>
/// This is the one piece of Beastmaster that is fully knowable without the action data — the
/// cycle order and the window are mechanics, not numbers off a sheet — so it is implemented for
/// real and unit-tested rather than stubbed. When the action ids land, the rotation asks this
/// what colour it wants next and picks the matching action.
/// </para>
///
/// <para>
/// The window length is a placeholder: the job is documented as roughly seven seconds and the
/// exact figure is not published. It is a named constant so there is one place to correct, and
/// <see cref="WindowSecondsIsConfirmed"/> exists so callers and tests can tell a measured value
/// from an assumed one.
/// </para>
/// </summary>
public sealed class ArtemisInstinctTracker
{
    /// <summary>
    /// Combo window in seconds. **ASSUMED, not measured** — "~7s" from the job's description.
    /// Verify against the real buff duration once 7.56 is live.
    /// </summary>
    public const float WindowSeconds = 7f;

    /// <summary>False until the window has been measured in game. Keeps the guess honest.</summary>
    public const bool WindowSecondsIsConfirmed = false;

    /// <summary>Test seam, matching the other trackers in this codebase.</summary>
    internal Func<DateTime> UtcNow = () => DateTime.UtcNow;

    private InstinctColor _last = InstinctColor.None;
    private DateTime _lastUtc = DateTime.MinValue;

    /// <summary>The colour last spent, or None when no chain is running.</summary>
    public InstinctColor LastColor => IsWindowOpen ? _last : InstinctColor.None;

    /// <summary>Whether a chain is still live.</summary>
    public bool IsWindowOpen =>
        _last != InstinctColor.None && SecondsSinceLast < WindowSeconds;

    /// <summary>Seconds since the last spend; <see cref="float.MaxValue"/> when none.</summary>
    public float SecondsSinceLast => _lastUtc == DateTime.MinValue
        ? float.MaxValue
        : (float)(UtcNow() - _lastUtc).TotalSeconds;

    /// <summary>
    /// The colour that would score the Intentional Combo next, or None when no chain is running
    /// (in which case any colour opens one).
    /// </summary>
    public InstinctColor PreferredNext => IsWindowOpen
        ? BSTActions.NextClockwise(_last)
        : InstinctColor.None;

    /// <summary>
    /// Would spending this colour now score the Intentional Combo? True when it is the clockwise
    /// successor. Opening a fresh chain is not an Intentional Combo — there is nothing to chain
    /// from — so this is false with the window closed.
    /// </summary>
    public bool WouldBeIntentional(InstinctColor color) =>
        color != InstinctColor.None && IsWindowOpen && color == PreferredNext;

    /// <summary>
    /// Would spending this colour still combo, weakly? Any colour continues a live chain
    /// off-order; only the clockwise one is Intentional.
    /// </summary>
    public bool WouldCombo(InstinctColor color) =>
        color != InstinctColor.None && IsWindowOpen;

    /// <summary>Record a spend. Call from the dispatch callback, not from the decision.</summary>
    public void Record(InstinctColor color)
    {
        if (color == InstinctColor.None)
            return;

        _last = color;
        _lastUtc = UtcNow();
    }

    /// <summary>Drop the chain — combat end, zone change, or a beast swap that resets it.</summary>
    public void Reset()
    {
        _last = InstinctColor.None;
        _lastUtc = DateTime.MinValue;
    }

    /// <summary>One-line readout for the debug panel.</summary>
    public string Describe() => !IsWindowOpen
        ? "no chain"
        : $"{_last} → want {PreferredNext} ({WindowSeconds - SecondsSinceLast:0.0}s left)";
}
