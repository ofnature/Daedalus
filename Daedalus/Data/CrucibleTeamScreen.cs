using System.Linq;

namespace Daedalus.Data;

/// <summary>
/// Where the team screen (XBMPetParty — Team Composition and the horn screen alike) keeps each listed beast: value 5
/// is how many are listed, then one block of <see cref="Stride"/> values per beast, starting with its rank (text) and
/// its icon (<see cref="IconBase"/> + Bestiary number). Read off the 2026-10-24 recording: Goobbue "5" / 242030 at
/// 6 / 7, Buffalo at 83 / 84, then 160 / 161. Pure.
/// </summary>
public static class CrucibleTeamScreen
{
    public const int CountIndex = 5;
    public const int FirstRankIndex = 6;
    public const int FirstIconIndex = 7;
    public const int Stride = 77;
    public const long IconBase = 242000;

    public static int RankIndex(int member) => FirstRankIndex + (Stride * member);

    public static int IconIndex(int member) => FirstIconIndex + (Stride * member);

    /// <summary>
    /// The beast and its rank from one block; null when the values are not a beast (or not a rank). The rank text
    /// carries the game's paw glyph in front of the number (U+E036, a private-use icon character), so only its
    /// digits are read.
    /// </summary>
    public static (int PetId, int Rank)? Member(string rankText, long icon)
    {
        var petId = icon - IconBase;
        var digits = new string(rankText.Where(char.IsAsciiDigit).ToArray());
        if (petId is < 1 or > 50 || !int.TryParse(digits, out var rank) || rank is < 1 or > CrucibleLevelPlan.MaxRank)
            return null;
        return ((int)petId, rank);
    }
}
