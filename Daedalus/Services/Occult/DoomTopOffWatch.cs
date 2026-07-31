using System;
using System.Collections.Generic;

namespace Daedalus.Services.Occult;

/// <summary>
/// Cross-toon "I am Doomed — heal me to FULL" board for the Necromancer Deep Freeze combo.
/// <para>
/// Deep Freeze Dooms the caster for 10s and the Doom is dispelled ONLY at 100% HP, so a
/// Doomed toon at 90% is in more danger than a healthy toon at 40% — the exact inversion
/// HP-deficit healing gets wrong. Local healers already catch this by reading the Doom
/// status directly (<c>HealerPartyHelper.HasDoom</c>, id 1769); this board is the explicit
/// LAN channel on top of it: the caster announces before/as it fires, so healers on other
/// boxes prioritise the top-off even when the caster is outside their status-read range,
/// and the caster can REFUSE to fire when no healer is listening.
/// </para>
/// Static-backed on purpose — rotations read a config COPY, so transient cross-toon flags
/// must never live on the config object (the ExternalCombatOverride / IsDebugWindowOpen
/// lesson).
/// </summary>
public static class DoomTopOffWatch
{
    /// <summary>Deep Freeze's Doom is 10s; keep the request slightly longer so the heal lands.</summary>
    public const double RequestTtlSeconds = 12.0;

    private static readonly Dictionary<string, DateTime> _requests = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object _lock = new();

    /// <summary>Testable clock.</summary>
    internal static Func<DateTime> UtcNow { get; set; } = () => DateTime.UtcNow;

    /// <summary>Raised when THIS toon needs to announce a top-off request (wired to the LAN bus).</summary>
    public static Action<string>? OnLocalRequest { get; set; }

    /// <summary>Announce that a character needs healing to 100% (local record + LAN broadcast).</summary>
    public static void RequestTopOff(string characterName)
    {
        if (string.IsNullOrWhiteSpace(characterName))
            return;

        Record(characterName);
        OnLocalRequest?.Invoke(characterName);
    }

    /// <summary>Record a request without re-broadcasting (used by the LAN receive path).</summary>
    public static void Record(string characterName)
    {
        if (string.IsNullOrWhiteSpace(characterName))
            return;

        lock (_lock)
            _requests[characterName] = UtcNow().AddSeconds(RequestTtlSeconds);
    }

    /// <summary>True while this character has an unexpired top-off request.</summary>
    public static bool NeedsTopOff(string characterName)
    {
        if (string.IsNullOrWhiteSpace(characterName))
            return false;

        lock (_lock)
            return _requests.TryGetValue(characterName, out var until) && UtcNow() < until;
    }

    /// <summary>Clear a request once the toon is back to full (or the Doom resolved).</summary>
    public static void Clear(string characterName)
    {
        if (string.IsNullOrWhiteSpace(characterName))
            return;

        lock (_lock)
            _requests.Remove(characterName);
    }

    /// <summary>Names with live requests — Debug/LAN window readout.</summary>
    public static IReadOnlyList<string> ActiveRequests()
    {
        lock (_lock)
        {
            var now = UtcNow();
            var live = new List<string>();
            foreach (var kv in _requests)
            {
                if (now < kv.Value)
                    live.Add(kv.Key);
            }

            return live;
        }
    }

    /// <summary>Test/reload hygiene.</summary>
    internal static void Reset()
    {
        lock (_lock)
            _requests.Clear();
        OnLocalRequest = null;
        UtcNow = () => DateTime.UtcNow;
    }
}
