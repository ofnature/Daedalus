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

    // ── Healing (H1) ─────────────────────────────────────────────────────────────────────
    // Deliberately parallel to the damage fields rather than a separate object: one fight is
    // one encounter with two views, and a combatant that both deals damage and heals (every
    // tank, every self-healing DPS) is one row in each tab, not two identities.

    /// <summary>Total healing done INCLUDING the part that landed on full health bars.</summary>
    public long TotalHealing { get; internal set; }

    /// <summary>The portion of <see cref="TotalHealing"/> that restored nothing.</summary>
    public long Overheal { get; internal set; }

    /// <summary>HoT tick healing attributed to this combatant (included in <see cref="TotalHealing"/>).</summary>
    public long HotHealing { get; internal set; }

    /// <summary>Direct heal casts — HoT ticks excluded, same reasoning as <see cref="HitCount"/>.</summary>
    public int HealCount { get; internal set; }

    /// <summary>Critical direct heals, for the heal-crit rate.</summary>
    public int HealCritCount { get; internal set; }

    /// <summary>
    /// Healing that actually restored HP. This is the number the parser headlines: raw healing
    /// rewards pouring casts into full health bars, which would rank a Warrior spamming
    /// Bloodwhetting above a healer who wasted nothing.
    /// </summary>
    public long EffectiveHealing => TotalHealing - Overheal;

    /// <summary>Share of this combatant's healing that was wasted (0..1).</summary>
    public float OverhealPercent => TotalHealing > 0 ? (float)Overheal / TotalHealing : 0f;

    /// <summary>Crit rate over direct heals only.</summary>
    public float HealCritPercent => HealCount > 0 ? 100f * HealCritCount / HealCount : 0f;

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

    /// <summary>
    /// Damage from DoT ticks on enemies that could not be attributed to any caster — the game
    /// aggregates all DoTs on a target into one tick, and when neither the packet source nor the
    /// status list disambiguates it (several friendly DoT sources, typical with Trust casters),
    /// the tick lands here instead of in a row. Non-zero means every DoT user's row is
    /// undercounting; shown in the parser footer so the undercount is visible, never silent.
    /// </summary>
    public long UnattributedDotDamage { get; private set; }

    public void AddUnattributedDot(int amount)
    {
        if (IsActive && amount > 0)
            UnattributedDotDamage += amount;
    }

    /// <summary>
    /// Raw HoT/DoT tick packets processed this encounter (before any attribution). Zero across a
    /// DoT-heavy fight means the ActorControl hook isn't receiving ticks at all — pipeline
    /// diagnosis, shown in the parser's "(?)" tooltip.
    /// </summary>
    public int DotTicksProcessed { get; internal set; }

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

    /// <summary>Total healing done this encounter, overheal included.</summary>
    public long TotalHealing { get; private set; }

    /// <summary>Healing this encounter that landed on full health bars.</summary>
    public long TotalOverheal { get; private set; }

    /// <summary>Healing that actually restored HP.</summary>
    public long EffectiveHealing => TotalHealing - TotalOverheal;

    /// <summary>
    /// HoT ticks processed this encounter. Zero across a fight with a Scholar or Astrologian
    /// means ActorControl category 1540 is not arriving — the healing twin of
    /// <see cref="DotTicksProcessed"/>, and the same pipeline check.
    /// </summary>
    public int HotTicksProcessed { get; internal set; }

    /// <summary>
    /// Adds a direct heal. <paramref name="overheal"/> is the portion that restored nothing;
    /// it is tracked rather than discarded so the meter can headline effective healing and
    /// still show the waste.
    /// </summary>
    public void AddHeal(in CombatantIdentity caster, int amount, int overheal, bool isCrit)
    {
        if (!IsActive || amount <= 0)
            return;

        var stats = GetOrCreate(caster);

        // Clamp: a stale shadow-HP read could in principle report more overheal than heal,
        // which would make EffectiveHealing negative and invert the ranking.
        var wasted = Math.Clamp(overheal, 0, amount);

        stats.TotalHealing += amount;
        stats.Overheal += wasted;
        stats.HealCount++;
        if (isCrit) stats.HealCritCount++;

        TotalHealing += amount;
        TotalOverheal += wasted;
    }

    /// <summary>
    /// Adds an attributed HoT tick. Counts toward healing totals but NOT toward
    /// <see cref="CombatantStats.HealCount"/> — ticks carry no crit flag, so folding them in
    /// would dilute the heal-crit rate exactly as DoT ticks would dilute crit/DH.
    ///
    /// <para>
    /// HoT ticks arrive on ActorControl 1540 with the source entity in the packet, so unlike
    /// merged DoT ticks there is no unattributed bucket here — attribution is exact or the
    /// tick is not ours to count.
    /// </para>
    /// </summary>
    public void AddHotTick(in CombatantIdentity caster, int amount, int overheal = 0)
    {
        if (!IsActive || amount <= 0)
            return;

        var stats = GetOrCreate(caster);
        var wasted = Math.Clamp(overheal, 0, amount);

        stats.TotalHealing += amount;
        stats.HotHealing += amount;
        stats.Overheal += wasted;

        TotalHealing += amount;
        TotalOverheal += wasted;
    }

    private CombatantStats GetOrCreate(in CombatantIdentity caster)
    {
        if (combatants.TryGetValue(caster.Key, out var stats))
            return stats;

        stats = new CombatantStats
        {
            EntityId = caster.Key,
            Kind = caster.Kind,
            Name = caster.Name,
            JobAbbrev = caster.JobAbbrev,
        };
        combatants[caster.Key] = stats;
        return stats;
    }

    /// <summary>Combatants sorted by effective damage (self-reported preferred), highest first.</summary>
    public List<CombatantStats> GetRanked()
    {
        var list = new List<CombatantStats>(combatants.Values);
        list.Sort((a, b) => b.EffectiveDamage.CompareTo(a.EffectiveDamage));
        return list;
    }

    /// <summary>Combatants that healed, sorted by effective healing, highest first.</summary>
    public List<CombatantStats> GetRankedByHealing()
    {
        var list = new List<CombatantStats>();
        foreach (var stats in combatants.Values)
        {
            if (stats.TotalHealing > 0)
                list.Add(stats);
        }

        list.Sort((a, b) => b.EffectiveHealing.CompareTo(a.EffectiveHealing));
        return list;
    }

    /// <summary>Effective HPS for one combatant, on this client's encounter clock.</summary>
    public float GetHps(CombatantStats stats)
        => DurationSeconds > 0f ? stats.EffectiveHealing / DurationSeconds : 0f;

    /// <summary>Raw HPS including overheal — the toggle the parser offers beside effective.</summary>
    public float GetRawHps(CombatantStats stats)
        => DurationSeconds > 0f ? stats.TotalHealing / DurationSeconds : 0f;

    /// <summary>Party effective HPS.</summary>
    public float GetPartyHps()
        => DurationSeconds > 0f ? EffectiveHealing / DurationSeconds : 0f;

    /// <summary>This combatant's share of the encounter's effective healing (0..1).</summary>
    public float GetHealingShare(CombatantStats stats)
        => EffectiveHealing > 0 ? (float)stats.EffectiveHealing / EffectiveHealing : 0f;

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
