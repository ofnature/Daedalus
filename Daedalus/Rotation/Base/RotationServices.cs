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

    /// <summary>BMR safety hints — used by dash guards (Smudge) to veto hazardous landing spots.</summary>
    public static IBossModSafetyService? BossModSafety { get; set; }

    /// <summary>
    /// Occult Crescent phantom duty-action layer — runs after every job's modules
    /// (BaseRotation.ExecuteInternal), inert outside the zone.
    /// </summary>
    public static Daedalus.Rotation.Phantom.PhantomActionLayer? PhantomLayer { get; set; }

    /// <summary>
    /// Variant dungeon duty-action layer — same hook sites as the phantom layer,
    /// inert outside the five variant territories.
    /// </summary>
    public static Daedalus.Rotation.Phantom.VariantActionLayer? VariantLayer { get; set; }

    /// <summary>
    /// RSR-compat IPC surface — melee rotations broadcast their anticipated positional
    /// finisher on RSR's ActionUpdater event gates so positional-following movement
    /// plugins ("Follow RSR's desired positional") follow Daedalus.
    /// </summary>
    public static Daedalus.Ipc.RsrCompatIpc? RsrCompat { get; set; }
}
