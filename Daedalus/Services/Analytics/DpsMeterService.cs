using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using Daedalus.Config;
using Daedalus.Services.Network;

namespace Daedalus.Services.Analytics;

/// <summary>
/// Accumulates per-combatant damage from <see cref="ICombatEventService.OnDamageDealt"/> into
/// encounters. The hook callback only enqueues; identity resolution (object table) and
/// accumulation happen in <see cref="Update"/> on the framework thread — same split as
/// BossMechanicDetector. Only friendly→hostile damage counts; pets merge into their owner.
/// </summary>
public sealed class DpsMeterService : IDpsMeterService, IDisposable
{
    private readonly ICombatEventService combatEventService;
    private readonly ParserConfig config;
    private readonly Func<uint, uint, ResolvedDamage?> resolver;
    private readonly IObjectTable? objectTable;

    private readonly ConcurrentQueue<DamageDealtEvent> pending = new();
    private readonly ConcurrentQueue<DotTickEvent> pendingTicks = new();
    private readonly ConcurrentQueue<LanDpsReportPayload> remoteReports = new();
    private readonly List<DpsEncounter> history = new();

    /// <summary>
    /// Sanity ceiling for a single DoT tick. The ActorControl param layout is community-
    /// documented, not ClientStructs-verified — if a game patch shuffles params, amounts
    /// turn absurd; this keeps garbage out of the meter while the decode gets fixed.
    /// </summary>
    internal const int MaxPlausibleTickAmount = 2_000_000;

    /// <summary>
    /// Looks up which entity applied status <c>effectId</c> on <c>targetId</c> (0 = unknown).
    /// Default reads the target's status list; injectable for tests.
    /// </summary>
    internal Func<uint, uint, uint> StatusSourceLookup;

    /// <summary>Sole friendly status source on the target (0 = none or ambiguous). Injectable for tests.</summary>
    internal Func<uint, uint> SoleFriendlyStatusSourceLookup;

    /// <summary>Whether the tick's target is a hostile NPC (gates the unattributed counter). Injectable for tests.</summary>
    internal Func<uint, bool> IsHostileTargetLookup;

    /// <summary>
    /// Known DoT statuses on the target with their casters and relative tick-potency weights —
    /// feeds the ACT-style split of aggregated multi-source ticks. Injectable for tests.
    /// </summary>
    internal Func<uint, IReadOnlyList<(uint SourceId, int Weight)>> DotSourcesLookup;

    // IPC/LAN self-report sharing (milestone 2) — null when party coordination is off.
    private CoordinationBus? bus;
    private readonly Func<ushort>? territoryProvider;
    private DateTime lastReportSentUtc = DateTime.MinValue;
    private const double ReportIntervalSeconds = 2.0;

    /// <summary>
    /// Combat-flag flickers (phase cutscenes, e.g. Porta Decumana's mid-fight transition)
    /// must not split one fight into two encounters: the encounter only finalizes after
    /// this long continuously out of combat.
    /// </summary>
    internal const double EncounterLingerSeconds = 8.0;

    /// <summary>Test hook — wall clock for encounter timing.</summary>
    internal Func<DateTime> UtcNow = () => DateTime.UtcNow;

    // Per-encounter caches so the object table is hit once per combatant, not per packet.
    private readonly Dictionary<uint, CombatantIdentity?> casterCache = new();
    private readonly Dictionary<uint, string?> targetCache = new();

    private DpsEncounter? current;
    private DateTime lastInCombatUtc;

    public DpsMeterService(
        ICombatEventService combatEventService,
        IObjectTable objectTable,
        ParserConfig config,
        IPartyList? partyList = null,
        Func<ushort>? territoryProvider = null)
    {
        this.combatEventService = combatEventService;
        this.objectTable = objectTable;
        this.config = config;
        this.territoryProvider = territoryProvider;
        this.resolver = DefaultResolve;
        this.IsPartyMemberName = name => IsInPartyList(partyList, name);
        this.StatusSourceLookup = DefaultStatusSourceLookup;
        this.SoleFriendlyStatusSourceLookup = DefaultSoleFriendlyStatusSourceLookup;
        this.IsHostileTargetLookup = DefaultIsHostileTarget;
        this.DotSourcesLookup = DefaultDotSourcesLookup;

        combatEventService.OnDamageDealt += OnDamageDealt;
        combatEventService.OnDotTick += OnDotTick;
    }

