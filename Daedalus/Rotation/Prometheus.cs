using System.Collections.Generic;
using Dalamud.Game.ClientState.JobGauge;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.ClientState.Party;
using Dalamud.Plugin.Services;
using Daedalus.Data;
using Daedalus.Rotation.Base;
using Daedalus.Rotation.Common;
using Daedalus.Rotation.Common.Helpers;
using Daedalus.Rotation.Common.Scheduling;
using Daedalus.Rotation.PrometheusCore.Context;
using Daedalus.Rotation.PrometheusCore.Helpers;
using Daedalus.Rotation.PrometheusCore.Modules;
using Daedalus.Services;
using Daedalus.Services.Action;
using Daedalus.Services.Content;
using Daedalus.Services.Debuff;
using Daedalus.Services.Party;
using Daedalus.Services.Prediction;
using Daedalus.Services.Stats;
using Daedalus.Services.Targeting;
using Daedalus.Services.Training;
using Daedalus.Timeline;

namespace Daedalus.Rotation;

/// <summary>
/// Machinist rotation module (RSR-style reactive execution).
/// Orchestrates modular execution: each module handles a specific concern.
/// Named after Prometheus, the Greek titan of fire and technology.
/// </summary>
[Rotation("Prometheus", JobRegistry.Machinist, Role = RotationRole.RangedDps)]
public sealed class Prometheus : BaseRangedDpsRotation<IPrometheusContext, IPrometheusModule>
{
    /// <inheritdoc />
    public override string Name => "Prometheus";

    /// <inheritdoc />
    public override uint[] SupportedJobIds => [JobRegistry.Machinist];

    /// <inheritdoc />
    public override DebugState DebugState => _debugState;

    /// <inheritdoc />
    protected override List<IPrometheusModule> Modules => _modules;

    /// <summary>
    /// Gets the Prometheus-specific debug state. Used for Machinist-specific debug display.
    /// </summary>
    public PrometheusDebugState PrometheusDebug => _prometheusDebugState;

    // Persistent debug state
    private readonly PrometheusDebugState _prometheusDebugState = new();

    // IRotation-compatible debug state (for common debug interface)
    private readonly DebugState _debugState = new();

    // Helpers (shared across modules)
    private readonly PrometheusStatusHelper _statusHelper;
    private readonly RangedDpsPartyHelper _partyHelper;

    // Modules (sorted by priority - lower = higher priority)
    private readonly List<IPrometheusModule> _modules;

    // Timeline service for fight-aware rotation (optional)
    private readonly ITimelineService? _timelineService;

    // Party coordination service for burst alignment (optional)
    private readonly IPartyCoordinationService? _partyCoordinationService;

    // Training service for explaining rotation decisions (optional)
    private readonly ITrainingService? _trainingService;

    // Duty classification for trial/raid vs dungeon Queen logic
    private readonly IDutyContentService? _dutyContentService;

    // RSR 14-step Queen battery tracker (trial/raid only)
    private readonly PrometheusQueenTracker _queenTracker = new();

    // Gauge values (read each frame)
    private int _heat;
    private int _battery;
    private float _overheatRemaining;
    private float _queenRemaining;
    private int _lastQueenBattery;

    // Scheduler
    private readonly RotationScheduler _scheduler;

