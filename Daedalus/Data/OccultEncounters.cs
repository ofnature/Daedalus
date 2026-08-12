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
}