    /// <summary>Test constructor — inject a resolver instead of the object table.</summary>
    internal DpsMeterService(
        ICombatEventService combatEventService,
        ParserConfig config,
        Func<uint, uint, ResolvedDamage?> resolver,
        Func<string, bool>? isPartyMemberName = null,
        Func<ushort>? territoryProvider = null)
    {
        this.combatEventService = combatEventService;
        this.config = config;
        this.resolver = resolver;
        this.territoryProvider = territoryProvider;
        this.IsPartyMemberName = isPartyMemberName ?? (_ => true);
        this.StatusSourceLookup = (_, _) => 0;
        this.SoleFriendlyStatusSourceLookup = _ => 0;
        this.IsHostileTargetLookup = _ => true;
        this.DotSourcesLookup = _ => Array.Empty<(uint, int)>();

        combatEventService.OnDamageDealt += OnDamageDealt;
        combatEventService.OnDotTick += OnDotTick;
    }

    /// <summary>Is this character name in OUR party right now? Gate for merging remote reports.</summary>
    internal Func<string, bool> IsPartyMemberName;

    private static bool IsInPartyList(IPartyList? partyList, string name)
    {
        if (partyList == null || name.Length == 0)
            return false;

        foreach (var member in partyList)
        {
            if (member.Name.TextValue == name)
                return true;
        }

        return false;
    }

    public DpsEncounter? Current => current is { IsActive: true } ? current : null;

    public IReadOnlyList<DpsEncounter> History => history;

    /// <summary>
    /// Attaches the coordination bus so this toon broadcasts its own parse (~2s cadence in
    /// combat) and merges other Daedalus toons' authoritative self-reports.
    /// </summary>
    public void AttachCoordinationBus(CoordinationBus coordinationBus)
    {
        bus = coordinationBus;
        coordinationBus.OnDpsReport += (_, report) => remoteReports.Enqueue(report);
    }

    public void Update()
    {
        var now = UtcNow();
        var inCombat = combatEventService.IsInCombat;

        if (inCombat && current is not { IsActive: true })
        {
            // New pull — drop anything queued between fights, start fresh. (A combat-flag
            // flicker inside the linger window keeps the existing encounter instead.)
            while (pending.TryDequeue(out _)) { }
            while (pendingTicks.TryDequeue(out _)) { }
            casterCache.Clear();
            targetCache.Clear();
            current = new DpsEncounter { StartUtc = now };
        }

        if (current is { IsActive: true })
        {
            if (inCombat)
            {
                lastInCombatUtc = now;
                // Wall clock from encounter start — survives combat-flag flickers, and keeps
                // one consistent clock for every displayed number (ACT semantics).
                var duration = (float)(now - current.StartUtc).TotalSeconds;
                if (duration > current.DurationSeconds)
                    current.DurationSeconds = duration;
            }

            DrainQueue(current);
            DrainTicks(current);

            if (!inCombat && (now - lastInCombatUtc).TotalSeconds >= EncounterLingerSeconds)
            {
                current.IsActive = false;
                SendSelfReport(current, isFinal: true);
                if (current.TotalDamage > 0)
                {
                    history.Insert(0, current);
                    var cap = Math.Max(1, config.FightHistoryCount);
                    while (history.Count > cap)
                        history.RemoveAt(history.Count - 1);
                }
            }
            else if (inCombat && (now - lastReportSentUtc).TotalSeconds >= ReportIntervalSeconds)
            {
                lastReportSentUtc = now;
                SendSelfReport(current, isFinal: false);
            }
        }

        DrainRemoteReports();
    }

    public void Reset()
    {
        current = null;
        history.Clear();
        while (pending.TryDequeue(out _)) { }
        while (pendingTicks.TryDequeue(out _)) { }
        casterCache.Clear();
        targetCache.Clear();
    }

    public void Dispose()
    {
        combatEventService.OnDamageDealt -= OnDamageDealt;
        combatEventService.OnDotTick -= OnDotTick;
    }

    private void OnDamageDealt(DamageDealtEvent evt)
    {
        if (!config.Enabled)
            return;

        pending.Enqueue(evt);
    }

