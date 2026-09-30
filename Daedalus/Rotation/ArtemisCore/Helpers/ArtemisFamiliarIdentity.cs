using Daedalus.Data;

namespace Daedalus.Rotation.ArtemisCore.Helpers;

/// <summary>
/// Which beast the summoned familiar is — and so the colour of its Trick.
/// </summary>
public static class ArtemisFamiliarIdentity
{
    /// <summary>
    /// The familiar's in-world name is the direct observation and wins when it matches a Bestiary
    /// entry. Otherwise the Battlehorn last pressed names the beast through the roster (its XBMPet
    /// RowId is the Bestiary number) — that covers a non-English client or an in-game spelling the
    /// table does not have. Null when neither says, and the rotation then falls back to not pairing.
    /// </summary>
    public static BstFamiliar? Resolve(bool hasFamiliar, string? familiarName, uint activeBattlehornRow)
    {
        if (!hasFamiliar)
            return null;

        return BstFamiliars.ByName(familiarName) ?? BstFamiliars.ByBestiaryNo(activeBattlehornRow);
    }
}
