namespace Daedalus.Rotation.NikeCore.Helpers;

/// <summary>What Samurai does with a GCD when the target is not in melee range.</summary>
public enum NikeOutOfMeleeAction
{
    /// <summary>In melee — the normal combo, nothing special.</summary>
    Melee,

    /// <summary>Dash in with Hissatsu: Gyoten (an oGCD) and let the melee combo land after it.</summary>
    Gyoten,

    /// <summary>
    /// Already closing and about to arrive: push the melee combo and let the scheduler hold it
    /// until in range, rather than spending the GCD on Enpi.
    /// </summary>
    HoldForApproach,

    /// <summary>Genuinely stuck out of range — throw Enpi to keep the GCD rolling.</summary>
    Enpi,
}

/// <summary>Everything the out-of-melee decision needs, gathered live by the damage module.</summary>
public readonly record struct NikeOutOfMeleeSituation(
    bool InMelee,
    bool GyotenUsable,
    bool Closing,
    float SecondsToMelee,
    float GcdSeconds,
    float SecondsAlreadyHeld,
    bool EnpiUsable);

/// <summary>
/// Samurai's out-of-melee priority. Field 2026-09-24, Shinryu Paradox at the Hollow King spawn: with
/// the boss invulnerable and an add up, SAM targeted the add but stood at the boss and spammed Enpi.
/// The old gate threw Enpi the instant the target was out of reach — no gap closer, and no notion
/// that the character might already be on its way. Enpi then took a GCD that a melee hit, a moment
/// later, would have used far better.
/// </summary>
public static class NikeOutOfMeleePolicy
{
    public static NikeOutOfMeleeAction Decide(in NikeOutOfMeleeSituation s)
    {
        if (s.InMelee)
            return NikeOutOfMeleeAction.Melee;

        // (a) Close the gap outright. Gyoten is an oGCD, so this costs no GCD at all.
        if (s.GyotenUsable)
            return NikeOutOfMeleeAction.Gyoten;

        // (b) Already arriving: a melee GCD is due within about one GCD, so do not spend it on Enpi.
        // Bounded by SecondsAlreadyHeld, so a bad estimate — blocked path, target sidestepping, a
        // dodge that looked like an approach — costs at most one GCD before Enpi takes over.
        if (s.Closing
            && s.SecondsToMelee <= s.GcdSeconds
            && s.SecondsAlreadyHeld < s.GcdSeconds)
            return NikeOutOfMeleeAction.HoldForApproach;

        // (c) Too far, not moving, movement blocked, or a mechanic keeping us out.
        return s.EnpiUsable ? NikeOutOfMeleeAction.Enpi : NikeOutOfMeleeAction.Melee;
    }
}
