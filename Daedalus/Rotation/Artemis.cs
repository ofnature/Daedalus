using System.Collections.Generic;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Party;
using Dalamud.Plugin.Services;
using Daedalus.Data;
using Daedalus.Rotation.ArtemisCore.Context;
using Daedalus.Rotation.ArtemisCore.Helpers;
using Daedalus.Rotation.ArtemisCore.Modules;
using Daedalus.Rotation.Base;
using Daedalus.Rotation.Common;
using Daedalus.Rotation.Common.Helpers;
using Daedalus.Rotation.Common.Scheduling;
using Daedalus.Services;
using Daedalus.Services.Action;
using Daedalus.Services.Debuff;
using Daedalus.Services.Positional;
using Daedalus.Services.Positional.Navigation;
using Daedalus.Services.Prediction;
using Daedalus.Services.Stats;
using Daedalus.Services.Targeting;
using Daedalus.Timeline;

namespace Daedalus.Rotation;

/// <summary>
/// Beastmaster rotation. Named after Artemis, goddess of the hunt and of wild animals.
///
/// <para>
/// A limited job: level 1-50, no role actions <i>at all</i> (not "none learned"), and barred from
/// roulettes, Eureka/Bozja/Occult Crescent, Variant/Criterion, Ultimates, Deep Dungeons and PvP.
/// So there is no burst alignment, no tank swap, no co-healer arbitration and no countdown work
/// here — the target is open-world solo play and the Crucible of the Unbroken.
/// </para>
///
/// <para>
/// <b>Role is MeleeDps but the movement machinery is suppressed</b> via
/// <see cref="LimitedJobContentPolicy"/>. RotationRole is metadata only, so declaring it costs
/// nothing; the positional and max-melee behaviour is opt-in through the base class and this job
/// opts out. Beastmaster's published kit states no flank or rear bonus on any action, and the
/// max-melee/boundary-camp movers were built for eight-man content it cannot enter.
/// </para>
/// </summary>
[Rotation("Artemis", JobRegistry.Beastmaster, Role = RotationRole.MeleeDps)]
public sealed class Artemis : BaseMeleeDpsRotation<IArtemisContext, IArtemisModule>
{
    /// <inheritdoc />
    public override string Name => "Artemis";

    /// <inheritdoc />
    public override uint[] SupportedJobIds => [JobRegistry.Beastmaster];

    /// <inheritdoc />
    public override DebugState DebugState => _debugState;

    /// <inheritdoc />
    protected override List<IArtemisModule> Modules => _modules;

    /// <summary>Beastmaster-specific debug state, for the debug window.</summary>
    public ArtemisDebugState ArtemisDebug => _artemisDebugState;

    private readonly ArtemisDebugState _artemisDebugState = new();
    private readonly DebugState _debugState = new();

    private readonly ArtemisInstinctTracker _instinct = new();
    private readonly MeleeDpsPartyHelper _partyHelper;
    private readonly List<IArtemisModule> _modules;
    private readonly ITimelineService? _timelineService;
    private readonly RotationScheduler _scheduler;
    private readonly ArtemisBattlehornState _battlehorns = new();
    private readonly Daedalus.Services.Beastmaster.BattlehornReader _battlehornReader;
    private readonly CaptureModule _capture;

