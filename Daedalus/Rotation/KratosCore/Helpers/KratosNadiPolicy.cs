namespace Daedalus.Rotation.KratosCore.Helpers;

/// <summary>The form a Perfect Balance GCD should take to build the intended Beast Chakra.</summary>
public enum MnkPbForm
{
    OpoOpo,
    Raptor,
    Coeurl,
}

/// <summary>
/// Decides which nadi a Perfect Balance window should build and the next form to press toward it.
///
/// Masterful Blitz reads the 3 Beast Chakra you built: 3 identical → Lunar Nadi (Elixir Burst),
/// 3 different → Solar Nadi (Rising Phoenix), both nadi present → Phantom Rush. Phantom Rush (the
/// strongest GCD) needs BOTH nadi, so each PB must build the nadi you're MISSING — never the one you
/// already hold. The old logic rebuilt whichever nadi you already had, so MNK never reached Phantom Rush.
/// No-nadi opens Solar first (the Balance "Solar-Lunar" opener — safest for unknown kill times / AutoDuty).
/// </summary>
public static class KratosNadiPolicy
{
    /// <summary>True if this PB should build a Solar Nadi (3 different forms); false for Lunar / Phantom (3 identical Opo).</summary>
    public static bool ShouldBuildSolar(bool hasLunarNadi, bool hasSolarNadi)
    {
        if (hasLunarNadi && hasSolarNadi) return false; // both → 3 identical Opo → Phantom Rush
        if (hasLunarNadi) return true;                  // have Lunar → build the missing Solar
        if (hasSolarNadi) return false;                 // have Solar → build the missing Lunar
        return true;                                    // no nadi → Solar first (safest opener)
    }

    /// <summary>
    /// Next Beast Chakra form to press under Perfect Balance, given current nadi and which chakra slots
    /// are already filled. Lunar/Phantom = always Opo-opo (3 identical); Solar = fill one of each form.
    /// </summary>
    public static MnkPbForm NextForm(
        bool hasLunarNadi,
        bool hasSolarNadi,
        bool hasOpoChakra,
        bool hasRaptorChakra,
        bool hasCoeurlChakra)
    {
        if (!ShouldBuildSolar(hasLunarNadi, hasSolarNadi))
            return MnkPbForm.OpoOpo;

        // Solar: one of each form. Fill missing slots in Opo → Raptor → Coeurl order.
        if (!hasOpoChakra) return MnkPbForm.OpoOpo;
        if (!hasRaptorChakra) return MnkPbForm.Raptor;
        return MnkPbForm.Coeurl;
    }
}
