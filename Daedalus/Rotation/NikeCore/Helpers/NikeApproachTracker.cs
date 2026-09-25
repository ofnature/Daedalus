using System;

namespace Daedalus.Rotation.NikeCore.Helpers;

/// <summary>
/// Is the character actually getting closer to its target, and how fast? Plus how long the rotation
/// has been holding Enpi back on the strength of that.
///
/// <para>
/// Measured from the distance itself rather than from a movement flag, because "moving" says nothing
/// about direction: a dodge away from the target moves the character too, and holding Enpi through a
/// dodge would just idle the GCD. Sampled over a short window so frame-to-frame jitter in position
/// does not read as a sprint in either direction.
/// </para>
/// </summary>
public sealed class NikeApproachTracker
{
    /// <summary>Minimum spacing between samples; closer than this, position jitter dominates.</summary>
    public const double SampleSeconds = 0.15;

    /// <summary>Below this rate of closing (y/s) the character is not meaningfully approaching.</summary>
    public const float MinClosingSpeed = 1.0f;

    private readonly Func<DateTime> _utcNow;
    private ulong _targetId;
    private float _lastDistance;
    private DateTime _lastSampleUtc = DateTime.MinValue;
    private DateTime? _holdStartedUtc;

    public NikeApproachTracker(Func<DateTime>? utcNow = null)
        => _utcNow = utcNow ?? (() => DateTime.UtcNow);

    /// <summary>Rate the gap is shrinking, in yalms per second. Negative when the gap is growing.</summary>
    public float ClosingSpeed { get; private set; }

    /// <summary>The gap is growing — we are being moved away, whatever else is going on.</summary>
    public bool DistanceIncreasing => ClosingSpeed <= -MinClosingSpeed;

    /// <summary>Seconds the rotation has been holding Enpi back for this approach.</summary>
    public float SecondsHeld => _holdStartedUtc is { } t ? (float)(_utcNow() - t).TotalSeconds : 0f;

    /// <summary>Feed the current distance to the current target. Call once per frame.</summary>
    public void Sample(ulong targetId, float distance)
    {
        var now = _utcNow();

        // A new target is a new approach: nothing measured against the old one applies.
        if (targetId != _targetId)
        {
            _targetId = targetId;
            _lastDistance = distance;
            _lastSampleUtc = now;
            ClosingSpeed = 0f;
            _holdStartedUtc = null;
            return;
        }

        var dt = (now - _lastSampleUtc).TotalSeconds;
        if (dt < SampleSeconds)
            return;

        ClosingSpeed = (float)((_lastDistance - distance) / dt);
        _lastDistance = distance;
        _lastSampleUtc = now;
    }

    /// <summary>Record whether this frame held Enpi back, so the hold can be capped.</summary>
    public void NoteHolding(bool holding)
    {
        if (!holding)
            _holdStartedUtc = null;
        else
            _holdStartedUtc ??= _utcNow();
    }
}
