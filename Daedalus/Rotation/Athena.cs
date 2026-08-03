using System.Collections.Generic;
using Dalamud.Game.ClientState.JobGauge;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Party;
using Dalamud.Plugin.Services;
using Daedalus.Data;
using Daedalus.Rotation.AthenaCore.Context;
using Daedalus.Rotation.Common;
using Daedalus.Rotation.Common.Helpers;
using Daedalus.Rotation.Common.Scheduling;
using Daedalus.Rotation.AthenaCore.Helpers;
using Daedalus.Rotation.AthenaCore.Modules;
using Daedalus.Rotation.Base;
using Daedalus.Services;
using Daedalus.Services.Action;
using Daedalus.Services.Cooldown;
using Daedalus.Services.Debuff;
using Daedalus.Services.Healing;
using Daedalus.Services.Party;
using Daedalus.Services.Prediction;
using Daedalus.Services.Scholar;
using Daedalus.Services.Stats;
using Daedalus.Services.Targeting;
using Daedalus.Services.Training;
using Daedalus.Timeline;

namespace Daedalus.Rotation;

/// <summary>
/// Scholar rotation module (RSR-style reactive execution).
/// Orchestrates modular execution: each module handles a specific concern.
/// Named after Athena, the Greek goddess of wisdom and strategic warfare.
/// </summary>
[Rotation("Athena", JobRegistry.Scholar, JobRegistry.Arcanist, Role = RotationRole.Healer)]
public sealed class Athena : BaseHealerRotation<IAthenaContext, IAthenaModule>
{
    /// <inheritdoc />
    public override string Name => "Athena";

    /// <inheritdoc />
    public override uint[] SupportedJobIds => [JobRegistry.Scholar, JobRegistry.Arcanist];

    /// <inheritdoc />
    public override DebugState DebugState => _debugState;

    /// <inheritdoc />
    protected override List<IAthenaModule> Modules => _modules;

    /// <inheritdoc />
    protected override HealerPartyHelper HealerParty => _partyHelper;

    /// <summary>
    /// Gets the Athena-specific debug state.
    /// </summary>
    public AthenaDebugState AthenaDebug => _debugState;

    // Scholar-specific services
    private readonly AetherflowTrackingService _aetherflowService;
    private readonly FairyGaugeService _fairyGaugeService;
    private readonly FairyStateManager _fairyStateManager;

    // Debug state
    private readonly AthenaDebugState _debugState = new();

    // Helpers
    private readonly AthenaStatusHelper _statusHelper;
    private readonly AthenaPartyHelper _partyHelper;

    // Timeline integration
    private readonly ITimelineService? _timelineService;

    // Training mode
    private readonly ITrainingService? _trainingService;

    // Modules (sorted by priority)
    private readonly List<IAthenaModule> _modules;

    // Scheduler
    private readonly RotationScheduler _scheduler;

    public Athena(
        IPluginLog log,
        IActionTracker actionTracker,
        CombatEventService combatEventService,
        IDamageIntakeService damageIntakeService,
        IDamageTrendService damageTrendService,
        Configuration configuration,
        IObjectTable objectTable,
        IPartyList partyList,
        TargetingService targetingService,
        HpPredictionService hpPredictionService,
        ActionService actionService,
        PlayerStatsService playerStatsService,
        DebuffDetectionService debuffDetectionService,
        ICooldownPlanner cooldownPlanner,
        HealingSpellSelector healingSpellSelector,
        ShieldTrackingService shieldTrackingService,
        IJobGauges jobGauges,
        ITimelineService? timelineService = null,
        IPartyCoordinationService? partyCoordinationService = null,
        ITrainingService? trainingService = null,
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
            healingSpellSelector,
            cooldownPlanner,
            shieldTrackingService,
            partyCoordinationService,
            errorMetrics,
            tinctureDispatcher,
            pullIntentService)
    {
        // Store timeline service
        _timelineService = timelineService;

        // Store training service
        _trainingService = trainingService;

        // Initialize scheduler
        _scheduler = new RotationScheduler(actionService, jobGauges, configuration, timelineService, errorMetrics);
        _scheduler.TargetingService = targetingService; // enables the corpse hard-target swap for raises

        // Initialize Scholar-specific services
        _aetherflowService = new AetherflowTrackingService();
        _fairyGaugeService = new FairyGaugeService();
        _fairyStateManager = new FairyStateManager(objectTable);

        // Initialize helpers
        _statusHelper = new AthenaStatusHelper();
        _partyHelper = new AthenaPartyHelper(objectTable, partyList, hpPredictionService, configuration, _statusHelper);

        // Initialize modules (ordered by priority - lower = executed first)
        _modules = new List<IAthenaModule>
        {
            new FairyModule(),           // Priority 3 - Summon fairy if needed
            new ResurrectionModule(),    // Priority 5 - Dead members are useless
            new HealingModule(),         // Priority 10 - Keep party alive
            new DefensiveModule(),       // Priority 20 - Mitigation
            new BuffModule(),            // Priority 30 - Buffs and utilities
            new DamageModule(),          // Priority 50 - DPS when safe
        };

        _modules.Sort((a, b) => a.Priority.CompareTo(b.Priority));

        // Declare healer role for multi-healer coordination
        PartyCoordinationService?.DeclareHealerRole(JobRegistry.Scholar, Configuration.PartyCoordination.PreferredHealerRole);

        Log.Info("Athena (Scholar) rotation initialized");
    }

