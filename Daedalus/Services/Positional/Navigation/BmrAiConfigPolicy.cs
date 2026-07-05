using Daedalus.Data;

namespace Daedalus.Services.Positional.Navigation;

/// <summary>
/// Pure decision logic for auto-managing BossMod Reborn's AI movement config by role: how far each role
/// stands from the target, and which positional to feed BMR for the next melee GCD. Extracted so it can
/// be unit-tested without the BMR IPC.
/// </summary>
public static class BmrAiConfigPolicy
{
    /// <summary>BMR's default melee stand distance (hug the hitbox).</summary>
    public const float MeleeStandDistance = 2.6f;

    /// <summary>Healers, ranged physical, and caster DPS — the jobs that should hold at range.</summary>
    public static bool IsBacklineJob(uint jobId) =>
        JobRegistry.IsHealer(jobId)
        || JobRegistry.IsRangedPhysicalDps(jobId)
        || JobRegistry.IsCasterDps(jobId);

    /// <summary>Max distance from the target by role: backline holds at <paramref name="rangedDistance"/>; melee/tank hug.</summary>
    public static float ResolveMaxDistance(uint jobId, float rangedDistance) =>
        IsBacklineJob(jobId) ? rangedDistance : MeleeStandDistance;

    /// <summary>
    /// Maps Daedalus's next required positional to BMR's <c>Positional</c> enum name. Backline jobs and
    /// "no requirement" → <c>Any</c> (don't force a positional). Beats a static single positional because
    /// it follows the rotation's actual next GCD (RPR Gibbet↔Gallows, MNK forms, NIN). When boundary
    /// camping is live, melee also gets <c>Any</c>: Daedalus owns the standing angle via positional arcs,
    /// BMR only keeps range and dodges — feeding it a positional would have it fight us over the angle.
    /// </summary>
    public static string ResolveDesiredPositional(uint jobId, PositionalType? requiredPositional, bool boundaryCampingActive)
    {
        if (IsBacklineJob(jobId) || boundaryCampingActive)
            return "Any";

        return requiredPositional switch
        {
            PositionalType.Rear => "Rear",
            PositionalType.Flank => "Flank",
            PositionalType.Front => "Front",
            _ => "Any",
        };
    }
}
