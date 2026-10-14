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
    private readonly Daedalus.Services.Beastmaster.BeastmasterGaugeReader _gaugeReader;
    private readonly CaptureModule _capture;
    private readonly ArtemisTrickPairing _pairing = new();
    private readonly ICombatEventService _combatEvents;
    private static readonly Daedalus.Services.Beastmaster.BattlehornGame HornGame = new();
    private readonly Daedalus.Services.Beastmaster.BattlehornAssigner _hornAssigner = new(HornGame);

    // Set each frame, read by the action-effect hook between frames.
    private uint _playerEntityId;
    private uint _familiarEntityId;

    // Keeping a familiar out: which horn's beast was out last, since when there has been none, and when a
    // horn was last tried.
    private int _lastOutHorn = ArtemisFamiliarKeeper.None;
    private System.DateTime _familiarGoneSinceUtc = System.DateTime.MinValue;
    private System.DateTime _lastHornTryUtc = System.DateTime.MinValue;

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
        _gaugeReader = new Daedalus.Services.Beastmaster.BeastmasterGaugeReader(log);

        // The action-effect hook sees both halves of the pairing: our Battlehorn presses (which beast
        // came out) and the familiar's own action resolving (the moment to answer its Trick).
        _combatEvents = combatEventService;
        _combatEvents.OnAbilityUsed += OnAbilityUsed;
        _capture = new CaptureModule(beastCaptureLedger, _battlehornReader.IsPetUnlocked);

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

    /// <summary>
    /// Keep the Battlehorns on the chosen team, out of combat: the assigner opens the Master's Bestiary
    /// when they don't match, sets them and closes it. Runs here rather than in the modules so it also
    /// works with no target and nothing to press.
    /// </summary>
    protected override void UpdateJobSpecificServices(IPlayerCharacter player, bool inCombat)
    {
        base.UpdateJobSpecificServices(player, inCombat);

        var cfg = Configuration.Beastmaster;
        _hornAssigner.Tick(
            () => _battlehornReader.UnlockedCount() is null ? null
                : BattlehornTeams.ById(cfg.BattlehornTeam) is { } team
                ? BattlehornTeams.Resolve(team, cfg.SavedBattlehorns,
                    no => _battlehornReader.IsPetUnlocked((uint)no) == true).Rows
                : new int[3],
            allowed: cfg.AutoSetBattlehorns && !inCombat);
    }

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
            log: Log,
            trickPairing: _pairing,
            activeBattlehornRow: _battlehorns.Active?.PactNameId ?? 0,
            gauge: _gaugeReader.TryRead());

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
            _pairing.Reset();
        }

        _playerEntityId = context.Player.EntityId;
        _familiarEntityId = context is ArtemisContext artemisContext ? artemisContext.FamiliarEntityId : 0;
        if (!context.HasFamiliar)
            _pairing.Reset();

        // The roster is player-assigned and changes out of combat, so re-read it each frame rather
        // than caching: it is three bytes off a struct we already have a pointer to.
        _battlehornReader.TryRead(_battlehorns);
        _artemisDebugState.Battlehorns = _battlehorns.Describe(
                context.FamiliarBeast?.Name ?? (context.HasFamiliar ? "unidentified familiar" : null),
                Daedalus.Services.Beastmaster.FamiliarLookup.HasOneWithNature(context.Player))
            + (context.FamiliarBeast is { } beast ? $" (Trick {beast.TrickAffinity})" : "")
            + (context.Gauge is { } gauge ? $" | {gauge.Describe()}" : " | gauge unreadable");

        // No familiar, no Trick, no combos: call one back before anything else. The horn is a cast, so the
        // modules sit this frame out rather than queue a weaponskill on top of it.
        if (TryKeepFamiliarOut(context, isMoving))
            return;

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

    /// <summary>
    /// Blow a Battlehorn when no familiar is out. Returns true when a horn was actually sent this frame.
    /// </summary>
    private bool TryKeepFamiliarOut(IArtemisContext context, bool isMoving)
    {
        var now = System.DateTime.UtcNow;
        var player = context.Player;

        if (context.HasFamiliar)
        {
            _familiarGoneSinceUtc = System.DateTime.MinValue;

            // Learn which horn this beast is on from the beast itself, so a death straight after a plugin
            // reload — when no Battlehorn cast was ever witnessed — still brings the right one back.
            if (context.FamiliarBeast is { } beast)
            {
                for (var i = 0; i < ArtemisBattlehornState.SlotCount; i++)
                {
                    if (_battlehorns[i].PactNameId == (uint)beast.BestiaryNo)
                        _lastOutHorn = i;
                }
            }

            return false;
        }

        if (_familiarGoneSinceUtc == System.DateTime.MinValue)
            _familiarGoneSinceUtc = now;

        System.Span<bool> usable = stackalloc bool[ArtemisBattlehornState.SlotCount];
        for (var i = 0; i < usable.Length; i++)
        {
            var horn = BSTActions.Battlehorns[i];
            usable[i] = !_battlehorns[i].IsEmpty
                && ActionAvailability.MeetsLevelAndLearned(player.Level, ActionService, horn)
                && ActionService.GetActionStatusCode(horn.ActionId, player.GameObjectId) == 0;
        }

        var mounted = RotationServices.Condition?[
            Dalamud.Game.ClientState.Conditions.ConditionFlag.Mounted] == true;
        // Not with the Master's Bestiary open: reassigning a horn there dismisses the familiar on purpose,
        // and a horn blown in the middle of it would fight whoever is doing the assigning.
        var canAct = !player.IsDead && player.CurrentHp > 0 && !isMoving && !mounted && !player.IsCasting
            && !HornGame.IsBestiaryOpen();

        var slot = ArtemisFamiliarKeeper.PickHorn(
            Configuration.Beastmaster.KeepFamiliarOut, context.HasFamiliar, canAct,
            (now - _familiarGoneSinceUtc).TotalSeconds, (now - _lastHornTryUtc).TotalSeconds,
            _lastOutHorn, usable);
        if (slot == ArtemisFamiliarKeeper.None)
            return false;

        _lastHornTryUtc = now;
        var chosen = BSTActions.Battlehorns[slot];
        if (!ActionService.ExecuteOgcd(chosen, player.GameObjectId))
        {
            // The game said ready and then refused the press. Whatever the reason, it will not have changed
            // in three seconds, and each refusal is an error toast — so back right off.
            _lastHornTryUtc = now.AddSeconds(
                ArtemisFamiliarKeeper.RefusedRetrySeconds - ArtemisFamiliarKeeper.RetrySeconds);
            return false;
        }

        _artemisDebugState.PlannedAction = chosen.Name;
        _artemisDebugState.DamageState = "No familiar — calling one back";
        return true;
    }

    /// <summary>Battlehorn action ids, in slot order.</summary>
    private static readonly uint[] BattlehornIds = [44881, 44892, 44894];

    /// <summary>Auto-attack action ids — the familiar's swings are not its Trick resolving.</summary>
    private static bool IsAutoAttack(uint actionId) => actionId is 7 or 8;

    private void OnAbilityUsed(uint casterEntityId, uint actionId)
    {
        if (casterEntityId == 0)
            return;

        if (casterEntityId == _playerEntityId)
        {
            var slot = System.Array.IndexOf(BattlehornIds, actionId);
            if (slot >= 0)
            {
                _battlehorns.OnSummoned(slot);
                _lastOutHorn = slot;
            }

            return;
        }

        if (casterEntityId == _familiarEntityId && !IsAutoAttack(actionId))
            _pairing.OnFamiliarAction();
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _combatEvents.OnAbilityUsed -= OnAbilityUsed;
        base.Dispose(disposing);
    }
}
