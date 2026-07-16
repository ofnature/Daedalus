using System.Collections.Generic;
using System.Linq;
using Daedalus.Services.Blu;

namespace Daedalus.Data;

/// <summary>One per-duty utility slot the party must cover (checklist + assignment — never automated).</summary>
public sealed record BluDutyRequirement(
    uint TerritoryId,
    string DutyName,
    BluCapabilities Capability,
    int RequiredCount,
    string RoleLabel);

/// <summary>A requirement evaluated against the live BLU roster.</summary>
public sealed record BluDutyAssignmentResult(
    BluDutyRequirement Requirement,
    IReadOnlyList<string> Assigned)
{
    public bool Satisfied => Assigned.Count >= Requirement.RequiredCount;
}

/// <summary>
/// Coil of Bahamut per-duty utility assignments (data: burn-reference/blu-loadouts.md §3 +
/// proteus-plan.md Coil notes). The capability election assigns carriers deterministically;
/// the LAN Party window shows a red warning pre-pull when fewer capable toons than required.
/// Mechanic execution stays MANUAL — the LAN spec's do-not-implement list stands (no fight
/// timeline sync, no mechanic solving).
/// </summary>
public static class BluDutyAssignments
{
    // TerritoryType row ids — XIVAPI ContentFinderCondition-verified 2026-07-15.
    public const uint BindingCoilTurn5 = 245;  // T5 — Twintania
    public const uint SecondCoilTurn4 = 358;   // T9 — Nael deus Darnus
    public const uint FinalCoilTurn4 = 196;    // T13 — Bahamut Prime

    public static readonly BluDutyRequirement[] All =
    [
        new(BindingCoilTurn5, "T5 — Twintania", BluCapabilities.Level5Death, 1,
            "Level 5 Death carrier (adds/tornado)"),
        new(SecondCoilTurn4, "T9 — Nael deus Darnus", BluCapabilities.StickyTongue, 2,
            "Sticky Tongue puller (golem drag)"),
        new(FinalCoilTurn4, "T13 — Bahamut Prime", BluCapabilities.Avail, 2,
            "Avail redirect (Earth Shaker)"),
        new(FinalCoilTurn4, "T13 — Bahamut Prime", BluCapabilities.Level5Death, 2,
            "Level 5 Death carrier (P2 adds)"),
    ];

    /// <summary>True when the territory has BLU utility slots to cover (a Coil turn we track).</summary>
    public static bool HasRequirements(uint territoryId) => All.Any(r => r.TerritoryId == territoryId);

    /// <summary>T13 is the only duty with Moon Flute stagger groups (Gigaflare pushes at 76%/52%).</summary>
    public static bool UsesMoonFluteStagger(uint territoryId) => territoryId == FinalCoilTurn4;

    /// <summary>
    /// Evaluate every requirement of the territory against the live BLU roster: deterministic
    /// carrier assignment (capability filter + SenderId sort) plus a satisfied/short verdict.
    /// </summary>
    public static List<BluDutyAssignmentResult> Evaluate(
        uint territoryId, IReadOnlyList<BluPeerCapability> bluRoster)
        => All.Where(r => r.TerritoryId == territoryId)
            .Select(r => new BluDutyAssignmentResult(
                r, BluPartyElection.ElectOwners(bluRoster, r.Capability, r.RequiredCount)))
            .ToList();
}
