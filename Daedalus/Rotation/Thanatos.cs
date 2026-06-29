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
using Daedalus.Rotation.ThanatosCore.Context;
using Daedalus.Rotation.ThanatosCore.Helpers;
using Daedalus.Rotation.ThanatosCore.Modules;
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
/// Reaper rotation module (RSR-style reactive execution).
/// Orchestrates modular execution: each module handles a specific concern.
/// Named after Thanatos, the Greek god of death.
/// </summary>
[Rotation("Thanatos", JobRegistry.Reaper, Role = RotationRole.MeleeDps)]
public sealed class Thanatos : BaseMeleeDpsRotation<IThanatosContext, IThanatosModule>
{
    /// <inheritdoc />
    public override string Name => "Thanatos";

    /// <inheritdoc />
    public override uint[] SupportedJobIds => [JobRegistry.Reaper];

    /// <inheritdoc />
    public override DebugState DebugState => _debugState;

    /// <inheritdoc />
    protected override List<IThanatosModule> Modules => _modules;

    /// <summary>
    /// Gets the Thanatos-specific debug state. Used for Reaper-specific debug display.
    /// </summary>
    public ThanatosDebugState ThanatosDebug => _thanatosDebugState;

    // Persistent debug state
    private readonly ThanatosDebugState _thanatosDebugState = new();

    // IRotation-compatible debug state (for common debug interface)
    private readonly DebugState _debugState = new();

    // Helpers (shared across modules)
    private readonly ThanatosStatusHelper _statusHelper;
    private readonly MeleeDpsPartyHelper _partyHelper;

    // Modules (sorted by priority - lower = higher priority)
    private readonly List<IThanatosModule> _modules;

    // Timeline service for fight-aware rotation (optional)
    private readonly ITimelineService? _timelineService;

    // Party coordination service for raid buff synchronization (optional)
    private readonly IPartyCoordinationService? _partyCoordinationService;

    // Training service for explaining rotation decisions (optional)
    private readonly ITrainingService? _trainingService;

    // Duty classifier for the content-aware Enshroud low-HP gate (optional)
    private readonly Daedalus.Services.Content.IDutyContentService? _dutyContentService;

    // Gauge values (read each frame)
    private int _soul;
    private int _shroud;
    private int _lemureShroud;
    private int _voidShroud;
    private float _enshroudTimer;

    // Positional anticipation
    private readonly DelegatePositionalAnticipationProvider _positionalAnticipationProvider;

    // Scheduler
    private readonly RotationScheduler _scheduler;

    public Thanatos(
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
        Daedalus.Services.Pull.IPullIntentService? pullIntentService = null,
        Daedalus.Services.Content.IDutyContentService? dutyContentService = null)
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
        _dutyContentService = dutyContentService;

        _positionalAnticipationProvider = new DelegatePositionalAnticipationProvider(() =>
        {
            var player = ObjectTable.LocalPlayer;
            return player == null ? null : ResolveNextRequiredPositional(player);
        });
        _scheduler = new RotationScheduler(actionService, jobGauges, configuration, timelineService, errorMetrics);

        // Initialize helpers
        _statusHelper = new ThanatosStatusHelper();
        _partyHelper = new MeleeDpsPartyHelper(objectTable, partyList);

        // Initialize modules (ordered by priority - lower = executed first)
        _modules = new List<IThanatosModule>
        {
            new BuffModule(BurstWindowService, _dutyContentService),    // Priority 20 - Buff management (Arcane Circle, Enshroud)
            new DamageModule(BurstWindowService, SmartAoEService),  // Priority 30 - DPS rotation
        };

