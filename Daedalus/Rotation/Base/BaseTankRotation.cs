using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Party;
using Dalamud.Plugin.Services;
using Daedalus.Rotation.Common;
using Daedalus.Rotation.Tank;
using Daedalus.Services;
using Daedalus.Services.Action;
using Daedalus.Services.Debuff;
using Daedalus.Services.Party;
using Daedalus.Services.Prediction;
using Daedalus.Services.Stats;
using Daedalus.Services.Tank;
using Daedalus.Services.Targeting;
using Daedalus.Timeline;

namespace Daedalus.Rotation.Base;

/// <summary>
/// Base class for tank rotation implementations.
/// Provides ITankRotation interface implementation and tank-specific services.
/// </summary>
/// <typeparam name="TContext">The tank job-specific context type.</typeparam>
/// <typeparam name="TModule">The tank job-specific module interface type.</typeparam>
public abstract class BaseTankRotation<TContext, TModule> : BaseRotation<TContext, TModule>, ITankRotation
    where TContext : ITankRotationContext
    where TModule : IRotationModule<TContext>
{
    #region ITankRotation Implementation

    /// <inheritdoc />
    public bool IsMainTank { get; protected set; }

    /// <inheritdoc />
    public int GaugeValue { get; protected set; }

    #endregion

    #region Tank-Specific Services

    protected readonly IEnmityService EnmityService;
    protected readonly ITankCooldownService TankCooldownService;
    protected readonly ITimelineService? TimelineService;
    protected readonly IPartyCoordinationService? PartyCoordinationService;
    protected readonly Daedalus.Services.Positional.Navigation.IPositionalMovementService? PositionalMovementService;

    #endregion

    #region Combo Tracking

    /// <summary>
    /// Current combo step (1-3, or 0 for no combo).
    /// </summary>
    protected int ComboStep { get; set; }

    /// <summary>
    /// Last combo action ID.
    /// </summary>
    protected uint LastComboAction { get; set; }

    /// <summary>
    /// Time remaining on current combo chain.
    /// </summary>
    protected float ComboTimeRemaining { get; set; }

    #endregion

    #region Debug State

    // Tank rotations typically have both job-specific and common debug states
    protected readonly DebugState CommonDebugState = new();

    // Cached list to avoid per-frame heap allocation when passing player ID to DamageTrendService
    private readonly System.Collections.Generic.List<uint> _damageTrendIds = new(1);

    #endregion

    #region Constructor

    protected BaseTankRotation(
        IPluginLog log,
        IActionTracker actionTracker,
        ICombatEventService combatEventService,
        IDamageIntakeService damageIntakeService,
        IDamageTrendService damageTrendService,
        Configuration configuration,
        IObjectTable objectTable,
        IPartyList partyList,
        ITargetingService targetingService,
        IHpPredictionService hpPredictionService,
        ActionService actionService,
        IPlayerStatsService playerStatsService,
        IDebuffDetectionService debuffDetectionService,
        IEnmityService enmityService,
        ITankCooldownService tankCooldownService,
        ITimelineService? timelineService = null,
        IPartyCoordinationService? partyCoordinationService = null,
        IErrorMetricsService? errorMetrics = null,
        Daedalus.Services.Consumables.ITinctureDispatcher? tinctureDispatcher = null,
        Daedalus.Services.Pull.IPullIntentService? pullIntentService = null,
        Daedalus.Services.Positional.Navigation.IPositionalMovementService? positionalMovementService = null)
        : base(
            log,
            actionTracker,
            combatEventService,
            damageIntakeService,
            damageTrendService,
            configuration,
            objectTable,
            partyList,
            targetingService,
            hpPredictionService,
            actionService,
            playerStatsService,
            debuffDetectionService,
            errorMetrics,
            tinctureDispatcher: tinctureDispatcher,
            pullIntentService: pullIntentService)
    {
        EnmityService = enmityService;
        TankCooldownService = tankCooldownService;
        TimelineService = timelineService;
        PartyCoordinationService = partyCoordinationService;
        PositionalMovementService = positionalMovementService;
    }

    #endregion

    #region Tank-Specific Methods

    /// <summary>
    /// Reads the job-specific gauge value.
    /// Must be implemented by each tank job.
    /// </summary>
    protected abstract int ReadGaugeValue();

    /// <summary>
    /// Determines the current combo step based on the last combo action and timer.
    /// Must be implemented by each tank job since combo chains vary.
    /// </summary>
    protected abstract int DetermineComboStep(uint comboAction, float comboTimer);

    /// <summary>
    /// Updates combo state from game memory.
    /// </summary>
    protected virtual void UpdateComboState()
    {
        LastComboAction = SafeGameAccess.GetComboAction(ErrorMetrics);
        ComboTimeRemaining = SafeGameAccess.GetComboTimer(ErrorMetrics);
        ComboStep = DetermineComboStep(LastComboAction, ComboTimeRemaining);
    }

    /// <summary>
    /// Updates gauge value from game memory.
    /// </summary>
    protected virtual void UpdateGaugeValue()
    {
        GaugeValue = ReadGaugeValue();
    }

    /// <summary>
    /// Syncs tank-specific debug state to common debug state for UI compatibility.
    /// Override in derived classes to map job-specific fields.
    /// </summary>
    protected abstract void SyncDebugState(TContext context);

    #endregion

    #region Override Base Methods

    /// <summary>
    /// Updates tank-specific state (gauge, combo, enmity).
    /// </summary>
    protected override void UpdateJobSpecificServices(IPlayerCharacter player, bool inCombat)
    {
        // Read job gauge
        UpdateGaugeValue();

        // Read combo state
        UpdateComboState();

        // Pre-ranged walk-in (sub-15 GLD/MRD gathering)
        UpdatePreRangedWalkIn(player, inCombat);

        // Update damage trend service with player entity ID (tanks track their own damage intake)
        if (inCombat)
        {
            _damageTrendIds.Clear();
            _damageTrendIds.Add(player.EntityId);
            DamageTrendService.Update(1f / 60f, _damageTrendIds);
        }
    }

    /// <summary>
    /// All four tank ranged GCDs (Shield Lob / Tomahawk / Unmend / Lightning Shot) unlock at 15;
    /// below that a pre-job-stone GLD/MRD has NO tool for a mob outside melee.
    /// </summary>
    protected const byte TankRangedGcdMinLevel = 15;

    /// <summary>
    /// Below the ranged-GCD level, walk into melee on the engage target instead of standing there:
    /// a sub-15 GLD/MRD facing a parked ranged/caster mob has no Shield Lob/Tomahawk to tag it, no
    /// gap closer, and (unlike melee DPS) no max-melee walk-in — combat stalled until the mob
    /// happened to wander in. Reuses the melee max-melee maintenance path: one-directional walk-in
    /// with the vNav-flex dead-band, BMR destination/segment safety, and the movement arbiter's
    /// yield-to-BossMod + churn guards. Inert at 15+ (the ranged GCD gathers as before).
    /// </summary>
    private void UpdatePreRangedWalkIn(IPlayerCharacter player, bool inCombat)
    {
        if (PositionalMovementService == null || !inCombat)
            return;
        if (player.Level >= TankRangedGcdMinLevel)
            return;
        if (!Configuration.EnableAutoMovement || !Configuration.Tank.WalkToTargetWithoutRangedTool)
            return;

        // Raw hard target first (probe-free — the attackability probe false-negatives at range,
        // exactly when we need to walk in; same lesson as the melee walk-in fix), then the enemy
        // strategy so a gathered add we aren't hard-targeting still pulls us over.
        var target = TargetingService.GetRawEnemyHardTarget() as Dalamud.Game.ClientState.Objects.Types.IBattleChara
            ?? TargetingService.FindEnemy(Configuration.Targeting.EnemyStrategy, 25f, player)
                as Dalamud.Game.ClientState.Objects.Types.IBattleChara;
        if (target == null)
            return;

        var snapshot = new Daedalus.Services.Positional.Navigation.PositionalMovementTarget(
            target.Position,
            target.HitboxRadius,
            target.Rotation,
            HasPositionalImmunity: true); // tanks have no positionals; arcs are disabled below anyway

        var request = new Daedalus.Services.Positional.Navigation.PositionalMovementUpdateRequest(
            AnticipationProvider: null,
            AnticipationContext: default,
            PlayerPosition: player.Position,
            PlayerHitboxRadius: player.HitboxRadius,
            Target: snapshot,
            ActionService: ActionService,
            InCombat: inCombat,
            EnableMovement: false,
            MaintainMaxMelee: true,
            MaxMeleeTarget: snapshot,
            MaxMeleeTargetFollowsPlayer: true,
            VNavFlex: Configuration.Nav.VNavFlex);

        PositionalMovementService.Update(in request);
    }

    /// <summary>
    /// Override to sync debug state after module updates.
    /// </summary>
    protected override void UpdateModuleDebugStates(TContext context)
    {
        base.UpdateModuleDebugStates(context);

        // Sync tank debug state to common state for UI
        if (Configuration.IsDebugWindowOpen)
        {
            SyncDebugState(context);
        }
    }

    #endregion
}
