using System.Linq;

namespace Daedalus.Data;

/// <summary>
/// The Master's Bestiary detail panel (XBMMonsterBookDetail). It is drawn from text elements rather than window values:
/// element 12 "No. 1", element 40 "RANK 3", element 42 "21/100" (EXP).
/// <para>Its RANK is synced to the board the Crucible is set to, like the team screens: with a Sync-10 board up, a
/// rank-25 Damselfly and a rank-15 Behemoth both read "RANK 10" (Korha 2026-10-31; the Bestiary read off the First
/// Master's Board showed them true). The EXP is never synced: the Behemoth read 50/100, and a rank-25 beast reads
/// "-/-" whatever the board. So a rank is only ever raised from here, and EXP falling is the beast ranking up.</para>
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

    /// <summary>
    /// The beast, its rank and its EXP from the panel's texts; null when the panel shows no beast (or no rank). EXP
    /// "-/-" is rank 25 whatever rank is shown (EXP null); no EXP text is EXP null.
    /// </summary>
    public static (int PetId, int Rank, int? Exp)? Parse(string numberText, string rankText, string expText)
    {
        if (!int.TryParse(Digits(numberText), out var petId) || petId is < 1 or > 50)
            return null;
        if (!int.TryParse(Digits(rankText), out var rank) || rank is < 1 or > CrucibleLevelPlan.MaxRank)
            return null;

        var before = expText.Split('/')[0];
        if (before.Contains('-') && Digits(before).Length == 0)
            return (petId, CrucibleLevelPlan.MaxRank, null);

        // "21/100": the EXP before the slash.
        return (petId, rank, int.TryParse(Digits(before), out var e) ? (int?)e : null);
    }

    /// <summary>
    /// The rank and EXP to keep after a reading. The shown rank may be the board's sync, so it only ever raises the
    /// known rank. EXP is true: when it reads lower than last time at the same known rank, the beast ranked up since
    /// (one rank: it assumes under 100 EXP banked between readings, so read after each run). Unknown EXP keeps the known EXP.
    /// </summary>
    public static (int Rank, int? Exp) Merge(int? knownRank, int? knownExp, int readRank, int? readExp)
    {
        if (knownRank is not { } known || readRank > known)
            return (readRank, readExp);
        if (readExp is not { } exp)
            return (known, knownExp);
        if (knownExp is { } last && exp < last && known < CrucibleLevelPlan.MaxRank)
            return (known + 1, exp);
        return (known, exp);
    }

    private static string Digits(string text) => new(text.Where(char.IsAsciiDigit).ToArray());
}
