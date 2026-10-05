namespace Daedalus.Config;

/// <summary>
/// Which plugin Daedalus asks "is something moving the character right now?" — the read that makes
/// Daedalus's own movement (walk-ins, positional hops, max-melee keeping) stand down while another
/// plugin steers, and that the gap-closer guard checks before a dash.
/// <para>
/// Separate from <see cref="BossHandling"/>, which answers the safety questions (is this spot
/// safe, may I cast here). Usually the same plugin does both, which is the default; this exists for
/// setups where the plugin doing the moving is not the one reading the mechanics.
/// </para>
/// </summary>
public enum MovementPolling
{
    /// <summary>Whatever the mechanics engine is (BossMod Reborn's AI or Minerva). The default.</summary>
    MechanicsEngine = 0,

    /// <summary>BossMod Reborn's AI (<c>BossMod.AI.IsNavigating</c>), whatever the mechanics engine.</summary>
    BossMod = 1,

    /// <summary>Minerva (<c>Minerva.IsSteering</c>), whatever the mechanics engine.</summary>
    Minerva = 2,

    /// <summary>
    /// No engine: only vnavmesh paths count as movement. For running with no steering plugin, so a
    /// stale or misread steering flag can never hold Daedalus's own movement.
    /// </summary>
    VnavmeshOnly = 3,
}
