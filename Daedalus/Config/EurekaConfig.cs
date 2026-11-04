namespace Daedalus.Config;

/// <summary>How the Magia Board is kept turned (the game's own /magiaauto does the turning).</summary>
public enum EurekaMagiaMode
{
    /// <summary>Daedalus leaves the board alone.</summary>
    Off,
    /// <summary>/magiaauto attack: the element that beats the current target.</summary>
    Attack,
    /// <summary>/magiaauto defense: the target's own element (less damage taken from it).</summary>
    Defense,
    /// <summary>By job: defense on a tank, attack on everyone else.</summary>
    TankDefense,
}

/// <summary>Eureka settings: Logos Actions, the Magia Board, and the elemental level guard.</summary>
public sealed class EurekaConfig
{
    /// <summary>
    /// Logos Actions, run by the same duty-action layer as Bozja's Lost Actions (same shape of settings:
    /// <see cref="BozjaConfig.EnableLostActions"/> is the Logos master toggle here).
    /// </summary>
    public BozjaConfig Logos { get; set; } = new();

    public EurekaMagiaMode MagiaMode { get; set; } = EurekaMagiaMode.TankDefense;

    /// <summary>
    /// Farm mode skips enemies more than this many elemental levels above you (they hit much harder past +1, and
    /// give no EXP past +8). 0 = only your level or below.
    /// </summary>
    public int MaxLevelsAbove { get; set; } = 3;
}
