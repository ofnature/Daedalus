namespace Daedalus.Services;

/// <summary>
/// One damage effect from any friendly or hostile caster, decoded from the ActionEffect hook.
/// Extended values (hits over 65,535) are already unpacked into <see cref="Amount"/>.
/// </summary>
public readonly record struct DamageDealtEvent(
    uint CasterEntityId,
    uint TargetEntityId,
    int Amount,
    uint ActionId,
    bool IsCrit,
    bool IsDirectHit);

/// <summary>
/// One HoT/DoT tick from the ActorControl packet stream (category 23 — ticks never appear
/// in ActionEffect). Param semantics are the community-documented layout: p1 = effect
/// (status) id, p2 = kind, p3 = amount, p4 = source entity id on newer packets (0 when
/// absent). Receivers should attribute via <see cref="PossibleSourceId"/> first, then by
/// finding <see cref="EffectId"/> on the target's status list.
/// </summary>
public readonly record struct DotTickEvent(
    uint TargetEntityId,
    uint EffectId,
    uint Kind,
    int Amount,
    uint PossibleSourceId);

/// <summary>
/// Interface for combat event tracking, primarily used for shadow HP management.
/// </summary>
public interface ICombatEventService
{
    /// <summary>ActorControl hook installed and enabled (DoT tick source). Diagnostics.</summary>
    bool ActorControlHookActive => false;

    /// <summary>Total ActorControl packets seen by the hook this session. Diagnostics.</summary>
    long ActorControlInvocations => 0;

    /// <summary>ActorControl packets with the HoT/DoT category (23) this session. Diagnostics.</summary>
    long ActorControlHotDotCount => 0;

    /// <summary>Per-category ActorControl packet counts this session ("cat×count …"). Diagnostics.</summary>
    string DescribeActorControlCategories() => "";

    /// <summary>AddScreenLog (fly-text) hook installed and enabled. Diagnostics.</summary>
    bool ScreenLogHookActive => false;

    /// <summary>Total fly-text entries seen by the AddScreenLog hook this session. Diagnostics.</summary>
    long ScreenLogInvocations => 0;

    /// <summary>Fly-text entries targeting hostile NPCs this session (where DoT ticks live). Diagnostics.</summary>
    long ScreenLogHostileTargetCount => 0;

    /// <summary>
    /// Event raised when a healing effect from the local player lands.
    /// The uint parameter is the target entity ID that received the heal.
    /// </summary>
    event System.Action<uint>? OnLocalPlayerHealLanded;

    /// <summary>
    /// Event raised when damage is received by any party member.
    /// Parameters: (entityId, damageAmount)
    /// </summary>
    event System.Action<uint, int>? OnDamageReceived;

    /// <summary>
    /// Event raised when the local player deals damage to any target.
    /// Used for personal DPS tracking in analytics.
    /// Parameters: (targetEntityId, damageAmount, actionId)
    /// </summary>
    event System.Action<uint, int, uint>? OnLocalPlayerDamageDealt;

    /// <summary>
    /// Event raised per damage effect from ANY caster in range — party members, Trust NPCs,
    /// pets, enemies. Used by the DPS parser. Raised from the hook thread; subscribers must
    /// enqueue and process on the framework thread.
    /// </summary>
    event System.Action<DamageDealtEvent>? OnDamageDealt;

    /// <summary>
    /// Event raised per HoT/DoT tick (ActorControl category 23). Used by the DPS parser for
    /// DoT attribution. Raised from the hook thread; subscribers must enqueue.
    /// </summary>
    event System.Action<DotTickEvent>? OnDotTick;

    /// <summary>
    /// Event raised when any heal effect lands (from any source, not just local player).
    /// Used for co-healer tracking.
    /// Parameters: (healerEntityId, targetEntityId, healAmount)
    /// </summary>
    event System.Action<uint, uint, int>? OnAnyHealReceived;

    /// <summary>
    /// Event raised when any ability is used (action effect resolves).
    /// Used for timeline sync.
    /// Parameters: (sourceEntityId, actionId)
    /// </summary>
    event System.Action<uint, uint>? OnAbilityUsed;

    /// <summary>
    /// Event raised when a local player ability resolves with target count.
    /// Parameters: (actionId, targetCount)
    /// </summary>
    event System.Action<uint, int>? OnLocalAbilityResolved;

    /// <summary>
    /// Gets the shadow HP for an entity, or the fallback value if not tracked.
    /// </summary>
    uint GetShadowHp(uint entityId, uint fallbackHp);

    /// <summary>
    /// Registers a predicted heal amount for calibration when the heal lands.
    /// </summary>
    void RegisterPredictionForCalibration(int predictedAmount);

    /// <summary>
    /// Gets aggregated overheal statistics for the current session.
    /// </summary>
    CombatEventService.OverhealStatistics GetOverhealStatistics();

    /// <summary>
    /// Resets all overheal statistics for a new session.
    /// </summary>
    void ResetOverhealStatistics();

    /// <summary>
    /// Notifies the service that combat state has changed.
    /// Call this when entering or leaving combat.
    /// </summary>
    void UpdateCombatState(bool inCombat);

    /// <summary>
    /// Gets the duration of the current combat in seconds.
    /// Returns 0 if not in combat.
    /// </summary>
    float GetCombatDurationSeconds();

    /// <summary>
    /// Whether the player is currently in combat.
    /// </summary>
    bool IsInCombat { get; }
}
