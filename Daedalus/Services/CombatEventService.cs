using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using Daedalus.Services.Calculation;

namespace Daedalus.Services;

/// <summary>
/// Record of a healing event from the local player.
/// </summary>
public record HealEvent(DateTime Timestamp, uint TargetId, string TargetName, uint ActionId, int Amount, int OverhealAmount);

/// <summary>
/// Hooks into ActionEffectHandler.Receive to track HP changes in real-time,
/// before the game's visible HP bars update.
/// </summary>
public sealed unsafe class CombatEventService : ICombatEventService, IDisposable
{
    private readonly IPluginLog log;
    private readonly IObjectTable objectTable;
    private readonly Configuration? configuration;

    /// <summary>Raw packet dumps are opt-in — a diagnostic firehose, not normal-play logging.</summary>
    private bool DumpRawPackets => configuration?.Debug.DumpRawCombatPackets == true;
    private readonly Hook<ActionEffectHandler.Delegates.Receive>? receiveHook;
    private readonly Hook<ProcessActorControlDelegate>? actorControlHook;

    // ProcessPacketActorControl — same signature BMR (WorldStateGameSync) and RSR hook.
    private delegate void ProcessActorControlDelegate(
        uint actorId, uint category, uint p1, uint p2, uint p3, uint p4,
        uint p5, uint p6, uint p7, uint p8, ulong targetId, byte replaying);

    private const string ActorControlSignature = "E8 ?? ?? ?? ?? 0F B7 0B 83 E9 64";

    // Legacy HoT/DoT category (community docs, BMR enum "HotDot=23"). Field-verified 2026-07-04:
    // in the current patch this only carries out-of-combat self-regen ticks (p1=0, p2=kind,
    // p3=amount, p4=source). Kept wired in case other content still uses it — player-targeted
    // ticks are dropped downstream, so it can never double-count an enemy tick.
    private const uint ActorControlHotDot = 23;

    // Current-patch DoT tick, field-verified 2026-07-04 (Worqor trash, SCH Biolysis): fires per
    // SOURCE per target every ~3s server tick with p1 = status id (0 = source's DoTs aggregated),
    // p2 = amount (matches Biolysis-only damage, crits included), p3 = source entity id,
    // p4 = 0xFFFFFFFF. Sibling category 1540 is the HoT equivalent (p1 = status id, p2 = heal,
    // p3 = source, p4 = 1) — deliberately NOT consumed: heals must never enter the damage meter,
    // and hostile-target heal ticks would otherwise hit the attribution fallbacks.
    private const uint ActorControlDotTick = 1541;

    /// <summary>
    /// Event raised when a healing effect from the local player lands.
    /// The uint parameter is the target entity ID that received the heal.
    /// Subscribers can use this to clear pending heals for that target.
    /// </summary>
    public event System.Action<uint>? OnLocalPlayerHealLanded;

    /// <summary>
    /// Event raised when damage is received by any party member.
    /// Used by DamageIntakeService to track damage patterns for healing triage.
    /// Parameters: (entityId, damageAmount)
    /// </summary>
    public event System.Action<uint, int>? OnDamageReceived;

    /// <summary>
    /// Event raised when any heal effect lands (from any source, not just local player).
    /// Used by CoHealerDetectionService to track other healers' healing output.
    /// Parameters: (healerEntityId, targetEntityId, healAmount)
    /// </summary>
    public event System.Action<uint, uint, int>? OnAnyHealReceived;

    /// <summary>
    /// Event raised when any ability is used (action effect resolves).
    /// Used by TimelineService for timeline sync.
    /// Parameters: (sourceEntityId, actionId)
    /// </summary>
    public event System.Action<uint, uint>? OnAbilityUsed;
    public event System.Action<uint, int>? OnLocalAbilityResolved;

