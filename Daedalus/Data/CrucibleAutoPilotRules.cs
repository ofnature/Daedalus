using System;
using System.Collections.Generic;
using System.Linq;

namespace Daedalus.Data;

/// <summary>What to do with the spoils-of-battle window, one step at a time.</summary>
public enum CrucibleLootStep
{
    /// <summary>Take the first item (<c>XBMContentsBooty [4 | 0]</c>).</summary>
    TakeItem,

    /// <summary>Take the territory tokens (<c>[3]</c>).</summary>
    TakeTokens,

    /// <summary>Leave what is left (<c>[0]</c>).</summary>
    Leave,
}

/// <summary>
/// The Crucible autopilot's decisions, apart from the screens. Every click and prompt here was recorded from the
/// player's own runs (docs/crucible-screens.md); anything else is left alone. Pure.
/// </summary>
public static class CrucibleAutoPilotRules
{
    /// <summary>Wait after the horns are set before starting the battle — the player can still see and change them.</summary>
    public static readonly TimeSpan StartBattleAfter = TimeSpan.FromSeconds(1.5);

    /// <summary>
    /// The next loot step: beast gear while gear is wanted and not yet done, then the tokens (once), then leave.
    /// Taking everything closes the window by itself, so Leave only fires if it is still open.
    /// </summary>
    public static CrucibleLootStep NextLootStep(bool takeItems, bool gearDone, bool tokensTried)
    {
        if (takeItems && !gearDone)
            return CrucibleLootStep.TakeItem;
        if (!tokensTried)
            return CrucibleLootStep.TakeTokens;
        return CrucibleLootStep.Leave;
    }

    // The spoils window (XBMContentsBooty): the OFFERS are rows of five values from 7, the XBMItem id at 9 + 5·r (no
    // names). Taking row r is [4 | r]. Proven by the player's own picks (recording 2026-10-22): offers 77 (G2 beast
    // potion), 10 (twisted headband); [4 | 1] → "Take the twisted headband?". Values 27-76 are the Crucible items
    // held and 78-127 the beast gear held (Ring of Sacrifice there from the start; the Astral Wristlet appears only
    // after it was taken) — reading those as offers took a potion over the gear (field 2026-10-25, Saar).
    public const int LootFirstOfferId = 9;
    public const int LootRowStride = 5;
    public const int LootMaxRows = 4;

    public static int LootItemIdIndex(int row) => LootFirstOfferId + (LootRowStride * row);

    /// <summary>At most this many pieces of gear from one spoils window — a guard, the window offers at most four.</summary>
    public const int MaxGearTakes = 4;

    /// <summary>The beast gear already held: rows of five from 78, a flag at 78 + 5·k (up to ten).</summary>
    public const int HeldGearFirstFlag = 78;
    public const int HeldGearMaxRows = 10;

    public static int HeldGearFlagIndex(int row) => HeldGearFirstFlag + (LootRowStride * row);

    /// <summary>
    /// Whether the spoils' gear is done: no untaken gear left, the guard reached, or the last take did not add to
    /// the gear held (the game refused it — e.g. a full beast-gear inventory). Every piece of gear is taken, not just
    /// the first (user 2026-10-25). A TAKEN piece stays listed in the offers (recording 2026-10-25: the Ninja Suit
    /// still at row 0 after "Take the ninja suit?"), so taken rows are remembered rather than read back.
    /// </summary>
    public static bool LootGearDone(bool gearOffered, int takes, bool lastTakeRefused)
        => !gearOffered || takes >= MaxGearTakes || lastTakeRefused;

    /// <summary>
    /// The first offered row that is beast gear, or -1. Beast gear is what keeps the run alive (Ring of Sacrifice,
    /// Belt of Constitution, Mythril Armor…); Crucible items and feed are left. The rows are not sorted by type, so
    /// "row 0" was luck (field 2026-10-25: no gear taken on Saar's clear).
    /// </summary>
    public static int PickGear(IReadOnlyList<int> offers, Func<int, bool> isGear, IReadOnlySet<int>? takenRows = null)
    {
        for (var i = 0; i < offers.Count; i++)
        {
            if (offers[i] != 0 && isGear(offers[i]) && takenRows?.Contains(i) != true)
                return i;
        }

        return -1;
    }

    /// <summary>The treasure coffer's offers: an XBMItem id at 6 + 5·i, up to four; a pick is [2, i].</summary>
    public const int TreasureFirstOfferId = 6;
    public const int TreasureMaxRows = 4;

    public static int TreasureItemIdIndex(int row) => TreasureFirstOfferId + (LootRowStride * row);

