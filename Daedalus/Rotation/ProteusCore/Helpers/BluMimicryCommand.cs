using System;
using Daedalus.Config.DPS;

namespace Daedalus.Rotation.ProteusCore.Helpers;

/// <summary>
/// Manual mimicry request from the BLU Mimicry window to the rotation. STATIC-BACKED on purpose
/// (the config-copy lesson: rotations read a copied Configuration, so transient UI→rotation flags
/// must not ride config). The BuffModule consumes the request: it scans for the requested role
/// and casts, bypassing the auto-mimicry toggle and the configured role. Requests expire after
/// 10s (no target found / cast kept failing) and clear the moment the requested buff is active.
/// </summary>
public static class BluMimicryCommand
{
    private static BluRole? _requested;
    private static DateTime _requestedAtUtc;

    private const double TimeoutSeconds = 10;

    /// <summary>UI: ask the rotation to mimic this role from whoever is in range.</summary>
    public static void Request(BluRole role)
    {
        _requested = role;
        _requestedAtUtc = DateTime.UtcNow;
    }

    /// <summary>The live request, or null (expired requests self-clear).</summary>
    public static BluRole? GetPending()
    {
        if (_requested == null)
            return null;
        if ((DateTime.UtcNow - _requestedAtUtc).TotalSeconds > TimeoutSeconds)
        {
            _requested = null;
            return null;
        }
        return _requested;
    }

    public static void Clear() => _requested = null;

    // ── Auto-recast suppression ─────────────────────────────────────────────
    // After a MANUAL removal (window button) the auto-mimicry would instantly re-buff — hold it
    // briefly so the removal sticks long enough to matter. The loadout-apply flow never sets
    // this (it WANTS the recast the moment the apply completes).

    private static DateTime _suppressAutoUntilUtc = DateTime.MinValue;

    public static void SuppressAuto(double seconds)
        => _suppressAutoUntilUtc = DateTime.UtcNow.AddSeconds(seconds);

    public static bool AutoSuppressed => DateTime.UtcNow < _suppressAutoUntilUtc;

    public static void ClearSuppression() => _suppressAutoUntilUtc = DateTime.MinValue;
}
