namespace Daedalus.Rotation.Common.Helpers;

/// <summary>
/// Whether max-melee range-keeping (walking in when the target is out of reach) may run. Pure.
/// <para>
/// The general rule: the master movement toggle and the Maintain-max-melee switch, not for limited jobs
/// (<see cref="LimitedJobContentPolicy.AllowsAutoMovement"/>), and not solo under Solo Position Lock. The Crucible is
/// the exception — it is Beastmaster's own content, it is melee and it is solo, and nothing else puts the axe in
/// reach: field 2026-10-25, Saar's Beastmaster stood out of range ("submitted but not cast" every 5 s, "Target out of
/// range"), fights ran 4 minutes instead of 1.5 and the run died at the Banemite every time. On a board, the
/// Beastmaster's own "keep in melee range" setting stands in for the general switches.
/// </para>
/// </summary>
public static class MeleeRangeKeeping
{
    public static bool Allowed(
        bool autoMovement, bool maintainMaxMelee, bool limitedJob, bool soloPositionLock, bool solo,
        bool onCrucibleBoard, bool crucibleKeepInMelee)
    {
        if (!autoMovement)
            return false;

        if (onCrucibleBoard && crucibleKeepInMelee)
            return true;

        return maintainMaxMelee && !limitedJob && !(soloPositionLock && solo);
    }
}
