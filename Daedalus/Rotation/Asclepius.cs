using System;
using System.Collections.Generic;
using Dalamud.Game.ClientState.JobGauge;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Party;
using Dalamud.Plugin.Services;
using Daedalus.Data;
using Daedalus.Rotation.ApolloCore.Helpers;
using Daedalus.Rotation.AsclepiusCore.Helpers;
using Daedalus.Rotation.Common;
using Daedalus.Rotation.Common.Helpers;
using Daedalus.Rotation.Common.Scheduling;
using Daedalus.Rotation.AsclepiusCore.Context;
using Daedalus.Rotation.AsclepiusCore.Modules;
using Daedalus.Rotation.Base;
using Daedalus.Services;
using Daedalus.Services.Action;
using Daedalus.Services.Cooldown;
using Daedalus.Services.Debuff;
using Daedalus.Services.Healing;
using Daedalus.Services.Party;
using Daedalus.Services.Prediction;
using Daedalus.Services.Sage;
using Daedalus.Services.Stats;
using Daedalus.Services.Targeting;
using Daedalus.Services.Training;
using Daedalus.Timeline;

namespace Daedalus.Rotation;

/// <summary>
/// Sage rotation module (RSR-style reactive execution).
/// Orchestrates modular execution: each module handles a specific concern.
/// Named after Asclepius, the Greek god of medicine.
/// </summary>
[Rotation("Asclepius", JobRegistry.Sage, Role = RotationRole.Healer)]
public sealed class Asclepius : BaseHealerRotation<IAsclepiusContext, IAsclepiusModule>
{
    /// <inheritdoc />
    public override string Name => "Asclepius";

    /// <inheritdoc />
    public override uint[] SupportedJobIds => [JobRegistry.Sage];

    /// <inheritdoc />
    public override DebugState DebugState => _debugState;

    /// <inheritdoc />
    protected override List<IAsclepiusModule> Modules => _modules;

    /// <inheritdoc />
    protected override HealerPartyHelper HealerParty => _partyHelper;

    /// <summary>
    /// Gets the Asclepius-specific debug state.
    /// </summary>
    public AsclepiusDebugState AsclepiusDebug => _debugState;

    // Sage-specific services
    private readonly IAddersgallTrackingService _addersgallService;
    private readonly IAdderstingTrackingService _adderstingService;
    private readonly IKardiaManager _kardiaManager;
    private readonly IEukrasiaStateService _eukrasiaService;

    // Debug state
    private readonly AsclepiusDebugState _debugState = new();

    // Helpers
    private readonly AsclepiusStatusHelper _statusHelper;
    private readonly AsclepiusPartyHelper _partyHelper;

    // Timeline integration
    private readonly ITimelineService? _timelineService;

    // Training mode
    private readonly ITrainingService? _trainingService;

    // Live parser — ranks allies for the no-tank Kardia fallback (null-safe, fail-open)
    private readonly Daedalus.Services.Analytics.IDpsMeterService? _dpsMeterService;

    // Modules (sorted by priority)
    private readonly List<IAsclepiusModule> _modules;

    // Scheduler
    private readonly RotationScheduler _scheduler;

    public Asclepius(
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
        Daedalus.Services.Pull.IPullIntentService? pullIntentService = null,
        Daedalus.Services.Analytics.IDpsMeterService? dpsMeterService = null)
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

        // Store parser (Kardia no-tank fallback ranking)
        _dpsMeterService = dpsMeterService;

        // Initialize scheduler
        _scheduler = new RotationScheduler(actionService, jobGauges, configuration, timelineService, errorMetrics);

        // Initialize Sage-specific services
        _addersgallService = new AddersgallTrackingService(jobGauges);
        _adderstingService = new AdderstingTrackingService(jobGauges);
        _kardiaManager = new KardiaManager(partyList, objectTable);
        _eukrasiaService = new EukrasiaStateService();

        // Initialize helpers
        _statusHelper = new AsclepiusStatusHelper();
        _partyHelper = new AsclepiusPartyHelper(objectTable, partyList, hpPredictionService, configuration, _statusHelper);

        // Initialize modules (ordered by priority - lower = executed first)
        _modules = new List<IAsclepiusModule>
        {
            new KardiaModule(),         // Priority 3 - Ensure Kardia is placed
            new ResurrectionModule(),   // Priority 5 - Raise dead party members
            new HealingModule(),        // Priority 10 - Addersgall heals, oGCDs, GCD heals
            new DefensiveModule(),      // Priority 20 - Kerachole, Taurochole, Holos, Panhaima
            new DamageModule(),         // Priority 50 - DoT, Dosis, Phlegma, Psyche
        };

        _modules.Sort((a, b) => a.Priority.CompareTo(b.Priority));

        // Declare healer role for multi-healer coordination
        PartyCoordinationService?.DeclareHealerRole(JobRegistry.Sage, Configuration.PartyCoordination.PreferredHealerRole);

