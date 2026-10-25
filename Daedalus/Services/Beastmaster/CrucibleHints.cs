namespace Daedalus.Services.Beastmaster;

/// <summary>
/// What the boss engine knows about the next hit on the beastmaster, for Artemis on a Crucible board. Static on
/// purpose, like <see cref="CrucibleTracker.Current"/>: rotations read a COPY of the configuration, and a transient
/// value kept on an instance they never see is how flags have frozen before.
/// </summary>
public static class CrucibleHints
{
    /// <summary>
    /// Seconds until the next predicted tankbuster on the beastmaster; <see cref="float.MaxValue"/> when none is known.
    /// Minerva ("Minerva.Hints.SecondsUntilTankbuster", NaN when none) or BossMod (the earlier of its two buster
    /// forecasts), whichever engine is in charge. Written by the plugin each frame on a board.
    /// </summary>
    public static float SecondsUntilTankbuster { get; set; } = float.MaxValue;
}
