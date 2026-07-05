using System.Collections.Generic;
using Dalamud.Game.ClientState.JobGauge;
using Dalamud.Game.ClientState.JobGauge.Types;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Party;
using Dalamud.Plugin.Services;
using Daedalus.Data;
using Daedalus.Rotation.Base;
using Daedalus.Rotation.Common;
using Daedalus.Rotation.Common.Helpers;
using Daedalus.Rotation.Common.Scheduling;
using Daedalus.Rotation.PersephoneCore.Context;
using Daedalus.Rotation.PersephoneCore.Helpers;
using Daedalus.Rotation.PersephoneCore.Modules;
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
/// Summoner rotation module (RSR-style reactive execution).
/// Orchestrates modular execution: each module handles a specific concern.
/// Named after Persephone, the Greek queen of the underworld who commands souls and summons.
/// </summary>
[Rotation("Persephone", JobRegistry.Summoner, Role = RotationRole.Caster)]
public sealed class Persephone : BaseCasterDpsRotation<IPersephoneContext, IPersephoneModule>
{
    /// <inheritdoc />
    public override string Name => "Persephone";

    /// <inheritdoc />
    public override uint[] SupportedJobIds => [JobRegistry.Summoner];

    /// <inheritdoc />
    public override DebugState DebugState => _debugState;

    /// <inheritdoc />
    protected override List<IPersephoneModule> Modules => _modules;

    /// <summary>
    /// Gets the Persephone-specific debug state. Used for Summoner-specific debug display.
    /// </summary>
    public PersephoneDebugState PersephoneDebug => _persephoneDebugState;

    // Persistent debug state
    private readonly PersephoneDebugState _persephoneDebugState = new();

    // IRotation-compatible debug state (for common debug interface)
    private readonly DebugState _debugState = new();

    // Helpers (shared across modules)
    private readonly PersephoneStatusHelper _statusHelper;
    private readonly PersephonePartyHelper _partyHelper;

    // Modules (sorted by priority - lower = higher priority)
    private readonly List<IPersephoneModule> _modules;

    // Timeline service for fight-aware rotation (optional)
    private readonly ITimelineService? _timelineService;

    // Party coordination service for raid buff synchronization (optional)
    private readonly IPartyCoordinationService? _partyCoordinationService;

    // Training service for explaining rotation decisions (optional)
    private readonly ITrainingService? _trainingService;

    // Dalamud job gauge service for reliable SMN gauge access
    private readonly IJobGauges _jobGauges;

    // Gauge values (read each frame)
    private int _aetherflowStacks;
    private int _attunement;
    private int _attunementStacks;
    private float _attunementTimer;
    private float _summonTimer;
    private bool _ifritReady;
    private bool _titanReady;
    private bool _garudaReady;

    // Demi-summon tracking (determined by status effects and action usage)
    private bool _isBahamutActive;
    private bool _isPhoenixActive;
    private bool _isSolarBahamutActive;

    // Phase tracking for Enkindle/Astral Flow usage
    private bool _hasUsedEnkindleThisPhase;
    private bool _hasUsedAstralFlowThisPhase;
    private float _lastDemiSummonTimer;

    // Scheduler
    private readonly RotationScheduler _scheduler;

    public Persephone(
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
            burstWindowService,
            errorMetrics,
            tinctureDispatcher: tinctureDispatcher,
            pullIntentService: pullIntentService)
    {
        _jobGauges = jobGauges;
        _timelineService = timelineService;
        _partyCoordinationService = partyCoordinationService;
        _trainingService = trainingService;

        _scheduler = new RotationScheduler(actionService, jobGauges, configuration, timelineService, errorMetrics);

        // Initialize helpers
        _statusHelper = new PersephoneStatusHelper();
        _partyHelper = new PersephonePartyHelper(objectTable, partyList);

        // Initialize modules (ordered by priority - lower = executed first)
        _modules = new List<IPersephoneModule>
        {
            new BuffModule(BurstWindowService),    // Priority 20 - oGCD management (Enkindle, Astral Flow, Aetherflow, Searing Light)
            new DamageModule(BurstWindowService, SmartAoEService),  // Priority 30 - GCD rotation
        };

        // Sort by priority
        _modules.Sort((a, b) => a.Priority.CompareTo(b.Priority));
    }

    #region Abstract Implementation

