using System.Numerics;

namespace Daedalus.Services.Positional.Navigation;

/// <summary>
/// A mechanics engine that is switched off. Reports unavailable and fails open on everything.
/// <para>
/// Exists so "BossMod is not the selected engine" can be expressed as an ENGINE rather than as a
/// boolean threaded into <see cref="BmrAiConfigService"/>. That service already gates every one
/// of its actions on <c>IsAvailable</c> — including the AI preset it creates and the movement
/// pause it toggles — so handing it this instead of the live BMR adapter switches all of it off
/// at once, with no new branch that could be forgotten.
/// </para>
/// </summary>
public sealed class InactiveSafetyService : IBossModSafetyService
{
    public static readonly InactiveSafetyService Instance = new();

    public bool IsAvailable => false;

    public void BeginUpdateSnapshot() { }

    public bool ShouldAbortMovement() => false;

    public PositionSafety QueryPositionSafety(
        Vector3 destination,
        float imminentWindowSeconds = PositionalMovementConstants.DefaultImminentWindowSeconds)
        => PositionSafety.Safe;

    public bool IsSegmentSafe(Vector3 from, Vector3 to) => true;

    public float NextDamageInSeconds => float.MaxValue;

    public float ForbiddenZoneActivationInSeconds => float.MaxValue;

    public int ForbiddenZonesCount => 0;

    public bool IsBmrNavigating => false;

    public Vector3? BmrNaviTarget => null;
}
