using Dalamud.Plugin.Services;
using Daedalus.Services.Positional.Navigation;

namespace Daedalus.Rotation.Base;

/// <summary>
/// Static service references set by Plugin on init, available to all rotations without DI.
/// </summary>
public static class RotationServices
{
    public static ICondition? Condition { get; set; }

    /// <summary>vNav adapter — used to treat plugin-driven pathing as "moving" so hard-casts hold.</summary>
    public static IVNavService? VNav { get; set; }

    /// <summary>
    /// Movement arbiter — exposes BMR AI steering (input injection, invisible to <see cref="IVNavService.IsPathRunning"/>)
    /// so hard-casts also hold while BossMod is dodging.
    /// </summary>
    public static IMovementArbiter? MovementArbiter { get; set; }
}
