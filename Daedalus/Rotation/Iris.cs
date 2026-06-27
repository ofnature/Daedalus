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
using Daedalus.Rotation.IrisCore.Context;
using Daedalus.Rotation.IrisCore.Helpers;
using Daedalus.Rotation.IrisCore.Modules;
using Daedalus.Services;
using Daedalus.Services.Action;
using Daedalus.Services.Debuff;
using Daedalus.Services.Party;
using Daedalus.Services.Prediction;
using Daedalus.Services.Stats;
using Daedalus.Services.Targeting;
using Daedalus.Services.Training;
using Daedalus.Timeline;

namespace Daedalus.Rotation;

/// <summary>
/// Pictomancer rotation module (RSR-style reactive execution).
/// Orchestrates modular execution: each module handles a specific concern.
/// Named after Iris, the Greek goddess of the rainbow - fitting for a painter who creates with colors.
/// </summary>
[Rotation("Iris", JobRegistry.Pictomancer, Role = RotationRole.Caster)]
public sealed class Iris : BaseCasterDpsRotation<IIrisContext, IIrisModule>
{
    /// <inheritdoc />
    public override string Name => "Iris";

    /// <inheritdoc />
    public override uint[] SupportedJobIds => [JobRegistry.Pictomancer];

    /// <inheritdoc />
    public override DebugState DebugState => _debugState;

    /// <inheritdoc />
    protected override List<IIrisModule> Modules => _modules;

    /// <summary>
    /// Gets the Iris-specific debug state. Used for Pictomancer-specific debug display.
    /// </summary>
    public IrisDebugState IrisDebug => _irisDebugState;

    // Persistent debug state
    private readonly IrisDebugState _irisDebugState = new();

    // IRotation-compatible debug state (for common debug interface)
    private readonly DebugState _debugState = new();

    // Helpers (shared across modules)
    private readonly IrisStatusHelper _statusHelper;
    private readonly CasterPartyHelper _partyHelper;

    // Modules (sorted by priority - lower = higher priority)
    private readonly List<IIrisModule> _modules;

    // Timeline service for fight-aware rotation (optional)
    private readonly ITimelineService? _timelineService;

    // Party coordination service for multi-Daedalus IPC (optional)
    private readonly IPartyCoordinationService? _partyCoordinationService;

    // Training service for decision explanations (optional)
    private readonly ITrainingService? _trainingService;

    // Gauge values (read each frame)
    private int _paletteGauge;
    private int _whitePaint;
    private bool _hasBlackPaint;
    private byte _creatureMotif;
    private bool _hasWeaponCanvas;
    private bool _hasLandscapeCanvas;
    private bool _mogReady;
    private bool _madeenReady;

    // Combo tracking (read from game each frame)
    private uint _comboAction;
    private float _comboTimer;

    // Scheduler
    private readonly RotationScheduler _scheduler;

