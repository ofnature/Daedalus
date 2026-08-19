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

    public BossHandlingRouter(
        IBossModSafetyService bossMod, IBossModSafetyService minerva, Func<BossHandling> selection)
    {
        _bossMod = bossMod;
        _minerva = minerva;
        _selection = selection;
    }

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

    public bool IsBmrNavigating => Active.IsBmrNavigating;

    public Vector3? BmrNaviTarget => Active.BmrNaviTarget;
}
