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
using Daedalus.Rotation.AresCore.Context;
using Daedalus.Rotation.AresCore.Helpers;
using Daedalus.Rotation.AresCore.Modules;
using Daedalus.Services;
using Daedalus.Services.Action;
using Daedalus.Services.Cooldown;
using Daedalus.Services.Debuff;
using Daedalus.Services.Prediction;
using Daedalus.Services.Stats;
using Daedalus.Services.Party;
using Daedalus.Services.Tank;
using Daedalus.Services.Targeting;
using Daedalus.Services.Training;
using Daedalus.Timeline;

namespace Daedalus.Rotation;

/// <summary>
/// Warrior rotation module (scheduler-driven execution).
/// Named after Ares, the Greek god of war and battle fury.
/// </summary>
[Rotation("Ares", JobRegistry.Warrior, JobRegistry.Marauder, Role = RotationRole.Tank)]
public sealed class Ares : BaseTankRotation<IAresContext, IAresModule>
{
    public override string Name => "Ares";
    public override uint[] SupportedJobIds => [JobRegistry.Warrior, JobRegistry.Marauder];
    public override DebugState DebugState => _debugState;
    protected override List<IAresModule> Modules => _modules;

    public AresDebugState AresDebug => _aresDebugState;

    private readonly AresDebugState _aresDebugState = new();
    private readonly DebugState _debugState = new();
    private readonly AresStatusHelper _statusHelper;
    private readonly AresPartyHelper _partyHelper;
    private readonly ITrainingService? _trainingService;
    private readonly IBurstWindowService? _burstWindowService;
    private readonly List<IAresModule> _modules;
    private readonly RotationScheduler _scheduler;

    public Ares(
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
        IJobGauges jobGauges,
        ITimelineService? timelineService = null,
        IPartyCoordinationService? partyCoordinationService = null,
        ITrainingService? trainingService = null,
        IErrorMetricsService? errorMetrics = null,
        IBurstWindowService? burstWindowService = null,
        Daedalus.Services.Consumables.ITinctureDispatcher? tinctureDispatcher = null,
        Daedalus.Services.Pull.IPullIntentService? pullIntentService = null)
        : base(log, actionTracker, combatEventService, damageIntakeService, damageTrendService,
               configuration, objectTable, partyList, targetingService, hpPredictionService,
               actionService, playerStatsService, debuffDetectionService, enmityService,
               tankCooldownService, timelineService, partyCoordinationService, errorMetrics,
               tinctureDispatcher, pullIntentService)
    {
        _trainingService = trainingService;
        _burstWindowService = burstWindowService;

        _scheduler = new RotationScheduler(actionService, jobGauges, configuration, timelineService, errorMetrics);

        _statusHelper = new AresStatusHelper();
        _partyHelper = new AresPartyHelper(objectTable, partyList);

        _modules = new List<IAresModule>
        {
            new EnmityModule(),
            new MitigationModule(),
            new BuffModule(_burstWindowService),
            new DamageModule(),
        };

        _modules.Sort((a, b) => a.Priority.CompareTo(b.Priority));
    }

    protected override int ReadGaugeValue()
    {
        return SafeGameAccess.GetWarBeastGauge(ErrorMetrics);
    }

    protected override int DetermineComboStep(uint comboAction, float comboTimer)
        => ComputeComboStep(comboAction, comboTimer);

    internal static int ComputeComboStep(uint comboAction, float comboTimer)
    {
        if (comboAction == 0 || comboTimer <= 0) return 0;
        if (comboAction == WARActions.HeavySwing.ActionId) return 1;
        if (comboAction == WARActions.Maim.ActionId) return 2;
        if (comboAction == WARActions.Overpower.ActionId) return 1;
        return 0;
    }

    protected override void UpdateMpForecast(IPlayerCharacter player)
    {
        MpForecastService.Update((int)player.CurrentMp, (int)player.MaxMp, hasLucidDreaming: false);
    }

    protected override IAresContext CreateContext(IPlayerCharacter player, bool inCombat, bool isMoving)
    {
        return new AresContext(
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
            enmityService: EnmityService,
            tankCooldownService: TankCooldownService,
            statusHelper: _statusHelper,
            partyHelper: _partyHelper,
            debugState: _aresDebugState,
            beastGauge: GaugeValue,
            comboStep: ComboStep,
            lastComboAction: LastComboAction,
            comboTimeRemaining: ComboTimeRemaining,
            timelineService: TimelineService,
            partyCoordinationService: PartyCoordinationService,
            trainingService: _trainingService,
            log: Log);
    }

    protected override void ExecuteModules(IAresContext context, bool isMoving, bool inCombat)
    {
        if (Configuration.Targeting.PauseAllOnStandStillPunisher
            && PlayerSafetyHelper.IsStandStillPunisherActive(context.Player))
        {
            return;
        }
        if (Configuration.Targeting.PauseOnPlayerChannel
            && PlayerSafetyHelper.IsPlayerIntentChannelActive(context.Player))
        {
            return;
        }

        if (TryDispatchTincture(context, inCombat)) return;

        _scheduler.Reset();
        foreach (var module in _modules)
        {
            module.CollectCandidates(context, _scheduler, isMoving);
        }

        if (inCombat && ActionService.CanExecuteOgcd)
        {
            _scheduler.DispatchOgcd(context);
        }
        if (ActionService.CanExecuteGcd)
        {
            _scheduler.DispatchGcd(context);
        }
    }

    protected override void SyncDebugState(IAresContext context)
    {
        _debugState.PlanningState = _aresDebugState.DamageState;
        _debugState.PlannedAction = _aresDebugState.PlannedAction;
        _debugState.DpsState = _aresDebugState.DamageState;
        _debugState.DefensiveState = _aresDebugState.MitigationState;
        EnemyPackDebugHelper.SyncAoEDps(_debugState, _aresDebugState, context.Configuration.Tank.GetEffectiveAoEMinTargets(JobRegistry.Warrior), JobAoERadiusYalms.Tank);
        _debugState.PlayerHpPercent = (float)context.Player.CurrentHp / context.Player.MaxHp;
        _debugState.PartyListCount = context.PartyList.Length;
        _debugState.TargetInfo = TargetingDebugHelper.FormatTargetInfo(context.CurrentTarget, context.TargetingService);
    }
}