    private void OnDotTick(DotTickEvent evt)
    {
        if (!config.Enabled)
            return;

        pendingTicks.Enqueue(evt);
    }

    private void SendSelfReport(DpsEncounter encounter, bool isFinal)
    {
        if (bus == null || !config.ShareOverNetwork)
            return;

        var report = BuildSelfReport(encounter, isFinal);
        if (report == null)
            return;

        if (territoryProvider != null)
            report.TerritoryId = territoryProvider();
        bus.BroadcastDpsReport(report);
    }

    /// <summary>
    /// Builds this toon's self-report from its own row of the encounter, or null when it
    /// hasn't dealt damage yet. Static + pure for testability.
    /// </summary>
    internal static LanDpsReportPayload? BuildSelfReport(DpsEncounter encounter, bool isFinal)
    {
        foreach (var stats in encounter.GetRanked())
        {
            if (stats.Kind != CombatantKind.Self)
                continue;
            if (stats.TotalDamage <= 0)
                return null;

            return new LanDpsReportPayload
            {
                CharacterName = stats.Name,
                JobAbbrev = stats.JobAbbrev,
                EncounterStartTicks = encounter.StartUtc.Ticks,
                TotalDamage = stats.TotalDamage,
                DurationSeconds = encounter.DurationSeconds,
                CritPercent = stats.CritPercent,
                DirectHitPercent = stats.DirectHitPercent,
                IsFinal = isFinal,
            };
        }

        return null;
    }

    private void DrainRemoteReports()
    {
        while (remoteReports.TryDequeue(out var report))
        {
            if (!ShouldAcceptReport(report))
                continue;

            FindEncounterForReport(report)?.ApplyRemoteReport(report);
        }
    }

    /// <summary>
    /// Cross-bleed gate. Reports broadcast on the LAN reach ALL our toons — including ones
    /// fighting something else entirely (observed live: a Lv81 dungeon parse merging into a
    /// Porta Decumana run; start times alone can't discriminate concurrent fights). Only
    /// merge senders that are actually in OUR party — a different instance can never
    /// contain them — with a territory check as belt-and-braces.
    /// </summary>
    internal bool ShouldAcceptReport(LanDpsReportPayload report)
    {
        if (report.TerritoryId != 0 && territoryProvider != null)
        {
            var territory = territoryProvider();
            if (territory != 0 && territory != report.TerritoryId)
                return false;
        }

        return IsPartyMemberName(report.CharacterName);
    }

    /// <summary>
    /// Picks the encounter a (party-gated) report applies to: the live one while fighting;
    /// after our combat ends, the newest history entry still catches trailing finals
    /// (start within ±30s — partied toons pull together).
    /// </summary>
    internal DpsEncounter? FindEncounterForReport(LanDpsReportPayload report)
    {
        if (current is { IsActive: true })
            return current;

        if (history.Count > 0)
        {
            var newest = history[0];
            if (Math.Abs(newest.StartUtc.Ticks - report.EncounterStartTicks) <= 30 * TimeSpan.TicksPerSecond)
                return newest;
        }

        return null;
    }

    private void DrainQueue(DpsEncounter encounter)
    {
        while (pending.TryDequeue(out var evt))
        {
            var resolved = resolver(evt.CasterEntityId, evt.TargetEntityId);
            if (resolved == null)
                continue;

            encounter.AddDamage(resolved.Value.Caster, resolved.Value.TargetName, evt.Amount, evt.IsCrit, evt.IsDirectHit);
        }
    }

    private void DrainTicks(DpsEncounter encounter)
    {
        while (pendingTicks.TryDequeue(out var tick))
        {
            encounter.DotTicksProcessed++;

            if (tick.Amount <= 0 || tick.Amount > MaxPlausibleTickAmount)
                continue;

            var resolved = TryAttributeTick(tick);
            if (resolved == null)
            {
                // ACT-style estimate: split the merged tick across the known DoT statuses on the
                // target by relative tick potency. Off, or no known DoTs found → count the drop
                // (only on hostile targets — enemy DoTs on players / HoT ticks are correctly
                // nobody's damage and stay invisible).
                if (config.EstimateSharedDotTicks && TrySplitAggregatedTick(encounter, tick))
                    continue;
                if (IsHostileTargetLookup(tick.TargetEntityId))
                    encounter.AddUnattributedDot(tick.Amount);
                continue;
            }

            encounter.AddDotDamage(resolved.Value.Caster, resolved.Value.TargetName, tick.Amount);
        }
    }

