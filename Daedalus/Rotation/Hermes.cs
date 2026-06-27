using System;
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
using Daedalus.Rotation.HermesCore.Context;
using Daedalus.Rotation.ApolloCore.Helpers;
using Daedalus.Rotation.HermesCore.Helpers;
using Daedalus.Rotation.HermesCore.Modules;
using Daedalus.Services;
using Daedalus.Services.Action;
using Daedalus.Services.Debuff;
using Daedalus.Services.Positional;
using Daedalus.Services.Positional.Navigation;
using Daedalus.Services.Prediction;
using Daedalus.Services.Party;
using Daedalus.Services.Stats;
using Daedalus.Services.Targeting;
using Daedalus.Services.Training;
using Daedalus.Timeline;

namespace Daedalus.Rotation;

/// <summary>
/// Ninja rotation module (RSR-style reactive execution).
/// Orchestrates modular execution: each module handles a specific concern.
/// Named after Hermes, the Greek god of speed and trickery.
/// </summary>
[Rotation("Hermes", JobRegistry.Ninja, JobRegistry.Rogue, Role = RotationRole.MeleeDps)]
public sealed class Hermes : BaseMeleeDpsRotation<IHermesContext, IHermesModule>
{
    /// <inheritdoc />
    public override string Name => "Hermes";

    /// <inheritdoc />
    public override uint[] SupportedJobIds => [JobRegistry.Ninja, JobRegistry.Rogue];

    /// <inheritdoc />
    public override DebugState DebugState => _debugState;

    /// <inheritdoc />
    protected override List<IHermesModule> Modules => _modules;

    /// <summary>
    /// Gets the Hermes-specific debug state. Used for Ninja-specific debug display.
    /// </summary>
    public HermesDebugState HermesDebug => _hermesDebugState;

    // Persistent debug state
    private readonly HermesDebugState _hermesDebugState = new();

    // IRotation-compatible debug state (for common debug interface)
    private readonly DebugState _debugState = new();

    // Helpers (shared across modules)
    private readonly HermesStatusHelper _statusHelper;
    private readonly MeleeDpsPartyHelper _partyHelper;
    private readonly MudraHelper _mudraHelper;
    private readonly HermesNinjutsuExecutor _ninjutsuExecutor;

    // Modules (sorted by priority - lower = higher priority)
    private readonly List<IHermesModule> _modules;

    // Timeline service for fight-aware rotation (optional)
    private readonly ITimelineService? _timelineService;

    // Party coordination service for burst alignment (optional)
    private readonly IPartyCoordinationService? _partyCoordinationService;

    // Training service for decision explanations (optional)
    private readonly ITrainingService? _trainingService;

    // Gauge values (read each frame)
    private int _ninki;
    private int _kazematoi;

    // Scheduler
    private readonly RotationScheduler _scheduler;

    private readonly NinjaPositionalAnticipationProvider _positionalAnticipationProvider;
    private readonly NinjaBurstApproachService _burstApproachService;

    // Downtime probe — session accumulators reset on combat entry
    private bool _wasInCombatLastFrame;
    private DateTime _lastDowntimeProbeTime;
    private float _sessionMudraReservationSeconds;
    private float _sessionTrueIdleSeconds;

    public Hermes(
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
        NinjaPositionalAnticipationProvider ninjaPositionalAnticipationProvider,
        NinjaBurstApproachService ninjaBurstApproachService,
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
        _positionalAnticipationProvider = ninjaPositionalAnticipationProvider;
        _burstApproachService = ninjaBurstApproachService;
        _timelineService = timelineService;
        _partyCoordinationService = partyCoordinationService;
        _trainingService = trainingService;

        _scheduler = new RotationScheduler(actionService, jobGauges, configuration, timelineService, errorMetrics);

        // Initialize helpers
        _statusHelper = new HermesStatusHelper();
        _partyHelper = new MeleeDpsPartyHelper(objectTable, partyList);
        _mudraHelper = new MudraHelper();
        _ninjutsuExecutor = new HermesNinjutsuExecutor();

        // Initialize modules (ordered by priority - lower = executed first)
        _modules = new List<IHermesModule>
        {
            new NinjutsuModule(_ninjutsuExecutor),  // Priority 10 - Mudra sequences (highest priority)
            new BuffModule(BurstWindowService),      // Priority 20 - Buff management
            new DamageModule(BurstWindowService, SmartAoEService, _ninjutsuExecutor),    // Priority 30 - DPS rotation
        };

        // Sort by priority
        _modules.Sort((a, b) => a.Priority.CompareTo(b.Priority));
    }

