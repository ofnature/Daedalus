namespace Daedalus.Config;

/// <summary>
/// Which plugin Daedalus defers to for boss mechanics — dodging, telegraph safety, and the
/// "may I stand here and cast" question the rotation asks before every hard cast.
/// <para>
/// Only one may drive at a time. Two mechanics engines steering the same character fight each
/// other frame by frame: one walks out of a zone, the other walks back in, and the character
/// stands still in the middle of it. So this is a choice, not a pair of toggles.
/// </para>
/// </summary>
public enum BossHandling
{
    /// <summary>
    /// BossMod Reborn. The default, and what every existing install has been running — the
    /// safety queries, the AI-movement preset management and the forecast all speak to it.
    /// </summary>
    BossMod = 0,

    /// <summary>
    /// Minerva. Answers the same questions from its own geometry and does its own dodging, so
    /// Daedalus stops managing BMR's AI preset entirely and asks Minerva per action instead.
    /// </summary>
    Minerva = 1,
}
