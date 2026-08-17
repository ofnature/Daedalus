using System.Collections.Generic;

namespace Daedalus.Data;

/// <summary>
/// The canonical critical-encounter roster per Occult Crescent zone.
/// <para>
/// Taken from GAME DATA, not a wiki: the <c>DynamicEvent</c> sheet, rows 33-47 for South Horn
/// and 49-63 for North Horn. The boundary is provable rather than eyeballed — row 32 is
/// The Dalriada, the last Bozja entry, which caps at 48 participants where every Occult row
/// caps at 72. Rows 48 and 64/65 are the Forked Tower raids, which are not critical encounters.
/// Cross-checked against consolegameswiki: 15 each, and both agree.
/// </para>
/// <para>
/// The weakness log only knows what it has SEEN, so it can never tell you what is missing. This
/// is the other half of that answer — see <c>docs/occult-encounter-checklist.md</c>.
/// </para>
/// </summary>
public static class OccultEncounters
{
    public const ushort SouthHornTerritoryId = 1252;
    public const ushort NorthHornTerritoryId = 1346;

    /// <summary>South Horn's 15 critical encounters (DynamicEvent rows 33-47).</summary>
    public static readonly IReadOnlyList<string> SouthHornCriticalEncounters =
    [
        "Scourge of the Mind",
        "The Black Regiment",
        "The Unbridled",
        "Crawling Death",
        "Calamity Bound",
        "Trial by Claw",
        "From Times Bygone",
        "Company of Stone",
        "Shark Attack",
        "On the Hunt",
        "With Extreme Prejudice",
        "Noise Complaint",
        "Cursed Concern",
        "Eternal Watch",
        "Flame of Dusk",
    ];

    /// <summary>North Horn's 15 critical encounters (DynamicEvent rows 49-63).</summary>
    public static readonly IReadOnlyList<string> NorthHornCriticalEncounters =
    [
        "Many Mouths to Feed",
        "Doubled Trouble",
        "Quarried Away",
        "Forbidden Folios",
        "Cursed Resurgence",
        "Imbalanced Diet",
        "Web of Terror",
        "A Beast Unleashed",
        "Dark Artistry",
        "Familiar Tactics",
        "Appalling Behavior",
        "Tiny Terror",
        "Lost on the Wind",
        "Ahead of the Competition",
        "Accept No Imitators",
    ];

    /// <summary>The roster for a territory, or empty when it is not an Occult zone.</summary>
    public static IReadOnlyList<string> CriticalEncountersFor(ushort territoryId) => territoryId switch
    {
        SouthHornTerritoryId => SouthHornCriticalEncounters,
        NorthHornTerritoryId => NorthHornCriticalEncounters,
        _ => [],
    };

    /// <summary>
    /// The 48-player raids, which are NOT critical encounters (DynamicEvent rows 48, 64 and 65).
    /// <para>
    /// They run inside the Horn territories rather than in a map of their own — the content
    /// finder row for "The Forked Tower: Magic (Extreme)" points at TerritoryType 1346, North
    /// Horn, and no PlaceName for "Forked" exists at all. So the weakness log already records
    /// their enemies with no change; what it needs is to file them correctly, because a raid boss
    /// stamped as a critical encounter quietly inflates a coverage bucket that is already the
    /// least meaningful one.
    /// </para>
    /// <para>
    /// Told apart from critical encounters by participant cap, the same evidence that fixed the
    /// roster boundary: every Occult CE caps at 72, every Forked Tower row at 48.
    /// </para>
    /// </summary>
    public static readonly IReadOnlyList<string> ForkedTowerEvents =
    [
        "The Forked Tower: Blood",
        "The Forked Tower: Magic",
        "The Forked Tower: Magic (Extreme)",
    ];

    /// <summary>Is this encounter stamp one of the 48-player raids rather than a critical encounter?</summary>
    public static bool IsForkedTower(string? encounterName)
    {
        if (string.IsNullOrWhiteSpace(encounterName))
            return false;

        foreach (var n in ForkedTowerEvents)
        {
            if (string.Equals(n, encounterName, System.StringComparison.OrdinalIgnoreCase))
                return true;
        }

        // Defensive on the prefix too: the raid tiers are named as a family and a future one
        // would otherwise silently land in the critical-encounter bucket.
        return encounterName.StartsWith("The Forked Tower", System.StringComparison.OrdinalIgnoreCase);
    }
}
