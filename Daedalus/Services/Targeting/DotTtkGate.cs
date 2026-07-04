using Daedalus.Config;
using Daedalus.Services.Combat;

namespace Daedalus.Services.Targeting;

/// <summary>
/// RSR TimeToKill parity for DoT application: skip targets that will die before the DoT pays for
/// its GCD (e.g. Biolysis on a 4k-HP add that two Broils finish). Fail-open — an unknown TTK
/// (no service wired, fresh pull, HP not declining) never skips, so opener DoTs are unaffected.
/// </summary>
public static class DotTtkGate
{
    public static bool ShouldSkip(ITimeToKillService? timeToKill, TargetingConfig config, ulong targetGameObjectId)
    {
        if (timeToKill == null || !config.EnableDotTimeToKillCheck)
            return false;

        return timeToKill.GetTtkSeconds(targetGameObjectId) < config.DotTimeToKillThresholdSeconds;
    }
}
