using System.Collections.Generic;
using System.Linq;

namespace Daedalus.Services.Blu;

/// <summary>One BLU toon in the election pool: its sender id and advertised capability bits.</summary>
public readonly record struct BluPeerCapability(string SenderId, BluCapabilities Capabilities)
{
    public bool Has(BluCapabilities flag) => (Capabilities & flag) == flag;
}

/// <summary>
/// Deterministic owner election over the LAN roster's BLU toons (the RoleAssignment pattern:
/// same inputs on every machine → same answer, no negotiation round). Filter by capability,
/// sort by SenderId (ordinal), first wins. A toon whose loadout lacks the spell can never be
/// assigned it; no capable toon → unassigned (never block the party).
/// </summary>
public static class BluPartyElection
{
    /// <summary>The single owner for a capability, or null when nobody advertises it.</summary>
    public static string? ElectOwner(IReadOnlyList<BluPeerCapability> bluRoster, BluCapabilities required)
        => ElectOwners(bluRoster, required, 1).FirstOrDefault();

    /// <summary>The first <paramref name="count"/> capable toons in SenderId order (may return fewer).</summary>
    public static IReadOnlyList<string> ElectOwners(
        IReadOnlyList<BluPeerCapability> bluRoster, BluCapabilities required, int count)
        => bluRoster
            .Where(p => p.SenderId.Length > 0 && p.Has(required))
            .Select(p => p.SenderId)
            .Distinct()
            .OrderBy(s => s, System.StringComparer.Ordinal)
            .Take(count)
            .ToArray();

    /// <summary>
    /// Gobskin caster: prefer a healer-role toon with Gobskin (mimicry makes it 250p vs 100p),
    /// fall back to any Gobskin-capable toon.
    /// </summary>
    public static string? ElectGobskinOwner(IReadOnlyList<BluPeerCapability> bluRoster)
        => ElectOwner(bluRoster, BluCapabilities.Gobskin | BluCapabilities.HealerRole)
           ?? ElectOwner(bluRoster, BluCapabilities.Gobskin);

    /// <summary>
    /// Moon Flute stagger split (T13 Gigaflare pushes): Flute-capable toons in SenderId order,
    /// even index → 'A', odd → 'B' — half the party is never in Waning at a push. Toons without
    /// Moon Flute (or not found) read as group 'A' (they never Flute anyway).
    /// </summary>
    public static char StaggerGroupFor(IReadOnlyList<BluPeerCapability> bluRoster, string senderId)
    {
        var ordered = bluRoster
            .Where(p => p.SenderId.Length > 0 && p.Has(BluCapabilities.MoonFlute))
            .Select(p => p.SenderId)
            .Distinct()
            .OrderBy(s => s, System.StringComparer.Ordinal)
            .ToArray();
        var index = System.Array.IndexOf(ordered, senderId);
        return index >= 0 && index % 2 == 1 ? 'B' : 'A';
    }
}