    /// <inheritdoc />
    protected override void ReadGaugeValues()
    {
        // Use Dalamud's IJobGauges for reliable gauge access instead of raw FFXIVClientStructs
        // bitfield parsing. Dalamud maintains these properties across game/struct updates.
        var gauge = _jobGauges.Get<SMNGauge>();

        _aetherflowStacks = gauge.AetherflowStacks;
        _attunement = (int)gauge.AttunementType;
        _attunementStacks = gauge.AttunementCount;
        _attunementTimer = gauge.AttunementTimerRemaining / 1000f;
        _summonTimer = gauge.SummonTimerRemaining / 1000f;
        _ifritReady = gauge.IsIfritReady;
        _titanReady = gauge.IsTitanReady;
        _garudaReady = gauge.IsGarudaReady;

        // Track demi-summon phase changes
        if (_summonTimer > 0 && _lastDemiSummonTimer <= 0)
        {
            // New demi-summon phase started - reset per-phase usage tracking
            _hasUsedEnkindleThisPhase = false;
            _hasUsedAstralFlowThisPhase = false;
        }

        // Live re-probe every frame while the summon timer runs (RSR probes live, never latches):
        // the old latch-once-at-phase-start could capture a stale Astral Flow button on the summon
        // frame, and a WRONG latch never self-corrected — only a missing one did. Field log
        // 2026-07-05: Enkindle Bahamut + Deathflare submitted during a real Phoenix window (game
        // GCDs read Fountain of Fire), costing the Rekindle. The button names the phase
        // definitively while its replacement is up; after Deathflare/Rekindle/Sunflare is spent it
        // reverts, so a none-result keeps the last known phase for the rest of the window.
        (_isBahamutActive, _isPhoenixActive, _isSolarBahamutActive) = UpdateDemiPhaseLatch(
            timerActive: _summonTimer > 0,
            actionService: ActionService,
            current: (_isBahamutActive, _isPhoenixActive, _isSolarBahamutActive));

        _lastDemiSummonTimer = _summonTimer;
    }

    /// <summary>
    /// Pure latch step for the demi phase flags: while the timer runs, a definitive Astral Flow
    /// probe result overwrites the flags; a none-result (button reverted after the flow ability
    /// was spent) keeps them; timer down clears them.
    /// </summary>
    internal static (bool Bahamut, bool Phoenix, bool SolarBahamut) UpdateDemiPhaseLatch(
        bool timerActive,
        IActionService actionService,
        (bool Bahamut, bool Phoenix, bool SolarBahamut) current)
    {
        if (!timerActive)
            return (false, false, false);

        SMNActions.ResolveDemiPhase(actionService, out var bahamut, out var phoenix, out var solar);
        return bahamut || phoenix || solar ? (bahamut, phoenix, solar) : current;
    }

    /// <summary>
    /// Updates MP forecast. Summoners use Lucid Dreaming for MP management.
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
    protected override IPersephoneContext CreateContext(IPlayerCharacter player, bool inCombat, bool isMoving)
    {
        return new PersephoneContext(
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
            debugState: _persephoneDebugState,
            aetherflowStacks: _aetherflowStacks,
            attunement: _attunement,
            attunementStacks: _attunementStacks,
            attunementTimer: _attunementTimer,
            summonTimer: _summonTimer,
            ifritReady: _ifritReady,
            titanReady: _titanReady,
            garudaReady: _garudaReady,
            isBahamutActive: _isBahamutActive,
            isPhoenixActive: _isPhoenixActive,
            isSolarBahamutActive: _isSolarBahamutActive,
            hasUsedEnkindleThisPhase: _hasUsedEnkindleThisPhase,
            hasUsedAstralFlowThisPhase: _hasUsedAstralFlowThisPhase,
            timelineService: _timelineService,
            partyCoordinationService: _partyCoordinationService,
            trainingService: _trainingService,
            log: Log);
    }

    /// <inheritdoc />
    protected override void SyncDebugState(IPersephoneContext context)
    {
        // Map Summoner debug state to common debug state fields
        _debugState.PlanningState = _persephoneDebugState.PlanningState;
        _debugState.PlannedAction = _persephoneDebugState.PlannedAction;
        _debugState.DpsState = _persephoneDebugState.DamageState;
        // Note: BuffState is tracked in PersephoneDebugState but not in common DebugState

        EnemyPackDebugHelper.SyncAoEDps(_debugState, _persephoneDebugState, context.Configuration.Summoner.AoEMinTargets, JobAoERadiusYalms.Caster);

        // Party/player info
        _debugState.PlayerHpPercent = (float)context.Player.CurrentHp / context.Player.MaxHp;
        _debugState.PartyListCount = context.PartyList.Length;
        _debugState.TargetInfo = TargetingDebugHelper.FormatTargetInfo(null, context.TargetingService);
    }

    /// <inheritdoc />
    protected override void ExecuteModules(IPersephoneContext context, bool isMoving, bool inCombat)
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
