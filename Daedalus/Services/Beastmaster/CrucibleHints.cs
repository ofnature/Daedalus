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
    /// The board's degree (0 Standard, 1-3 First-Third Degree) while on a board, else 0: the layout agent's choice,
    /// which the game keeps. Written by the plugin each frame.
    /// </summary>
    public static int Degree { get; set; }

    /// <summary>
    /// The beastmaster holds gear that hits back when struck (Briar Armor): on a degree Artemis then has the beastmaster
    /// take the hits (user, 2026-11-06). Written by the autopilot from the board HUD; false off a board.
    /// </summary>
    public static bool CounterArmorHeld { get; set; }

    /// <summary>
    /// The player paused the Crucible automation (autopilot, walker, loop, horn setter) — to do something by hand, e.g.
    /// record using a potion or feeding (user, 2026-10-27). Combat is untouched. Not saved: a reload starts unpaused.
    /// </summary>
    public static bool AutomationPaused { get; set; }

    /// <summary>
    /// Seconds until the next raidwide that Snarl's Cover can take; <see cref="float.MaxValue"/> when none is known or
    /// the next one is uncoverable (<c>ArtemisCooldownRules.UncoverableRaidwides</c>). Minerva
    /// ("Minerva.Hints.SecondsUntilRaidwide" + "NextRaidwideActionId") or BossMod's raidwide forecast.
    /// </summary>
    public static float SecondsUntilRaidwide { get; set; } = float.MaxValue;

    /// <summary>
    /// The beasts a later battle of this board still plans to use (<c>CrucibleDegreeTeams.NeededLater</c>), written by
    /// the horn setter when it sets the horns for a battle. Empty off a degree or with no plan: every beast is then
    /// treated as needed.
    /// </summary>
    public static System.Collections.Generic.IReadOnlyCollection<int> BeastsNeededLater { get; set; } = [];

    /// <summary>True once the horn setter has written <see cref="BeastsNeededLater"/> for the battle being fought.</summary>
    public static bool BeastsNeededLaterKnown { get; set; }

    /// <summary>
    /// The tankbuster an enemy is casting at the beastmaster right now (its action id, one of
    /// <c>CrucibleAutoPilotRules.TankbusterCastIds</c>), 0 when none: the autopilot answers it with a held Tome of the
    /// Impervious or Crucible Tannin. Written by Artemis each frame in a battle; only trusted while
    /// <see cref="BusterCastUtc"/> is fresh.
    /// </summary>
    public static uint BusterCastOnMe { get; set; }

    /// <summary>When <see cref="BusterCastOnMe"/> was last written.</summary>
    public static System.DateTime BusterCastUtc { get; set; }

    /// <summary>The tankbuster being cast at the beastmaster now, or 0.</summary>
    public static uint BusterCastNow(System.DateTime now)
        => (now - BusterCastUtc).TotalSeconds < 1.0 ? BusterCastOnMe : 0;

    /// <summary>
    /// Enemies casting something that should be interrupted, from Minerva's "Minerva.Hints.TargetsToInterrupt"
    /// (Bone Knight Ossify, Succubus Fanaticism, Younger Tablitaur Rallying Cheer, Pullet Caustic Vomit, Biloko Natural
    /// Nurture, Progenitrix Massive Explosion, Cyclops Camaraderie, Sahagin Dreadwash, Administrator Confound, slime
    /// Rupture). Empty when none, off a board, or with another boss engine.
    /// </summary>
    public static System.Collections.Generic.IReadOnlyList<ulong> TargetsToInterrupt { get; set; } = [];

    /// <summary>
    /// The Siren's adds are bunched and the hard target catches them all: the autopilot may use a Fang. Written by Artemis
    /// on Legendary; 0 when not. Only trusted while <see cref="FangAimUtc"/> is fresh (the rotation stops writing off a
    /// target).
    /// </summary>
    public static ulong FangTargetId { get; set; }

    /// <summary>When <see cref="FangTargetId"/> was last written.</summary>
    public static System.DateTime FangAimUtc { get; set; }

    /// <summary>A Fang may go out now.</summary>
    public static bool FangReady(System.DateTime now)
        => FangTargetId != 0 && (now - FangAimUtc).TotalSeconds < 1.0;
}
