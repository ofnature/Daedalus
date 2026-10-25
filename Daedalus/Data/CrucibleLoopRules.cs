using System;
using System.Collections.Generic;

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

    /// <summary>How close Lauda must be to talk to her (no walking: the loop starts where the last run left you).</summary>
    public const float TalkRange = 6.5f;

    /// <summary>Her list option for the Crucible, when she shows a list first.</summary>
    public const string MenuOption = "Crucible of the Unbroken";

    /// <summary>The first line of her Crucible menu.</summary>
    public const string ChallengeOption = "Challenge the Crucible of the Unbroken.";

    /// <summary>Give up if no entry screen moves on for this long.</summary>
    public static readonly TimeSpan StuckAfter = TimeSpan.FromSeconds(30);

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
