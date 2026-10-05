using System;
using System.Numerics;
using Daedalus.Config;

namespace Daedalus.Services.Positional.Navigation;

/// <summary>
/// Sends every safety question to whichever mechanics engine the user picked.
/// <para>
/// A router rather than a flag threaded through each consumer, because there are eight of them —
/// the movement arbiter, the positional service, the cast hold, the forward-dash guard, the
/// phantom layer, the raise hold, and two in the plugin pump. Adding a second engine by editing
/// eight call sites would guarantee one of them keeps talking to the wrong plugin, which is the
/// silent failure this whole design is trying to avoid.
/// </para>
/// <para>
/// Consumers keep taking <see cref="IBossModSafetyService"/> and never learn there is a choice.
/// </para>
/// </summary>
public sealed class BossHandlingRouter : IBossModSafetyService
{
    private readonly IBossModSafetyService _bossMod;
    private readonly IBossModSafetyService _minerva;
    private readonly Func<BossHandling> _selection;
    private readonly Func<MovementPolling> _movementSelection;

    /// <param name="movementSelection">
    /// Which plugin the "is something steering the character" reads go to (Settings ▸ General ▸
    /// Boss handling). Null means follow the mechanics engine.
    /// </param>
    public BossHandlingRouter(
        IBossModSafetyService bossMod, IBossModSafetyService minerva, Func<BossHandling> selection,
        Func<MovementPolling>? movementSelection = null)
    {
        _bossMod = bossMod;
        _minerva = minerva;
        _selection = selection;
        _movementSelection = movementSelection ?? (() => MovementPolling.MechanicsEngine);
    }

    /// <summary>
    /// The plugin polled for movement, or null for vnavmesh only. Follows the mechanics engine unless
    /// the user named one.
    /// </summary>
    private IBossModSafetyService? Mover => _movementSelection() switch
    {
        MovementPolling.BossMod => _bossMod,
        MovementPolling.Minerva => _minerva,
        MovementPolling.VnavmeshOnly => null,
        _ => Active,
    };

    /// <summary>Which plugin the movement reads go to right now, for the readouts.</summary>
    public string MovementPolledFrom => _movementSelection() switch
    {
        MovementPolling.BossMod => "BossMod Reborn AI",
        MovementPolling.Minerva => "Minerva",
        MovementPolling.VnavmeshOnly => "vnavmesh only",
        _ => Selected == BossHandling.Minerva ? "Minerva (mechanics engine)" : "BossMod Reborn AI (mechanics engine)",
    };

    /// <summary>Which engine is selected right now. Read per call — the user may switch mid-session.</summary>
    public BossHandling Selected => _selection();

    /// <summary>
    /// The engine in charge. NOT "whichever is installed": if the user picked Minerva and Minerva
    /// is not loaded, this still routes to Minerva, which reports unavailable and every consumer
    /// fails open. Silently falling back to BossMod would mean the setting lies about who is
    /// driving, and the user would be debugging the wrong plugin.
    /// </summary>
    private IBossModSafetyService Active => Selected == BossHandling.Minerva ? _minerva : _bossMod;

    public bool IsAvailable => Active.IsAvailable;

    public void BeginUpdateSnapshot() => Active.BeginUpdateSnapshot();

    public bool ShouldAbortMovement() => Active.ShouldAbortMovement();

    public PositionSafety QueryPositionSafety(
        Vector3 destination,
        float imminentWindowSeconds = PositionalMovementConstants.DefaultImminentWindowSeconds)
        => Active.QueryPositionSafety(destination, imminentWindowSeconds);

    public bool IsSegmentSafe(Vector3 from, Vector3 to) => Active.IsSegmentSafe(from, to);

    public float NextDamageInSeconds => Active.NextDamageInSeconds;

    public float ForbiddenZoneActivationInSeconds => Active.ForbiddenZoneActivationInSeconds;

    public int ForbiddenZonesCount => Active.ForbiddenZonesCount;

    /// <summary>Movement read: goes to the polled plugin (<see cref="Mover"/>), not the mechanics engine.</summary>
    public bool IsBmrNavigating => Mover?.IsBmrNavigating ?? false;

    /// <inheritdoc cref="IsBmrNavigating"/>
    public Vector3? BmrNaviTarget => Mover?.BmrNaviTarget;

    public ulong[] ForbiddenTargets => Active.ForbiddenTargets;

    public ulong[] PriorityTargets => Active.PriorityTargets;

    public ulong PullTargetId => Active.PullTargetId;

    public Daedalus.Rotation.Roleplay.RoleplayRequest[] RoleplayActions => Active.RoleplayActions;
}
