using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using Daedalus.Config;

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
    private readonly List<DpsEncounter> history = new();

    // Per-encounter caches so the object table is hit once per combatant, not per packet.
    private readonly Dictionary<uint, CombatantIdentity?> casterCache = new();
    private readonly Dictionary<uint, string?> targetCache = new();

    private DpsEncounter? current;
    private bool wasInCombat;

    public DpsMeterService(
        ICombatEventService combatEventService,
        IObjectTable objectTable,
        ParserConfig config)
    {
        this.combatEventService = combatEventService;
        this.objectTable = objectTable;
        this.config = config;
        this.resolver = DefaultResolve;

        combatEventService.OnDamageDealt += OnDamageDealt;
    }

    /// <summary>Test constructor — inject a resolver instead of the object table.</summary>
    internal DpsMeterService(
        ICombatEventService combatEventService,
        ParserConfig config,
        Func<uint, uint, ResolvedDamage?> resolver)
    {
        this.combatEventService = combatEventService;
        this.config = config;
        this.resolver = resolver;

        combatEventService.OnDamageDealt += OnDamageDealt;
    }

    public DpsEncounter? Current => current is { IsActive: true } ? current : null;

    public IReadOnlyList<DpsEncounter> History => history;

    public void Update()
    {
        var inCombat = combatEventService.IsInCombat;

        if (inCombat && !wasInCombat)
        {
            // New pull — drop anything queued between fights, start fresh.
            while (pending.TryDequeue(out _)) { }
            casterCache.Clear();
            targetCache.Clear();
            current = new DpsEncounter();
        }

        if (current is { IsActive: true })
        {
            if (inCombat)
            {
                var duration = combatEventService.GetCombatDurationSeconds();
                if (duration > current.DurationSeconds)
                    current.DurationSeconds = duration;
            }

            DrainQueue(current);

            if (!inCombat)
            {
                current.IsActive = false;
                if (current.TotalDamage > 0)
                {
                    history.Insert(0, current);
                    var cap = Math.Max(1, config.FightHistoryCount);
                    while (history.Count > cap)
                        history.RemoveAt(history.Count - 1);
                }
            }
        }

        wasInCombat = inCombat;
    }

    public void Reset()
    {
        current = null;
        history.Clear();
        while (pending.TryDequeue(out _)) { }
        casterCache.Clear();
        targetCache.Clear();
    }

    public void Dispose()
    {
        combatEventService.OnDamageDealt -= OnDamageDealt;
    }

    private void OnDamageDealt(DamageDealtEvent evt)
    {
        if (!config.Enabled)
            return;

        pending.Enqueue(evt);
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