    /// <inheritdoc />
    protected override void BroadcastHealerGaugeState(IPlayerCharacter player)
    {
        var aetherflow = _aetherflowService.CurrentStacks;
        var fairyGauge = _fairyGaugeService.CurrentGauge;
        PartyCoordinationService?.BroadcastGaugeState(JobRegistry.Scholar, aetherflow, fairyGauge, 0);
    }

    #region Abstract Implementation

    /// <inheritdoc />
    protected override void UpdateMpForecast(IPlayerCharacter player)
    {
        MpForecastService.Update(
            (int)player.CurrentMp,
            (int)player.MaxMp,
            AthenaStatusHelper.HasLucidDreaming(player));
    }

    /// <inheritdoc />
    protected override void UpdateJobSpecificServices(IPlayerCharacter player, bool inCombat)
    {
        // Call base healer service updates
        base.UpdateJobSpecificServices(player, inCombat);

        // Update Scholar-specific debug state
        _debugState.AetherflowStacks = _aetherflowService.CurrentStacks;
        _debugState.FairyGauge = _fairyGaugeService.CurrentGauge;
        _debugState.FairyState = _fairyStateManager.CurrentState.ToString();
        _debugState.PlayerHpPercent = player.MaxHp > 0 ? (float)player.CurrentHp / player.MaxHp : 1f;

        // Populate shared DebugState fields for the debug snapshot
        _debugState.AoEStatus = _debugState.AoEHealState;
        _debugState.LilyCount = _debugState.AetherflowStacks;
        _debugState.BloodLilyCount = _debugState.FairyGauge / 33; // integer division
        _debugState.LilyStrategy = _debugState.AetherflowState;
    }

    /// <inheritdoc />
    protected override IAthenaContext CreateContext(IPlayerCharacter player, bool inCombat, bool isMoving)
    {
        return new AthenaContext(
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
            objectTable: ObjectTable,
            partyList: PartyList,
            playerStatsService: PlayerStatsService,
            targetingService: TargetingService,
            aetherflowService: _aetherflowService,
            fairyGaugeService: _fairyGaugeService,
            fairyStateManager: _fairyStateManager,
            statusHelper: _statusHelper,
            partyHelper: _partyHelper,
            cooldownPlanner: CooldownPlanner,
            healingSpellSelector: HealingSpellSelector,
            coHealerDetectionService: CoHealerDetectionService,
            bossMechanicDetector: BossMechanicDetector,
            shieldTrackingService: ShieldTrackingService,
            partyCoordinationService: PartyCoordinationService,
            timelineService: _timelineService,
            trainingService: _trainingService,
            debugState: _debugState,
            log: Log);
    }

    /// <summary>
    /// Fully scheduler-driven execution. All modules push candidates and the scheduler
    /// dispatches the highest-priority candidate from each queue. Push priorities preserve
    /// legacy ordering: Fairy (0-7), Resurrection (1-2), Healing (5-80), Defensive (75-130),
    /// Buff (200-250), Damage (285-330).
    /// </summary>
    protected override void ExecuteModules(IAthenaContext context, bool isMoving, bool inCombat)
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
                context.Debug.DpsState = stuck;
        }
    }

    #endregion

}
