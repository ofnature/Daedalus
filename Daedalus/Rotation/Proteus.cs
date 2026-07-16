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
using Daedalus.Rotation.ProteusCore.Context;
using Daedalus.Rotation.ProteusCore.Helpers;
using Daedalus.Rotation.ProteusCore.Modules;
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
/// Blue Mage rotation module (scheduler-driven execution).
/// Named after Proteus, the shape-shifting sea god — fitting for the job that steals its kit.
/// BLU has no gauge and no level-gated action set: availability is learned+slotted, and the
/// configured role (DPS/Tank/Healer) decides which modules act and which archetype Aetheric
/// Mimicry copies. See burn-reference/proteus-plan.md for the design.
/// </summary>
[Rotation("Proteus", JobRegistry.BlueMage, Role = RotationRole.Caster)]
public sealed class Proteus : BaseCasterDpsRotation<IProteusContext, IProteusModule>
{
    /// <inheritdoc />
    public override string Name => "Proteus";

    /// <inheritdoc />
    public override uint[] SupportedJobIds => [JobRegistry.BlueMage];

    /// <inheritdoc />
    public override DebugState DebugState => _debugState;

    /// <inheritdoc />
    protected override List<IProteusModule> Modules => _modules;

    /// <summary>Blue Mage-specific debug state.</summary>
    public ProteusDebugState ProteusDebug => _proteusDebugState;

    private readonly ProteusDebugState _proteusDebugState = new();
    private readonly DebugState _debugState = new();
    private readonly ProteusStatusHelper _statusHelper;
    private readonly CasterPartyHelper _partyHelper;
    private readonly List<IProteusModule> _modules;
    private readonly ITimelineService? _timelineService;
    private readonly ITrainingService? _trainingService;
    private readonly IPartyCoordinationService? _partyCoordinationService;
    private readonly IBluLoadoutService? _bluLoadoutService;
    private readonly Daedalus.Services.Blu.IDeathImmunityLedger? _deathImmunityLedger;
    private readonly RotationScheduler _scheduler;

    public Proteus(
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
        ITrainingService? trainingService = null,
        IBurstWindowService? burstWindowService = null,
        IErrorMetricsService? errorMetrics = null,
        IPartyCoordinationService? partyCoordinationService = null,
        Daedalus.Services.Consumables.ITinctureDispatcher? tinctureDispatcher = null,
        Daedalus.Services.Pull.IPullIntentService? pullIntentService = null,
        IBluLoadoutService? bluLoadoutService = null,
        Daedalus.Services.Blu.IDeathImmunityLedger? deathImmunityLedger = null)
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
        _trainingService = trainingService;
        _partyCoordinationService = partyCoordinationService;
        _bluLoadoutService = bluLoadoutService;
        _deathImmunityLedger = deathImmunityLedger;

        _scheduler = new RotationScheduler(actionService, jobGauges, configuration, timelineService, errorMetrics);

        _statusHelper = new ProteusStatusHelper();
        _partyHelper = new CasterPartyHelper(objectTable, partyList);

        _modules = new List<IProteusModule>
        {
            new MitigationModule(),   // Priority 10 — Diamondback survival
            new HealingModule(),      // Priority 15 — White Wind
            new BuffModule(),         // Priority 20 — Mimicry + Mighty Guard upkeep
            new DamageModule(),       // Priority 30 — GCD damage
        };

        _modules.Sort((a, b) => a.Priority.CompareTo(b.Priority));
    }

    #region Abstract Implementation

    /// <inheritdoc />
    protected override void ReadGaugeValues()
    {
        // Blue Mage has no job gauge — state is MP + statuses only.
    }

    /// <inheritdoc />
    protected override void UpdateMpForecast(IPlayerCharacter player)
    {
        MpForecastService.Update(
            (int)player.CurrentMp,
            (int)player.MaxMp,
            hasLucidDreaming: false);
    }

    /// <inheritdoc />
    protected override IProteusContext CreateContext(IPlayerCharacter player, bool inCombat, bool isMoving)
    {
        return new ProteusContext(
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
            debugState: _proteusDebugState,
            timelineService: _timelineService,
            trainingService: _trainingService,
            partyCoordinationService: _partyCoordinationService,
            log: Log,
            loadoutService: _bluLoadoutService,
            deathLedger: _deathImmunityLedger);
    }

    /// <inheritdoc />
    protected override void SyncDebugState(IProteusContext context)
    {
        _debugState.PlanningState = _proteusDebugState.DamageState;
        _debugState.PlannedAction = _proteusDebugState.PlannedAction;
        _debugState.DpsState = _proteusDebugState.DamageState;
        _debugState.DefensiveState = _proteusDebugState.MitigationState;

        _debugState.PlayerHpPercent = (float)context.Player.CurrentHp / context.Player.MaxHp;
        _debugState.PartyListCount = context.PartyList.Length;
        _debugState.TargetInfo = TargetingDebugHelper.FormatTargetInfo(null, context.TargetingService);
    }

    /// <inheritdoc />
    protected override void ExecuteModules(IProteusContext context, bool isMoving, bool inCombat)
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

        // GCD dispatch is NOT combat-gated: Mimicry and Mighty Guard apply between pulls / on
        // duty pop (same rule as the tank stance-on-pop work).
        if (ActionService.CanExecuteGcd)
        {
            var gcd = _scheduler.DispatchGcd(context);
            if (StuckReasonHelper.Describe(gcd.Dispatched, gcd.GateFailReasons) is { } stuck)
                context.Debug.DamageState = stuck;
        }
    }

    #endregion
}
