using Daedalus.Config.DPS;

namespace Daedalus.Data;

/// <summary>
/// One reference loadout: Core spells are must-haves (missing one means the role does not
/// function), Flex spells are content-dependent alternates for the remaining slots.
/// </summary>
public sealed record BluLoadout(string Name, BluRole Role, uint[] Core, uint[] Flex);

/// <summary>
/// Blue Academy reference loadouts (patch 7.5, mage.blue — pulled 2026-07-02; full data +
/// per-fight Coil notes in burn-reference/blu-loadouts.md). Feeds the Missing window's
/// role-loadout checklist: ✔ learned+slotted / ● learned not slotted / ✗ not learned.
/// Regenerate alongside BLUSpellbook when a patch shifts the meta.
/// </summary>
public static class BLULoadouts
{
    /// <summary>Slotted in every Blue Academy loadout, all three roles.</summary>
    private static readonly uint[] CommonCore =
    [
        11386, // Song of Torment
        11426, // Feather Rain
        11429, // Shock Strike
        18317, // Angel Whisper (the BLU raise)
        18322, // Aetheric Mimicry
        18323, // Surpanakha
        18325, // J Kick
        23288, // Phantom Flurry
        23290, // Nightbloom
        34580, // Sea Shanty
        34582, // Being Mortal
    ];

    public static readonly BluLoadout Dps = new(
        "DPS",
        BluRole.Dps,
        Core:
        [
            .. CommonCore,
            11393, // Bristle
            11415, // Moon Flute
            18308, // Sonic Boom
            18309, // Whistle
            23264, // Triple Trident
            23265, // Tingle
            23267, // Cold Fog
            23275, // The Rose of Destruction
            23285, // Matra Magic
            34576, // Winged Reprobation
            34567, // Breath of Magic
        ],
        Flex:
        [
            34579, // Mortal Flame
            11407, // Final Sting
            11411, // Off-guard
            11421, // Peculiar Light
            34574, // Conviction Marcato
            18305, // Magic Hammer
            34578, // Candy Cane
            34568, // Wild Rage
            18316, // Revenge Blast
            // Dungeon package (freeze -> shatter + gather):
            23282, // Hydro Pull
            11419, // the Ram's Voice
            23277, // Ultravibration
            11405, // Missile
        ]);

    public static readonly BluLoadout Tank = new(
        "Tank",
        BluRole.Tank,
        Core:
        [
            .. CommonCore,
            11406, // White Wind
            11395, // Blood Drain
            11388, // Bad Breath
            11424, // Diamondback
            11417, // Mighty Guard
            11421, // Peculiar Light
            18305, // Magic Hammer
            18308, // Sonic Boom
            18320, // Devour
            23273, // Chelonian Gate
            23280, // Dragon Force
            34563, // Goblin Punch
        ],
        Flex:
        [
            // Dungeon variant swaps (replace Peculiar Light / Magic Hammer / Chelonian Gate / Sonic Boom):
            11410, // Toad Oil
            11419, // the Ram's Voice
            11405, // Missile
            23277, // Ultravibration
            23282, // Hydro Pull
        ]);

    public static readonly BluLoadout Healer = new(
        "Healer",
        BluRole.Healer,
        Core:
        [
            .. CommonCore,
            11406, // White Wind
            11411, // Off-guard
            11415, // Moon Flute
            18303, // Pom Cure
            18304, // Gobskin
            18305, // Magic Hammer
            18308, // Sonic Boom
            23269, // Stotram
            23272, // Angel's Snack
            23275, // The Rose of Destruction
        ],
        Flex:
        [
            // Off-healer variant keeps DPS tools instead of Gobskin/Stotram/Magic Hammer:
            18309, // Whistle
            23264, // Triple Trident
            23265, // Tingle
            23267, // Cold Fog
        ]);

    /// <summary>
    /// Solo farm/overworld set (user-designed 2026-07-12): the DPS kit with Basic Instinct +
    /// Mighty Guard (BI cancels MG's damage penalty — free tank stance while solo), White Wind
    /// self-sustain instead of Off-guard, Final Sting execute instead of Revenge Blast, and the
    /// freeze→shatter pair. Exactly 24 core slots.
    /// </summary>
    public static readonly BluLoadout Solo = new(
        "Solo",
        BluRole.Solo,
        Core:
        [
            .. CommonCore,      // 11 slots
            11393, // Bristle
            11415, // Moon Flute
            18308, // Sonic Boom
            23275, // The Rose of Destruction
            23285, // Matra Magic
            34567, // Breath of Magic
            34579, // Mortal Flame
            23276, // Basic Instinct
            11417, // Mighty Guard  (swapped in for Revenge Blast)
            11406, // White Wind    (swapped in for Off-guard — the solo heal)
            11407, // Final Sting
            11419, // the Ram's Voice
            23277, // Ultravibration
        ],
        Flex:
        [
            11410, // Toad Oil
            23267, // Cold Fog
            11430, // Glass Dance
            23287, // Both Ends
            11388, // Bad Breath
            11405, // Missile
            23282, // Hydro Pull
            18320, // Devour
            18318, // Exuviation
        ]);

    /// <summary>All reference loadouts, in display order.</summary>
    public static readonly BluLoadout[] All = [Tank, Dps, Healer, Solo];
}
