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
    /// The next loot step: an item first when items are wanted (once), then the tokens (once), then leave. Taking
    /// everything closes the window by itself, so Leave only fires if it is still open.
    /// </summary>
    public static CrucibleLootStep NextLootStep(bool takeItems, bool itemTried, bool tokensTried)
    {
        if (takeItems && !itemTried)
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

    /// <summary>
    /// The first offered row that is beast gear, or -1. Beast gear is what keeps the run alive (Ring of Sacrifice,
    /// Belt of Constitution, Mythril Armor…); Crucible items and feed are left. The rows are not sorted by type, so
    /// "row 0" was luck (field 2026-10-25: no gear taken on Saar's clear).
    /// </summary>
    public static int PickGear(IReadOnlyList<int> offers, Func<int, bool> isGear)
    {
        for (var i = 0; i < offers.Count; i++)
        {
            if (offers[i] != 0 && isGear(offers[i]))
                return i;
        }

        return -1;
    }

    /// <summary>The start of each Yes/No prompt the autopilot's own clicks raise; any other prompt is the player's.</summary>
    public static readonly string[] KnownPrompts =
    [
        "Take ",                          // "Take 203 territory tokens?" / "Take the astral wristlet?"
        "Some loot remains unclaimed.",   // leaving the spoils
        "Choose the ",                    // treasure coffer
        "Conclude purchasing and leave",  // leaving the shop
        "Rest ",                          // campsite: "Rest and recover 45%…" / "Rest alone while your familiars keep watch…"
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