    public Prometheus(
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
        IJobGauges jobGauges,
        ITimelineService? timelineService = null,
        IPartyCoordinationService? partyCoordinationService = null,
        ITrainingService? trainingService = null,
        IBurstWindowService? burstWindowService = null,
        IErrorMetricsService? errorMetrics = null,
        Daedalus.Services.Consumables.ITinctureDispatcher? tinctureDispatcher = null,
        Daedalus.Services.Pull.IPullIntentService? pullIntentService = null,
        IDutyContentService? dutyContentService = null)
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
            burstWindowService,
            errorMetrics,
            tinctureDispatcher: tinctureDispatcher,
            pullIntentService: pullIntentService)
    {
        _timelineService = timelineService;
        _partyCoordinationService = partyCoordinationService;
        _trainingService = trainingService;
        _dutyContentService = dutyContentService;

        _scheduler = new RotationScheduler(actionService, jobGauges, configuration, timelineService, errorMetrics);

        PrePullModule?.Register(new PrometheusPrePullCandidate());

        // Initialize helpers
        _statusHelper = new PrometheusStatusHelper();
        _partyHelper = new RangedDpsPartyHelper(objectTable, partyList);

        // Initialize modules (ordered by priority - lower = executed first)
        _modules = new List<IPrometheusModule>
        {
            new BuffModule(BurstWindowService, _dutyContentService, _queenTracker),    // Priority 20 - Buff management (Wildfire, Hypercharge, Queen)
            new DamageModule(BurstWindowService, SmartAoEService),  // Priority 30 - DPS rotation
        };

        // Sort by priority
        _modules.Sort((a, b) => a.Priority.CompareTo(b.Priority));
    }

    /// <inheritdoc />
    public override void OnTerritoryChanged(ushort territoryType)
    {
        _queenTracker.Reset();
    }

    #region Abstract Implementation

    /// <inheritdoc />
    protected override void ReadGaugeValues()
    {
        _heat = SafeGameAccess.GetMchHeat(ErrorMetrics);
        _battery = SafeGameAccess.GetMchBattery(ErrorMetrics);
        _overheatRemaining = SafeGameAccess.GetMchOverheatTimer(ErrorMetrics);
        _queenRemaining = SafeGameAccess.GetMchQueenTimer(ErrorMetrics);
        _lastQueenBattery = SafeGameAccess.GetMchLastQueenBattery(ErrorMetrics);
    }

    /// <inheritdoc />
    protected override int DetermineComboStep(uint comboAction, float comboTimer)
        => ComputeComboStep(comboAction, comboTimer);

    internal static int ComputeComboStep(uint comboAction, float comboTimer)
    {
        // MCH basic combo: Split Shot -> Slug Shot -> Clean Shot
        // (Heated versions at higher levels)
        if (comboTimer <= 0)
            return 0;

        return comboAction switch
        {
            // Combo starters
            2866 => 1, // Split Shot
            7411 => 1, // Heated Split Shot

            // Second hits
            2868 => 2, // Slug Shot
            7412 => 2, // Heated Slug Shot

            _ => 0
        };
    }

    /// <summary>
    /// Updates MP forecast. Machinists don't use MP for abilities.
    /// </summary>
    protected override void UpdateMpForecast(IPlayerCharacter player)
    {
        // Machinists don't use MP for any abilities
        MpForecastService.Update(
            (int)player.CurrentMp,
            (int)player.MaxMp,
            hasLucidDreaming: false);
    }

    /// <inheritdoc />
    protected override IPrometheusContext CreateContext(IPlayerCharacter player, bool inCombat, bool isMoving)
    {
        return new PrometheusContext(
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
            statusHelper: _statusHelper,
            partyHelper: _partyHelper,
            debugState: _prometheusDebugState,
            heat: _heat,
            battery: _battery,
            overheatRemaining: _overheatRemaining,
            queenRemaining: _queenRemaining,
            lastQueenBattery: _lastQueenBattery,
            comboStep: ComboStep,
            lastComboAction: LastComboAction,
            comboTimeRemaining: ComboTimeRemaining,
            timelineService: _timelineService,
            log: Log,
            partyCoordinationService: _partyCoordinationService,
            trainingService: _trainingService);
    }

    /// <inheritdoc />
    protected override void SyncDebugState(IPrometheusContext context)
    {
        _debugState.PlannedAction = string.IsNullOrEmpty(_prometheusDebugState.PlannedAction)
            ? "None" : _prometheusDebugState.PlannedAction;
        _debugState.DpsState = string.IsNullOrEmpty(_prometheusDebugState.DamageState)
            ? (context.InCombat ? "Idle" : "Out of combat") : _prometheusDebugState.DamageState;

        if (!string.IsNullOrEmpty(_prometheusDebugState.BuffState))
            _debugState.PlanningState = _prometheusDebugState.BuffState;
        else if (!string.IsNullOrEmpty(_prometheusDebugState.DamageState))
            _debugState.PlanningState = _prometheusDebugState.DamageState;
        else
            _debugState.PlanningState = context.InCombat ? "Active" : "Idle";

        var aoeMin = context.Configuration.Machinist.AoEMinTargets;
        EnemyPackDebugHelper.SyncAoEDps(_debugState, _prometheusDebugState, aoeMin, JobAoERadiusYalms.PhysicalRanged);

        var target = TargetingService.FindEnemy(
            context.Configuration.Targeting.EnemyStrategy,
            FFXIVConstants.RangedTargetingRange,
            context.Player)
            ?? TargetingService.FindNearbyEnemy(FFXIVConstants.RangedTargetingRange, context.Player)
               as IBattleChara;
        _debugState.TargetInfo = target != null
            ? $"{target.Name} ({(float)target.CurrentHp / target.MaxHp:P0})"
            : "None";

        _debugState.PlayerHpPercent = (float)context.Player.CurrentHp / context.Player.MaxHp;
        _debugState.PartyListCount = context.PartyList.Length;
    }

    /// <inheritdoc />
    protected override void ExecuteModules(IPrometheusContext context, bool isMoving, bool inCombat)
    {
        if (Configuration.Targeting.PauseAllOnStandStillPunisher
            && PlayerSafetyHelper.IsStandStillPunisherActive(context.Player))
            return;
        if (Configuration.Targeting.PauseOnPlayerChannel
            && PlayerSafetyHelper.IsPlayerIntentChannelActive(context.Player))
            return;

        if (TryDispatchTincture(context, inCombat)) return;

        if (!inCombat && PrometheusPrePullCandidate.TryDispatchPrePullGcd(context))
            return;

        _scheduler.Reset();
        foreach (var module in _modules)
            module.CollectCandidates(context, _scheduler, isMoving);

        if (inCombat && ActionService.CanExecuteOgcd)
            _scheduler.DispatchOgcd(context);

        if (ActionService.CanExecuteGcd)
            _scheduler.DispatchGcd(context);

        SyncDebugState(context);
    }

    #endregion
}