    /// <summary>
    /// Attributes a DoT tick: the packet's source id when present (newer packets carry it),
    /// otherwise whoever applied the ticking status on the target; as a last resort, when
    /// exactly ONE friendly combatant has any status on the enemy, an aggregated tick can only
    /// be theirs. Multi-source aggregated ticks (typical with Trust casters DoTing alongside
    /// you) are dropped and counted as unattributed — visible, never guessed.
    /// </summary>
    internal ResolvedDamage? TryAttributeTick(in DotTickEvent tick)
    {
        if (tick.PossibleSourceId != 0 && tick.PossibleSourceId != Data.FFXIVConstants.InvalidTargetId)
        {
            var direct = resolver(tick.PossibleSourceId, tick.TargetEntityId);
            if (direct != null)
                return direct;
        }

        if (tick.EffectId != 0)
        {
            var sourceId = StatusSourceLookup(tick.TargetEntityId, tick.EffectId);
            if (sourceId != 0)
                return resolver(sourceId, tick.TargetEntityId);
        }

        // Aggregated tick (no source, no effect id — the game rolls all DoTs on a target into
        // one tick): if a single friendly is the only status source on this enemy, it's theirs.
        var sole = SoleFriendlyStatusSourceLookup(tick.TargetEntityId);
        if (sole != 0)
            return resolver(sole, tick.TargetEntityId);

        return null;
    }

    private uint DefaultStatusSourceLookup(uint targetId, uint effectId)
    {
        var obj = objectTable?.SearchById(targetId);
        if (obj is not IBattleChara chara)
            return 0;

        var statusList = chara.StatusList;
        if (statusList == null)
            return 0;

        foreach (var status in statusList)
        {
            if (status != null && status.StatusId == effectId)
                return status.SourceId;
        }

        return 0;
    }

    /// <summary>
    /// Returns the entity id of the ONLY friendly combatant with any status on the target, or 0
    /// when there are none / several. Used to attribute aggregated DoT ticks in the solo case.
    /// </summary>
    private uint DefaultSoleFriendlyStatusSourceLookup(uint targetId)
    {
        var obj = objectTable?.SearchById(targetId);
        if (obj is not IBattleChara chara)
            return 0;

        var statusList = chara.StatusList;
        if (statusList == null)
            return 0;

        uint sole = 0;
        foreach (var status in statusList)
        {
            if (status == null || status.SourceId == 0)
                continue;

            var sourceId = status.SourceId;
            if (!casterCache.TryGetValue(sourceId, out var identity))
            {
                identity = ResolveCaster(sourceId);
                casterCache[sourceId] = identity;
            }
            if (identity == null)
                continue; // not a countable friendly (enemy self-buffs etc.)

            if (sole == 0)
                sole = sourceId;
            else if (sole != sourceId)
                return 0; // two-plus friendly sources — ambiguous, stay honest
        }

        return sole;
    }

    private bool DefaultIsHostileTarget(uint targetId) => ResolveHostileTargetName(targetId) != null;

    /// <summary>
    /// Splits an aggregated multi-source tick across the target's known DoT casters by relative
    /// tick potency. Returns false when nothing is known about the target's DoTs.
    /// </summary>
    private bool TrySplitAggregatedTick(DpsEncounter encounter, in DotTickEvent tick)
    {
        var sources = DotSourcesLookup(tick.TargetEntityId);
        if (sources.Count == 0)
            return false;

        long totalWeight = 0;
        foreach (var (_, weight) in sources)
            totalWeight += weight;
        if (totalWeight <= 0)
            return false;

        var remaining = tick.Amount;
        for (var i = 0; i < sources.Count; i++)
        {
            var share = i == sources.Count - 1
                ? remaining
                : (int)((long)tick.Amount * sources[i].Weight / totalWeight);
            remaining -= share;
            if (share <= 0)
                continue;

            var resolved = resolver(sources[i].SourceId, tick.TargetEntityId);
            if (resolved != null)
                encounter.AddDotDamage(resolved.Value.Caster, resolved.Value.TargetName, share);
            else
                encounter.AddUnattributedDot(share);
        }

        return true;
    }

