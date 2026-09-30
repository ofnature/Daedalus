using System;
using System.Collections.Generic;
using System.Linq;

namespace Daedalus.Data;

/// <summary>
/// A prebuilt Battlehorn roster. Each horn lists beasts in preference order (Bestiary numbers):
/// the first one captured and not already on another horn is used, so a team still fills before
/// its level 50 beasts are caught.
/// </summary>
/// <param name="Id">Stable id kept in the config.</param>
/// <param name="Name">Dropdown label.</param>
/// <param name="UseFor">One line on what it is for.</param>
/// <param name="Horns">Three horns, each a preference list of Bestiary numbers. Empty for the
/// player's own saved team, which comes from the config instead.</param>
public sealed record BattlehornTeam(string Id, string Name, string UseFor, int[][] Horns);

/// <summary>Beasts resolved onto the three horns, and the horns nobody captured could fill.</summary>
/// <param name="Rows">Bestiary number per horn; 0 = leave that horn as it is.</param>
/// <param name="Missing">One entry per horn that could not be filled, naming what it wanted.</param>
public sealed record BattlehornTeamResolution(int[] Rows, IReadOnlyList<string> Missing);

/// <summary>
/// The prebuilt teams, from the community guides summarised in docs/beastmaster-rotation-and-pets.md §5.
/// Horn order is swap order: horn 1 comes out first, each later horn after a Parting Blow.
/// </summary>
public static class BattlehornTeams
{
    /// <summary>Id of the player's own team, saved from the horns as they stand.</summary>
    public const string SavedId = "saved";

    public static readonly IReadOnlyList<BattlehornTeam> All =
    [
        new("single-target", "Single target / Crucible, piercing-weak boss (Bug team)",
            "Damselfly opens, Mantis's vulnerability amplifies, Wespe's Final Sting finishes.",
            [[44, 18, 8], [16], [10]]),
        new("crucible-slashing", "Crucible, slashing-weak boss",
            "The Bug team ending on Dullahan instead of Wespe.",
            [[44, 8], [16], [18]]),
        new("balanced", "Balanced damage + utility",
            "Vulture dispels buffs, Bat drains and cleanses, Colibri's multi-hit release.",
            [[11], [19], [32]]),
        new("dungeon", "Dungeon / AoE packs",
            "Chimera freezes the pack, Buffalo stuns it, Treant (or Behemoth) keeps it locked down.",
            [[38, 21], [26], [36, 50]]),
        new("party-support", "Party support",
            "Squirrel haste, Dullahan physical damage up, Ice Golem magic damage up.",
            [[2], [18], [47]]),
        new("levelling", "Levelling",
            "Early catches: Buffalo for packs, Mantis (Diremite before it), Wespe to finish.",
            [[26], [16, 8], [10]]),
        new(SavedId, "My saved horns",
            "The three horns you saved with \"Save current horns\".",
            []),
    ];

    /// <summary>The team with this id, or null.</summary>
    public static BattlehornTeam? ById(string? id) =>
        All.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.Ordinal));

    /// <summary>
    /// Pick a captured beast for each horn. <paramref name="saved"/> is used for
    /// <see cref="SavedId"/>. A beast already chosen for an earlier horn is skipped, since one beast
    /// can sit on one horn only.
    /// </summary>
    public static BattlehornTeamResolution Resolve(BattlehornTeam team, int[]? saved, Func<int, bool> isCaptured)
    {
        var horns = team.Id == SavedId
            ? Enumerable.Range(0, 3)
                .Select(i => saved is { Length: > 0 } && i < saved.Length && saved[i] > 0 ? new[] { saved[i] } : Array.Empty<int>())
                .ToArray()
            : team.Horns;

        var rows = new int[3];
        var missing = new List<string>();
        for (var horn = 0; horn < 3 && horn < horns.Length; horn++)
        {
            foreach (var no in horns[horn])
            {
                if (rows.Contains(no) || !isCaptured(no))
                    continue;
                rows[horn] = no;
                break;
            }

            if (rows[horn] == 0 && horns[horn].Length > 0)
                missing.Add($"horn {horn + 1}: "
                    + string.Join(" or ", horns[horn].Select(n => BstFamiliars.ByBestiaryNo((uint)n)?.Name ?? $"#{n}")));
        }

        return new BattlehornTeamResolution(rows, missing);
    }
}
