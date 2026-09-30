using System;
using System.Collections.Generic;

namespace Daedalus.Data;

/// <summary>
/// Which Bestiary beast a wild enemy counts as, by the enemy's in-world name ("Black Eft" is a
/// Salamander, "Roselet" a Flying Trap). The game data has no such link — checked 2026-09-30, the
/// XBM sheets and BNpcBase carry none — so this is compiled from the community:
/// Console Games Wiki (each beast's Spawns table and the Master's Bestiary table), Hardcore Gamer,
/// Polygon (via Gamixo), TheGamer, Eorzea Weather, eo-place and Icy Veins, 2026-09-30.
///
/// <para>
/// Left out on purpose: names the sources disagree on or flag unconfirmed (a guide listing a duty
/// boss by its species name, "some users have obtained" mentions, Arbor Buzzard), "Ocean Roseling"
/// (its levequest namesake can't be tamed and the name can't tell them apart) and "Lone Coeurl" (a
/// FATE mob that dies in seconds). Most rows come from one source; a wrong one costs a Capture,
/// which still deals damage, and a Gauge scan of that enemy overrides this table.
/// </para>
/// </summary>
public static class BstCaptureMobs
{
    private static readonly Dictionary<string, int> ByName = new(StringComparer.OrdinalIgnoreCase)
    {
        // 2. Squirrel
        ["Chinchilla"] = 2,
        ["Deepvoid Deathmouse"] = 2,
        ["Ground Squirrel"] = 2,
        ["Nutmuncher Marmot"] = 2,
        ["Pack Rat"] = 2,
        ["Star Marmot"] = 2,
        ["Wet Rat"] = 2,
        ["White Joker"] = 2,
        // 3. Lamb
        ["Downy Dunstan"] = 3,
        ["Lost Lamb"] = 3,
        ["Mad Karakul Ewe"] = 3,
        ["Ornery Karakul"] = 3,
        ["Wild Ram"] = 3,
        // 4. Pugil
        ["Mud Pugil"] = 4,
        ["Oannes"] = 4,
        ["Pug Pugil"] = 4,
        ["Pugil"] = 4,
        ["Sastasha Pugil"] = 4,
        ["Sirius Pugil"] = 4,
        ["Swamp Pugil"] = 4,
        // 5. Opo-opo
        ["Galago"] = 5,
        ["Gully Galago"] = 5,
        ["North Shroud Opo-Opo"] = 5,
        ["Opo-opo"] = 5,
        // 6. Dodo
        ["Fat Dodo"] = 6,
        ["Feral Dodo"] = 6,
        ["Gluttonous Gertrude"] = 6,
        ["Ruffled Dodo"] = 6,
        ["Stray Dodo"] = 6,
        ["Wild Dodo"] = 6,
        // 7. Coblyn
        ["Asbestos Coblyn"] = 7,
        ["Copper Coblyn"] = 7,
        ["Copperbell Coblyn"] = 7,
        ["Lead Coblyn"] = 7,
        ["Rusty Coblyn"] = 7,
        ["Synthetic Doblyn"] = 7,
        // 8. Diremite
        ["Diremite"] = 8,
        ["Tam-Tara Diremite"] = 8,
        // 9. Megalocrab
        ["Beryl Crab"] = 9,
        ["Big Claw"] = 9,
        ["Cancer"] = 9,
        ["Cave Crab"] = 9,
        ["Decoy Crab"] = 9,
        ["Megalocrab"] = 9,
        ["Old Six-arms"] = 9,
        ["Shearing Sheridan"] = 9,
        ["Snipper"] = 9,
        ["Spawning Megalocrab"] = 9,
        ["Suspicious Megalocrab"] = 9,
        ["Territorial Snipper"] = 9,
        // 10. Wespe
        ["Huge Hornet"] = 10,
        ["Killer Wespe"] = 10,
        ["King Wespe"] = 10,
        ["Stinging Sophie"] = 10,
        ["Temple Bee"] = 10,
        ["Wespe"] = 10,
        // 11. Vulture
        ["Bald Buzzard"] = 11,
        ["Bateleur"] = 11,
        ["Bronze Buzzard"] = 11,
        ["Chasm Buzzard"] = 11,
        ["Coldwind Bateleur"] = 11,
        ["Lammergeyer"] = 11,
        ["Nesting Buzzard"] = 11,
        ["Northern Vulture"] = 11,
        ["Pecking Condor"] = 11,
        ["Roc"] = 11,
        ["Screamer"] = 11,
        ["Sylphlands Condor"] = 11,
        ["Territorial Vulture"] = 11,
        ["Vuokho"] = 11,
        // 12. Mandragora
        ["Mandragora"] = 12,
        ["Mandragora Prince"] = 12,
        ["Marching Mandragora"] = 12,
        ["Ripe Rampager"] = 12,
        ["Tiny Mandragora"] = 12,
        // 13. Geshunpest
        ["Geshunpest"] = 13,
        // 14. Puk
        ["Peapuk"] = 14,
        ["Peryton"] = 14,
        ["Pteroc"] = 14,
        ["Puk Hatchling"] = 14,
        ["Scalepuk"] = 14,
        ["Territorial Puk"] = 14,
        // 15. Crab
        ["Bubbly Bernie"] = 15,
        ["Karkinos"] = 15,
        ["Moraby Stoneshell"] = 15,
        ["Sandshell"] = 15,
        ["Stoneshell"] = 15,
        ["Territorial Stoneshell"] = 15,
        ["Thickshell"] = 15,
        // 16. Mantis
        ["Cluster Mantis"] = 16,
        ["Death Claw"] = 16,
        ["Killer Mantis"] = 16,
        ["Mantis King"] = 16,
        ["Preying Mantis"] = 16,
        ["Rageclaw"] = 16,
        ["Scythe Mantis"] = 16,
        // 17. Slime
        ["Ichorous Ire"] = 17,
        ["Sewer Syrup"] = 17,
        ["Slipsand"] = 17,
        ["Territorial Slime"] = 17,
        // 18. Dullahan
        ["Bockman"] = 18,
        ["Daedalus"] = 18,
        ["Doctore"] = 18,
        // 19. Bat
        ["Attic Bat"] = 19,
        ["Barbastelle"] = 19,
        ["Black Bat"] = 19,
        ["Bloodsucker Bat"] = 19,
        ["Cave Bat"] = 19,
        ["Dusk Bat"] = 19,
        ["Lesser Kalong"] = 19,
        ["Sand Bat"] = 19,
        ["Sun Bat"] = 19,
        ["Temple Bat"] = 19,
        // 20. Flying Trap
        ["Leafbleed Roseling"] = 20,
        ["Matagaigai"] = 20,
        ["Roselet"] = 20,
        ["Roseling"] = 20,
        ["Territorial Roselet"] = 20,
        // 21. Ziz
        ["Axe Beak"] = 21,
        ["Gaunt Beak"] = 21,
        ["Gold Back"] = 21,
        ["Great Yellow Pelican"] = 21,
        ["Rothlyt Pelican"] = 21,
        ["Rudis Beak"] = 21,
        ["Sable Back"] = 21,
        ["Territorial Pelican"] = 21,
        ["Ziz"] = 21,
        ["Ziz Gorlin"] = 21,
        // 22. Sabotender
        ["Cactuar"] = 22,
        ["Cactuar Jack"] = 22,
        ["Cochineal Cactuar"] = 22,
        ["Sabotender"] = 22,
        ["Sabotender Bailaor"] = 22,
        ["Sabotender Bailarina"] = 22,
        ["Territorial Cactuar"] = 22,
        ["Territorial Flowertender"] = 22,
        ["Territorial Sabotender"] = 22,
        ["Uncommon Cactuar"] = 22,
        // 23. Golem
        ["Arkose Golem"] = 23,
        ["Basalt Golem"] = 23,
        ["Clay Golem"] = 23,
        ["Crater Golem"] = 23,
        ["Lunar Golem"] = 23,
        ["Marble Guardian"] = 23,
        ["Number 128"] = 23,
        ["Nunyunuwi"] = 23,
        ["Pumice Golem"] = 23,
        ["Sandstone Golem"] = 23,
        ["Slate Golem"] = 23,
        ["Stone Golem"] = 23,
        ["U'Ghamaro Golem"] = 23,
        // 24. Apkallu
        ["Apkallu"] = 24,
        ["Apkallu Caller"] = 24,
        ["Bloody Mary"] = 24,
        // 25. Adamantoise
        ["Adamantoise"] = 25,
        ["Alpha Tortoise"] = 25,
        ["Aspidochelone"] = 25,
        ["Bronze Tortoise"] = 25,
        ["Giant Tortoise"] = 25,
        ["Iron Tortoise"] = 25,
        ["Shredder"] = 25,
        // 26. Buffalo
        ["Aurochs"] = 26,
        ["Bonnacon"] = 26,
        ["Butting Buffalo"] = 26,
        ["Large Buffalo"] = 26,
        ["Menuis"] = 26,
        ["Rabid Aurochs"] = 26,
        ["Rutting Buffalo"] = 26,
        ["Thunderhooves"] = 26,
        ["Wounded Aurochs"] = 26,
        // 27. Uragnite
        ["Living Fossil"] = 27,
        ["Scaphite"] = 27,
        ["Sirius Uragnite"] = 27,
        ["Uragnite"] = 27,
        ["Zoredonite"] = 27,
        // 28. Worm
        ["Alpha Sandworm"] = 28,
        ["Giant Tunnel Worm"] = 28,
        ["Minhocao"] = 28,
        ["Sandworm"] = 28,
        ["Ulhuadshi"] = 28,
        // 29. Spriggan
        ["Spiteful"] = 29,
        ["Spriggan"] = 29,
        ["Spriggan Copper Carrier"] = 29,
        ["Spriggan Copper Copper"] = 29,
        ["Spriggan Graverobber"] = 29,
        ["Tamed Spriggan"] = 29,
        ["Territorial Spriggan"] = 29,
        // 30. Goobbue
        ["Alpha Goobbue"] = 30,
        ["Croque-Mitaine"] = 30,
        ["Elder Goobbue"] = 30,
        ["Goobbue"] = 30,
        ["Jolly Green"] = 30,
        ["Keeper of Halidom"] = 30,
        ["Longarm"] = 30,
        ["Mildewed Goobbue"] = 30,
        ["Mossless Goobbue"] = 30,
        ["Sweet Tooth Goobbue"] = 30,
        // 31. Gigantoad
        ["Bone Nix"] = 31,
        ["Cane Toad"] = 31,
        ["Croakadile"] = 31,
        ["Doomed Gigantoad"] = 31,
        ["Dreamtoad"] = 31,
        ["Gigantoad"] = 31,
        ["Giggling Gigantoad"] = 31,
        ["Laughing Toad"] = 31,
        ["Nix"] = 31,
        ["Riverbank Toad"] = 31,
        ["Rivertoad"] = 31,
        ["Sandtoad"] = 31,
        ["Territorial Gigantoad"] = 31,
        ["Toxic Toad"] = 31,
        ["Vodyanoi"] = 31,
        ["Winter Nix"] = 31,
        // 32. Colibri
        ["Colibri"] = 32,
        ["Myradrosh"] = 32,
        ["Painted Colibri"] = 32,
        // 33. Coeurl
        ["Alpha Coeurl"] = 33,
        ["Chopper"] = 33,
        ["Coeurl"] = 33,
        ["Deep Jungle Coeurl"] = 33,
        ["Golden Coeurl"] = 33,
        ["Jungle Coeurl"] = 33,
        ["Master Coeurl"] = 33,
        ["Ose"] = 33,
        ["Sekhmet"] = 33,
        ["Territorial Coeurl"] = 33,
        ["Territorial Young Coeurl"] = 33,
        ["Young Coeurl"] = 33,
        // 34. Raptor
        ["Alpha Anole"] = 34,
        ["Anole"] = 34,
        ["Grass Raptor"] = 34,
        ["Lindwurm"] = 34,
        ["Restless Raptor"] = 34,
        ["Territorial Raptor"] = 34,
        ["Velociraptor"] = 34,
        // 35. Drake
        ["Ashdrake"] = 35,
        ["Augmented Battle Drake"] = 35,
        ["Battle Drake"] = 35,
        ["Inferno Drake"] = 35,
        ["Lava Drake"] = 35,
        ["Sundrake"] = 35,
        ["Woken Drake"] = 35,
        ["Zahar'ak Battle Drake"] = 35,
        // 36. Treant
        ["Diseased Treant"] = 36,
        ["Dryad"] = 36,
        ["Great Oak"] = 36,
        ["Old-growth Treant"] = 36,
        ["Sylphlands Sentinel"] = 36,
        ["Treant"] = 36,
        ["Treant Sapling"] = 36,
        ["Wulgaru"] = 36,
        // 37. Antling
        ["Myrmidon Marshal"] = 37,
        ["Myrmidon Princess"] = 37,
        ["Territorial Antling Princess"] = 37,
        ["Territorial Antling Sentry"] = 37,
        // 38. Chimera
        ["Alpha Chimera"] = 38,
        ["Chimera"] = 38,
        ["Dhorme Chimera"] = 38,
        ["Garm"] = 38,
        ["Gorgimera"] = 38,
        // 39. Morbol
        ["Alpha Morbol"] = 39,
        ["Capricious Cassie"] = 39,
        ["Halitostroper"] = 39,
        ["Jaded Jody"] = 39,
        ["Leafbleed Morbol"] = 39,
        ["Morbol"] = 39,
        ["Stroper"] = 39,
        ["Territorial Morbol"] = 39,
        ["Toxic Tamlyn"] = 39,
        ["Voluptuous Vivian"] = 39,
        // 40. Ghost
        ["Betrayed Soul"] = 40,
        ["Bloated Bogy"] = 40,
        ["Bogy"] = 40,
        ["Corrupted Nymian"] = 40,
        ["Corrupted Nymian Curate"] = 40,
        ["Drifting Soul"] = 40,
        ["Dune Bogy"] = 40,
        ["Errant Soul"] = 40,
        ["Forsaken Soul"] = 40,
        ["Loam Bogy"] = 40,
        ["Revenant"] = 40,
        ["Spare Soul"] = 40,
        ["Spare Will"] = 40,
        ["Wicked Soul"] = 40,
        // 41. Salamander
        ["Axolotl"] = 41,
        ["Bark Eft"] = 41,
        ["Black Eft"] = 41,
        ["Eft"] = 41,
        ["Kurrea"] = 41,
        ["Lentic Mudpuppy"] = 41,
        ["Mudpuppy"] = 41,
        ["Salamander"] = 41,
        ["Territorial Salamander"] = 41,
        // 42. Cobra
        ["Alpha Cobra"] = 42,
        ["Coliseum Python"] = 42,
        ["Laideronnette"] = 42,
        ["Lake Cobra"] = 42,
        ["Territorial Cobra"] = 42,
        // 43. Hydra
        ["Five-headed Dragon"] = 43,
        ["Hydra"] = 43,
        ["Lampalagua"] = 43,
        // 44. Damselfly
        ["Gadfly"] = 44,
        ["Monarch Ogrefly"] = 44,
        // 45. Rotting Goobbue
        ["Decaying Gourmand"] = 45,
        // 46. Zu
        ["Alpha Zu"] = 46,
        ["Anzu Crone"] = 46,
        ["Cornu"] = 46,
        ["Zu"] = 46,
        // 47. Ice Golem
        ["Ice Commander"] = 47,
        ["Ice Soldier"] = 47,
        ["Wandil"] = 47,
        // 48. Karlabos
        ["Karlabos"] = 48,
        // 49. Rafflesia
        ["Rafflesia"] = 49,
        // 50. Behemoth
        ["King Behemoth"] = 50,
    };

    /// <summary>How many enemy names the table knows.</summary>
    public static int Count => ByName.Count;

    /// <summary>The beast this enemy counts as, or null when the table doesn't know the name.</summary>
    public static BstFamiliar? BeastFor(string? enemyName) =>
        !string.IsNullOrWhiteSpace(enemyName) && ByName.TryGetValue(enemyName.Trim(), out var no)
            ? BstFamiliars.ByBestiaryNo((uint)no)
            : null;

    /// <summary>Every enemy name for one beast (for the Bestiary list).</summary>
    public static IEnumerable<string> NamesFor(int bestiaryNo)
    {
        foreach (var (name, no) in ByName)
            if (no == bestiaryNo)
                yield return name;
    }
}
