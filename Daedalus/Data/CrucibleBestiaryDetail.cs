using System.Linq;

namespace Daedalus.Data;

/// <summary>
/// The Master's Bestiary detail panel (XBMMonsterBookDetail) — the one place the game shows a beast's TRUE rank: the
/// team screens sync ranks to the board ("Sync from 5" on board 1 shows a rank-9 Damselfly as 5). It is drawn from
/// text elements rather than window values: element 12 "No. 1", element 40 "RANK 3", element 42 "21/100" (EXP).
/// Pure.
/// </summary>
public static class CrucibleBestiaryDetail
{
    public const string Window = "XBMMonsterBookDetail";
    public const string Notebook = "XBMMonsterNotebook";
    public const uint NumberNode = 12;
    public const uint RankNode = 40;
    public const uint ExpNode = 42;

    /// <summary>Beasts per Bestiary page (two pages, 50 beasts).</summary>
    public const int PageSize = 25;

    /// <summary>The beast and its rank from the panel's texts; null when the panel shows no beast (or no rank).</summary>
    public static (int PetId, int Rank, int Exp)? Parse(string numberText, string rankText, string expText)
    {
        if (!int.TryParse(Digits(numberText), out var petId) || petId is < 1 or > 50)
            return null;
        if (!int.TryParse(Digits(rankText), out var rank) || rank is < 1 or > CrucibleLevelPlan.MaxRank)
            return null;

        // "21/100": the EXP before the slash.
        var exp = int.TryParse(Digits(expText.Split('/')[0]), out var e) ? e : 0;
        return (petId, rank, exp);
    }

    private static string Digits(string text) => new(text.Where(char.IsAsciiDigit).ToArray());
}
