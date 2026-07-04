using System.Collections.Generic;

namespace Daedalus.Services.Analytics;

/// <summary>
/// Damage-over-time status ids → relative tick-potency weights, for splitting the game's
/// aggregated DoT ticks (one tick per target carrying every DoT's damage) across their casters.
/// Only the RATIO between weights matters — values are approximate per-tick potencies, and
/// Trust avatars reuse the same status ids as player jobs. Status ids verified against the
/// XIVAPI Status sheet 2026-07-04.
/// </summary>
internal static class DotStatusWeights
{
    private static readonly Dictionary<uint, int> Weights = new()
    {
        // WHM
        [143] = 30,   // Aero
        [144] = 50,   // Aero II
        [1871] = 75,  // Dia

        // SCH
        [179] = 20,   // Bio
        [189] = 40,   // Bio II
        [1895] = 75,  // Biolysis

        // AST
        [838] = 40,   // Combust
        [843] = 50,   // Combust II
        [1881] = 70,  // Combust III

        // SGE
        [2614] = 40,  // Eukrasian Dosis
        [2615] = 60,  // Eukrasian Dosis II
        [2616] = 75,  // Eukrasian Dosis III

        // BRD
        [124] = 15,   // Venomous Bite
        [129] = 15,   // Windbite
        [1200] = 20,  // Caustic Bite
        [1201] = 25,  // Stormbite

        // BLM
        [161] = 35,   // Thunder
        [162] = 40,   // Thunder II
        [163] = 50,   // Thunder III
        [1210] = 35,  // Thunder IV
        [3871] = 60,  // High Thunder
        [3872] = 40,  // High Thunder II

        // DRG
        [118] = 40,   // Chaos Thrust
        [2719] = 45,  // Chaotic Spring

        // SAM
        [1228] = 50,  // Higanbana

        // MNK
        [246] = 45,   // Demolish

        // GNB
        [1837] = 60,  // Sonic Break
        [1838] = 30,  // Bow Shock

        // MCH
        [1866] = 50,  // Bioblaster
    };

    public static bool TryGetWeight(uint statusId, out int weight) => Weights.TryGetValue(statusId, out weight);
}
