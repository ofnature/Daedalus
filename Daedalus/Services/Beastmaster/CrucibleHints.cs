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

    /// <summary>
    /// Seconds until the next raidwide that Snarl's Cover can take; <see cref="float.MaxValue"/> when none is known or
    /// the next one is uncoverable (<c>ArtemisCooldownRules.UncoverableRaidwides</c>). Minerva
    /// ("Minerva.Hints.SecondsUntilRaidwide" + "NextRaidwideActionId") or BossMod's raidwide forecast.
    /// </summary>
    public static float SecondsUntilRaidwide { get; set; } = float.MaxValue;

    /// <summary>
    /// Enemies casting something that should be interrupted, from Minerva's "Minerva.Hints.TargetsToInterrupt"
    /// (Bone Knight Ossify, Succubus Fanaticism, Younger Tablitaur Rallying Cheer, Pullet Caustic Vomit, Biloko Natural
    /// Nurture, Progenitrix Massive Explosion, Cyclops Camaraderie, Sahagin Dreadwash, Administrator Confound, slime
    /// Rupture). Empty when none, off a board, or with another boss engine.
    /// </summary>
    public static System.Collections.Generic.IReadOnlyList<ulong> TargetsToInterrupt { get; set; } = [];
}