    /// <summary>
    /// Event raised when the local player deals damage to any target.
    /// Used for personal DPS tracking in analytics.
    /// Parameters: (targetEntityId, damageAmount, actionId)
    /// </summary>
    public event System.Action<uint, int, uint>? OnLocalPlayerDamageDealt;

    /// <summary>
    /// Event raised per damage effect from ANY caster in range (party, Trust NPCs, pets, enemies).
    /// Used by the DPS parser. Raised from the hook thread — subscribers must enqueue.
    /// </summary>
    public event System.Action<DamageDealtEvent>? OnDamageDealt;

    /// <summary>
    /// Event raised per HoT/DoT tick (ActorControl category 23 — ticks never appear in
    /// ActionEffect). Raised from the hook thread — subscribers must enqueue.
    /// </summary>
    public event System.Action<DotTickEvent>? OnDotTick;

    // Shadow HP tracking: EntityId -> (CurrentHp, LastActionUpdate)
    // LastActionUpdate is set when HP changes from action effects (heal/damage)
    // to prevent InitializeHp from overwriting before game HP catches up
    private readonly ConcurrentDictionary<uint, (uint Hp, DateTime LastActionUpdate)> shadowHp = new();

    // How long to protect shadow HP from being overwritten by InitializeHp after an action effect
    // This prevents double-casting heals due to race condition with game HP updates
    private const int ActionUpdateProtectionMs = 3000;

    // Recent heals from local player
    private readonly Queue<HealEvent> recentHeals = new();
    private const int MaxHealHistory = 20;
    private readonly object healLock = new();

    // Last time each local-player ability resolved (for healing debug panels)
    private readonly Dictionary<uint, DateTime> lastLocalAbilityUsedUtc = new();
    private readonly object abilityUseLock = new();

    // Overheal statistics tracking (session only)
    private readonly Dictionary<uint, SpellOverhealStats> spellOverhealStats = new();
    private readonly Dictionary<uint, TargetOverhealStats> targetOverhealStats = new();
    private readonly Queue<OverhealEvent> recentOverhealEvents = new();
    private const int MaxOverhealHistory = 50;
    private readonly object overhealLock = new();
    private DateTime sessionStartTime = DateTime.UtcNow;

    // Internal tracking classes for overheal statistics
    private sealed class SpellOverhealStats
    {
        public string SpellName { get; set; } = "";
        public int TotalHealing { get; set; }
        public int TotalOverheal { get; set; }
        public int CastCount { get; set; }
    }

    private sealed class TargetOverhealStats
    {
        public string TargetName { get; set; } = "";
        public int TotalHealing { get; set; }
        public int TotalOverheal { get; set; }
        public int HealCount { get; set; }
    }

    /// <summary>
    /// An overheal event for the timeline.
    /// </summary>
    public record OverhealEvent(DateTime Timestamp, string SpellName, string TargetName, int HealAmount, int OverhealAmount);

    // For calibration: store the last predicted heal (raw, without correction factor).
    // _lastPredictedHealRaw uses Interlocked.Exchange for thread safety (existing pattern).
    // _lastPredictionTimeTicks uses Interlocked.Read/Exchange for thread safety:
    //   RegisterPredictionForCalibration() writes from the game frame thread,
    //   ReceiveDetour() reads from the hook callback thread — a cross-thread access.
    private int _lastPredictedHealRaw;
    private long _lastPredictionTimeTicks;

    // Combat duration tracking
    private readonly object _combatStateLock = new();
    private DateTime? _combatStartTime;
    private volatile bool _isInCombat;

    // ActionEffectType values from FFXIVClientStructs
    private const byte EffectTypeDamage = 3;
    private const byte EffectTypeHeal = 4;

    // Damage/heal effect field semantics (verified against BossmodReborn ActionEffect.cs):
    // Param4 bit 0x40 = large-value flag, real value = Value + Param3 * 0x10000.
    // Damage Param0: bit 0x20 = crit, bit 0x40 = direct hit.
    private const byte LargeValueFlag = 0x40;
    private const byte DamageCritFlag = 0x20;
    private const byte DamageDirectHitFlag = 0x40;

