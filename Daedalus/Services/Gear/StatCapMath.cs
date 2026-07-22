using System;

namespace Daedalus.Services.Gear;

/// <summary>
/// The per-piece substat cap formula (pure — the Lumina plumbing lives in
/// <see cref="StatCapService"/>). A piece's maximum for a substat is the ItemLevel sheet's
/// per-ilvl base for that stat scaled by the BaseParam sheet's per-equip-slot percentage:
/// <c>cap = round(ilvlBase × slotPercent / 1000)</c> — the same formula every community gear
/// tool uses (Ariyala/xivgear/etro). Validated against live pieces via /daedalus dumpgear.
/// </summary>
public static class StatCapMath
{
    /// <param name="ilvlStatBase">ItemLevel sheet value for the stat at the piece's ilvl.</param>
    /// <param name="slotPercent">BaseParam sheet slot percentage (per-mille, e.g. Ring = 300).</param>
    public static int Cap(int ilvlStatBase, int slotPercent)
    {
        if (ilvlStatBase <= 0 || slotPercent <= 0)
            return 0;

        // Round-half-up, matching in-game behavior (banker's rounding produces off-by-ones).
        return (int)Math.Floor((ilvlStatBase * (long)slotPercent) / 1000.0 + 0.5);
    }
}
