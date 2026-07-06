using System;
using System.Collections.Generic;

namespace Daedalus.Rotation.PrometheusCore.Helpers;

/// <summary>
/// Rolling time-to-kill estimate for the engaged pack, from total-pack-HP samples. Built for the
/// Automaton Queen hold: trash packs don't get LOW, they MELT — an HP%-threshold hold missed a Queen
/// deployed 2.5s before pack death onto a mob still at ~45% HP (2026-07-01 log). Rate over the last
/// few seconds catches that case: hold when totalHp / killRate says the pack dies before the Queen's
/// ~5s ramp pays off. A new pull (total HP jumping UP) makes the rate negative → no estimate → no
/// hold, so the window self-heals across pulls.
/// </summary>
public sealed class PackTtkEstimator
{
    private const float WindowSeconds = 6f;
    private const float MinSpanSeconds = 2f;

    private readonly Queue<(DateTime Time, long TotalHp)> _samples = new();

    public void Sample(long totalHp, DateTime now)
    {
        _samples.Enqueue((now, totalHp));
        while (_samples.Count > 0 && (now - _samples.Peek().Time).TotalSeconds > WindowSeconds)
            _samples.Dequeue();
    }

    /// <summary>
    /// Estimated seconds until the pack is dead, or null when no meaningful estimate exists
    /// (too few samples, pack HP flat or rising — e.g. a fresh pull entering the window).
    /// </summary>
    public float? EstimateTtkSeconds()
    {
        if (_samples.Count < 2)
            return null;

        var oldest = _samples.Peek();
        (DateTime Time, long TotalHp) newest = default;
        foreach (var s in _samples)
            newest = s;

        var span = (float)(newest.Time - oldest.Time).TotalSeconds;
        if (span < MinSpanSeconds)
            return null;

        var killRate = (oldest.TotalHp - newest.TotalHp) / span; // HP per second
        if (killRate <= 0)
            return null;

        return newest.TotalHp / killRate;
    }

    public void Reset() => _samples.Clear();
}
