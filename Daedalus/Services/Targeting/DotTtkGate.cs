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
    /// <summary>
    /// The cutoff to use: the configured one, raised to <paramref name="minTimeToKillSeconds"/> when a caller
    /// needs the DoT to outlive more (a pack the AoE would hit instead). Null when nothing gates.
    /// </summary>
    public static float? Cutoff(ITimeToKillService? timeToKill, TargetingConfig config, float minTimeToKillSeconds = 0f)
    {
        if (timeToKill == null)
            return null;
        var configured = config.EnableDotTimeToKillCheck ? config.DotTimeToKillThresholdSeconds : 0f;
        var cutoff = System.Math.Max(configured, minTimeToKillSeconds);
        return cutoff > 0f ? cutoff : null;
    }

    public static bool ShouldSkip(ITimeToKillService? timeToKill, TargetingConfig config, ulong targetGameObjectId,
        float minTimeToKillSeconds)
        => Cutoff(timeToKill, config, minTimeToKillSeconds) is { } cutoff
           && timeToKill!.GetTtkSeconds(targetGameObjectId) < cutoff;

    public static bool ShouldSkip(ITimeToKillService? timeToKill, TargetingConfig config, ulong targetGameObjectId)
    {
        if (timeToKill == null || !config.EnableDotTimeToKillCheck)
            return false;

        return timeToKill.GetTtkSeconds(targetGameObjectId) < config.DotTimeToKillThresholdSeconds;
    }
}