    /// <summary>
    /// Decodes a damage/heal effect value, unpacking the extended high bits for hits over 65,535.
    /// Without this, ushort <c>Value</c> alone silently truncates big hits (routine at level 100).
    /// </summary>
    public static int DecodeEffectValue(ushort value, byte param3, byte param4)
        => value + ((param4 & LargeValueFlag) != 0 ? param3 * 0x10000 : 0);

    public CombatEventService(
        IGameInteropProvider gameInterop, IPluginLog log, IObjectTable objectTable,
        Configuration? configuration = null)
    {
        this.log = log;
        this.objectTable = objectTable;
        this.configuration = configuration;

        try
        {
            receiveHook = gameInterop.HookFromAddress<ActionEffectHandler.Delegates.Receive>(
                (nint)ActionEffectHandler.MemberFunctionPointers.Receive,
                ReceiveDetour);
            receiveHook.Enable();
            log.Info("CombatEventService: ActionEffect hook enabled");
        }
        catch (Exception ex)
        {
            log.Error(ex, "CombatEventService: Failed to create ActionEffect hook");
        }

        try
        {
            actorControlHook = gameInterop.HookFromSignature<ProcessActorControlDelegate>(
                ActorControlSignature, ActorControlDetour);
            actorControlHook.Enable();
            log.Info("CombatEventService: ActorControl hook enabled (DoT ticks)");
        }
        catch (Exception ex)
        {
            // Fail-open: everything except DoT tick attribution works without this hook.
            log.Error(ex, "CombatEventService: Failed to create ActorControl hook — DoT ticks will not be attributed");
        }

        try
        {
            addScreenLogHook = gameInterop.HookFromSignature<AddScreenLogDelegate>(
                AddScreenLogSignature, AddScreenLogDetour);
            addScreenLogHook.Enable();
            log.Info("CombatEventService: AddScreenLog hook enabled (fly-text tick diagnostics)");
        }
        catch (Exception ex)
        {
            log.Error(ex, "CombatEventService: Failed to create AddScreenLog hook — fly-text tick capture unavailable");
        }
    }

    // Fly-text feed (same hook DamageInfo uses). Field finding 2026-07-04: enemy DoT ticks never
    // arrive as ActorControl 23 in the current patch — only self regen/HoT ticks do — yet the game
    // renders the aggregated tick above the enemy, so this feed must carry it. Capped raw dumps
    // to learn the tick discriminator (autos share fly-text kinds with ticks; autos duplicate
    // ActionEffect amounts, ticks arrive ~3s cadence with no matching action).
    private delegate void AddScreenLogDelegate(
        Character* target, Character* source, int kind, int option,
        int actionKind, int actionId, int val1, int val2, int val3, int val4);

    private const string AddScreenLogSignature = "E8 ?? ?? ?? ?? BF ?? ?? ?? ?? EB 39";
    private readonly Hook<AddScreenLogDelegate>? addScreenLogHook;
    private long screenLogInvocations;
    private long screenLogHostileTargetCount;
    private long screenLogDumped;
    private DateTime lastScreenLogDumpUtc;
    private const long ScreenLogDumpCap = 150;
    private const double ScreenLogDumpRearmSeconds = 30;

    public bool ScreenLogHookActive => addScreenLogHook is { IsEnabled: true };
    public long ScreenLogInvocations => Interlocked.Read(ref screenLogInvocations);
    public long ScreenLogHostileTargetCount => Interlocked.Read(ref screenLogHostileTargetCount);

    // Diagnostic dumps mirror into the in-game Debug Log tab (+ daedalus-debug.log) — /xllog's
    // scrollback rotates too fast to catch a fight after the fact. Attached post-construction:
    // DebugLogService is built later in the Plugin constructor.
    private Debug.DebugLogService? debugLog;