        Log.Info("Asclepius (Sage) rotation initialized");
    }

    /// <inheritdoc />
    public override void OnTerritoryChanged(ushort territoryType)
    {
        // Clear latched Kardia state so duty start re-establishes Kardion on the new
        // tank (or self when solo) instead of trusting stale prior-zone confirmation.
        _kardiaManager.ResetSession();
    }

    /// <inheritdoc />
    protected override void BroadcastHealerGaugeState(IPlayerCharacter player)
    {
        var addersgall = _addersgallService.CurrentStacks;
        var addersting = _adderstingService.CurrentStacks;
        PartyCoordinationService?.BroadcastGaugeState(JobRegistry.Sage, addersgall, addersting, 0);
    }

    #region Abstract Implementation

    /// <inheritdoc />
    protected override unsafe void ExecuteInternal(IPlayerCharacter player)
    {
        ActionService.KardiaRecastGuard = targetId =>
        {
            if (!_kardiaManager.ShouldBlockKardiaUse(player, targetId))
                return false;

            _debugState.KardiaBlockedThisFrame = true;
            return true;
        };

        try
        {
            base.ExecuteInternal(player);
        }
        finally
        {
            ActionService.KardiaRecastGuard = null;
        }
    }

    /// <inheritdoc />
    protected override void UpdateMpForecast(IPlayerCharacter player)
    {
        MpForecastService.Update(
            (int)player.CurrentMp,
            (int)player.MaxMp,
            AsclepiusStatusHelper.HasLucidDreaming(player));
    }

    /// <inheritdoc />
    protected override void UpdateJobSpecificServices(IPlayerCharacter player, bool inCombat)
    {
        // Call base healer service updates
        base.UpdateJobSpecificServices(player, inCombat);

        // Update Kardia target tracking
        _kardiaManager.UpdateKardiaTarget(player);

        var tank = _partyHelper.FindTankInParty(player);
        var kardiaTargetId = _kardiaManager.CurrentKardiaTarget;
        if (kardiaTargetId == 0)
        {
            kardiaTargetId = AsclepiusStatusHelper.FindKardionTargetId(
                player,
                ObjectTable,
                PartyList,
                _partyHelper.GetAllPartyMembers(player),
                tank);
        }

        if (kardiaTargetId == 0
            && tank != null
            && AsclepiusStatusHelper.InferKardionOnTank(player, tank, ObjectTable, PartyList))
        {
            kardiaTargetId = tank.GameObjectId;
        }

        if (kardiaTargetId != 0)
            _kardiaManager.SyncDetectedBearer(kardiaTargetId);

        // Update Sage-specific debug state
        _debugState.KardiaBlockedThisFrame = false;
        _debugState.KardiaExecutedThisFrame = false;
        _debugState.AddersgallStacks = _addersgallService.CurrentStacks;
        _debugState.AddersgallTimer = _addersgallService.TimerRemaining;
        _debugState.AdderstingStacks = _adderstingService.CurrentStacks;
        _debugState.EukrasiaActive = _eukrasiaService.IsEukrasiaActive(player);
        _debugState.ZoeActive = _eukrasiaService.IsZoeActive(player);
        _debugState.KardiaTargetGameObjectId = kardiaTargetId;
        _debugState.KardiaTargetName = ResolveDebugTargetName(player, kardiaTargetId);
        _debugState.TankGameObjectId = tank?.GameObjectId ?? 0;
        _debugState.TankTargetName = tank?.Name?.TextValue ?? "None";
        _debugState.TankHasKardion = tank != null
            && AsclepiusStatusHelper.TankHasKardion(player, tank, ObjectTable, PartyList, kardiaTargetId);
        if (_debugState.TankHasKardion && tank != null)
            _kardiaManager.ConfirmTankKardion(tank);
        _debugState.KardiaTarget = kardiaTargetId != 0
            ? $"{_debugState.KardiaTargetName} ({kardiaTargetId})"
            : "None";
        _debugState.KardiaState = _debugState.TankHasKardion
            ? "Kardion on tank"
            : kardiaTargetId != 0 ? "Kardion active" : "No Kardion";
        _debugState.SoteriaStacks = _kardiaManager.GetSoteriaStacks(player);
        _debugState.PlayerHpPercent = player.MaxHp > 0 ? (float)player.CurrentHp / player.MaxHp : 1f;

        // Populate shared DebugState resource fields for the debug snapshot
        _debugState.LilyCount = _debugState.AddersgallStacks;
        _debugState.BloodLilyCount = _debugState.AdderstingStacks;
        _debugState.LilyStrategy = _debugState.AddersgallStrategy;
    }

    /// <inheritdoc />
    protected override IAsclepiusContext CreateContext(IPlayerCharacter player, bool inCombat, bool isMoving)
    {
        return new AsclepiusContext(
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
            addersgallService: _addersgallService,
            adderstingService: _adderstingService,
            kardiaManager: _kardiaManager,
            eukrasiaService: _eukrasiaService,
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
            log: Log,
            dpsMeter: _dpsMeterService);
    }

    /// <summary>
    /// Fully scheduler-driven execution. All modules push candidates and the scheduler
    /// dispatches the highest-priority candidate from each queue. Push priorities preserve
    /// legacy ordering: Kardia (0-2), Resurrection (1-2), Healing (5-80), Defensive (75-130),
    /// Damage (285-330).
    /// </summary>
    protected override void ExecuteModules(IAsclepiusContext context, bool isMoving, bool inCombat)
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

    private string ResolveDebugTargetName(IPlayerCharacter player, ulong gameObjectId)
    {
        if (gameObjectId == 0)
            return "None";

        foreach (var member in _partyHelper.GetAllPartyMembers(player))
        {
            if (member.GameObjectId == gameObjectId)
                return member.Name?.TextValue ?? $"ID:{gameObjectId}";
        }

        var obj = ObjectTable.SearchById(gameObjectId);
        return obj?.Name.TextValue ?? $"ID:{gameObjectId}";
    }

}
