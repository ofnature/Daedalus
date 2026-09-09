namespace Daedalus.Config.DPS;

/// <summary>
/// Beastmaster (Artemis) configuration.
/// <para>
/// Deliberately small. Beastmaster is a limited job with a Lv50 cap and no role actions, so there
/// is no burst window, no party coordination and no mitigation to configure — see
/// <see cref="Daedalus.Rotation.Common.Helpers.LimitedJobContentPolicy"/>.
/// </para>
/// </summary>
public sealed class BeastmasterConfig
{
    /// <summary>
    /// Fire the four instinctual skills (Avalanche / Mistral / Spinning / Gale Axe).
    /// They spend the whole TP gauge and run on their own 5s recast, independent of the GCD.
    /// </summary>
    public bool EnableInstinctualSkills { get; set; } = true;

    /// <summary>
    /// Prefer the affinity that completes an intentional combo (Sunstrider / Moonstalker) over
    /// simply firing whichever instinctual skill is off cooldown.
    /// </summary>
    public bool PreferIntentionalCombos { get; set; } = true;

    /// <summary>Order the familiar's instinctual skill with Trick when it is ready.</summary>
    public bool EnableTrick { get; set; } = true;

    /// <summary>
    /// Fire Parting Blow. Off by default: it deals 1,000 potency in 8y but the familiar RETREATS
    /// afterwards, which ends Trick until the player summons again. That is a trade the player
    /// should opt into, not one the rotation should make unprompted.
    /// </summary>
    public bool EnablePartingBlow { get; set; }
}