    public void AttachDebugLog(Debug.DebugLogService service) => debugLog = service;

    private void AddScreenLogDetour(
        Character* target, Character* source, int kind, int option,
        int actionKind, int actionId, int val1, int val2, int val3, int val4)
    {
        Interlocked.Increment(ref screenLogInvocations);
        try
        {
            var targetId = target != null ? target->GameObject.EntityId : 0u;
            var sourceId = source != null ? source->GameObject.EntityId : 0u;

            // BattleNpc entity-id range — fly text on enemies is where DoT ticks must live.
            if (targetId is >= 0x40000000 and < 0x50000000)
            {
                Interlocked.Increment(ref screenLogHostileTargetCount);

                // Budget re-arms after a quiet spell so the LATEST engagement always has fresh
                // dumps in /xllog — a session-wide cap burned out before anyone could look.
                var now = DateTime.UtcNow;
                if (screenLogDumped >= ScreenLogDumpCap
                    && (now - lastScreenLogDumpUtc).TotalSeconds > ScreenLogDumpRearmSeconds)
                {
                    screenLogDumped = 0;
                }

                if (DumpRawPackets && screenLogDumped < ScreenLogDumpCap)
                {
                    screenLogDumped++;
                    lastScreenLogDumpUtc = now;
                    var line = $"[ScreenLog] kind={kind} option={option} actionKind={actionKind} actionId={actionId} "
                        + $"val1={val1} val2={val2} val3={val3} val4={val4} src={sourceId:X8} tgt={targetId:X8}";
                    log.Info(line);
                    debugLog?.Log(Debug.DebugLogCategory.General, Debug.DebugLogSeverity.Info, line);
                }
            }
        }
        catch (Exception ex)
        {
            log.Error(ex, "CombatEventService: Error in AddScreenLog detour");
        }

        addScreenLogHook?.Original(target, source, kind, option, actionKind, actionId, val1, val2, val3, val4);
    }

    // Hook liveness diagnostics (parser "(?)" tooltip): zero invocations across a fight means
    // the signature hooked a dead spot; invocations without HotDot means the category moved.
    // Field finding 2026-07-04: hook live, category 23 nearly absent (2/session in a Biolysis
    // fight that should tick ~every 3s) — the histogram + capped raw dumps below exist to find
    // which category actually carries tick damage in the current patch.
    private long actorControlInvocations;
    private long actorControlHotDotCount;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<uint, long> actorControlCategoryCounts = new();
    private const long RawDumpPerCategoryCap = 5;

    public bool ActorControlHookActive => actorControlHook is { IsEnabled: true };
    public long ActorControlInvocations => System.Threading.Interlocked.Read(ref actorControlInvocations);
    public long ActorControlHotDotCount => System.Threading.Interlocked.Read(ref actorControlHotDotCount);

    /// <summary>Per-category packet counts this session, e.g. "17×204 34×41 23×2", highest first.</summary>
    public string DescribeActorControlCategories()
    {
        var parts = new List<string>();
        foreach (var pair in actorControlCategoryCounts)
            parts.Add($"{pair.Key}×{pair.Value}");
        parts.Sort((a, b) => long.Parse(b[(b.IndexOf('×') + 1)..]).CompareTo(long.Parse(a[(a.IndexOf('×') + 1)..])));
        return string.Join(" ", parts);
    }

