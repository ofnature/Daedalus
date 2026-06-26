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
using Daedalus.Rotation.KratosCore.Context;
using Daedalus.Rotation.KratosCore.Helpers;
using Daedalus.Rotation.KratosCore.Modules;
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
/// Monk rotation module (RSR-style reactive execution).
/// Orchestrates modular execution: each module handles a specific concern.
/// Named after Kratos, the Greek god of strength and power.
/// </summary>
[Rotation("Kratos", JobRegistry.Monk, JobRegistry.Pugilist, Role = RotationRole.MeleeDps)]
public sealed class Kratos : BaseMeleeDpsRotation<IKratosContext, IKratosModule>
{
    /// <inheritdoc />
    public override string Name => "Kratos";

    /// <inheritdoc />
    public override uint[] SupportedJobIds => [JobRegistry.Monk, JobRegistry.Pugilist];

    /// <inheritdoc />
    public override DebugState DebugState => _debugState;

    /// <inheritdoc />
    protected override List<IKratosModule> Modules => _modules;

    /// <summary>
    /// Gets the Kratos-specific debug state. Used for Monk-specific debug display.
    /// </summary>
    public KratosDebugState KratosDebug => _kratosDebugState;

    // Persistent debug state
    private readonly KratosDebugState _kratosDebugState = new();

    // IRotation-compatible debug state (for common debug interface)
    private readonly DebugState _debugState = new();

    // Helpers (shared across modules)
    private readonly KratosStatusHelper _statusHelper;
    private readonly MeleeDpsPartyHelper _partyHelper;

    // Modules (sorted by priority - lower = higher priority)
    private readonly List<IKratosModule> _modules;

    // Timeline service for fight-aware rotation (optional)
    private readonly ITimelineService? _timelineService;

    // Party coordination service for raid buff synchronization (optional)
    private readonly IPartyCoordinationService? _partyCoordinationService;

    // Training service for decision explanations (optional)
    private readonly ITrainingService? _trainingService;

    // Gauge values (read each frame)
    private int _chakra;
    private byte[] _beastChakra = new byte[3];
    private bool _hasLunarNadi;
    private bool _hasSolarNadi;

    // Positional anticipation
    private readonly DelegatePositionalAnticipationProvider _positionalAnticipationProvider;

    // Scheduler
    private readonly RotationScheduler _scheduler;

    public Kratos(
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

        _positionalAnticipationProvider = new DelegatePositionalAnticipationProvider(GetNextRequiredPositional);
        _scheduler = new RotationScheduler(actionService, jobGauges, configuration, timelineService, errorMetrics);

        // Initialize helpers
        _statusHelper = new KratosStatusHelper();
        _partyHelper = new MeleeDpsPartyHelper(objectTable, partyList);

        // Initialize modules (ordered by priority - lower = executed first)
        _modules = new List<IKratosModule>
        {
            new BuffModule(BurstWindowService),    // Priority 20 - Buff management (RoF, Brotherhood, PB)
            new DamageModule(BurstWindowService, SmartAoEService),  // Priority 30 - DPS rotation
        };

        // Sort by priority
        _modules.Sort((a, b) => a.Priority.CompareTo(b.Priority));
    }

    #region Abstract Implementation

    /// <inheritdoc />
    protected override void ReadGaugeValues()
    {
        _chakra = SafeGameAccess.GetMnkChakra(ErrorMetrics);
        _beastChakra = SafeGameAccess.GetMnkBeastChakra(ErrorMetrics);
        _hasLunarNadi = SafeGameAccess.HasMnkLunarNadi(ErrorMetrics);
        _hasSolarNadi = SafeGameAccess.HasMnkSolarNadi(ErrorMetrics);
    }

    /// <inheritdoc />
    protected override int DetermineComboStep(uint comboAction, float comboTimer)
        => ComputeComboStep(comboAction, comboTimer);

    internal static int ComputeComboStep(uint comboAction, float comboTimer)
    {
        // Monk doesn't have traditional combos like other jobs
        // Instead, forms determine which actions are available
        // The form system is tracked via status effects, not combo state
        // Return 0 as Monk uses the form system instead
        return 0;
    }

