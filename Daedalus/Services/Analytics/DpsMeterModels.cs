using System;
using System.Collections.Generic;

namespace Daedalus.Services.Analytics;

/// <summary>How a combatant relates to the parse — drives row tags and source dots.</summary>
public enum CombatantKind
{
    /// <summary>The local player. Gold dot; numbers are exact.</summary>
    Self,

    /// <summary>Another player character (party, alliance). Grey dot + HUMAN tag until
    /// they self-report over IPC/LAN (milestone 2 flips them to green).</summary>
    Player,

    /// <summary>Trust / duty support / squadron NPC ally. Grey dot + TRUST tag.</summary>
    Support,
}

/// <summary>
/// Resolved identity of a damage source. <see cref="Key"/> is the entity the damage is
/// attributed to — for pets/summons this is the owner, so pet damage merges into the owner row.
/// </summary>
public readonly record struct CombatantIdentity(uint Key, CombatantKind Kind, string Name, string JobAbbrev);

/// <summary>A damage event resolved against the object table, ready for accumulation.</summary>
public readonly record struct ResolvedDamage(CombatantIdentity Caster, string TargetName);

/// <summary>Accumulated per-combatant damage totals within one encounter.</summary>
public sealed class CombatantStats
{
    public uint EntityId { get; init; }
    public CombatantKind Kind { get; init; }
    public string Name { get; init; } = "";
    public string JobAbbrev { get; init; } = "";

    public long TotalDamage { get; internal set; }
    public int HitCount { get; internal set; }
    public int CritCount { get; internal set; }
    public int DirectHitCount { get; internal set; }

    public float CritPercent => HitCount > 0 ? 100f * CritCount / HitCount : 0f;
    public float DirectHitPercent => HitCount > 0 ? 100f * DirectHitCount / HitCount : 0f;
}

/// <summary>
/// One combat encounter's damage totals. Pure accumulation — no Dalamud dependencies,
/// fully unit-testable. The service resolves identities; this class only counts.
/// </summary>
public sealed class DpsEncounter
{
    private readonly Dictionary<uint, CombatantStats> combatants = new();
    private readonly Dictionary<string, long> damageByTarget = new();

    public DateTime StartUtc { get; init; } = DateTime.UtcNow;

    /// <summary>True while the fight is running; frozen stats once ended.</summary>
    public bool IsActive { get; internal set; } = true;

    /// <summary>Combat duration in seconds — updated while active, frozen at end.</summary>
    public float DurationSeconds { get; internal set; }

    public long TotalDamage { get; private set; }

    /// <summary>Name of the enemy that has received the most damage — the encounter title.</summary>
    public string TargetName { get; private set; } = "";

    public int CombatantCount => combatants.Count;

    public void AddDamage(in CombatantIdentity caster, string targetName, int amount, bool isCrit, bool isDirectHit)
    {
        if (!IsActive || amount < 0)
            return;

        if (!combatants.TryGetValue(caster.Key, out var stats))
        {
            stats = new CombatantStats
            {
                EntityId = caster.Key,
                Kind = caster.Kind,
                Name = caster.Name,
                JobAbbrev = caster.JobAbbrev,
            };
            combatants[caster.Key] = stats;
        }

        stats.TotalDamage += amount;
        stats.HitCount++;
        if (isCrit) stats.CritCount++;
        if (isDirectHit) stats.DirectHitCount++;

        TotalDamage += amount;

        if (!string.IsNullOrEmpty(targetName))
        {
            damageByTarget.TryGetValue(targetName, out var soFar);
            var updated = soFar + amount;
            damageByTarget[targetName] = updated;
            if (TargetName.Length == 0
                || (TargetName != targetName && updated > damageByTarget.GetValueOrDefault(TargetName)))
            {
                TargetName = targetName;
            }
        }
    }

    /// <summary>Combatants sorted by total damage, highest first.</summary>
    public List<CombatantStats> GetRanked()
    {
        var list = new List<CombatantStats>(combatants.Values);
        list.Sort((a, b) => b.TotalDamage.CompareTo(a.TotalDamage));
        return list;
    }

    /// <summary>DPS for one combatant over the encounter duration.</summary>
    public float GetDps(CombatantStats stats)
        => DurationSeconds > 0f ? stats.TotalDamage / DurationSeconds : 0f;

    /// <summary>Sum of all combatant DPS.</summary>
    public float GetPartyDps()
        => DurationSeconds > 0f ? TotalDamage / DurationSeconds : 0f;

    /// <summary>This combatant's share of the encounter's total damage (0..1).</summary>
    public float GetDamageShare(CombatantStats stats)
        => TotalDamage > 0 ? (float)stats.TotalDamage / TotalDamage : 0f;
}