    #region Abstract Implementation

    /// <inheritdoc />
    protected override void ReadGaugeValues()
    {
        _ninki = SafeGameAccess.GetNinNinki(ErrorMetrics);
        _kazematoi = SafeGameAccess.GetNinKazematoi(ErrorMetrics);
    }

    /// <inheritdoc />
    protected override uint GetMeleeRangeActionId() => NINActions.SpinningEdge.ActionId;

    /// <inheritdoc />
    protected override IPositionalAnticipationProvider? GetPositionalAnticipationProvider()
        => _positionalAnticipationProvider;

    /// <inheritdoc />
    protected override bool IsPositionalMovementEnabled()
        => Configuration.Ninja.EnablePositionalMovement || Configuration.Ninja.EnforcePositionals;

    /// <summary>
    /// NIN positionals: Aeolian Edge = rear, Armor Crush = flank.
    /// After Gust Slash, pick based on Kazematoi stacks.
    /// </summary>
    protected override PositionalType? GetNextRequiredPositional()
    {
        if (LastComboAction == NINActions.GustSlash.ActionId)
            return HermesKazematoiRules.GetFinisherPositional(_kazematoi);
        return null;
    }

    /// <inheritdoc />
    protected override PositionalAnticipationContext CreatePositionalAnticipationContext(IPlayerCharacter player)
        => base.CreatePositionalAnticipationContext(player) with { Kazematoi = _kazematoi };

    /// <inheritdoc />
    protected override void UpdatePositionalMovement(IPlayerCharacter player, bool inCombat)
    {
        var burst = EvaluateBurstApproachContext(player);

        if (PositionalMovementService != null
            && !(burst.InBurstPrep && !burst.AlreadyInMelee && Configuration.Ninja.EnableBurstMeleeApproach))
        {
            var resolvedTarget = PositionalTarget
                ?? TargetingService.FindEnemy(
                    Configuration.Targeting.EnemyStrategy, 25f, ObjectTable.LocalPlayer!) as IBattleChara;

            PositionalMovementTarget? movementTarget = resolvedTarget is { } t
                ? new PositionalMovementTarget(
                    t.Position,
                    t.HitboxRadius,
                    t.Rotation,
                    PositionalService.HasPositionalImmunity(t))
                : null;

            var request = new PositionalMovementUpdateRequest(
                AnticipationProvider: GetPositionalAnticipationProvider(),
                AnticipationContext: CreatePositionalAnticipationContext(player),
                PlayerPosition: player.Position,
                PlayerHitboxRadius: player.HitboxRadius,
                Target: movementTarget,
                ActionService: ActionService,
                InCombat: inCombat,
                EnableMovement: IsPositionalMovementEnabled() && IsAutoMovementAllowed() && ShouldApplyPositionalRequirements(player),
                AllowMovementDuringActionLock: true,
                MaintainMaxMelee: IsMaxMeleeMaintenanceAllowed(),
                MaxMeleeTarget: ResolveMaxMeleeTarget(player, out var maxMeleeFollowsPlayer),
                MaxMeleeTargetFollowsPlayer: maxMeleeFollowsPlayer,
                VNavFlex: Configuration.Nav.VNavFlex);

            PositionalMovementService.Update(in request);
        }

        UpdateBurstMeleeApproach(player, inCombat, burst);
    }

    private readonly record struct BurstApproachContext(
        bool InBurstPrep,
        bool AlreadyInMelee,
        IBattleChara? Target,
        uint KunaisBaneActionId);

    private BurstApproachContext EvaluateBurstApproachContext(IPlayerCharacter player)
    {
        var kunaisBaneActionId = player.Level >= NINActions.KunaisBane.MinLevel
            ? NINActions.KunaisBane.ActionId
            : NINActions.TrickAttack.ActionId;

        var target = ResolveBurstApproachTarget(player);
        var alreadyInMelee = target != null
            && DistanceHelper.IsActionInRange(kunaisBaneActionId, player, target);

        var kunaisBaneReady = HermesBurstPrepHelper.IsKunaisBaneOrTrickReady(ActionService, player.Level);
        var inBurstPrep = _statusHelper.HasSuiton(player)
            || _mudraHelper.HasSuitonBurstLatch
            || (kunaisBaneReady && !alreadyInMelee && !_statusHelper.HasSuiton(player));

        return new BurstApproachContext(inBurstPrep, alreadyInMelee, target, kunaisBaneActionId);
    }

