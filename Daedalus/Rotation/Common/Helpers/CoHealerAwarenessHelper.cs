using Dalamud.Game.ClientState.Objects.Types;
using Daedalus.Config;
using Daedalus.Services.Healing;

namespace Daedalus.Rotation.Common.Helpers;

/// <summary>
/// Shared co-healer awareness heuristic used by all four healer rotations.
/// Skips a single-target heal when a co-healer's in-flight cast would already
/// cover most of the target's missing HP, avoiding double-healing waste.
/// </summary>
public static class CoHealerAwarenessHelper
{
    /// <summary>
    /// Returns true if a co-healer has a pending heal on the given target that
    /// covers at least <paramref name="coverageThreshold"/> of the missing HP —
    /// meaning the current healer should skip their own cast.
    /// </summary>
    /// <param name="enabled">Master toggle (<c>HealingConfig.EnableCoHealerAwareness</c>).</param>
    /// <param name="service">Shared co-healer detection service (may be null).</param>
    /// <param name="target">Heal target; must be non-null.</param>
    /// <param name="coverageThreshold">Fraction of missing HP the co-healer must cover (<c>HealingConfig.CoHealerPendingHealThreshold</c>).</param>
    public static bool CoHealerWillCover(
        bool enabled,
        ICoHealerDetectionService? service,
        IBattleChara target,
        float coverageThreshold)
    {
        if (!enabled) return false;
        if (service is null || !service.HasCoHealer) return false;
        if (target is null) return false;

        var pending = service.CoHealerPendingHeals;
        if (!pending.TryGetValue(target.EntityId, out var pendingHeal) || pendingHeal <= 0)
            return false;

        var missingHp = target.MaxHp - target.CurrentHp;
        if (missingHp <= 0) return true;

        var coverage = (float)pendingHeal / missingHp;
        return coverage >= coverageThreshold;
    }

    /// <summary>
    /// Resolves whether non-critical GCD heals should be deferred (left to oGCDs + the main healer),
    /// combining the per-job "GCD Heals Only When Solo Healer" toggle with this toon's healer role.
    /// A <see cref="HealerRoleAssignment.Main"/> healer never defers (it owns GCD healing), so two
    /// healers don't both wait on each other. Co/Auto defer when the master toggle is on and a co-healer
    /// is present. Callers still apply their own critical-HP floor (GCD-emergency threshold) on top.
    /// </summary>
    /// <param name="role">This toon's role (<c>HealingConfig.HealerRole</c>).</param>
    /// <param name="restrictEnabled">Per-job master toggle (e.g. <c>RestrictGcdHealsWithCoHealer</c>).</param>
    /// <param name="hasCoHealer">Whether another healer is present in the party.</param>
    public static bool ShouldDeferGcdHeals(HealerRoleAssignment role, bool restrictEnabled, bool hasCoHealer)
    {
        if (role == HealerRoleAssignment.Main)
            return false;

        return restrictEnabled && hasCoHealer;
    }
}