    private void ActorControlDetour(
        uint actorId, uint category, uint p1, uint p2, uint p3, uint p4,
        uint p5, uint p6, uint p7, uint p8, ulong targetId, byte replaying)
    {
        System.Threading.Interlocked.Increment(ref actorControlInvocations);
        try
        {
            var categoryCount = actorControlCategoryCounts.AddOrUpdate(category, 1, static (_, n) => n + 1);

            // Raw param dumps: first few packets of every category, plus all tick packets.
            if (DumpRawPackets
                && (categoryCount <= RawDumpPerCategoryCap
                    || category is ActorControlHotDot or ActorControlDotTick))
            {
                var line = $"[ActorControl] cat={category} actor={actorId:X8} p1={p1} p2={p2} p3={p3} p4={p4} "
                    + $"p5={p5} p6={p6} p7={p7} p8={p8} target={targetId:X} replay={replaying}";
                log.Info(line);
                debugLog?.Log(Debug.DebugLogCategory.General, Debug.DebugLogSeverity.Info, line);
            }

            if (category == ActorControlHotDot)
            {
                System.Threading.Interlocked.Increment(ref actorControlHotDotCount);
                OnDotTick?.Invoke(new DotTickEvent(actorId, p1, p2, unchecked((int)p3), p4));
            }
            else if (category == ActorControlDotTick)
            {
                System.Threading.Interlocked.Increment(ref actorControlHotDotCount);
                OnDotTick?.Invoke(new DotTickEvent(
                    actorId, EffectId: p1, Kind: p4, Amount: unchecked((int)p2), PossibleSourceId: p3));
            }
        }
        catch (Exception ex)
        {
            log.Error(ex, "CombatEventService: Error processing HotDot actor control");
        }

        actorControlHook?.Original(actorId, category, p1, p2, p3, p4, p5, p6, p7, p8, targetId, replaying);
    }


    /// <summary>
    /// Register a predicted heal (raw value without correction factor) for calibration.
    /// When the actual heal arrives, it will be used to calibrate the formula.
    /// </summary>
    public void RegisterPredictionForCalibration(int rawPredictedHeal)
    {
        Interlocked.Exchange(ref _lastPredictedHealRaw, rawPredictedHeal);
        Interlocked.Exchange(ref _lastPredictionTimeTicks, DateTime.UtcNow.Ticks);
    }

    /// <summary>
    /// Gets the shadow HP for an entity, or the fallback value if not tracked.
    /// </summary>
    public uint GetShadowHp(uint entityId, uint fallbackHp)
        => shadowHp.TryGetValue(entityId, out var entry) ? entry.Hp : fallbackHp;

    /// <summary>
    /// Initializes or updates the shadow HP for an entity.
    /// Call this each frame to ensure new party members are tracked.
    /// Respects timestamp protection: won't overwrite HP that was recently updated by action effects.
    /// </summary>
    public void InitializeHp(uint entityId, uint currentHp)
    {
        if (shadowHp.TryGetValue(entityId, out var existing))
        {
            // Skip if recently updated by action effect (heal/damage)
            // This prevents race condition where game HP hasn't caught up yet
            var timeSinceAction = (DateTime.UtcNow - existing.LastActionUpdate).TotalMilliseconds;
            if (timeSinceAction < ActionUpdateProtectionMs)
                return;

            // Only update if HP changed (avoids dictionary writes when values are stable)
            if (existing.Hp == currentHp)
                return;
        }

        // Initialize with MinValue timestamp (not from action effect)
        shadowHp[entityId] = (currentHp, DateTime.MinValue);
    }

    /// <summary>
    /// Gets all currently tracked shadow HP values.
    /// </summary>
    public List<(uint EntityId, uint Hp)> GetAllShadowHp()
    {
        var result = new List<(uint EntityId, uint Hp)>(shadowHp.Count);
        foreach (var kvp in shadowHp)
            result.Add((kvp.Key, kvp.Value.Hp));
        return result;
    }

    /// <summary>
    /// Clears all tracked shadow HP. Call on zone transitions.
    /// </summary>
    public void Clear()
        => shadowHp.Clear();

    /// <summary>
    /// Gets recent healing events from the local player.
    /// </summary>
    public IReadOnlyList<HealEvent> GetRecentHeals()
    {
        lock (healLock)
        {
            return recentHeals.Reverse().ToList();
        }
    }

    /// <summary>
    /// Clears the heal history.
    /// </summary>
    public void ClearHeals()
    {
        lock (healLock)
        {
            recentHeals.Clear();
        }
    }

