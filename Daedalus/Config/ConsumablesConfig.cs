namespace Daedalus.Config;

/// <summary>
/// Configuration for combat consumable automation. Currently scoped to tinctures.
/// </summary>
public sealed class ConsumablesConfig
{
    /// <summary>
    /// Master toggle. When true, Daedalus will attempt to use combat tinctures
    /// in high-end content. Defaults OFF -- pots cost real gil and players
    /// must opt in deliberately.
    /// </summary>
    public bool EnableAutoTincture { get; set; } = false;

    /// <summary>
    /// When true, fires a one-shot per-fight chat warning if the master toggle
    /// is on but no matching tincture is in inventory.
    /// </summary>
    public bool WarnOnEmptyInventory { get; set; } = true;

    /// <summary>
    /// Allow tinctures outside high-end zones (legacy Coil clears, unsynced farm...). Default
    /// OFF — the high-end-only gate is the gil guard, and it is the reason auto-pot looks
    /// "broken" in normal content.
    /// </summary>
    public bool UseOutsideHighEnd { get; set; } = false;

    /// <summary>
    /// Phoenix Down safety net: when EVERY healer in the party is dead, hardcast item 4570
    /// (8s cast, 15y) on the nearest dead healer. Default OFF until field-validated —
    /// see docs/lan-ipc-plan.md Phase 3. Tanks hold it unless they are the last one alive.
    /// </summary>
    public bool EnablePhoenixDown { get; set; } = false;

    /// <summary>
    /// Cascading ether use: when MP drops below <see cref="EtherMpThreshold"/>, drink the
    /// strongest ether in the bag, stepping down the ladder as the good ones run out. Built for
    /// raise-heavy field content (Occult Crescent), where repeated raises outrun Lucid Dreaming.
    /// Default OFF — it spends real consumables, so it stays opt-in like the tincture gate.
    /// </summary>
    public bool EnableEthers { get; set; } = false;

    /// <summary>
    /// MP fraction below which an ether is drunk. Well under the Lucid Dreaming threshold
    /// (0.70) so the free cooldown always goes first and an item is only spent when MP keeps
    /// falling anyway.
    /// </summary>
    private float _etherMpThreshold = 0.35f;
    public float EtherMpThreshold
    {
        get => _etherMpThreshold;
        set => _etherMpThreshold = System.Math.Clamp(value, 0f, 1f);
    }

    /// <summary>Warn in chat when the ether stock thins out (see EtherPolicy.RunningLowCount).</summary>
    public bool WarnOnLowEthers { get; set; } = true;
}