    // What a point of each stat is worth in a coffer pick. Every Crucible death so far was HP attrition (no healing,
    // HP carried between battles), so staying alive outranks damage. A "taken"/"vulnerability" weight is negative
    // because lower is better there.
    private static readonly (string Stat, float Weight)[] TreasureWeights =
    [
        ("Maximum HP", 1.0f),
        ("Damage Taken", -1.5f),
        ("Physical Vulnerability", -1.0f),
        ("Magic Vulnerability", -0.6f),
        ("Physical Damage Dealt", 0.5f),
        ("Magic Damage Dealt", 0.15f),
        ("Damage Dealt", 0.6f),
        ("Haste", 0.4f),
        ("Critical Hit Rate", 0.3f),
        ("Evasion", 0.3f),
        ("Rapid Recast", 0.2f),
        ("Movement Speed", 0.2f),
        ("TP Accumulation", 0.1f),
        ("Block Strength", 0.05f),
        ("Loot Drop Rate", 0.05f),
        ("Territory Tokens Earned", 0.05f),
    ];

    private static readonly System.Text.RegularExpressions.Regex TreasureStatLine =
        new(@"^(?<stat>[A-Za-z' ]+?) (?<sign>[+-])(?<n>\d+)%", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>Per point of a resistance the board calls for ("Paralysis Resistance +100%" scores 35).</summary>
    public const float WantedResistanceWeight = 0.35f;

    /// <summary>
    /// The statuses each board's fights make worth resisting, by board (XBMContent row, = CrucibleBoard.Index). Research 2026-10-26
    /// (docs/crucible-boards-guide.md): Board 2's Taurus/Ahriman Doom; Board 3's Guttler Thunderbolt and Sahagin spikes
    /// (uncleansable Paralysis); First Master's Borgny (poison resistance "trivializes" it) and Morbol; Second Master's
    /// Lauda (anti-paralysis "mandatory"), King Ahriman's Dirty Eye Doom, Medusa and Deepeye petrification.
    /// </summary>
    public static IReadOnlyList<string> WantedResistances(int board) => board switch
    {
        2 => ["Doom"],
        3 => ["Paralysis"],
        4 => ["Poison", "Paralysis"],
        5 => ["Paralysis", "Doom", "Petrification"],
        _ => [],
    };

    private static readonly System.Text.RegularExpressions.Regex RecoverAfterBattle =
        new(@"Recovers (?<n>\d+)% of maximum HP at the end of battles", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// How much a piece of beast gear is worth from a coffer, read from its effect text (XBMItem column 12, e.g.
    /// "Maximum HP +20%"): survival first (Max HP, damage taken down, HP back after battles, the revive), then damage.
    /// A chance to Stun, Doom or chain yourself costs points. Unknown text scores 0.
    /// </summary>
    /// <para>A resistance the board calls for (<paramref name="wantedResistances"/>, e.g. "Paralysis" for
    /// "Paralysis Resistance +100%") scores <see cref="WantedResistanceWeight"/> per point.</para>
    public static float TreasureScore(string? effect, IReadOnlyCollection<string>? wantedResistances = null)
    {
        if (string.IsNullOrEmpty(effect))
            return 0f;

        var score = 0f;
        foreach (var raw in effect.Split('\n', '\r'))
        {
            var line = raw.Trim();
            var m = TreasureStatLine.Match(line);
            if (m.Success)
            {
                var stat = m.Groups["stat"].Value;
                var value = int.Parse(m.Groups["n"].Value) * (m.Groups["sign"].Value == "-" ? -1 : 1);
                if (wantedResistances != null && stat.EndsWith(" Resistance", StringComparison.Ordinal)
                    && wantedResistances.Contains(stat[..^" Resistance".Length]))
                {
                    score += WantedResistanceWeight * value;
                    continue;
                }

                foreach (var (name, weight) in TreasureWeights)
                {
                    if (string.Equals(stat, name, StringComparison.Ordinal))
                    {
                        score += weight * value;
                        break;
                    }
                }

                continue;
            }

            var recover = RecoverAfterBattle.Match(line);
            if (recover.Success)
                score += 1.2f * int.Parse(recover.Groups["n"].Value);
            else if (line.StartsWith("Revives beastmaster", StringComparison.Ordinal))
                score += 40f;
            else if (line.Contains("on self", StringComparison.Ordinal))
                score -= 8f;
        }

        return score;
    }

    /// <summary>
    /// The coffer offer to pick: the best <see cref="TreasureScore"/> among offers not yet taken (the first on a tie),
    /// or -1. A thief's knife yields two picks from one coffer; the window stays open for the second.
    /// </summary>
    public static int PickTreasure(
        IReadOnlyList<int> offers, Func<int, string?> effectOf, IReadOnlySet<int>? takenRows = null,
        IReadOnlyCollection<string>? wantedResistances = null)
    {
        var best = -1;
        var bestScore = float.MinValue;
        for (var i = 0; i < offers.Count; i++)
        {
            if (offers[i] == 0 || takenRows?.Contains(i) == true)
                continue;
            var score = TreasureScore(effectOf(offers[i]), wantedResistances);
            if (score > bestScore)
            {
                bestScore = score;
                best = i;
            }
        }

        return best;
    }

    /// <summary>
    /// For Legendary, whether to take beast gear on this board: where holding 10 pieces pays (Walking Armory, Boards 2/3
    /// and up) the gear is taken, as the Legendary runs found do (Meoni's series, Merton9999 on the forum) — it also keeps
    /// the three beasts alive; where it pays nothing (Board 1) none is taken, for Nameday Attire.
    /// </summary>
    public static bool LegendaryTakesGear(CrucibleBoard board) => board.BonusValue(CrucibleBonus.WalkingArmory) > 0;

    /// <summary>
    /// For Legendary: the best offer that is NOT beast gear (Nameday Attire is lost the moment gear is acquired); the best
    /// gear only when every remaining offer is gear (a coffer has to be taken from).
    /// </summary>
    public static int PickTreasureAvoidingGear(
        IReadOnlyList<int> offers, Func<int, bool> isGear, Func<int, string?> effectOf, IReadOnlySet<int>? takenRows = null,
        IReadOnlyCollection<string>? wantedResistances = null)
    {
        var nonGear = offers.Select(id => isGear(id) ? 0 : id).ToList();
        var pick = PickTreasure(nonGear, effectOf, takenRows, wantedResistances);
        return pick >= 0 ? pick : PickTreasure(offers, effectOf, takenRows, wantedResistances);
    }

    /// <summary>The start of each Yes/No prompt the autopilot's own clicks raise; any other prompt is the player's.</summary>
    public static readonly string[] KnownPrompts =
    [
        "Take ",                          // "Take 203 territory tokens?" / "Take the astral wristlet?"
        "Some loot remains unclaimed.",   // leaving the spoils
        "Choose the ",                    // treasure coffer
        "Conclude purchasing and leave",  // leaving the shop
        "Rest ",                          // campsite: "Rest and recover 45%…" / "Rest alone while your familiars keep watch…"
        "Leave the campsite",             // campsite, Legendary: "Leave the campsite without resting? / You cannot return…"
    ];

    /// <summary>Answer Yes only to a prompt the autopilot itself just raised.</summary>
    public static bool ShouldConfirm(string prompt, string? expected) =>
        expected != null && prompt.StartsWith(expected, StringComparison.Ordinal) && KnownPrompts.Contains(expected);

    /// <summary>Under this much HP the beastmaster rests alone: 90% for you, against 45% when a beast shares it.</summary>
    public const float RestAloneBelowSelfHpPct = 0.50f;

    /// <summary>
    /// Who rests at a campsite with you: nobody (-1) when you are under half HP — your HP is the run, and alone you
    /// recover 90% instead of 45% (field 2026-10-25: rested Goobbue with the beastmaster at 13%, died the next fight
    /// at 37%); otherwise the standing team member with the least HP left, as a fraction of its max. -1 also when
    /// nobody on the team is standing.
    /// </summary>
    public static int CampsitePick(IReadOnlyList<(uint Hp, uint MaxHp)> team, float selfHpPct = 1f)
    {
        if (selfHpPct < RestAloneBelowSelfHpPct)
            return -1;

        var best = -1;
        var bestFraction = double.MaxValue;
        for (var i = 0; i < team.Count; i++)
        {
            var (hp, max) = team[i];
            if (hp == 0 || max == 0)
                continue;
            var fraction = (double)hp / max;
            if (fraction < bestFraction)
            {
                bestFraction = fraction;
                best = i;
            }
        }

        return best;
    }

    /// <summary>Start the battle once the horns have been set for it and have stood a moment.</summary>
    public static bool ShouldStartBattle(int upcomingBattle, int hornsReadyFor, TimeSpan sinceHornsReady, int alreadyStarted) =>
        upcomingBattle != 0
        && hornsReadyFor == upcomingBattle
        && sinceHornsReady >= StartBattleAfter
        && alreadyStarted != upcomingBattle;
}
