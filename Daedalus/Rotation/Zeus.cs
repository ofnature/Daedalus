using System.Collections.Generic;
using Dalamud.Game.ClientState.JobGauge;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Party;
using Dalamud.Plugin.Services;
using Daedalus.Data;
using Daedalus.Rotation.Base;
using Daedalus.Rotation.Common;
using Daedalus.Rotation.Common.Helpers;
using Daedalus.Rotation.Common.Scheduling;
using Daedalus.Rotation.ZeusCore.Context;
using Daedalus.Rotation.ZeusCore.Helpers;
using Daedalus.Rotation.ZeusCore.Modules;
using Daedalus.Services;
using Daedalus.Services.Action;
using Daedalus.Services.Debuff;
using Daedalus.Services.Party;
using Daedalus.Services.Positional;
using Daedalus.Services.Positional.Navigation;
using Daedalus.Services.Prediction;
using Daedalus.Services.Stats;
using Daedalus.Services.Targeting;
using Daedalus.Services.Training;
using Daedalus.Timeline;

namespace Daedalus.Rotation;

/// <summary>
/// Dragoon rotation module (RSR-style reactive execution).
/// Orchestrates modular execution: each module handles a specific concern.
/// Named after Zeus, the Greek god of sky and thunder.
/// </summary>
[Rotation("Zeus", JobRegistry.Dragoon, JobRegistry.Lancer, Role = RotationRole.MeleeDps)]
public sealed class Zeus : BaseMeleeDpsRotation<IZeusContext, IZeusModule>
{
    /// <inheritdoc />
    public override string Name => "Zeus";

    /// <inheritdoc />
    public override uint[] SupportedJobIds => [JobRegistry.Dragoon, JobRegistry.Lancer];

    /// <inheritdoc />
    public override DebugState DebugState => _debugState;

    /// <inheritdoc />
    protected override List<IZeusModule> Modules => _modules;

    /// <summary>
    /// Gets the Zeus-specific debug state. Used for Dragoon-specific debug display.
    /// </summary>
    public ZeusDebugState ZeusDebug => _zeusDebugState;

    // Persistent debug state
    private readonly ZeusDebugState _zeusDebugState = new();

    // IRotation-compatible debug state (for common debug interface)
    private readonly DebugState _debugState = new();

    // Helpers (shared across modules)
    private readonly ZeusStatusHelper _statusHelper;
    private readonly MeleeDpsPartyHelper _partyHelper;

    // Modules (sorted by priority - lower = higher priority)
    private readonly List<IZeusModule> _modules;

    // Timeline service for fight-aware rotation (optional)
    private readonly ITimelineService? _timelineService;

    // Party coordination service for raid buff synchronization (optional)
    private readonly IPartyCoordinationService? _partyCoordinationService;

    // Training service for decision explanations (optional)
    private readonly ITrainingService? _trainingService;

    // Gauge values (read each frame)
    private int _firstmindsFocus;
    private int _eyeCount;
    private bool _isLifeOfDragonActive;
    private float _lifeOfDragonRemaining;

    // Positional anticipation
    private readonly DelegatePositionalAnticipationProvider _positionalAnticipationProvider;

    // Scheduler
    private readonly RotationScheduler _scheduler;

