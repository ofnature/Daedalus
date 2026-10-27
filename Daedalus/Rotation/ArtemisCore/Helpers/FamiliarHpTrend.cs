using System;
using System.Collections.Generic;
using System.Linq;

namespace Daedalus.Rotation.ArtemisCore.Helpers;

/// <summary>
/// How fast the familiar out right now is losing HP: the biggest single drop seen on it lately, and its HP lost per
/// second over the last few seconds. Feeds <see cref="ArtemisCooldownRules.FallingLine"/>, so a familiar being hit
/// hard is retreated earlier than the flat line. Starts over when a different familiar comes out.
/// </summary>
public sealed class FamiliarHpTrend
{
    /// <summary>A drop counts toward "biggest hit" for this long.</summary>
    public const double HitMemorySeconds = 10;

    /// <summary>HP lost per second is measured over this window.</summary>
    public const double RateWindowSeconds = 3;

    private readonly Queue<(DateTime At, float Pct)> _samples = new();
    private readonly Queue<(DateTime At, float Drop)> _drops = new();
    private (DateTime At, float Pct)? _last;
    private ulong _familiarId;

    /// <summary>One reading of the familiar's HP (0-1). A new id clears what was known about the last one.</summary>
    public void Sample(ulong familiarId, float hpPct, DateTime now)
    {
        if (familiarId != _familiarId)
        {
            _familiarId = familiarId;
            _samples.Clear();
            _drops.Clear();
            _last = null;
        }

        if (_last is { } last && last.Pct > hpPct)
            _drops.Enqueue((now, last.Pct - hpPct));

        _last = (now, hpPct);
        _samples.Enqueue((now, hpPct));
        while (_samples.Count > 1 && (now - _samples.Peek().At).TotalSeconds > RateWindowSeconds)
            _samples.Dequeue();
        while (_drops.Count > 0 && (now - _drops.Peek().At).TotalSeconds > HitMemorySeconds)
            _drops.Dequeue();
    }

    /// <summary>The biggest single drop (fraction of max HP) in the last <see cref="HitMemorySeconds"/>.</summary>
    public float BiggestHitPct => _drops.Count == 0 ? 0f : _drops.Max(d => d.Drop);

    /// <summary>
    /// Fraction of max HP lost per second over the last <see cref="RateWindowSeconds"/>; 0 when healing or idle.
    /// Divided by the whole window even before it has filled, so a fresh familiar's first hit does not read as a
    /// huge rate.
    /// </summary>
    public float LossPerSecond
    {
        get
        {
            if (_last is not { } last || _samples.Count < 2)
                return 0f;
            var lost = _samples.Peek().Pct - last.Pct;
            return lost <= 0f ? 0f : lost / (float)RateWindowSeconds;
        }
    }
}
