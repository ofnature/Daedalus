using System;

namespace Daedalus.Config.DPS;

/// <summary>
/// The role Proteus plays as Blue Mage. BLU has no in-game role — this dropdown IS the role:
/// it selects which modules run AND which archetype Aetheric Mimicry copies.
/// </summary>
public enum BluRole
{
    /// <summary>DPS loadout: Sonic Boom filler, mimic a DPS (+20% crit/DH rate).</summary>
    Dps,

    /// <summary>Tank loadout: Mighty Guard stance, Diamondback, Goblin Punch filler, mimic a tank.</summary>
    Tank,

    /// <summary>Healer loadout: White Wind thresholds, mimic a healer (+20% healing potency).</summary>
    Healer,
}

/// <summary>
/// Blue Mage (Proteus) configuration. BLU availability is learned+slotted (not level), so there
/// are no level-gated toggles here — per-spell enables plus the role selector.
/// </summary>
[Serializable]
public sealed class BlueMageConfig
{
    /// <summary>The role dropdown — drives module selection and Aetheric Mimicry archetype.</summary>
    public BluRole Role { get; set; } = BluRole.Dps;

    /// <summary>Auto-apply Aetheric Mimicry matching <see cref="Role"/> (Mimicry Helper parity).</summary>
    public bool EnableMimicry { get; set; } = true;

    /// <summary>Maintain Mighty Guard while Role = Tank (dropped when leaving tank role).</summary>
    public bool EnableMightyGuard { get; set; } = true;

    /// <summary>Diamondback below the HP threshold while Role = Tank.</summary>
    public bool EnableDiamondback { get; set; } = true;

    private int _diamondbackHpPercent = 50;
    /// <summary>HP% at/below which Diamondback fires (tank role).</summary>
    public int DiamondbackHpPercent
    {
        get => _diamondbackHpPercent;
        set => _diamondbackHpPercent = Math.Clamp(value, 10, 90);
    }

    /// <summary>White Wind when injured allies are in its 15y radius (healer role; self-save any role).</summary>
    public bool EnableWhiteWind { get; set; } = true;

    private int _whiteWindHpPercent = 60;
    /// <summary>HP% at/below which White Wind is considered.</summary>
    public int WhiteWindHpPercent
    {
        get => _whiteWindHpPercent;
        set => _whiteWindHpPercent = Math.Clamp(value, 20, 90);
    }

    /// <summary>Song of Torment DoT maintenance (30s Bleeding).</summary>
    public bool EnableSongOfTorment { get; set; } = true;

    /// <summary>The Rose of Destruction on cooldown (30s ST nuke).</summary>
    public bool EnableRoseOfDestruction { get; set; } = true;

    /// <summary>Plaincracker AoE rotation (6y self AoE — count is player-anchored).</summary>
    public bool EnableAoERotation { get; set; } = true;

    private int _aoeMinTargets = 3;
    /// <summary>Enemies within Plaincracker's 6y self-radius before AoE replaces the ST filler.</summary>
    public int AoEMinTargets
    {
        get => _aoeMinTargets;
        set => _aoeMinTargets = Math.Clamp(value, 2, 8);
    }
}