    public Zeus(
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
        IPositionalService positionalService,
        IJobGauges jobGauges,
        IPositionalMovementService? positionalMovementService = null,
        ITimelineService? timelineService = null,
        IPartyCoordinationService? partyCoordinationService = null,
        ITrainingService? trainingService = null,
        IBurstWindowService? burstWindowService = null,
        IErrorMetricsService? errorMetrics = null,
        Daedalus.Services.Consumables.ITinctureDispatcher? tinctureDispatcher = null,
        Daedalus.Services.Pull.IPullIntentService? pullIntentService = null)
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
            positionalService,
            burstWindowService,
            errorMetrics,
            positionalMovementService: positionalMovementService,
            tinctureDispatcher: tinctureDispatcher,
            pullIntentService: pullIntentService)
    {
        _timelineService = timelineService;
        _partyCoordinationService = partyCoordinationService;
        _trainingService = trainingService;

        _positionalAnticipationProvider = new DelegatePositionalAnticipationProvider(() =>
        {
            var player = ObjectTable.LocalPlayer;
            return player == null ? null : ResolveNextRequiredPositional(player);
        });
        _scheduler = new RotationScheduler(actionService, jobGauges, configuration, timelineService, errorMetrics);

        // Initialize helpers
        _statusHelper = new ZeusStatusHelper();
        _partyHelper = new MeleeDpsPartyHelper(objectTable, partyList);

        // Initialize modules (ordered by priority - lower = executed first)
        _modules = new List<IZeusModule>
        {
            new BuffModule(BurstWindowService),    // Priority 20 - Buff management (Lance Charge, Battle Litany, Life Surge)
            new DamageModule(BurstWindowService, SmartAoEService),  // Priority 30 - DPS rotation
        };

        // Sort by priority
        _modules.Sort((a, b) => a.Priority.CompareTo(b.Priority));
    }

    #region Abstract Implementation

    /// <inheritdoc />
    protected override void ReadGaugeValues()
    {
        _firstmindsFocus = SafeGameAccess.GetDrgFirstmindsFocus(ErrorMetrics);
        _eyeCount = SafeGameAccess.GetDrgEyeCount(ErrorMetrics);
        _isLifeOfDragonActive = SafeGameAccess.IsDrgLifeOfDragonActive(ErrorMetrics);
        _lifeOfDragonRemaining = SafeGameAccess.GetDrgLifeOfDragonTimer(ErrorMetrics);
    }

    /// <summary>
    /// DRG positionals — LIVE-SHEET facts (verified 2026-07-20, user tooltip + XIVAPI: the
    /// "7.0 removed all DRG positionals" claim was stale, a later patch re-added them):
    /// Chaos Thrust / Chaotic Spring = REAR, Fang and Claw = FLANK, Wheeling Thrust = REAR;
    /// Drakesbane / Heavens' Thrust / Spiral Blow have none. Since 7.0 these are COMBO-chained
    /// (the old Bared / Wheel in Motion proc statuses no longer exist), so the next arc comes
    /// from the combo position — dual ids per step (base + upgrade), same rule as ComputeComboStep.
    /// </summary>
    protected override PositionalType? GetNextRequiredPositional()
    {
        var player = ObjectTable.LocalPlayer;
        if (player == null) return null;

        return ComputeNextPositional(LastComboAction);
    }

    internal static PositionalType? ComputeNextPositional(uint lastComboAction) => lastComboAction switch
    {
        87 or 36955 => PositionalType.Rear,   // Disembowel / Spiral Blow → Chaotic Spring (rear)
        88 or 25772 => PositionalType.Rear,   // Chaos Thrust / Chaotic Spring → Wheeling Thrust (rear)
        84 or 25771 => PositionalType.Flank,  // Full Thrust / Heavens' Thrust → Fang and Claw (flank)
        _ => null,                            // starter/step-2/Drakesbane next → no positional
    };

    /// <inheritdoc />
    protected override IPositionalAnticipationProvider? GetPositionalAnticipationProvider()
        => _positionalAnticipationProvider;

    /// <inheritdoc />
    protected override bool IsPositionalMovementEnabled()
        => Configuration.Dragoon.EnablePositionalMovement || Configuration.Dragoon.EnforcePositionals;

    /// <inheritdoc />
    protected override int DetermineComboStep(uint comboAction, float comboTimer)
        => ComputeComboStep(comboAction, comboTimer);

    internal static int ComputeComboStep(uint comboAction, float comboTimer)
    {
        // Dragoon uses traditional combo system
        if (comboTimer <= 0 || comboAction == 0)
            return 0;

        // Determine combo step based on last action
        return comboAction switch
        {
            // Single-target combo — every step must list base AND upgrade ids: the game reports
            // the EXECUTED id, and a missing upgrade id resets the combo at that step (Lv100
            // field log 2026-07-05: TT→Disembowel looped forever because Spiral Blow (96+)
            // wasn't mapped — step 3 never fired for the entire dungeon).
            75 or 16479 => 1,     // True Thrust / Raiden Thrust (proc'd starter — game reports this id)
            78 or 36954 => 2,     // Vorpal Thrust / Lance Barrage (Lv96 upgrade)
            87 or 36955 => 2,     // Disembowel / Spiral Blow (Lv96 upgrade)
            84 or 25771 => 3,  // Full Thrust / Heavens' Thrust
            88 or 25772 => 3,  // Chaos Thrust / Chaotic Spring

            // AoE combo
            86 or 25770 => 1,     // Doom Spike / Draconian Fury (proc'd AoE starter)
            7397 => 2,   // Sonic Thrust
            16477 => 3,  // Coerthan Torment

            _ => 0
        };
    }

    /// <summary>
    /// Updates MP forecast. Dragoons don't use MP for abilities.
    /// </summary>
    protected override void UpdateMpForecast(IPlayerCharacter player)
    {
        // Dragoons don't use MP for any abilities
        MpForecastService.Update(
            (int)player.CurrentMp,
            (int)player.MaxMp,
            hasLucidDreaming: false);
    }

    /// <inheritdoc />
    protected override IZeusContext CreateContext(IPlayerCharacter player, bool inCombat, bool isMoving)
    {
        return new ZeusContext(
            player: player,
            inCombat: inCombat,
            isMoving: isMoving,
            canExecuteGcd: ActionService.CanExecuteGcd,
            canExecuteOgcd: ActionService.CanExecuteOgcd,
            actionService: ActionService,
            actionTracker: ActionTracker,
            combatEventService: CombatEventService,
            damageIntakeService: DamageIntakeService,
            damageTrendService: DamageTrendService,
            frameCache: FrameCache,
            configuration: Configuration,
            debuffDetectionService: DebuffDetectionService,
            hpPredictionService: HpPredictionService,
            mpForecastService: MpForecastService,
            playerStatsService: PlayerStatsService,
            targetingService: TargetingService,
            objectTable: ObjectTable,
            partyList: PartyList,
            positionalService: PositionalService,
            statusHelper: _statusHelper,
            partyHelper: _partyHelper,
            debugState: _zeusDebugState,
            firstmindsFocus: _firstmindsFocus,
            eyeCount: _eyeCount,
            isLifeOfDragonActive: _isLifeOfDragonActive,
            lifeOfDragonRemaining: _lifeOfDragonRemaining,
            comboStep: ComboStep,
            lastComboAction: LastComboAction,
            comboTimeRemaining: ComboTimeRemaining,
            isAtRear: IsAtRear,
            isAtFlank: IsAtFlank,
            targetHasPositionalImmunity: TargetHasPositionalImmunity,
            timelineService: _timelineService,
            partyCoordinationService: _partyCoordinationService,
            trainingService: _trainingService,
            log: Log);
    }

    /// <inheritdoc />
    protected override void SyncDebugState(IZeusContext context)
    {
        // Map Dragoon debug state to common debug state fields
        _debugState.PlanningState = _zeusDebugState.PlanningState;
        _debugState.PlannedAction = _zeusDebugState.PlannedAction;
        _debugState.DpsState = _zeusDebugState.DamageState;
        // Note: BuffState is tracked in ZeusDebugState but not in common DebugState

        EnemyPackDebugHelper.SyncAoEDps(_debugState, _zeusDebugState, context.Configuration.Dragoon.AoEMinTargets, JobAoERadiusYalms.Melee);

        // Party/player info
        _debugState.PlayerHpPercent = (float)context.Player.CurrentHp / context.Player.MaxHp;
        _debugState.PartyListCount = context.PartyList.Length;
        _debugState.TargetInfo = TargetingDebugHelper.FormatTargetInfo(null, context.TargetingService);
    }

    /// <summary>
    /// Scheduler-driven execution. CollectCandidates runs per module, then the
    /// scheduler dispatches the highest-priority gate-passing candidate per phase.
    /// </summary>
    protected override void ExecuteModules(IZeusContext context, bool isMoving, bool inCombat)
    {
        if (Configuration.Targeting.PauseAllOnStandStillPunisher
            && PlayerSafetyHelper.IsStandStillPunisherActive(context.Player))
            return;
        if (Configuration.Targeting.PauseOnPlayerChannel
            && PlayerSafetyHelper.IsPlayerIntentChannelActive(context.Player))
            return;

        if (TryDispatchTincture(context, inCombat)) return;

        _scheduler.Reset();
        foreach (var module in _modules)
            module.CollectCandidates(context, _scheduler, isMoving);

        if (inCombat && ActionService.CanExecuteOgcd)
            _scheduler.DispatchOgcd(context);

        if (ActionService.CanExecuteGcd)
        {
            var gcd = _scheduler.DispatchGcd(context);
            if (StuckReasonHelper.Describe(gcd.Dispatched, gcd.GateFailReasons) is { } stuck)
                context.Debug.DamageState = stuck;
        }
    }

    #endregion
}
