using Daedalus.Config;
using Daedalus.Services.Prediction;

namespace Daedalus.Rotation.Common.Helpers;

/// <summary>
/// Shared helper for raising regen-style HoT apply thresholds when the
/// party is taking sustained damage. Used by WHM Regen, AST Aspected
/// Benefic, and any future healer handler that wants "apply the HoT
/// earlier than usual because big damage is landing".
/// </summary>
public static class DynamicRegenThresholdHelper
{
    /// <summary>
    /// Returns the effective HP threshold for applying a regen-style HoT.
    /// When <see cref="HealingConfig.EnableDynamicRegenThreshold"/> is on
    /// and the party is taking at least
    /// <see cref="HealingConfig.RegenHighDamageDpsThreshold"/> DPS, the
    /// threshold is raised to
    /// <see cref="HealingConfig.RegenHighDamageThreshold"/> so the HoT is
    /// ticking before the next hit lands. Otherwise the base threshold
    /// (caller-supplied per-action default) is returned unchanged.
    /// </summary>
    public static float GetEffectiveThreshold(
        HealingConfig healing,
        IDamageIntakeService damageIntakeService,
        float baseThreshold)
        => GetEffectiveThreshold(healing, damageIntakeService, baseThreshold, partyMaxHpTotal: 0f);

    /// <summary>
    /// A party losing at least this share of its combined max HP every second is taking heavy damage. The flat
    /// <see cref="HealingConfig.RegenHighDamageDpsThreshold"/> (300 DPS) alone is crossed by a boss's auto-attacks on
    /// the tank at any level past the first few, so the raised threshold (95%) applied the whole fight and the regen
    /// went out on a 90% tank every 15-20 s — a quarter of an Astrologian's GCDs (Saar, Forgiven Dissonance, 2026-10-06).
    /// </summary>
    public const float HeavyDamageShareOfPartyHpPerSecond = 0.05f;

    /// <inheritdoc cref="GetEffectiveThreshold(HealingConfig, IDamageIntakeService, float)"/>
    /// <param name="partyMaxHpTotal">The party's combined max HP; 0 to use the flat floor alone.</param>
    public static float GetEffectiveThreshold(
        HealingConfig healing,
        IDamageIntakeService damageIntakeService,
        float baseThreshold,
        float partyMaxHpTotal)
    {
        if (!healing.EnableDynamicRegenThreshold)
            return baseThreshold;

        var partyDamageRate = damageIntakeService.GetPartyDamageRate(3f);
        if (!IsHeavyDamage(partyDamageRate, healing.RegenHighDamageDpsThreshold, partyMaxHpTotal))
            return baseThreshold;

        // Only raise — never lower — the caller's base threshold.
        return baseThreshold > healing.RegenHighDamageThreshold
            ? baseThreshold
            : healing.RegenHighDamageThreshold;
    }

    /// <summary>Heavy damage: over the flat floor AND over the share of the party's HP. Pure, for tests.</summary>
    internal static bool IsHeavyDamage(float partyDamageRate, float flatFloor, float partyMaxHpTotal)
        => partyDamageRate >= System.Math.Max(flatFloor, partyMaxHpTotal * HeavyDamageShareOfPartyHpPerSecond);
}