    public Artemis(
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
        IErrorMetricsService? errorMetrics = null,
        Daedalus.Services.Consumables.ITinctureDispatcher? tinctureDispatcher = null,
        Daedalus.Services.Pull.IPullIntentService? pullIntentService = null,
        Daedalus.Services.Beastmaster.BeastCaptureLedger? beastCaptureLedger = null)
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
            burstWindowService: null,
            errorMetrics: errorMetrics,
            positionalMovementService: positionalMovementService,
            tinctureDispatcher: tinctureDispatcher,
            pullIntentService: pullIntentService)
    {
        _timelineService = timelineService;
        _partyHelper = new MeleeDpsPartyHelper(objectTable, partyList);
        _scheduler = new RotationScheduler(actionService, jobGauges, configuration, timelineService, errorMetrics);

        _battlehornReader = new Daedalus.Services.Beastmaster.BattlehornReader(log);
        _capture = new CaptureModule(beastCaptureLedger);

        // Capture first: a missed capture window cannot be retried this pull, while a dropped
        // damage GCD costs only that GCD.
        _modules = [_capture, new DamageModule()];
        _modules.Sort((a, b) => a.Priority.CompareTo(b.Priority));
    }

    #region Abstract Implementation

    /// <summary>
    /// Nothing to read. Beastmaster's TP gauges are absent from ClientStructs — there is no
    /// BeastmasterGauge type, and the two false friends (WAR's BeastGauge, MNK's BeastChakraGauge)
    /// belong to other jobs. The rotation gates on action status codes instead, so no gauge read is
    /// required for correctness; only the 400→1,000 potency scaling would want the number.
    /// </summary>
    protected override void ReadGaugeValues() { }

    /// <summary>
    /// Smash Axe → Axeblade Bite → Shieldsplitter. One chain, no AoE variant.
    /// </summary>
    protected override int DetermineComboStep(uint comboAction, float comboTimer)
        => ComputeComboStep(comboAction, comboTimer);

    internal static int ComputeComboStep(uint comboAction, float comboTimer)
    {
        if (comboTimer <= 0)
            return 0;

        return comboAction switch
        {
            44879 => 1, // Smash Axe → Axeblade Bite
            44883 => 2, // Axeblade Bite → Shieldsplitter
            _ => 0,
        };
    }

    /// <summary>Beastmaster spends TP, not MP.</summary>
    protected override void UpdateMpForecast(IPlayerCharacter player)
        => MpForecastService.Update((int)player.CurrentMp, (int)player.MaxMp, hasLucidDreaming: false);

    /// <inheritdoc />
    protected override IArtemisContext CreateContext(IPlayerCharacter player, bool inCombat, bool isMoving)
        => new ArtemisContext(
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
            partyHelper: _partyHelper,
            instinct: _instinct,
            debugState: _artemisDebugState,
            comboStep: ComboStep,
            lastComboAction: LastComboAction,
            comboTimeRemaining: ComboTimeRemaining,
            timelineService: _timelineService,
            log: Log);

    /// <inheritdoc />
    protected override void SyncDebugState(IArtemisContext context)
    {
        _debugState.PlanningState = _artemisDebugState.PlanningState;
        _debugState.PlannedAction = _artemisDebugState.PlannedAction;
        _debugState.DpsState = _artemisDebugState.DamageState;

        _debugState.PlayerHpPercent = (float)context.Player.CurrentHp / context.Player.MaxHp;
        _debugState.PartyListCount = context.PartyList.Length;
        _debugState.TargetInfo = TargetingDebugHelper.FormatTargetInfo(null, context.TargetingService);
    }

    /// <inheritdoc />
    protected override void ExecuteModules(IArtemisContext context, bool isMoving, bool inCombat)
    {
        if (Configuration.Targeting.PauseAllOnStandStillPunisher
            && PlayerSafetyHelper.IsStandStillPunisherActive(context.Player))
            return;
        if (Configuration.Targeting.PauseOnPlayerChannel
            && PlayerSafetyHelper.IsPlayerIntentChannelActive(context.Player))
            return;

        // Drop the instinct chain the moment combat ends: the 7s window cannot survive a pull gap,
        // and a stale chain would send the picker after a combo that expired.
        if (!inCombat)
        {
            _instinct.Reset();
            _capture.Reset();
        }

        // The roster is player-assigned and changes out of combat, so re-read it each frame rather
        // than caching: it is three bytes off a struct we already have a pointer to.
        _battlehornReader.TryRead(_battlehorns);
        _artemisDebugState.Battlehorns = _battlehorns.Describe();

        _scheduler.Reset();
        foreach (var module in _modules)
            module.CollectCandidates(context, _scheduler, isMoving);

        // oGCDs FIRST and unconditionally, not only inside a weave window. The instinctual skills
        // are Weaponskills on an independent 5s recast, so holding them for a weave slot would idle
        // the job's main damage source behind the combo chain.
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