    private void UpdateBurstMeleeApproach(IPlayerCharacter player, bool inCombat, BurstApproachContext burst)
    {
        _hermesDebugState.BurstApproachInBurstPrep = burst.InBurstPrep;
        _hermesDebugState.BurstApproachKbInRange = burst.AlreadyInMelee;
        _hermesDebugState.BurstApproachHasTarget = burst.Target != null;
        _hermesDebugState.BurstApproachTargetName = burst.Target?.Name?.TextValue ?? "";

        if (burst.InBurstPrep && !burst.AlreadyInMelee
            && PositionalMovementService?.State.Phase == PositionalMovementPhase.Moving)
        {
            PositionalMovementService.Cancel("burst prep — closing for Kunai's Bane");
        }

        var positionalPathActive = PositionalMovementService?.State.Phase == PositionalMovementPhase.Moving;

        PositionalMovementTarget? movementTarget = burst.Target is { } battleTarget
            ? new PositionalMovementTarget(
                battleTarget.Position,
                battleTarget.HitboxRadius,
                battleTarget.Rotation,
                PositionalService.HasPositionalImmunity(battleTarget))
            : null;

        var request = new NinjaBurstApproachRequest(
            Enabled: Configuration.Ninja.EnableBurstMeleeApproach && IsAutoMovementAllowed(),
            InCombat: inCombat,
            BurstPrepActive: burst.InBurstPrep,
            AlreadyInMeleeRange: burst.AlreadyInMelee,
            PositionalPathActive: positionalPathActive,
            PlayerPosition: player.Position,
            PlayerHitboxRadius: player.HitboxRadius,
            Target: movementTarget,
            ActionService: ActionService);

        _burstApproachService.Update(in request);

        SyncMovementDebugState();
    }

    /// <summary>
    /// Resolves a burst approach destination target even when the player is beyond melee range.
    /// Falls back to the hard target so vNav can close distance for Kunai's Bane.
    /// </summary>
    private IBattleChara? ResolveBurstApproachTarget(IPlayerCharacter player)
    {
        var strategy = Configuration.Targeting.EnemyStrategy;

        var target = TargetingService.FindEnemy(
            strategy,
            PositionalMovementConstants.BurstApproachTargetSearchNearYalms,
            player);

        target ??= TargetingService.FindEnemy(
            strategy,
            PositionalMovementConstants.BurstApproachTargetSearchFarYalms,
            player);

        target ??= TargetingService.GetUserEnemyTarget();

        return target as IBattleChara;
    }

    private void SyncMovementDebugState()
    {
        _hermesDebugState.PositionalMovementPhase = PositionalMovementService?.State.Phase.ToString() ?? "—";
        _hermesDebugState.PositionalMovementSkipReason = PositionalMovementService?.State.SkipReason ?? "";
        _hermesDebugState.BurstApproachPhase = _burstApproachService.State.Phase.ToString();
        _hermesDebugState.BurstApproachSkipReason = _burstApproachService.State.SkipReason ?? "";
    }

    /// <inheritdoc />
    protected override int DetermineComboStep(uint comboAction, float comboTimer)
        => ComputeComboStep(comboAction, comboTimer);

    internal static int ComputeComboStep(uint comboAction, float comboTimer)
    {
        // Ninja combo: Spinning Edge -> Gust Slash -> Aeolian Edge/Armor Crush
        // AoE combo: Death Blossom -> Hakke Mujinsatsu
        if (comboTimer <= 0)
            return 0;

        return comboAction switch
        {
            // Single target combo
            2240 => 1, // Spinning Edge
            2242 => 2, // Gust Slash

            // AoE combo
            2254 => 1, // Death Blossom
            16488 => 2, // Hakke Mujinsatsu (finisher)

            _ => 0
        };
    }

    /// <summary>
    /// Updates MP forecast. Ninjas don't use MP for abilities.
    /// </summary>
    protected override void UpdateMpForecast(IPlayerCharacter player)
    {
        // Ninjas don't use MP for any abilities
        MpForecastService.Update(
            (int)player.CurrentMp,
            (int)player.MaxMp,
            hasLucidDreaming: false);
    }

    /// <inheritdoc />
    protected override void UpdateCombatState(bool inCombat)
    {
        if (inCombat && !_wasInCombatLastFrame)
        {
            _sessionMudraReservationSeconds = 0f;
            _sessionTrueIdleSeconds = 0f;
            _lastDowntimeProbeTime = default;
        }

        _wasInCombatLastFrame = inCombat;
        base.UpdateCombatState(inCombat);
    }

