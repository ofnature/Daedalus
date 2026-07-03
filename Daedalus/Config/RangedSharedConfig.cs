using System;

namespace Daedalus.Config;

/// <summary>
/// Shared role-action configuration for ranged physical DPS jobs (BRD/MCH/DNC).
/// </summary>
public sealed class RangedSharedConfig
{
    /// <summary>Master toggle for Second Wind across all physical ranged DPS.</summary>
    public bool EnableSecondWind { get; set; } = true;

    /// <summary>HP percentage threshold to trigger Second Wind (0.0 to 1.0). Default 0.50.</summary>
    private float _secondWindHpThreshold = 0.50f;
    public float SecondWindHpThreshold
    {
        get => _secondWindHpThreshold;
        set => _secondWindHpThreshold = Math.Clamp(value, 0f, 1f);
    }

    /// <summary>
    /// Master toggle for the party mitigation cooldown (Shield Samba / Troubadour / Tactician).
    /// The three are the same non-stacking buff family — the push is skipped while any of them
    /// is already on the player.
    /// </summary>
    public bool EnablePartyMitigation { get; set; } = true;

    /// <summary>Engaged-enemy count that counts as a "big pull" worth mitigating. Default 3.</summary>
    private int _partyMitigationMinTargets = 3;
    public int PartyMitigationMinTargets
    {
        get => _partyMitigationMinTargets;
        set => _partyMitigationMinTargets = Math.Clamp(value, 1, 8);
    }

    /// <summary>
    /// Whether to use Head Graze for enemy cast interrupts.
    /// </summary>
    public bool EnableHeadGraze { get; set; } = true;

    /// <summary>
    /// Auto-cast Peloton (party movement-speed buff) while out of combat and moving, so the travel
    /// buff stays up between pulls. No effect in combat (the game cancels it on engage).
    /// </summary>
    public bool EnablePeloton { get; set; } = true;
}
