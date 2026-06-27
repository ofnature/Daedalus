using System.Collections.Generic;
using Dalamud.Game.ClientState.JobGauge;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Party;
using Dalamud.Plugin.Services;
using Daedalus.Data;
using Daedalus.Rotation.Base;
using Daedalus.Rotation.Common;
using Daedalus.Rotation.CirceCore.Context;
using Daedalus.Rotation.Common.Helpers;
using Daedalus.Rotation.Common.Scheduling;
using Daedalus.Rotation.CirceCore.Helpers;
using Daedalus.Rotation.CirceCore.Modules;
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
/// Red Mage rotation module (RSR-style reactive execution).
/// Orchestrates modular execution: each module handles a specific concern.
/// Named after Circe, the Greek goddess of sorcery who transformed her enemies with magic.
/// </summary>
[Rotation("Circe", JobRegistry.RedMage, Role = RotationRole.Caster)]
public sealed class Circe : BaseCasterDpsRotation<ICirceContext, ICirceModule>
{
    /// <inheritdoc />
    public override string Name => "Circe";

    /// <inheritdoc />
    public override uint[] SupportedJobIds => [JobRegistry.RedMage];

    /// <inheritdoc />
    public override DebugState DebugState => _debugState;

    /// <inheritdoc />
    protected override List<ICirceModule> Modules => _modules;

    /// <summary>
    /// Gets the Circe-specific debug state. Used for Red Mage-specific debug display.
    /// </summary>
    public CirceDebugState CirceDebug => _circeDebugState;

    // Persistent debug state
    private readonly CirceDebugState _circeDebugState = new();

    // IRotation-compatible debug state (for common debug interface)
    private readonly DebugState _debugState = new();

    // Helpers (shared across modules)
    private readonly CirceStatusHelper _statusHelper;
    private readonly CasterPartyHelper _partyHelper;

    // Modules (sorted by priority - lower = higher priority)
    private readonly List<ICirceModule> _modules;

    // Timeline service for fight-aware rotation (optional)
    private readonly ITimelineService? _timelineService;

    // Party coordination service for raid buff synchronization (optional)
    private readonly IPartyCoordinationService? _partyCoordinationService;

    // Training service for decision explanations (optional)
    private readonly ITrainingService? _trainingService;

    // Gauge values (read each frame)
    private int _blackMana;
    private int _whiteMana;
    private int _manaStacks;

    // Melee combo step tracking (0=None, 1=Zwerchhau next, 2=Redoublement next,
    // 3=Finisher next, 4=Scorch next, 5=Resolution next).
    // Computed via ManaStacks + the game's combo field (Verflare→Resolution).
    // combo field. Replaces the old raw combo-action/combo-timer pair which was
    // unreliable for the Enchanted chain.
    private int _meleeComboStep;

    // Moulinet (AoE melee) combo step tracking (0=None, 1=Deux next, 2=Trois next).
    // Computed via action replacement on Enchanted Moulinet.
    private int _moulinetStep;

    private bool _wasInCombat;
    private bool _suppressMeleeComboStep;

    // Scheduler
    private readonly RotationScheduler _scheduler;

    public Circe(
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
        _timelineService = timelineService;
        _partyCoordinationService = partyCoordinationService;
        _trainingService = trainingService;

        _scheduler = new RotationScheduler(actionService, jobGauges, configuration, timelineService, errorMetrics);

        // Initialize helpers
        _statusHelper = new CirceStatusHelper();
        _partyHelper = new CasterPartyHelper(objectTable, partyList);

        // Initialize modules (ordered by priority - lower = executed first)
        _modules = new List<ICirceModule>
        {
            new ResurrectionModule(),              // Priority 15 - Raise dead party members (Dualcast/Swiftcast Verraise)
            new BuffModule(BurstWindowService),    // Priority 20 - oGCD management (Fleche, Contre Sixte, Embolden, etc.)
            new DamageModule(BurstWindowService, SmartAoEService),  // Priority 30 - GCD rotation (Dualcast, melee combo, finishers)
        };

        // Sort by priority
        _modules.Sort((a, b) => a.Priority.CompareTo(b.Priority));
    }

