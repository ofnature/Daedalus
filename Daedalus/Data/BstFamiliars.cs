using System;
using System.Collections.Generic;

namespace Daedalus.Data;

/// <summary>
/// One beast from the Master's Bestiary.
/// </summary>
/// <param name="BestiaryNo">The Bestiary number, 1-50. It is also the beast's <b>XBMPet RowId</b> — the
/// value <c>ActionManager.BeastmasterPets</c> holds per Battlehorn (verified in game 2026-09-29, see
/// docs/battlehorn-assignment.md §2-3).</param>
/// <param name="Name">English name as the guides and the Bestiary write it.</param>
/// <param name="Classification">Decides the Borrow / Beast Mode Kinship.</param>
/// <param name="TrickAffinity">The colour of the beast's Trick — what the beastmaster's axe must answer.</param>
/// <param name="CaptureLevel">Lowest level you can capture it at.</param>
public sealed record BstFamiliar(
    int BestiaryNo,
    string Name,
    BeastClassification Classification,
    InstinctAffinity TrickAffinity,
    int CaptureLevel);

/// <summary>
/// All 50 beasts with their Trick affinity. Transcribed from the Icy Veins Beastmaster guide's
/// familiar and beast tables (2026-09-17 / 2026-09-10); summarised with capture locations in
/// docs/beastmaster-rotation-and-pets.md §4. This is the one fact the Artemis rotation could not read
/// from the client: which Heart the familiar's Trick grants, and so which axe completes the combo.
/// </summary>
public static class BstFamiliars
{
    public static readonly IReadOnlyList<BstFamiliar> All =
    [
        new(1, "Cu Sith", BeastClassification.Beastkin, InstinctAffinity.Rampant, 1),
        new(2, "Squirrel", BeastClassification.Beastkin, InstinctAffinity.Rampant, 2),
        new(3, "Lamb", BeastClassification.Beastkin, InstinctAffinity.Rampant, 2),
        new(4, "Pugil", BeastClassification.Wavekin, InstinctAffinity.Durant, 6),
        new(5, "Opo-opo", BeastClassification.Beastkin, InstinctAffinity.Rampant, 4),
        new(6, "Dodo", BeastClassification.Cloudkin, InstinctAffinity.Eldritch, 7),
        new(7, "Coblyn", BeastClassification.Soulkin, InstinctAffinity.Eldritch, 6),
        new(8, "Diremite", BeastClassification.Vilekin, InstinctAffinity.Rampant, 10),
        new(9, "Megalocrab", BeastClassification.Wavekin, InstinctAffinity.Durant, 10),
        new(10, "Wespe", BeastClassification.Vilekin, InstinctAffinity.Volant, 10),
        new(11, "Vulture", BeastClassification.Cloudkin, InstinctAffinity.Volant, 6),
        new(12, "Mandragora", BeastClassification.Seedkin, InstinctAffinity.Rampant, 5),
        new(13, "Geshunpest", BeastClassification.Ashkin, InstinctAffinity.Eldritch, 14),
        new(14, "Puk", BeastClassification.Scalekin, InstinctAffinity.Rampant, 4),
        new(15, "Crab", BeastClassification.Wavekin, InstinctAffinity.Durant, 13),
        new(16, "Mantis", BeastClassification.Vilekin, InstinctAffinity.Durant, 16),
        new(17, "Slime", BeastClassification.Ashkin, InstinctAffinity.Eldritch, 17),
        new(18, "Dullahan", BeastClassification.Soulkin, InstinctAffinity.Durant, 20),
        new(19, "Bat", BeastClassification.Cloudkin, InstinctAffinity.Volant, 7),
        new(20, "Flying Trap", BeastClassification.Seedkin, InstinctAffinity.Volant, 10),
        new(21, "Ziz", BeastClassification.Scalekin, InstinctAffinity.Durant, 16),
        new(22, "Sabotender", BeastClassification.Seedkin, InstinctAffinity.Rampant, 3),
        new(23, "Golem", BeastClassification.Soulkin, InstinctAffinity.Eldritch, 25),
        new(24, "Apkallu", BeastClassification.Cloudkin, InstinctAffinity.Durant, 30),
        new(25, "Adamantoise", BeastClassification.Scalekin, InstinctAffinity.Eldritch, 12),
        new(26, "Buffalo", BeastClassification.Beastkin, InstinctAffinity.Rampant, 9),
        new(27, "Uragnite", BeastClassification.Wavekin, InstinctAffinity.Durant, 14),
        new(28, "Worm", BeastClassification.Vilekin, InstinctAffinity.Eldritch, 31),
        new(29, "Spriggan", BeastClassification.Soulkin, InstinctAffinity.Rampant, 7),
        new(30, "Goobbue", BeastClassification.Beastkin, InstinctAffinity.Rampant, 12),
        new(31, "Gigantoad", BeastClassification.Wavekin, InstinctAffinity.Eldritch, 4),
        new(32, "Colibri", BeastClassification.Cloudkin, InstinctAffinity.Volant, 33),
        new(33, "Coeurl", BeastClassification.Beastkin, InstinctAffinity.Eldritch, 24),
        new(34, "Raptor", BeastClassification.Scalekin, InstinctAffinity.Durant, 3),
        new(35, "Drake", BeastClassification.Scalekin, InstinctAffinity.Rampant, 32),
        new(36, "Treant", BeastClassification.Seedkin, InstinctAffinity.Eldritch, 12),
        new(37, "Antling", BeastClassification.Vilekin, InstinctAffinity.Rampant, 38),
        new(38, "Chimera", BeastClassification.Beastkin, InstinctAffinity.Rampant, 38),
        new(39, "Morbol", BeastClassification.Seedkin, InstinctAffinity.Rampant, 31),
        new(40, "Ghost", BeastClassification.Ashkin, InstinctAffinity.Volant, 7),
        new(41, "Salamander", BeastClassification.Wavekin, InstinctAffinity.Durant, 6),
        new(42, "Cobra", BeastClassification.Scalekin, InstinctAffinity.Durant, 20),
        new(43, "Hydra", BeastClassification.Scalekin, InstinctAffinity.Durant, 50),
        new(44, "Damselfly", BeastClassification.Vilekin, InstinctAffinity.Volant, 50),
        new(45, "Rotting Goobbue", BeastClassification.Ashkin, InstinctAffinity.Eldritch, 50),
        new(46, "Zu", BeastClassification.Cloudkin, InstinctAffinity.Volant, 50),
        new(47, "Ice Golem", BeastClassification.Soulkin, InstinctAffinity.Durant, 50),
        new(48, "Karlabos", BeastClassification.Wavekin, InstinctAffinity.Durant, 50),
        new(49, "Rafflesia", BeastClassification.Seedkin, InstinctAffinity.Eldritch, 50),
        new(50, "Behemoth", BeastClassification.Beastkin, InstinctAffinity.Eldritch, 50),
    ];

    /// <summary>The beast in a Battlehorn slot, by its XBMPet RowId (= Bestiary number); null for 0 or unknown.</summary>
    public static BstFamiliar? ByBestiaryNo(uint bestiaryNo)
        => bestiaryNo is >= 1 and <= 50 ? All[(int)bestiaryNo - 1] : null;

    /// <summary>
    /// The beast whose name matches a familiar's in-world name, ignoring case ("squirrel" and
    /// "Squirrel" alike); null when nothing matches — a non-English client, or a name spelled
    /// differently in game.
    /// </summary>
    public static BstFamiliar? ByName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        var trimmed = name.Trim();
        foreach (var beast in All)
        {
            if (string.Equals(beast.Name, trimmed, StringComparison.OrdinalIgnoreCase))
                return beast;
        }

        return null;
    }
}
