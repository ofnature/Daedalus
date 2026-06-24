using Olympus.Config;

namespace Olympus.Rotation.Common.Helpers;

/// <summary>
/// Resolves effective AoE heal minimum target counts. When auto-adjust is enabled,
/// dungeon/trust parties (≤4) use 2 targets; raid parties (≥8) use 3.
/// </summary>
public static class AoEHealTargetHelper
{
    public const int DungeonPartySizeMax = 4;
    public const int RaidPartySizeMin = 8;
    public const int DungeonMinTargets = 2;
    public const int RaidMinTargets = 3;

    /// <summary>
    /// Returns the minimum injured/allied count required before AoE heals fire.
    /// </summary>
    public static int GetEffectiveMinTargets(HealingConfig healing, int partySize)
    {
        if (!healing.AutoAdjustAoEHealMinTargetsByPartySize)
            return healing.AoEHealMinTargets;

        return partySize switch
        {
            <= DungeonPartySizeMax => DungeonMinTargets,
            >= RaidPartySizeMin => RaidMinTargets,
            _ => healing.AoEHealMinTargets,
        };
    }
}
