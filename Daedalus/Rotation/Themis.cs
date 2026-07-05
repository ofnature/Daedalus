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
using Daedalus.Rotation.ThemisCore.Context;
using Daedalus.Rotation.ThemisCore.Helpers;
using Daedalus.Rotation.ThemisCore.Modules;
using Daedalus.Services;
using Daedalus.Services.Action;
using Daedalus.Services.Combat;
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
/// Paladin rotation module (scheduler-driven execution).
/// Named after Themis, the Greek goddess of divine law and order.
/// </summary>
[Rotation("Themis", JobRegistry.Paladin, JobRegistry.Gladiator, Role = RotationRole.Tank)]
public sealed class Themis : BaseTankRotation<IThemisContext, IThemisModule>
{
    /// <inheritdoc />
    public override string Name => "Themis";

    /// <inheritdoc />
    public override uint[] SupportedJobIds => [JobRegistry.Paladin, JobRegistry.Gladiator];

    /// <inheritdoc />
    public override DebugState DebugState => _debugState;

    /// <inheritdoc />
    protected override List<IThemisModule> Modules => _modules;

    /// <summary>
    /// Gets the Themis-specific debug state.
    /// </summary>
    public ThemisDebugState ThemisDebug => _themisDebugState;

    private readonly ThemisDebugState _themisDebugState = new();
    private readonly DebugState _debugState = new();
    private readonly ThemisStatusHelper _statusHelper;
    private readonly ThemisPartyHelper _partyHelper;
    private readonly ITrainingService? _trainingService;
    private readonly IBurstWindowService? _burstWindowService;
    private readonly ITimeToKillService? _timeToKillService;
    private readonly List<IThemisModule> _modules;

    // Scheduler (per-rotation, per-frame priority queue)
    private readonly RotationScheduler _scheduler;

