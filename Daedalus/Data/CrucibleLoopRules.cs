using System;
using System.Collections.Generic;
using System.Linq;

namespace Daedalus.Data;

/// <summary>
/// Going back into the Crucible for the next run, as the recordings show it (docs/crucible-screens.md): Lauda's menu
/// (a list with "Crucible of the Unbroken" when she also has a quest to offer), "Challenge the Crucible of the
/// Unbroken.", the board list, the team, the "※ … Proceed to challenge this board?" warning, Commence. Pure.
/// </summary>
public static class CrucibleLoopRules
{
    /// <summary>The NPC the Crucible is entered through.</summary>
    public const string EntryNpc = "Lauda";

    /// <summary>The Central Shroud, where Lauda stands (near Bentbranch, X:21.9 Y:22.8).</summary>
    public const ushort LaudaTerritoryId = 148;

    /// <summary>How close Lauda must be to talk to her (no walking: the loop starts where the last run left you).</summary>
    public const float TalkRange = 6.5f;

    /// <summary>Her list option for the Crucible, when she shows a list first.</summary>
    public const string MenuOption = "Crucible of the Unbroken";

    /// <summary>The first line of her Crucible menu.</summary>
    public const string ChallengeOption = "Challenge the Crucible of the Unbroken.";

    /// <summary>Give up if no entry screen moves on for this long.</summary>
    public static readonly TimeSpan StuckAfter = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The board layout's degree (AgentXBMStageDetailList.SelectedRank): 0 Standard, 1-3 First-Third Degree. The
    /// layout's callback 5 steps it up one (Korha 2026-11-04: 0, 1, 2, 3 in three presses) and does NOT wrap: four
    /// presses on Third left it on Third (Korha 2026-11-04 16:16). Callback 4 is the ◀ arrow: from Korha's node dump
    /// of the layout every recorded callback is the button's event param minus one (Challenge event 9 = callback 8,
    /// rankings box 4 = 3, ▶ 6 = 5), and ◀ is event 5. Inferred, not yet seen pressed — if it does nothing the loop
    /// gives up after a few steps. The choice survives backing out. -1 in the setting = leave whatever the game has.
    /// </summary>
    public const int DegreeCount = 4;

    /// <summary>The layout's callback that steps the degree up one (▶).</summary>
    public const int DegreeStepEvent = 5;

    /// <summary>The layout's callback that steps the degree down one (◀).</summary>
    public const int DegreeStepDownEvent = 4;

    /// <summary>The setting that leaves the game's degree alone.</summary>
    public const int DegreeAsSet = -1;

    /// <summary>The captured beasts still below rank 25. A rank never read counts as below.</summary>
    public static IReadOnlyList<int> BelowMaxRank(IEnumerable<int> captured, Func<int, int?> knownRank)
        => captured.Where(no => (knownRank(no) ?? 0) < CrucibleLevelPlan.MaxRank).ToList();

    /// <summary>
    /// "Loop until every beast is rank 25": the stop message once every captured beast is there, else null (go on).
    /// No captured beasts read yet is not done.
    /// </summary>
    public static string? LevelingFinished(IReadOnlyCollection<int> captured, Func<int, int?> knownRank)
        => captured.Count > 0 && BelowMaxRank(captured, knownRank).Count == 0
            ? "Loop finished: every beast is rank 25."
            : null;

    public static string DegreeName(int degree) => degree switch
    {
        0 => "Standard",
        1 => "First Degree",
        2 => "Second Degree",
        3 => "Third Degree",
        _ => "as set in game",
    };

    public enum DegreeAction { Ready, StepUp, StepDown, GiveUp }

    /// <summary>
    /// What to do about the degree on the layout: Ready when it is the wanted one (or the setting leaves it alone),
    /// else one step toward it. At most <see cref="DegreeCount"/> steps; still wrong after that, the step did nothing
    /// (a locked degree, or ◀ is not what we think): give up.
    /// </summary>
    public static DegreeAction NextDegreeAction(int current, int wanted, int stepsTaken)
    {
        if (wanted is < 0 or >= DegreeCount || current == wanted)
            return DegreeAction.Ready;
        if (stepsTaken >= DegreeCount)
            return DegreeAction.GiveUp;
        return current < wanted ? DegreeAction.StepUp : DegreeAction.StepDown;
    }

    /// <summary>Which line of Lauda's first list is the Crucible; -1 when it is not offered.</summary>
    public static int MenuPick(IReadOnlyList<string> options)
    {
        for (var i = 0; i < options.Count; i++)
        {
            if (string.Equals(options[i], MenuOption, StringComparison.Ordinal))
                return i;
        }

        return -1;
    }

    /// <summary>
    /// The warnings before a board ("※Your current team is smaller than the maximum allowance.", "※One or more of
    /// your familiars has a lower beast rank than is recommended.") — both end in "Proceed to challenge this board?".
    /// </summary>
    public static bool IsEntryWarning(string prompt) =>
        prompt.StartsWith('※') && prompt.Contains("Proceed to challenge this board?", StringComparison.Ordinal);
}
