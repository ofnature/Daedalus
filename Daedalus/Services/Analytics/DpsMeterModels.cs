using System;
using System.Collections.Generic;
using Daedalus.Services.Network;

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
    public string Name { get; internal set; } = "";
    public string JobAbbrev { get; internal set; } = "";

    public long TotalDamage { get; internal set; }
    public int HitCount { get; internal set; }
    public int CritCount { get; internal set; }
    public int DirectHitCount { get; internal set; }

    /// <summary>DoT tick damage attributed to this combatant (included in <see cref="TotalDamage"/>).</summary>
    public long DotDamage { get; internal set; }

    /// <summary>
    /// True once this combatant's own Daedalus instance has reported exact numbers over
    /// IPC/LAN — reported values override the locally-observed ones everywhere.
    /// </summary>
    public bool IsSelfReported { get; internal set; }
    public long ReportedDamage { get; internal set; }
    public float ReportedCritPercent { get; internal set; }
    public float ReportedDirectHitPercent { get; internal set; }

    // Segment accumulation: when the sender's combat flag flickers (phase cutscenes), its
    // encounter restarts and its cumulative counter resets. A report smaller than the last
    // one marks a new segment — completed segments accumulate into the base.
    internal long ReportedSegmentBase;
    internal long ReportedSegmentLast;

    /// <summary>Reported damage when self-reported, locally-observed total otherwise.</summary>
    public long EffectiveDamage => IsSelfReported ? ReportedDamage : TotalDamage;

    public float CritPercent => IsSelfReported ? ReportedCritPercent
        : HitCount > 0 ? 100f * CritCount / HitCount : 0f;

    public float DirectHitPercent => IsSelfReported ? ReportedDirectHitPercent
        : HitCount > 0 ? 100f * DirectHitCount / HitCount : 0f;
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

    /// <summary>
    /// Adds attributed DoT tick damage. Counts toward totals and share but NOT toward
    /// <see cref="CombatantStats.HitCount"/> — ticks carry no crit/DH flags, so folding
    /// them into hits would dilute the crit and direct-hit percentages.
    /// </summary>
    public void AddDotDamage(in CombatantIdentity caster, string targetName, int amount)
    {
        if (!IsActive || amount <= 0)
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
        stats.DotDamage += amount;
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

    /// <summary>Combatants sorted by effective damage (self-reported preferred), highest first.</summary>
    public List<CombatantStats> GetRanked()
    {
        var list = new List<CombatantStats>(combatants.Values);
        list.Sort((a, b) => b.EffectiveDamage.CompareTo(a.EffectiveDamage));
        return list;
    }

    /// <summary>
    /// DPS for one combatant — ALWAYS this client's encounter clock (ACT semantics).
    /// One clock keeps row DPS, share %, and party DPS mutually consistent; mixing the
    /// sender's fight clock in produced rows whose DPS exceeded the party total whenever
    /// a phase cutscene split the combat flags across clients.
    /// </summary>
    public float GetDps(CombatantStats stats)
        => DurationSeconds > 0f ? stats.EffectiveDamage / DurationSeconds : 0f;

    /// <summary>Sum of all combatant DPS (effective damage over this client's duration).</summary>
    public float GetPartyDps()
        => DurationSeconds > 0f ? GetEffectiveTotal() / DurationSeconds : 0f;

    /// <summary>This combatant's share of the encounter's effective total damage (0..1).</summary>
    public float GetDamageShare(CombatantStats stats)
    {
        var total = GetEffectiveTotal();
        return total > 0 ? (float)stats.EffectiveDamage / total : 0f;
    }

    private long GetEffectiveTotal()
    {
        long total = 0;
        foreach (var stats in combatants.Values)
            total += stats.EffectiveDamage;
        return total;
    }

    /// <summary>
    /// Applies a remote toon's self-report. Matched by character name (entity ids differ
    /// across clients); creates the row when this client never observed the sender at all
    /// (e.g. range-culled). Synthetic keys count down from uint.MaxValue to avoid colliding
    /// with real entity ids.
    /// </summary>
    public void ApplyRemoteReport(LanDpsReportPayload report)
    {
        if (report.CharacterName.Length == 0)
            return;

        CombatantStats? match = null;
        foreach (var stats in combatants.Values)
        {
            if (stats.Name == report.CharacterName)
            {
                match = stats;
                break;
            }
        }

        if (match == null)
        {
            var key = uint.MaxValue - (uint)combatants.Count;
            match = new CombatantStats
            {
                EntityId = key,
                Kind = CombatantKind.Player,
                Name = report.CharacterName,
                JobAbbrev = report.JobAbbrev,
            };
            combatants[key] = match;
        }

        // Sender's cumulative counter went backwards → its encounter restarted (combat-flag
        // flicker on its side). Bank the finished segment and keep accumulating.
        if (report.TotalDamage < match.ReportedSegmentLast)
            match.ReportedSegmentBase += match.ReportedSegmentLast;
        match.ReportedSegmentLast = report.TotalDamage;

        match.IsSelfReported = true;
        match.ReportedDamage = match.ReportedSegmentBase + report.TotalDamage;
        match.ReportedCritPercent = report.CritPercent;
        match.ReportedDirectHitPercent = report.DirectHitPercent;
        if (report.JobAbbrev.Length > 0)
            match.JobAbbrev = report.JobAbbrev;
    }
}
