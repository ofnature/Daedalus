using System.Collections.Generic;
using System.Linq;
using Daedalus.Data;

namespace Daedalus.Ipc;

/// <summary>
/// The eight standard party slot codes — MT, OT, H1, H2, M1, M2, R1, R2 — derived from the LAN
/// roster so nobody has to tick eight radio buttons on eight boxes.
/// <para>
/// This exists because Minerva kept the CONSUMING half of BossMod's party roles and dropped the
/// producing half: its <c>PartyRolesConfig</c> is the enum, a dictionary and an indexer, with no
/// auto-assign, no priority tables and no UI, so <c>Assignments</c> is permanently empty and every
/// module's <c>AddAIHints</c> receives <c>Unassigned</c>. Mechanics that need a role — tower soaks,
/// tether pairs — therefore never resolve. Daedalus already knows every toon's job because it is
/// running their rotation, and it knows them across machines, so it is the one thing in the fleet
/// that can fill this in without being told.
/// </para>
/// <para>
/// The ordering mirrors BossMod's own <c>AutoAssignRoles</c> rather than inventing one, so a fleet
/// that later turns on a real BossMod gets the same answer instead of a competing one.
/// </para>
/// </summary>
public static class PartySlotCode
{
    /// <summary>Main tank preference — BossMod's own default order.</summary>
    private static int MainTankPriority(uint jobId) => jobId switch
    {
        JobRegistry.Warrior or JobRegistry.Marauder => 1,
        JobRegistry.Paladin or JobRegistry.Gladiator => 2,
        JobRegistry.DarkKnight => 3,
        JobRegistry.Gunbreaker => 4,
        _ => 99,
    };

    /// <summary>H1 preference: WHM &gt; AST &gt; SCH &gt; SGE, as BossMod sorts them.</summary>
    private static int HealerPriority(uint jobId) => jobId switch
    {
        JobRegistry.WhiteMage or JobRegistry.Conjurer => 1,
        JobRegistry.Astrologian => 2,
        JobRegistry.Scholar => 3,
        JobRegistry.Sage => 4,
        _ => 99,
    };

    /// <summary>R1 preference: MCH &gt; BRD &gt; DNC &gt; casters, as BossMod sorts them.</summary>
    private static int RangedPriority(uint jobId) => jobId switch
    {
        JobRegistry.Machinist => 1,
        JobRegistry.Bard or JobRegistry.Archer => 2,
        JobRegistry.Dancer => 3,
        JobRegistry.BlackMage or JobRegistry.Thaumaturge => 4,
        JobRegistry.Summoner or JobRegistry.Arcanist => 5,
        JobRegistry.RedMage => 6,
        JobRegistry.Pictomancer => 7,
        JobRegistry.BlueMage => 8,
        _ => 99,
    };

    /// <summary>
    /// Codes per sender id. Members are ordered by CONTENT ID first, not by observation order or
    /// display order: both differ per machine, and every box has to derive the same answer for the
    /// same party or two toons act on the same tower.
    /// </summary>
    public static Dictionary<string, string> Assign(
        IEnumerable<(string SenderId, string Role, uint JobId, ulong ContentId)> members)
    {
        var result = new Dictionary<string, string>(System.StringComparer.Ordinal);

        var all = members
            .Where(m => !string.IsNullOrEmpty(m.SenderId))
            .OrderBy(m => m.ContentId)
            .ThenBy(m => m.SenderId, System.StringComparer.Ordinal)
            .ToList();

        var tanks = all.Where(m => m.Role == "Tank")
            .OrderBy(m => MainTankPriority(m.JobId)).ToList();
        var healers = all.Where(m => m.Role == "Healer")
            .OrderBy(m => HealerPriority(m.JobId)).ToList();

        var dps = all.Where(m => m.Role != "Tank" && m.Role != "Healer").ToList();
        var melee = dps.Where(m => JobRegistry.IsMeleeDps(m.JobId)).ToList();
        var ranged = dps
            .Where(m => JobRegistry.IsRangedPhysicalDps(m.JobId) || JobRegistry.IsCasterDps(m.JobId))
            .OrderBy(m => RangedPriority(m.JobId)).ToList();

        // BossMod promotes the lowest-priority ranged into a melee slot when there are three or
        // more of them, because the eight slots only hold two of each. Same rule here, or a
        // three-caster party leaves M2 empty and a caster unassigned.
        while (melee.Count < 2 && ranged.Count > 2)
        {
            melee.Add(ranged[^1]);
            ranged.RemoveAt(ranged.Count - 1);
        }

        Take(tanks, "MT", "OT");
        Take(healers, "H1", "H2");
        Take(melee, "M1", "M2");
        Take(ranged, "R1", "R2");

        return result;

        void Take(List<(string SenderId, string Role, uint JobId, ulong ContentId)> list, string a, string b)
        {
            // Only the first two of any family get a slot; the eight codes cannot express a third
            // tank, and an invented one would be acted on. Everyone else stays unassigned, which
            // is what the consumer already defaults to.
            for (var i = 0; i < list.Count; i++)
                result[list[i].SenderId] = i switch { 0 => a, 1 => b, _ => "" };
        }
    }
}
