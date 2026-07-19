namespace Daedalus.Config;

/// <summary>
/// Controls when to prioritize raising dead party members.
/// </summary>
public enum RaiseExecutionMode
{
    /// <summary>
    /// Raise first - Prioritize raising over most other actions.
    /// Good for progression where getting people up quickly matters.
    /// </summary>
    RaiseFirst,

    /// <summary>
    /// Balanced - Raise during weave windows, but don't interrupt critical healing.
    /// Default option that balances raising with party safety.
    /// </summary>
    Balanced,

    /// <summary>
    /// Heal first - Only raise when party HP is stable.
    /// Conservative option for when incoming damage is heavy.
    /// </summary>
    HealFirst
}

/// <summary>
/// Configuration for resurrection settings.
/// </summary>
public sealed class ResurrectionConfig
{
    /// <summary>
    /// Enable automatic resurrection of dead party members.
    /// </summary>
    public bool EnableRaise { get; set; } = true;

    /// <summary>
    /// Controls when to prioritize raising vs other actions.
    /// </summary>
    public RaiseExecutionMode RaiseMode { get; set; } = RaiseExecutionMode.Balanced;

    /// <summary>
    /// Allow hardcasting Raise when Swiftcast is on cooldown.
    /// Hardcast Raise takes 8 seconds and should only be used when safe.
    /// </summary>
    public bool AllowHardcastRaise { get; set; } = false;

    /// <summary>
    /// Minimum MP percentage required before attempting to raise (0.0 - 1.0).
    /// Default 0.25 means 25% MP minimum (2400 MP for Raise + buffer).
    /// </summary>
    public float RaiseMpThreshold { get; set; } = 0.25f;

    /// <summary>
    /// In alliance raids, also raise dead players OUTSIDE your own 8-man party (raise spells
    /// can target any alliance member; the finder previously only scanned the party list, so
    /// a corpse in another alliance party was invisible — field report 2026-07-19). Own party
    /// always takes precedence. Only ever applies inside instanced duties.
    /// </summary>
    public bool RaiseAllianceMembers { get; set; } = true;
}
