using System.Collections.Generic;
using Dalamud.Game.ClientState.JobGauge;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Party;
using Dalamud.Plugin.Services;
using Daedalus.Data;
using Daedalus.Rotation.ApolloCore.Context;
using Daedalus.Rotation.ApolloCore.Helpers;
using Daedalus.Rotation.ApolloCore.Modules;
using Daedalus.Rotation.Base;
using Daedalus.Rotation.Common;
using Daedalus.Rotation.Common.Helpers;
using Daedalus.Rotation.Common.Scheduling;
using Daedalus.Services;
using Daedalus.Services.Action;
using Daedalus.Services.Cooldown;
using Daedalus.Services.Debuff;
using Daedalus.Services.Healing;
using Daedalus.Services.Party;
using Daedalus.Services.Prediction;
using Daedalus.Services.Stats;
using Daedalus.Services.Targeting;
using Daedalus.Services.Training;
using Daedalus.Timeline;

namespace Daedalus.Rotation;

/// <summary>
/// White Mage rotation module (scheduler-driven execution).
/// Named after Apollo, the Greek god of healing and light.
/// </summary>
[Rotation("Apollo", JobRegistry.WhiteMage, JobRegistry.Conjurer, Role = RotationRole.Healer)]
public sealed class Apollo : BaseHealerRotation<IApolloContext, IApolloModule>
{
    public override string Name => "Apollo";
    public override uint[] SupportedJobIds => [JobRegistry.WhiteMage, JobRegistry.Conjurer];
    public override DebugState DebugState => _debugState;
    protected override List<IApolloModule> Modules => _modules;
    protected override HealerPartyHelper HealerParty => _partyHelper;

    private readonly DebugState _debugState = new();
    private readonly StatusHelper _statusHelper;
    private readonly PartyHelper _partyHelper;
    private readonly ITimelineService? _timelineService;
    private readonly ITrainingService? _trainingService;
    private readonly List<IApolloModule> _modules;
    private readonly RotationScheduler _scheduler;

    public Apollo(
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
        HealingSpellSelector healingSpellSelector,
        DebuffDetectionService debuffDetectionService,
        ICooldownPlanner cooldownPlanner,
        ShieldTrackingService shieldTrackingService,
        IJobGauges jobGauges,
        ITimelineService? timelineService = null,
        IPartyCoordinationService? partyCoordinationService = null,
        ITrainingService? trainingService = null,
        IErrorMetricsService? errorMetrics = null,
        Daedalus.Services.Consumables.ITinctureDispatcher? tinctureDispatcher = null,
        Daedalus.Services.Pull.IPullIntentService? pullIntentService = null)
        : base(log, actionTracker, combatEventService, damageIntakeService, damageTrendService,
               configuration, objectTable, partyList, targetingService, hpPredictionService,
               actionService, playerStatsService, debuffDetectionService, healingSpellSelector,
               cooldownPlanner, shieldTrackingService, partyCoordinationService, errorMetrics,
               tinctureDispatcher, pullIntentService)
    {
        _timelineService = timelineService;
        _trainingService = trainingService;

        _scheduler = new RotationScheduler(actionService, jobGauges, configuration, timelineService, errorMetrics);
        _scheduler.TargetingService = targetingService; // enables the corpse hard-target swap for raises

        _statusHelper = new StatusHelper();
        _partyHelper = new PartyHelper(objectTable, partyList, hpPredictionService, configuration);

        _modules = new List<IApolloModule>
        {
            new ResurrectionModule(),
            new HealingModule(),
            new DefensiveModule(),
            new BuffModule(),
            new DamageModule(),
        };
        _modules.Sort((a, b) => a.Priority.CompareTo(b.Priority));

        PartyCoordinationService?.DeclareHealerRole(JobRegistry.WhiteMage, Configuration.PartyCoordination.PreferredHealerRole);
    }

    protected override void BroadcastHealerGaugeState(IPlayerCharacter player)
    {
        var lilyCount = StatusHelper.GetLilyCount();
        var bloodLily = StatusHelper.GetBloodLilyCount();
        PartyCoordinationService?.BroadcastGaugeState(JobRegistry.WhiteMage, lilyCount, bloodLily, 0);
    }

    protected override void UpdateMpForecast(IPlayerCharacter player)
    {
        MpForecastService.Update((int)player.CurrentMp, (int)player.MaxMp, StatusHelper.HasLucidDreaming(player));
    }

    protected override IApolloContext CreateContext(IPlayerCharacter player, bool inCombat, bool isMoving)
    {
        return new ApolloContext(
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
            healingSpellSelector: HealingSpellSelector,
            hpPredictionService: HpPredictionService,
            mpForecastService: MpForecastService,
            objectTable: ObjectTable,
            partyList: PartyList,
            playerStatsService: PlayerStatsService,
            targetingService: TargetingService,
            statusHelper: _statusHelper,
            partyHelper: _partyHelper,
            cooldownPlanner: CooldownPlanner,
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
    /// Fully scheduler-driven execution. All modules push candidates; scheduler
    /// dispatches the highest-priority candidate from each queue. Resurrection (1-2),
    /// Healing (10-80), Defensive (90-130), Buff (200-250), Damage push priorities
    /// preserve the legacy module ordering. Damage stays on legacy TryExecute as a
    /// side effect (BlocksOnExecution = false).
    /// </summary>
    protected override void ExecuteModules(IApolloContext context, bool isMoving, bool inCombat)
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
            if (_scheduler.DispatchOgcd(context).Dispatched) return;
            // DamageModule still uses legacy TryExecute (no migration value — its
            // BlocksOnExecution = false means it never breaks the loop anyway).
            foreach (var module in _modules)
                if (module.TryExecute(context, isMoving)) return;
        }

        if (ActionService.CanExecuteGcd)
        {
            var gcd = _scheduler.DispatchGcd(context);
            if (gcd.Dispatched) return;
            foreach (var module in _modules)
                if (module.TryExecute(context, isMoving)) return;
            // Nothing fired (scheduler + legacy fallback) — surface why the queued GCDs were rejected.
            if (StuckReasonHelper.Describe(gcd.Dispatched, gcd.GateFailReasons) is { } stuck)
                context.Debug.DpsState = stuck;
        }
    }
}