    /// <summary>
    /// Gets UTC timestamp of the last time the local player resolved this action, if known.
    /// </summary>
    public bool TryGetLastLocalAbilityUsedUtc(uint actionId, out DateTime timestampUtc)
    {
        lock (abilityUseLock)
            return lastLocalAbilityUsedUtc.TryGetValue(actionId, out timestampUtc);
    }

    private void RecordLocalAbilityUsed(uint actionId)
    {
        lock (abilityUseLock)
            lastLocalAbilityUsedUtc[actionId] = DateTime.UtcNow;
    }

    /// <summary>
    /// Gets aggregated overheal statistics for the current session.
    /// </summary>
    public OverhealStatistics GetOverhealStatistics()
    {
        lock (overhealLock)
        {
            var totalHealing = 0;
            var totalOverheal = 0;

            var bySpell = new List<(uint ActionId, string SpellName, int TotalHealing, int TotalOverheal, int CastCount)>();
            foreach (var kvp in spellOverhealStats)
            {
                totalHealing += kvp.Value.TotalHealing;
                totalOverheal += kvp.Value.TotalOverheal;
                bySpell.Add((kvp.Key, kvp.Value.SpellName, kvp.Value.TotalHealing, kvp.Value.TotalOverheal, kvp.Value.CastCount));
            }

            var byTarget = new List<(uint TargetId, string TargetName, int TotalHealing, int TotalOverheal, int HealCount)>();
            foreach (var kvp in targetOverhealStats)
            {
                byTarget.Add((kvp.Key, kvp.Value.TargetName, kvp.Value.TotalHealing, kvp.Value.TotalOverheal, kvp.Value.HealCount));
            }

            var recentEvents = recentOverhealEvents.Reverse().ToList();

            return new OverhealStatistics(
                sessionStartTime,
                totalHealing,
                totalOverheal,
                bySpell,
                byTarget,
                recentEvents);
        }
    }