        // Sort by priority
        _modules.Sort((a, b) => a.Priority.CompareTo(b.Priority));
    }

    #region Abstract Implementation

    /// <inheritdoc />
    protected override void ReadGaugeValues()
    {
        _soul = SafeGameAccess.GetRprSoul(ErrorMetrics);
        _shroud = SafeGameAccess.GetRprShroud(ErrorMetrics);
        _lemureShroud = SafeGameAccess.GetRprLemureShroud(ErrorMetrics);
        _voidShroud = SafeGameAccess.GetRprVoidShroud(ErrorMetrics);
        _enshroudTimer = SafeGameAccess.GetRprEnshroudTimer(ErrorMetrics);
    }

    /// <summary>
    /// RPR positionals: Gibbet = flank, Gallows = rear.
    /// Enhanced status determines which one is buffed next.
    /// </summary>
    protected override PositionalType? GetNextRequiredPositional()
    {
        var player = ObjectTable.LocalPlayer;
        if (player == null) return null;

        foreach (var s in player.StatusList)
        {
            // EnhancedGibbet → next should be Gibbet (flank)
            if (s.StatusId == RPRActions.StatusIds.EnhancedGibbet)
                return PositionalType.Flank;
            // EnhancedGallows → next should be Gallows (rear)
            if (s.StatusId == RPRActions.StatusIds.EnhancedGallows)
                return PositionalType.Rear;
        }

        // Default: Gallows (rear) if no enhanced buff
        return null;
    }

    /// <inheritdoc />
    protected override IPositionalAnticipationProvider? GetPositionalAnticipationProvider()
        => _positionalAnticipationProvider;

    /// <inheritdoc />
    protected override bool IsPositionalMovementEnabled()
        => Configuration.Reaper.EnablePositionalMovement || Configuration.Reaper.EnforcePositionals;

    /// <inheritdoc />
    protected override int DetermineComboStep(uint comboAction, float comboTimer)
        => ComputeComboStep(comboAction, comboTimer);

    internal static int ComputeComboStep(uint comboAction, float comboTimer)
    {
        // Reaper combos:
        // ST: Slice -> Waxing Slice -> Infernal Slice
        // AoE: Spinning Scythe -> Nightmare Scythe
        if (comboTimer <= 0)
            return 0;

        return comboAction switch
        {
            // Single target combo
            24373 => 1, // Slice
            24374 => 2, // Waxing Slice

            // AoE combo
            24376 => 1, // Spinning Scythe

            _ => 0
        };
    }

    /// <summary>
    /// Updates MP forecast. Reapers don't use MP for abilities.
    /// </summary>
    protected override void UpdateMpForecast(IPlayerCharacter player)
    {
        // Reapers don't use MP for any abilities
        MpForecastService.Update(
            (int)player.CurrentMp,
            (int)player.MaxMp,
            hasLucidDreaming: false);
    }

    /// <inheritdoc />
    protected override IThanatosContext CreateContext(IPlayerCharacter player, bool inCombat, bool isMoving)
    {
        return new ThanatosContext(
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
            debugState: _thanatosDebugState,
            soul: _soul,
            shroud: _shroud,
            lemureShroud: _lemureShroud,
            voidShroud: _voidShroud,
            enshroudTimer: _enshroudTimer,
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
    protected override void SyncDebugState(IThanatosContext context)
    {
        // Map Reaper debug state to common debug state fields
        _debugState.PlanningState = _thanatosDebugState.PlanningState;
        _debugState.PlannedAction = _thanatosDebugState.PlannedAction;
        _debugState.DpsState = _thanatosDebugState.DamageState;
        // Note: BuffState is tracked in ThanatosDebugState but not in common DebugState

        EnemyPackDebugHelper.SyncAoEDps(_debugState, _thanatosDebugState, context.Configuration.Reaper.AoEMinTargets, JobAoERadiusYalms.Melee);

        // Party/player info
        _debugState.PlayerHpPercent = (float)context.Player.CurrentHp / context.Player.MaxHp;
        _debugState.PartyListCount = context.PartyList.Length;
        _debugState.TargetInfo = TargetingDebugHelper.FormatTargetInfo(null, context.TargetingService);
    }

    /// <inheritdoc />
    protected override void ExecuteModules(IThanatosContext context, bool isMoving, bool inCombat)
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
        {
            var ogcd = _scheduler.DispatchOgcd(context);
            // Surface why Enshroud was refused at execution (UseAction returned false) when it queued
            // but didn't win — otherwise the Enshroud row stays on the stale "Ready — queued" breadcrumb.
            if (ogcd.GateFailReasons is { } reasons)
            {
                foreach (var r in reasons)
                {
                    if (r.StartsWith("Enshroud:", System.StringComparison.Ordinal))
                    {
                        _thanatosDebugState.EnshroudDecision = r;
                        break;
                    }
                }
            }
        }

        if (ActionService.CanExecuteGcd)
        {
            var gcd = _scheduler.DispatchGcd(context);
            if (StuckReasonHelper.Describe(gcd.Dispatched, gcd.GateFailReasons) is { } stuck)
                context.Debug.DamageState = stuck;
        }
    }

    #endregion
}