    public Themis(
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
        Daedalus.Services.Pull.IPullIntentService? pullIntentService = null,
        ITimeToKillService? timeToKillService = null,
        Daedalus.Services.Positional.Navigation.IPositionalMovementService? positionalMovementService = null)
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
            enmityService,
            tankCooldownService,
            timelineService,
            partyCoordinationService,
            errorMetrics,
            tinctureDispatcher,
            pullIntentService,
            positionalMovementService)
    {
        _trainingService = trainingService;
        _burstWindowService = burstWindowService;
        _timeToKillService = timeToKillService;

        _scheduler = new RotationScheduler(
            actionService,
            jobGauges,
            configuration,
            timelineService,
            errorMetrics);

        _statusHelper = new ThemisStatusHelper();
        _partyHelper = new ThemisPartyHelper(objectTable, partyList);

        _modules = new List<IThemisModule>
        {
            new EnmityModule(),
            new MitigationModule(),
            new BuffModule(_burstWindowService),
            new DamageModule(),
        };

        _modules.Sort((a, b) => a.Priority.CompareTo(b.Priority));
    }

    #region Abstract Implementation

    /// <inheritdoc />
    protected override int ReadGaugeValue()
    {
        return SafeGameAccess.GetPldOathGauge(ErrorMetrics);
    }

    /// <inheritdoc />
    protected override int DetermineComboStep(uint comboAction, float comboTimer)
        => ComputeComboStep(comboAction, comboTimer);

    internal static int ComputeComboStep(uint comboAction, float comboTimer)
    {
        if (comboAction == 0 || comboTimer <= 0)
            return 0;

        if (comboAction == PLDActions.FastBlade.ActionId)
            return 2;

        if (comboAction == PLDActions.RiotBlade.ActionId)
            return 3;

        if (comboAction == PLDActions.TotalEclipse.ActionId)
            return 2;

        return 0;
    }

    /// <summary>
    /// True when Prominence is unlocked (level + job quest). Mid-combo, Total Eclipse
    /// hotbar replacement is authoritative; otherwise defers to action-manager learn status.
    /// </summary>
    internal static bool IsProminenceAvailable(IActionService actionService, byte level)
    {
        if (actionService.GetAdjustedActionId(PLDActions.TotalEclipse.ActionId) == PLDActions.Prominence.ActionId)
            return true;

        return ActionAvailability.MeetsLevelAndLearned(level, actionService, PLDActions.Prominence);
    }

    /// <summary>
    /// True while the AoE combo is unfinished (Total Eclipse cast, Prominence pending).
    /// PLD's AoE 1-2 is a combo-gated pair of SEPARATE actions (NOT a button-replacement combo —
    /// confirmed against RSR, which uses ProminencePvE / TotalEclipsePvE as distinct actions), so
    /// GetAdjustedActionId(TotalEclipse) never resolves to Prominence in-game. Detect via combo state:
    /// the last combo move was Total Eclipse and the window is still open. The WasLastGcd(Prominence)
    /// guard closes the combo the instant Prominence fires, preventing infinite Prominence re-selection.
    /// </summary>
    internal static bool IsInAoECombo(IActionService actionService, uint lastComboAction, float comboTimeRemaining)
    {
        if (comboTimeRemaining <= 0 || lastComboAction != PLDActions.TotalEclipse.ActionId)
            return false;

        return !actionService.WasLastGcd(PLDActions.Prominence.ActionId);
    }

    /// <summary>
    /// True while the single-target combo is unfinished (Fast Blade or Riot Blade cast).
    /// </summary>
    internal static bool IsInSingleTargetCombo(uint lastComboAction, float comboTimeRemaining) =>
        comboTimeRemaining > 0 &&
        (lastComboAction == PLDActions.FastBlade.ActionId ||
         lastComboAction == PLDActions.RiotBlade.ActionId);

    /// <summary>
    /// True when a filler proc would be overwritten by Royal Authority or Total Eclipse.
    /// Covers Atonement chain steps and Divine Might.
    /// </summary>
    internal static bool HasUnspentFillerProcs(IThemisContext context) =>
        context.AtonementStep > 0 || context.HasDivineMight;

    /// <summary>
    /// Resolves Requiescat → Imperator at L96+ via the action manager.
    /// </summary>
    internal static uint ResolveMagicPhaseActionId(IActionService actionService) =>
        actionService.GetAdjustedActionId(PLDActions.Requiescat.ActionId);

    /// <summary>
    /// True when Requiescat/Imperator is off cooldown and enabled.
    /// </summary>
    internal static bool IsMagicPhaseActionReady(IThemisContext context)
    {
        if (!context.Configuration.Tank.EnableRequiescat) return false;
        if (context.Player.Level < PLDActions.Requiescat.MinLevel) return false;
        return context.ActionService.IsActionReady(ResolveMagicPhaseActionId(context.ActionService));
    }

    /// <summary>
    /// GCD priority for Atonement chain steps: Sepulchre &gt; Supplication &gt; Atonement.
    /// Divine Might Holy Spirit uses priority 4 (between Supplication and Atonement).
    /// </summary>
    internal static int AtonementChainPriority(int atonementStep) => atonementStep switch
    {
        3 => 2,
        2 => 3,
        1 => 5,
        _ => 6,
    };

    /// <summary>
    /// Resolves Atonement chain step from button replacement and Atonement Ready proc (Dawntrail).
    /// 1 = Atonement, 2 = Supplication, 3 = Sepulchre, 0 = no proc active.
    /// </summary>
    internal static int ComputeAtonementStep(
        IActionService actionService,
        ThemisStatusHelper statusHelper,
        IPlayerCharacter player,
        byte level)
    {
        if (level < PLDActions.Atonement.MinLevel)
            return 0;

        var adjusted = actionService.GetAdjustedActionId(PLDActions.Atonement.ActionId);

        if (level >= PLDActions.Sepulchre.MinLevel && adjusted == PLDActions.Sepulchre.ActionId)
            return 3;

        if (level >= PLDActions.Supplication.MinLevel && adjusted == PLDActions.Supplication.ActionId)
            return 2;

        if (statusHelper.HasAtonementReady(player))
            return 1;

        return 0;
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
    protected override IThemisContext CreateContext(IPlayerCharacter player, bool inCombat, bool isMoving)
    {
        return new ThemisContext(
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
            debugState: _themisDebugState,
            oathGauge: GaugeValue,
            comboStep: ComboStep,
            lastComboAction: LastComboAction,
            comboTimeRemaining: ComboTimeRemaining,
            timelineService: TimelineService,
            partyCoordinationService: PartyCoordinationService,
            trainingService: _trainingService,
            timeToKillService: _timeToKillService,
            log: Log);
    }

    /// <inheritdoc />
    protected override void ExecuteModules(IThemisContext context, bool isMoving, bool inCombat)
    {
        // Capture WHY the whole rotation stops, so a stall is self-diagnosing in the Why Stuck panel
        // instead of just looking idle. Cleared each frame the rotation actually runs.
        context.Debug.PauseReason = "";

        // Preserve BaseRotation's safety pauses.
        if (Configuration.Targeting.PauseAllOnStandStillPunisher
            && PlayerSafetyHelper.IsStandStillPunisherActive(context.Player))
        {
            context.Debug.PauseReason = "Stand-still punisher (Pyretic) — all actions paused";
            return;
        }
        if (Configuration.Targeting.PauseOnPlayerChannel
            && PlayerSafetyHelper.IsPlayerIntentChannelActive(context.Player))
        {
            context.Debug.PauseReason = "Player channel/stance active — all actions paused";
            return;
        }

        if (!inCombat)
        {
            context.Debug.PauseReason = "Not in combat (rotation idle)";
            // Not an early return on its own — modules already self-gate on InCombat — but record it so a
            // combat-detection drop while enemies are engaged is visible as the stall cause.
        }

        if (TryDispatchTincture(context, inCombat)) return;

        _scheduler.Reset();
        foreach (var module in _modules)
        {
            module.CollectCandidates(context, _scheduler, isMoving);
        }

        // Out of combat the only oGCD candidate is the tank stance (duty-pop stance-on). Two blockers
        // had to fall: the old `inCombat &&` gate discarded the candidate outright, and CanExecuteOgcd
        // (weave math) returns 0 slots when the GCD is fully idle — always the case out of combat — so
        // OOC dispatch bypasses it entirely. In combat, behavior is unchanged.
        if (!inCombat || ActionService.CanExecuteOgcd)
        {
            _scheduler.DispatchOgcd(context);
        }
        if (ActionService.CanExecuteGcd)
        {
            var gcd = _scheduler.DispatchGcd(context);
            if (StuckReasonHelper.Describe(gcd.Dispatched, gcd.GateFailReasons) is { } stuck)
                context.Debug.DamageState = stuck;
        }
    }

    /// <inheritdoc />
    protected override void SyncDebugState(IThemisContext context)
    {
        _debugState.PlanningState = $"GCD:{_themisDebugState.GcdState} Rem:{_themisDebugState.GcdRemaining:F2}s Combat:{_themisDebugState.InCombat} CanGCD:{_themisDebugState.CanExecuteGcd} Tgt:{_themisDebugState.CurrentTarget}";
        _debugState.PlannedAction = _themisDebugState.PlannedAction;
        _debugState.DpsState = _themisDebugState.DamageState;
        _debugState.DefensiveState = _themisDebugState.MitigationState;
        EnemyPackDebugHelper.SyncAoEDps(_debugState, _themisDebugState, context.Configuration.Tank.GetEffectiveAoEMinTargets(JobRegistry.Paladin), JobAoERadiusYalms.Tank);
        _debugState.PlayerHpPercent = (float)context.Player.CurrentHp / context.Player.MaxHp;
        _debugState.PartyListCount = context.PartyList.Length;
        _debugState.TargetInfo = TargetingDebugHelper.FormatTargetInfo(context.CurrentTarget, context.TargetingService);
    }

    #endregion
}