    /// <summary>
    /// Resets all overheal statistics for a new session.
    /// </summary>
    public void ResetOverhealStatistics()
    {
        lock (overhealLock)
        {
            spellOverhealStats.Clear();
            targetOverhealStats.Clear();
            recentOverhealEvents.Clear();
            sessionStartTime = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Aggregated overheal statistics for display.
    /// </summary>
    public record OverhealStatistics(
        DateTime SessionStartTime,
        int TotalHealing,
        int TotalOverheal,
        List<(uint ActionId, string SpellName, int TotalHealing, int TotalOverheal, int CastCount)> BySpell,
        List<(uint TargetId, string TargetName, int TotalHealing, int TotalOverheal, int HealCount)> ByTarget,
        List<OverhealEvent> RecentOverhealEvents)
    {
        public float OverhealPercent => TotalHealing > 0 ? (float)TotalOverheal / TotalHealing * 100f : 0f;
        public TimeSpan SessionDuration => DateTime.UtcNow - SessionStartTime;
    }

    private void ReceiveDetour(
        uint casterEntityId,
        Character* casterPtr,
        Vector3* targetPos,
        ActionEffectHandler.Header* header,
        ActionEffectHandler.TargetEffects* effects,
        GameObjectId* targetEntityIds)
    {
        if (receiveHook == null) return;

        try
        {
            ProcessEffects(casterEntityId, header, effects, targetEntityIds);
        }
        catch (Exception ex)
        {
            log.Error(ex, "CombatEventService: Error processing action effects");
        }

        // Always call original
        receiveHook.Original(casterEntityId, casterPtr, targetPos, header, effects, targetEntityIds);
    }

    private void ProcessEffects(
        uint casterEntityId,
        ActionEffectHandler.Header* header,
        ActionEffectHandler.TargetEffects* effects,
        GameObjectId* targetEntityIds)
    {
        var localPlayer = objectTable.LocalPlayer;
        var isFromLocalPlayer = localPlayer != null && casterEntityId == localPlayer.EntityId;

        for (var i = 0; i < header->NumTargets; i++)
        {
            var targetId = (uint)targetEntityIds[i].ObjectId;
            var targetEffects = effects[i];

            var totalDelta = 0;
            var totalHeal = 0;
            for (var j = 0; j < 8; j++)
            {
                var effect = targetEffects.Effects[j];

                switch (effect.Type)
                {
                    case EffectTypeDamage:
                        var damage = DecodeEffectValue(effect.Value, effect.Param3, effect.Param4);
                        totalDelta -= damage;
                        OnDamageDealt?.Invoke(new DamageDealtEvent(
                            casterEntityId,
                            targetId,
                            damage,
                            header->ActionId,
                            (effect.Param0 & DamageCritFlag) != 0,
                            (effect.Param0 & DamageDirectHitFlag) != 0));
                        break;
                    case EffectTypeHeal:
                        var heal = DecodeEffectValue(effect.Value, effect.Param3, effect.Param4);
                        totalDelta += heal;
                        totalHeal += heal;
                        break;
                }
            }

            if (totalDelta != 0 && shadowHp.TryGetValue(targetId, out var current))
            {
                var newHp = (uint)Math.Max(0, (int)current.Hp + totalDelta);
                // Set timestamp to protect this value from being overwritten by InitializeHp
                shadowHp[targetId] = (newHp, DateTime.UtcNow);
            }

            // Raise damage received event for damage intake tracking
            if (totalDelta < 0)
            {
                OnDamageReceived?.Invoke(targetId, -totalDelta);

                // Track damage dealt by local player (for DPS tracking)
                if (isFromLocalPlayer)
                {
                    OnLocalPlayerDamageDealt?.Invoke(targetId, -totalDelta, header->ActionId);
                }
            }

            // Raise heal received event for ALL heals (co-healer tracking)
            if (totalHeal > 0)
            {
                OnAnyHealReceived?.Invoke(casterEntityId, targetId, totalHeal);
            }

            // Track heals from local player
            if (isFromLocalPlayer && totalHeal > 0)
            {
                var targetName = "Unknown";
                uint targetMaxHp = 0;
                uint targetCurrentHp = 0;
                var targetObj = objectTable.SearchById(targetId);
                if (targetObj != null)
                {
                    targetName = targetObj.Name.TextValue;
                    if (targetObj is Dalamud.Game.ClientState.Objects.Types.ICharacter character)
                    {
                        targetMaxHp = character.MaxHp;
                        // HP before heal = current HP (game hasn't updated yet)
                        targetCurrentHp = character.CurrentHp;
                    }
                }

                // Calculate overheal: how much exceeded target's missing HP.
                // Use shadow HP (which reflects pending heals/damage not yet visible in the
                // game's HP bar) rather than character.CurrentHp, which is stale at hook time.
                var overhealAmount = 0;
                if (targetMaxHp > 0)
                {
                    var shadowHpValue = GetShadowHp(targetId, targetCurrentHp);
                    var missingHp = (int)(targetMaxHp - shadowHpValue);
                    overhealAmount = Math.Max(0, totalHeal - missingHp);
                }

                var healEvent = new HealEvent(
                    DateTime.UtcNow,
                    targetId,
                    targetName,
                    header->ActionId,
                    totalHeal,
                    overhealAmount);

                lock (healLock)
                {
                    recentHeals.Enqueue(healEvent);
                    if (recentHeals.Count > MaxHealHistory)
                        recentHeals.Dequeue();
                }

                // Track overheal statistics
                lock (overhealLock)
                {
                    // Per-spell tracking
                    if (!spellOverhealStats.TryGetValue(header->ActionId, out var spellStats))
                    {
                        spellStats = new SpellOverhealStats { SpellName = $"Action{header->ActionId}" };
                        spellOverhealStats[header->ActionId] = spellStats;
                    }
                    spellStats.TotalHealing += totalHeal;
                    spellStats.TotalOverheal += overhealAmount;
                    spellStats.CastCount++;

                    // Per-target tracking
                    if (!targetOverhealStats.TryGetValue(targetId, out var targetStats))
                    {
                        targetStats = new TargetOverhealStats { TargetName = targetName };
                        targetOverhealStats[targetId] = targetStats;
                    }
                    targetStats.TargetName = targetName; // Update name in case it changed
                    targetStats.TotalHealing += totalHeal;
                    targetStats.TotalOverheal += overhealAmount;
                    targetStats.HealCount++;

                    // Add to overheal events timeline (only if there was overheal)
                    if (overhealAmount > 0)
                    {
                        recentOverhealEvents.Enqueue(new OverhealEvent(
                            DateTime.UtcNow,
                            $"Action{header->ActionId}",
                            targetName,
                            totalHeal,
                            overhealAmount));

                        if (recentOverhealEvents.Count > MaxOverhealHistory)
                            recentOverhealEvents.Dequeue();
                    }
                }
            }

            // When our heal effect lands, notify subscribers and calibrate
            if (isFromLocalPlayer && totalHeal > 0)
            {
                // Raise event so HpPredictionService can clear pending heals for this target
                OnLocalPlayerHealLanded?.Invoke(targetId);

                // Calibrate if we have a recent prediction (within 3 seconds).
                // Use Interlocked.Read for thread-safe access to the ticks field written by the game thread.
                var predictionTicks = Interlocked.Read(ref _lastPredictionTimeTicks);
                var timeSincePrediction = predictionTicks == 0
                    ? double.MaxValue
                    : (DateTime.UtcNow - new DateTime(predictionTicks, DateTimeKind.Utc)).TotalSeconds;
                var predictedHeal = Interlocked.Exchange(ref _lastPredictedHealRaw, 0);
                if (predictedHeal > 0 && timeSincePrediction < 3.0)
                {
                    HealingCalculator.CalibrateFromActual(predictedHeal, totalHeal);
                }
            }
        }

        // Raise ability used event for timeline sync (once per action, not per target)
        OnAbilityUsed?.Invoke(casterEntityId, header->ActionId);

        // Raise local ability resolved with target count for Smart AoE tracking
        if (isFromLocalPlayer)
        {
            RecordLocalAbilityUsed(header->ActionId);
            OnLocalAbilityResolved?.Invoke(header->ActionId, (int)header->NumTargets);
        }
    }

    /// <summary>
    /// Updates the combat state. Call this when entering or leaving combat.
    /// </summary>
    public void UpdateCombatState(bool inCombat)
    {
        lock (_combatStateLock)
        {
            if (inCombat && !_isInCombat)
            {
                // Entering combat — assign start time before flipping the flag
                _combatStartTime = DateTime.UtcNow;
                _isInCombat = true;
            }
            else if (!inCombat && _isInCombat)
            {
                // Leaving combat — clear flag before clearing the time
                _isInCombat = false;
                _combatStartTime = null;
            }
        }
    }

    /// <summary>
    /// Gets the duration of the current combat in seconds.
    /// Returns 0 if not in combat.
    /// </summary>
    public float GetCombatDurationSeconds()
    {
        lock (_combatStateLock)
        {
            if (!_isInCombat || !_combatStartTime.HasValue)
                return 0f;

            return (float)(DateTime.UtcNow - _combatStartTime.Value).TotalSeconds;
        }
    }

    /// <summary>
    /// Whether the player is currently in combat.
    /// </summary>
    public bool IsInCombat => _isInCombat;

    public void Dispose()
    {
        receiveHook?.Dispose();
        actorControlHook?.Dispose();
        addScreenLogHook?.Dispose();
        shadowHp.Clear();
        log.Info("CombatEventService: Disposed");
    }
}