    #region Abstract Implementation

    /// <inheritdoc />
    protected override void ReadGaugeValues()
    {
        _blackMana = SafeGameAccess.GetRdmBlackMana(ErrorMetrics);
        _whiteMana = SafeGameAccess.GetRdmWhiteMana(ErrorMetrics);
        _manaStacks = SafeGameAccess.GetRdmManaStacks(ErrorMetrics);

        UpdateMeleeComboStep();
        UpdateMoulinetStep();
    }

    /// <summary>
    /// Pure mapping from the adjusted Moulinet action ID to the Moulinet AoE
    /// chain step. Returns 1 if Deux is next (after Moulinet), 2 if Trois is
    /// next (after Deux), 0 otherwise. Extracted from the wrapper for unit-test
    /// coverage.
    /// </summary>
    internal static int ComputeMoulinetStep(uint adjustedMoulinetId)
    {
        if (adjustedMoulinetId == RDMActions.EnchantedMoulinetDeux.ActionId)
            return 1;
        if (adjustedMoulinetId == RDMActions.EnchantedMoulinetTrois.ActionId)
            return 2;
        return 0;
    }

    /// <summary>
    /// Updates the Moulinet (AoE melee) combo step using action replacement on
    /// Enchanted Moulinet. The chain is Moulinet → Moulinet Deux → Moulinet Trois.
    /// Only the Deux/Trois steps exist at Lv.96+; below that, Moulinet is a single hit.
    /// </summary>
    private unsafe void UpdateMoulinetStep()
    {
        _moulinetStep = 0;

        var actionManager = SafeGameAccess.GetActionManager(ErrorMetrics);
        if (actionManager == null)
            return;

        var adjustedId = actionManager->GetAdjustedActionId(RDMActions.EnchantedMoulinet.ActionId);
        _moulinetStep = ComputeMoulinetStep(adjustedId);
    }

    /// <summary>
    /// Pure mapping from ManaStacks / vanilla-combo state to the 5-step Enchanted
    /// melee chain index. Precedence (top wins):
    ///   1) comboTimer &gt; 0 AND comboAction is Verflare/Verholy ⇒ 4
    ///   2) comboTimer &gt; 0 AND comboAction is Scorch ⇒ 5
    ///   3) comboTimer &gt; 0 AND (ManaStacks ≥ 3 OR last hit was Redoublement) ⇒ 3
    ///   4) comboTimer &gt; 0 AND (ManaStacks ≥ 2 OR last hit was Zwerchhau) ⇒ 2
    ///   5) comboTimer &gt; 0 AND (ManaStacks ≥ 1 OR last hit was Riposte) ⇒ 1
    ///   else ⇒ 0
    /// Both comboAction and ManaStacks require an active combo timer so stale gauge
    /// or combo IDs from downtime do not start the chain at combat entry.
    /// Extracted from the wrapper for unit-test coverage.
    /// </summary>
    internal static int ComputeMeleeComboStep(int manaStacks, uint comboAction, float comboTimer)
    {
        if (comboTimer > 0)
        {
            if (comboAction == RDMActions.Verflare.ActionId || comboAction == RDMActions.Verholy.ActionId)
                return 4;
            if (comboAction == RDMActions.Scorch.ActionId)
                return 5;
            if (IsRedoublementComboAction(comboAction))
                return 3;
            if (IsZwerchhauComboAction(comboAction))
                return 2;
            if (IsRiposteComboAction(comboAction))
                return 1;
        }

        if (comboTimer > 0)
        {
            if (manaStacks >= 3)
                return 3;
            if (manaStacks >= 2)
                return 2;
            if (manaStacks >= 1)
                return 1;
        }

        return 0;
    }

    private static bool IsRiposteComboAction(uint actionId) =>
        actionId == RDMActions.Riposte.ActionId || actionId == RDMActions.EnchantedRiposte.ActionId;

    private static bool IsZwerchhauComboAction(uint actionId) =>
        actionId == RDMActions.Zwerchhau.ActionId || actionId == RDMActions.EnchantedZwerchhau.ActionId;

