using System.Collections.Generic;
using Daedalus.Models.Gear;

namespace Daedalus.Data;

/// <summary>
/// Which stats matter for a job — drives the aggregate panel's dimming and (phase 5) the sweep's
/// candidate set. Role-derived with per-job speed-stat selection; the full per-job priority
/// ORDER (Balance guidance) lands in phase 4's BalancePriorities.
/// </summary>
public static class GearStatRelevance
{
    public static IReadOnlySet<uint> For(uint jobId)
    {
        var set = new HashSet<uint>
        {
            GearStatIds.CriticalHit,
            GearStatIds.Determination,
            GearStatIds.DirectHit,
        };

        if (JobRegistry.IsTank(jobId))
        {
            set.Add(GearStatIds.Strength);
            set.Add(GearStatIds.Vitality);
            set.Add(GearStatIds.SkillSpeed);
            set.Add(GearStatIds.Tenacity);
        }
        else if (JobRegistry.IsHealer(jobId))
        {
            set.Add(GearStatIds.Mind);
            set.Add(GearStatIds.SpellSpeed);
            set.Add(GearStatIds.Piety);
        }
        else if (JobRegistry.IsCasterDps(jobId))
        {
            set.Add(GearStatIds.Intelligence);
            set.Add(GearStatIds.SpellSpeed);
        }
        else if (JobRegistry.IsRangedPhysicalDps(jobId))
        {
            set.Add(GearStatIds.Dexterity);
            set.Add(GearStatIds.SkillSpeed);
        }
        else if (JobRegistry.IsMeleeDps(jobId))
        {
            set.Add(UsesDexterity(jobId) ? GearStatIds.Dexterity : GearStatIds.Strength);
            set.Add(GearStatIds.SkillSpeed);
        }

        return set;
    }

    /// <summary>NIN/VPR (and their base classes) are the DEX melee.</summary>
    private static bool UsesDexterity(uint jobId) => jobId is
        JobRegistry.Ninja or JobRegistry.Rogue or JobRegistry.Viper;
}
