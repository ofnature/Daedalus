using Daedalus.Data;

namespace Daedalus.Rotation.Phantom;

/// <summary>
/// A healer mid-way through a long phantom cast when someone needs an urgent top-off — the Phantom
/// Necromancer's Deep Freeze call (Doom, cleared only at full HP), a Doom status, or False Prediction.
/// <para>
/// Occult Comet is an 8-second cast, the Phantom Summoner's are 4 to 6. A healer locked into one of
/// those cannot heal until it ends, and Deep Freeze's Doom kills in 10. So the healer cancels the cast
/// and the job's own healing takes the next GCD — and the phantom layer, which runs ahead of the job,
/// must not simply start the long cast again while the call stands.
/// </para>
/// </summary>
public static class PhantomHealCallPolicy
{
    /// <summary>
    /// Casts at least this long count. Occult Comet (8s) and the Summoner casts (4–6s) qualify;
    /// the 2.3–2.5s spells finish before the heal would have been ready anyway.
    /// </summary>
    public const float LongCastSeconds = 3f;

    /// <summary>A cast this close to finishing is let through: it lands before a cancel would help.</summary>
    public const float MinRemainingToCancelSeconds = 0.5f;

    /// <summary>A phantom cast that heals is the answer, not the obstacle.</summary>
    private static readonly uint[] HealingPhantomActionIds = [49068]; // Occult Cure III

    /// <summary>Is this a long, non-healing phantom action?</summary>
    public static bool IsLongNonHealPhantomCast(uint actionId, float castSeconds)
    {
        if (castSeconds < LongCastSeconds || System.Array.IndexOf(HealingPhantomActionIds, actionId) >= 0)
            return false;

        foreach (var def in PhantomActions.All)
        {
            if (def.ActionId == actionId)
                return true;
        }

        return false;
    }

    /// <summary>Cancel the cast in progress now?</summary>
    public static bool ShouldCancel(bool isHealer, bool healCallPending, bool castIsLongNonHealPhantom,
        float castRemainingSeconds)
        => isHealer && healCallPending && castIsLongNonHealPhantom
           && castRemainingSeconds > MinRemainingToCancelSeconds;

    /// <summary>Hold off starting a long phantom cast?</summary>
    public static bool ShouldHoldLongCast(bool isHealer, bool healCallPending, bool actionIsLongNonHealPhantom)
        => isHealer && healCallPending && actionIsLongNonHealPhantom;
}