    /// <summary>
    /// Determines the required positional for the next Monk GCD based on current form.
    /// Opo-opo form → Rear (Bootshine/LeapingOpo)
    /// Raptor form → Flank (TrueStrike/TwinSnakes/RisingRaptor)
    /// Coeurl form → Rear (Demolish, when DoT needs refresh) or Flank (Snap Punch/Pouncing Coeurl)
    /// No form / Formless → Rear (starts with Opo-opo)
    /// </summary>
    protected override PositionalType? GetNextRequiredPositional()
    {
        var player = ObjectTable.LocalPlayer;
        if (player == null) return null;

        // Check form status effects directly
        foreach (var status in player.StatusList)
        {
            if (status.StatusId == MNKActions.StatusIds.RaptorForm)
                return PositionalType.Flank;
            if (status.StatusId == MNKActions.StatusIds.CoeurlForm)
            {
                // Coeurl alternates: Demolish (rear) when DoT needs refresh, Snap Punch/Pouncing Coeurl (flank) otherwise.
                // Mirror the GetCoeurlAction condition from DamageModule.
                var target = TargetingService.FindEnemyForAction(
                    Configuration.Targeting.EnemyStrategy,
                    MNKActions.Demolish.ActionId,
                    player);
                if (target != null)
                {
                    var demolishNeedsRefresh = !_statusHelper.HasDemolish(target, player.EntityId)
                        || _statusHelper.GetDemolishRemaining(target, player.EntityId) < 3f;
                    return demolishNeedsRefresh ? PositionalType.Rear : PositionalType.Flank;
                }
                return PositionalType.Rear;
            }
            if (status.StatusId is MNKActions.StatusIds.OpoOpoForm or MNKActions.StatusIds.FormlessFist)
                return PositionalType.Rear;
        }

        // No form → starting Opo-opo → Rear
        return PositionalType.Rear;
    }

    /// <inheritdoc />
    protected override IPositionalAnticipationProvider? GetPositionalAnticipationProvider()
        => _positionalAnticipationProvider;

    /// <inheritdoc />
    protected override bool IsPositionalMovementEnabled()
        => Configuration.Monk.EnablePositionalMovement;

    /// <summary>
    /// Updates MP forecast. Monks don't use MP for abilities.
    /// </summary>
    protected override void UpdateMpForecast(IPlayerCharacter player)
    {
        MpForecastService.Update(
            (int)player.CurrentMp,
            (int)player.MaxMp,
            hasLucidDreaming: false);
    }

    /// <inheritdoc />
    protected override IKratosContext CreateContext(IPlayerCharacter player, bool inCombat, bool isMoving)
    {
        return new KratosContext(
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
            debugState: _kratosDebugState,
            chakra: _chakra,
            beastChakra: _beastChakra,
            hasLunarNadi: _hasLunarNadi,
            hasSolarNadi: _hasSolarNadi,
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
    protected override void SyncDebugState(IKratosContext context)
    {
        _debugState.PlannedAction = string.IsNullOrEmpty(_kratosDebugState.PlannedAction)
            ? "None" : _kratosDebugState.PlannedAction;
        _debugState.DpsState = string.IsNullOrEmpty(_kratosDebugState.DamageState)
            ? (context.InCombat ? "Idle" : "Out of combat") : _kratosDebugState.DamageState;

        if (!string.IsNullOrEmpty(_kratosDebugState.BuffState))
            _debugState.PlanningState = _kratosDebugState.BuffState;
        else if (!string.IsNullOrEmpty(_kratosDebugState.DamageState))
            _debugState.PlanningState = _kratosDebugState.DamageState;
        else
            _debugState.PlanningState = context.InCombat ? "Active" : "Idle";

        _debugState.AoEDpsEnemyCount = _kratosDebugState.NearbyEnemies;
        var aoeMin = context.Configuration.Monk.AoEMinTargets;
        _debugState.AoEDpsState = _kratosDebugState.NearbyEnemies >= aoeMin
            ? $"AoE ({_kratosDebugState.NearbyEnemies} enemies)"
            : _kratosDebugState.NearbyEnemies > 0
                ? $"ST ({_kratosDebugState.NearbyEnemies} nearby)"
                : "No enemies";

        var target = TargetingService.FindEnemyForAction(
            context.Configuration.Targeting.EnemyStrategy,
            MNKActions.Bootshine.ActionId,
            context.Player)
            ?? TargetingService.FindNearbyEnemy(25f, context.Player) as IBattleChara;
        _debugState.TargetInfo = target != null
            ? $"{target.Name} ({(float)target.CurrentHp / target.MaxHp:P0})"
            : "None";

        _debugState.PlayerHpPercent = (float)context.Player.CurrentHp / context.Player.MaxHp;
        _debugState.PartyListCount = context.PartyList.Length;
    }

    /// <inheritdoc />
    protected override void ExecuteModules(IKratosContext context, bool isMoving, bool inCombat)
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
            _scheduler.DispatchGcd(context);

        SyncDebugState(context);
    }

    #endregion
}