    /// <summary>
    /// Mudra/ninjutsu reservation windows keep the GCD open while the game slot catches up.
    /// Treat those as active rotation time, not unexplained downtime.
    /// </summary>
    protected override void TrackGcdState(IPlayerCharacter player)
    {
        var canAct = true;
        foreach (var status in player.StatusList)
        {
            if (FFXIVConstants.IncapacitationStatusIds.Contains(status.StatusId))
            {
                canAct = false;
                break;
            }
        }

        var rawGcdReady = ActionService.CanExecuteGcd;
        var mudraReservation = IsMudraReservationWindow(player, out var reservationReason);
        mudraReservation = rawGcdReady && mudraReservation;
        var gcdReady = rawGcdReady && !mudraReservation;

        var now = DateTime.UtcNow;
        var deltaTime = _lastDowntimeProbeTime != default
            ? Math.Min((float)(now - _lastDowntimeProbeTime).TotalSeconds, 0.1f)
            : 0f;
        _lastDowntimeProbeTime = now;

        if (canAct && deltaTime > 0f && rawGcdReady)
        {
            if (mudraReservation)
                _sessionMudraReservationSeconds += deltaTime;
            else
                _sessionTrueIdleSeconds += deltaTime;
        }

        _hermesDebugState.IsGcdReadyRaw = rawGcdReady;
        _hermesDebugState.IsMudraReservationWindow = mudraReservation;
        _hermesDebugState.IsTrueIdleDowntime = rawGcdReady && !mudraReservation;
        _hermesDebugState.DowntimeReservationReason = reservationReason;
        _hermesDebugState.SessionMudraReservationSeconds = _sessionMudraReservationSeconds;
        _hermesDebugState.SessionTrueIdleSeconds = _sessionTrueIdleSeconds;

        ActionTracker.TrackGcdState(
            gcdReady: gcdReady,
            ActionService.GcdRemaining,
            player.IsCasting,
            ActionService.AnimationLockRemaining > 0,
            ActionService.GcdRemaining > 0,
            playerAlive: canAct,
            playerPosition: player.Position,
            inMechanicWindow: false);
    }

    private bool IsMudraReservationWindow(IPlayerCharacter player, out string reason)
    {
        if (_mudraHelper.IsSequenceActive)
        {
            reason = "helper sequence";
            return true;
        }

        if (_statusHelper.IsMudraActive(player))
        {
            reason = "game mudra (496)";
            return true;
        }

        reason = "";
        return false;
    }

    /// <inheritdoc />
    protected override IHermesContext CreateContext(IPlayerCharacter player, bool inCombat, bool isMoving)
    {
        return new HermesContext(
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
            mudraHelper: _mudraHelper,
            debugState: _hermesDebugState,
            ninki: _ninki,
            kazematoi: _kazematoi,
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
    protected override void SyncDebugState(IHermesContext context)
    {
        _debugState.PlannedAction = string.IsNullOrEmpty(_hermesDebugState.PlannedAction)
            ? "None" : _hermesDebugState.PlannedAction;
        _debugState.DpsState = string.IsNullOrEmpty(_hermesDebugState.DamageState)
            ? (context.InCombat ? "Idle" : "Out of combat") : _hermesDebugState.DamageState;

        if (!string.IsNullOrEmpty(_hermesDebugState.NinjutsuState))
            _debugState.PlanningState = _hermesDebugState.NinjutsuState;
        else if (!string.IsNullOrEmpty(_hermesDebugState.BuffState))
            _debugState.PlanningState = _hermesDebugState.BuffState;
        else if (!string.IsNullOrEmpty(_hermesDebugState.DamageState))
            _debugState.PlanningState = _hermesDebugState.DamageState;
        else
            _debugState.PlanningState = context.InCombat ? "Active" : "Idle";

        var aoeMin = context.Configuration.Ninja.AoEMinTargets;
        EnemyPackDebugHelper.SyncAoEDps(_debugState, _hermesDebugState, aoeMin, JobAoERadiusYalms.Melee);

        var target = TargetingService.FindEnemy(
            context.Configuration.Targeting.EnemyStrategy,
            25f,
            context.Player)
            ?? TargetingService.GetUserEnemyTarget() as IBattleNpc;
        _debugState.TargetInfo = target != null
            ? $"{target.Name} ({(float)target.CurrentHp / target.MaxHp:P0})"
            : "None";
        _hermesDebugState.CurrentTarget = _debugState.TargetInfo;

        _debugState.PlayerHpPercent = (float)context.Player.CurrentHp / context.Player.MaxHp;
        _debugState.PartyListCount = context.PartyList.Length;
    }

    /// <inheritdoc />
    protected override void ExecuteModules(IHermesContext context, bool isMoving, bool inCombat)
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