    /// <summary>Known DoT statuses on the target: (caster, relative tick weight) per status.</summary>
    private IReadOnlyList<(uint SourceId, int Weight)> DefaultDotSourcesLookup(uint targetId)
    {
        var obj = objectTable?.SearchById(targetId);
        if (obj is not IBattleChara chara)
            return Array.Empty<(uint, int)>();

        var statusList = chara.StatusList;
        if (statusList == null)
            return Array.Empty<(uint, int)>();

        List<(uint, int)>? sources = null;
        foreach (var status in statusList)
        {
            if (status == null || status.SourceId == 0)
                continue;
            if (!DotStatusWeights.TryGetWeight(status.StatusId, out var weight))
                continue;

            sources ??= new List<(uint, int)>(4);
            sources.Add((status.SourceId, weight));
        }

        return sources ?? (IReadOnlyList<(uint, int)>)Array.Empty<(uint, int)>();
    }

    private ResolvedDamage? DefaultResolve(uint casterId, uint targetId)
    {
        var targetName = ResolveHostileTargetName(targetId);
        if (targetName == null)
            return null;

        if (!casterCache.TryGetValue(casterId, out var identity))
        {
            identity = ResolveCaster(casterId);
            casterCache[casterId] = identity;
        }

        if (identity == null)
            return null;

        return new ResolvedDamage(identity.Value, targetName);
    }

    /// <summary>Returns the target's name when it is a hostile battle NPC, else null (not counted).</summary>
    private string? ResolveHostileTargetName(uint targetId)
    {
        if (targetCache.TryGetValue(targetId, out var cached))
            return cached;

        string? name = null;
        var obj = objectTable?.SearchById(targetId);
        if (obj is IBattleNpc enemy && IsHostile(enemy))
            name = enemy.Name.TextValue;

        targetCache[targetId] = name;
        return name;
    }

    private CombatantIdentity? ResolveCaster(uint casterId)
    {
        if (objectTable == null)
            return null;

        var localPlayer = objectTable.LocalPlayer;
        if (localPlayer != null && casterId == localPlayer.EntityId)
            return new CombatantIdentity(casterId, CombatantKind.Self, localPlayer.Name.TextValue, GetJobAbbrev(localPlayer));

        var obj = objectTable.SearchById(casterId);
        if (obj is IPlayerCharacter player)
            return new CombatantIdentity(casterId, CombatantKind.Player, player.Name.TextValue, GetJobAbbrev(player));

        if (obj is IBattleNpc npc)
        {
            // Trust/Duty Support allies FIRST — the canonical test the party helpers use.
            // Must precede the owner merge: avatars are their own combatants, never pets.
            if (Rotation.Common.Helpers.BasePartyHelper.IsValidTrustNpc(obj, out _, includeDead: true))
                return new CombatantIdentity(casterId, CombatantKind.Support, npc.Name.TextValue, GetJobAbbrev(npc));

            // Pets, summons, chocobos — attribute to the owner so demis/Queen merge into the caster's row.
            if (npc.OwnerId != 0 && npc.OwnerId != Data.FFXIVConstants.InvalidTargetId)
            {
                if (casterCache.TryGetValue(npc.OwnerId, out var owner))
                    return owner;
                var resolvedOwner = ResolveCaster(npc.OwnerId);
                casterCache[npc.OwnerId] = resolvedOwner;
                return resolvedOwner;
            }

            // Enemies never get a row; anything friendly that fights is Trust/duty support.
            if (IsHostile(npc))
                return null;

            return new CombatantIdentity(casterId, CombatantKind.Support, npc.Name.TextValue, GetJobAbbrev(npc));
        }

        return null;
    }

    /// <summary>Same hostility test the targeting service uses: combatant subkind, or subkind 0.</summary>
    private static bool IsHostile(IBattleNpc npc)
        => (byte)npc.BattleNpcKind == Compat.BattleNpcKinds.Combatant || npc.SubKind == 0;

    private static string GetJobAbbrev(IBattleChara chara)
    {
        var job = chara.ClassJob;
        return job.RowId != 0 ? job.Value.Abbreviation.ToString() : "";
    }
}