    public Iris(
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
        IBurstWindowService? burstWindowService = null,
        IErrorMetricsService? errorMetrics = null,
        IPartyCoordinationService? partyCoordinationService = null,
        ITrainingService? trainingService = null,
        Daedalus.Services.Consumables.ITinctureDispatcher? tinctureDispatcher = null,
        Daedalus.Services.Pull.IPullIntentService? pullIntentService = null,
        ISmartAoEService? smartAoEService = null)
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
            smartAoEService: smartAoEService,
            tinctureDispatcher: tinctureDispatcher,
            pullIntentService: pullIntentService)
    {
        _timelineService = timelineService;
        _partyCoordinationService = partyCoordinationService;
        _trainingService = trainingService;

        _scheduler = new RotationScheduler(actionService, jobGauges, configuration, timelineService, errorMetrics);

        // Initialize helpers
        _statusHelper = new IrisStatusHelper();
        _partyHelper = new CasterPartyHelper(objectTable, partyList);

        // Initialize modules (ordered by priority - lower = executed first)
        _modules = new List<IIrisModule>
        {
            new BuffModule(BurstWindowService),    // Priority 30 - oGCD management (Muses, Portraits, Subtractive Palette, etc.)
            new DamageModule(BurstWindowService, SmartAoEService),  // Priority 50 - GCD rotation (combos, paint spenders, finishers)
        };

        // Sort by priority
        _modules.Sort((a, b) => a.Priority.CompareTo(b.Priority));
    }

    #region Abstract Implementation

    /// <inheritdoc />
    protected override void ReadGaugeValues()
    {
        _paletteGauge = SafeGameAccess.GetPctPaletteGauge(ErrorMetrics);
        _whitePaint = SafeGameAccess.GetPctWhitePaint(ErrorMetrics);
        _hasBlackPaint = SafeGameAccess.GetPctHasBlackPaint(ErrorMetrics);
        _creatureMotif = SafeGameAccess.GetPctCreatureMotif(ErrorMetrics);
        _hasWeaponCanvas = SafeGameAccess.GetPctHasWeaponCanvas(ErrorMetrics);
        _hasLandscapeCanvas = SafeGameAccess.GetPctHasLandscapeCanvas(ErrorMetrics);
        _mogReady = SafeGameAccess.GetPctMogReady(ErrorMetrics);
        _madeenReady = SafeGameAccess.GetPctMadeenReady(ErrorMetrics);

        // Read combo state from game
        _comboAction = SafeGameAccess.GetComboAction(ErrorMetrics);
        _comboTimer = SafeGameAccess.GetComboTimer(ErrorMetrics);
    }

    /// <summary>
    /// Updates MP forecast. Pictomancers use Lucid Dreaming for MP management.
    /// </summary>
    protected override void UpdateMpForecast(IPlayerCharacter player)
    {
        var hasLucid = BaseStatusHelper.HasLucidDreaming(player);
        MpForecastService.Update(
            (int)player.CurrentMp,
            (int)player.MaxMp,
            hasLucidDreaming: hasLucid);
    }

    /// <inheritdoc />
    protected override IIrisContext CreateContext(IPlayerCharacter player, bool inCombat, bool isMoving)
    {
        return new IrisContext(
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
            debugState: _irisDebugState,
            paletteGauge: _paletteGauge,
            whitePaint: _whitePaint,
            hasBlackPaint: _hasBlackPaint,
            creatureMotif: _creatureMotif,
            hasWeaponCanvas: _hasWeaponCanvas,
            hasLandscapeCanvas: _hasLandscapeCanvas,
            mogReady: _mogReady,
            madeenReady: _madeenReady,
            comboAction: _comboAction,
            comboTimer: _comboTimer,
            timelineService: _timelineService,
            log: Log,
            partyCoordinationService: _partyCoordinationService,
            trainingService: _trainingService,
            smartAoEService: SmartAoEService);
    }

    /// <inheritdoc />
    protected override void SyncDebugState(IIrisContext context)
    {
        // Map Pictomancer debug state to common debug state fields
        _debugState.PlanningState = _irisDebugState.PlanningState;
        _debugState.PlannedAction = _irisDebugState.PlannedAction;
        _debugState.DpsState = _irisDebugState.DamageState;
        // Note: BuffState is tracked in IrisDebugState but not in common DebugState

        EnemyPackDebugHelper.SyncAoEDps(_debugState, _irisDebugState, context.Configuration.Pictomancer.AoEMinTargets, JobAoERadiusYalms.Caster);

        // Party/player info
        _debugState.PlayerHpPercent = (float)context.Player.CurrentHp / context.Player.MaxHp;
        _debugState.PartyListCount = context.PartyList.Length;
        _debugState.TargetInfo = TargetingDebugHelper.FormatTargetInfo(null, context.TargetingService);
    }

    /// <inheritdoc />
    protected override void ExecuteModules(IIrisContext context, bool isMoving, bool inCombat)
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

        // PCT pushes pre-pull motif paints (GCD path) out of combat. oGCDs gate on inCombat.
        if (inCombat && ActionService.CanExecuteOgcd)
            _scheduler.DispatchOgcd(context);

        if (ActionService.CanExecuteGcd)
            _scheduler.DispatchGcd(context);
    }

    #endregion
}
