namespace Daedalus.Config;

/// <summary>
/// Shared role-action configuration for ranged physical DPS jobs (BRD/MCH/DNC).
/// </summary>
public sealed class RangedSharedConfig
{
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