    private static bool IsRedoublementComboAction(uint actionId) =>
        actionId == RDMActions.Redoublement.ActionId || actionId == RDMActions.EnchantedRedoublement.ActionId;

    /// <summary>
    /// Updates the melee combo step from Mana Stacks (steps 1-3) and the game's
    /// combo field for steps 4-5 (Scorch/Resolution).
    /// </summary>
    private void UpdateMeleeComboStep()
    {
        if (_suppressMeleeComboStep)
        {
            var minCombatSeconds = Configuration.RedMage.MeleeComboMinCombatSeconds;
            if (CombatEventService.GetCombatDurationSeconds() < minCombatSeconds)
            {
                _meleeComboStep = 0;
                return;
            }

            _suppressMeleeComboStep = false;
        }

        var comboAction = SafeGameAccess.GetComboAction(ErrorMetrics);
        var comboTimer = SafeGameAccess.GetComboTimer(ErrorMetrics);

        _meleeComboStep = ComputeMeleeComboStep(_manaStacks, comboAction, comboTimer);
    }

    /// <inheritdoc />
    protected override void UpdateCombatState(bool inCombat)
    {
        if (inCombat && !_wasInCombat)
        {
            _meleeComboStep = 0;
            _moulinetStep = 0;
            _suppressMeleeComboStep = true;
        }
        else if (!inCombat && _wasInCombat)
        {
            _meleeComboStep = 0;
            _moulinetStep = 0;
            _suppressMeleeComboStep = false;
        }

        _wasInCombat = inCombat;
        base.UpdateCombatState(inCombat);
    }

    /// <summary>
    /// Updates MP forecast. Red Mages use Lucid Dreaming for MP management.
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
    protected override ICirceContext CreateContext(IPlayerCharacter player, bool inCombat, bool isMoving)
    {
        return new CirceContext(
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
            debugState: _circeDebugState,
            blackMana: _blackMana,
            whiteMana: _whiteMana,
            manaStacks: _manaStacks,
            meleeComboStep: _meleeComboStep,
            moulinetStep: _moulinetStep,
            timelineService: _timelineService,
            partyCoordinationService: _partyCoordinationService,
            trainingService: _trainingService,
            log: Log);
    }

    /// <inheritdoc />
    protected override void SyncDebugState(ICirceContext context)
    {
        // Map Red Mage debug state to common debug state fields
        _debugState.PlanningState = _circeDebugState.PlanningState;
        _debugState.PlannedAction = _circeDebugState.PlannedAction;
        _debugState.DpsState = _circeDebugState.DamageState;
        // Note: BuffState is tracked in CirceDebugState but not in common DebugState

        EnemyPackDebugHelper.SyncAoEDps(_debugState, _circeDebugState, context.Configuration.RedMage.AoEMinTargets, JobAoERadiusYalms.Caster);

        // Party/player info
        _debugState.PlayerHpPercent = (float)context.Player.CurrentHp / context.Player.MaxHp;
        _debugState.PartyListCount = context.PartyList.Length;
        _debugState.TargetInfo = TargetingDebugHelper.FormatTargetInfo(null, context.TargetingService);
    }

    /// <inheritdoc />
    protected override void ExecuteModules(ICirceContext context, bool isMoving, bool inCombat)
    {
        if (Configuration.Targeting.PauseAllOnStandStillPunisher
            && PlayerSafetyHelper.IsStandStillPunisherActive(context.Player))
            return;
        if (Configuration.Targeting.PauseOnPlayerChannel
            && PlayerSafetyHelper.IsPlayerIntentChannelActive(context.Player))
            return;

        if (TryDispatchTincture(context, inCombat))
            return;

        _scheduler.Reset();
        foreach (var module in _modules)
            module.CollectCandidates(context, _scheduler, isMoving);

        // RDM ResurrectionModule fires Verraise via Dualcast/Swiftcast both pre and
        // post combat (raise during phase resets, downtime). Drop the inCombat gate
        // on the oGCD pass so Swiftcast can dispatch out of combat.
        if (ActionService.CanExecuteOgcd)
            _scheduler.DispatchOgcd(context);

        if (ActionService.CanExecuteGcd)
            _scheduler.DispatchGcd(context);
    }

    #endregion
}
