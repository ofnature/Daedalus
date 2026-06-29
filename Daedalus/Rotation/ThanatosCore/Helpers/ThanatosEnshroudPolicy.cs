using Daedalus.Services.Content;

namespace Daedalus.Rotation.ThanatosCore.Helpers;

/// <summary>
/// Pure policy for the content-aware Enshroud low-HP gate. Extracted so it can be unit-tested without
/// the Dalamud runtime.
/// </summary>
internal static class ThanatosEnshroudPolicy
{
    /// <summary>
    /// True when Enshroud should be SKIPPED because the only targets are about to die (burst would be
    /// wasted). Applied in dungeon / open-world content only — trials, raids, and high-end content have
    /// no low-HP gate because boss HP pools make it pointless. A threshold of 0 disables the gate.
    /// </summary>
    /// <param name="profile">Effective duty profile (from the auto-duty classifier).</param>
    /// <param name="bestTargetHpFraction">HP fraction (0-1) of the HEALTHIEST enemy in range; 1 when none/unknown so a healthy pack never trips the gate.</param>
    /// <param name="thresholdPercent">Skip threshold as a percent, e.g. 5 = 5%.</param>
    public static bool ShouldSkipForLowHp(EffectiveDutyProfile profile, float bestTargetHpFraction, float thresholdPercent)
    {
        if (thresholdPercent <= 0f)
            return false;

        // No low-HP gate in big-boss content — always burst.
        if (profile is EffectiveDutyProfile.Trial or EffectiveDutyProfile.Raid or EffectiveDutyProfile.HighEndRaid)
            return false;

        return bestTargetHpFraction * 100f < thresholdPercent;
    }
}
