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
    private readonly DamageModule _damage = new();
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

    /// <summary>Horn presses the game refused, in a row. Reset when a familiar is out.</summary>
    private int _hornRefusals;

    /// <summary>The beast last seen out, for the log line when it goes.</summary>
    private string _lastFamiliarName = "";

    /// <summary>What the keeper and the pre-fight Borrow last logged they were waiting for, so each is logged once.</summary>
    private string _hornWaitLogged = "", _preBorrowWaitLogged = "";

    /// <summary>When the boss pull was first held back for the first beast and the pre-fight Borrow.</summary>
    private System.DateTime _pullHoldSinceUtc = System.DateTime.MinValue;

    /// <summary>The first beast is out and the pre-fight Borrow is done or cannot be: nothing left to wait for.</summary>
    private bool _prePullSettled;

    /// <summary>This pull's hold has been let go; not held again until a fight has been fought.</summary>
    private bool _pullHoldReleased;

    /// <inheritdoc />
    protected override bool HoldsBossPull(IPlayerCharacter player)
    {
        if (_pullHoldReleased)
            return false;

        var now = System.DateTime.UtcNow;
        var first = _pullHoldSinceUtc == System.DateTime.MinValue;
        if (first)
            _pullHoldSinceUtc = now;
        var held = (now - _pullHoldSinceUtc).TotalSeconds;
        var cfg = Configuration.Beastmaster;
        var hold = ArtemisFamiliarKeeper.HoldsThePull(
            Daedalus.Services.Beastmaster.CrucibleTracker.Current.OnBoard,
            Daedalus.Services.Beastmaster.CrucibleHints.Degree > 0 || cfg.CrucibleGoal == CrucibleTeamGoal.Legendary,
            cfg.KeepFamiliarOut, _prePullSettled, held);
        if (hold)
        {
            // The boss engine walks the toon to the boss the moment it names it; the horn is a cast.
            RotationServices.BossModSafety?.RequestHold(1d);
            if (first)
                RotationServices.DebugLog?.Log(
                    Daedalus.Services.Debug.DebugLogCategory.Action, Daedalus.Services.Debug.DebugLogSeverity.Info,
                    "[Crucible] pull held: the first beast and the Borrow go out first");
            return true;
        }

        _pullHoldReleased = true;
        if (!first)
        {
            RotationServices.BossModSafety?.RequestHold(0d);
            RotationServices.DebugLog?.Log(
                Daedalus.Services.Debug.DebugLogCategory.Action, Daedalus.Services.Debug.DebugLogSeverity.Info,
                $"[Crucible] pull opened after {held:0.0}s ({(_prePullSettled ? "ready" : "out of time")}); horns: {HornNames()}");
        }

        return false;
    }

    // The pre-fight Borrow: when it was pressed, and whether it is given up on until the next fight.
    private System.DateTime _preBorrowPressedUtc = System.DateTime.MinValue;
    private bool _preBorrowGaveUp;

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
        _modules = [_capture, _damage];
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
            gauge: _gaugeReader.TryRead(),
            noReplacementHorn: NoReplacementHorn(player),
            wespeWaiting: WespeWaiting(player),
            wespeInRoster: WespeInRoster());

    /// <summary>
    /// Whether no horn but the out beast's own is ready (see ArtemisFamiliarKeeper.NoReplacementHorn). Read from the
    /// horns' cooldowns, not the press status: the status also says "no" during an animation lock, which made the check
    /// flicker in the weave slot where the retreat is decided (field 2026-10-27, logged "held" right after a retreat).
    /// </summary>
    private bool NoReplacementHorn(IPlayerCharacter player)
    {
        System.Span<bool> usable = stackalloc bool[ArtemisBattlehornState.SlotCount];
        for (var i = 0; i < usable.Length; i++)
        {
            var horn = BSTActions.Battlehorns[i];
            usable[i] = !_battlehorns[i].IsEmpty
                && ActionAvailability.MeetsLevelAndLearned(player.Level, ActionService, horn)
                && ActionService.IsActionReady(horn.ActionId);
        }

        return ArtemisFamiliarKeeper.NoReplacementHorn(_battlehorns.IsPopulatedFromGame, _lastOutHorn, usable);
    }

    /// <summary>A Wespe sits in a horn other than the out one, ready or not (see ArtemisFamiliarKeeper.WespeInRoster).</summary>
    private bool WespeInRoster()
    {
        System.Span<uint> beasts = stackalloc uint[ArtemisBattlehornState.SlotCount];
        for (var i = 0; i < beasts.Length; i++)
            beasts[i] = _battlehorns[i].PactNameId;
        return ArtemisFamiliarKeeper.WespeInRoster(_battlehorns.IsPopulatedFromGame, _lastOutHorn, beasts);
    }

    /// <summary>A Wespe waits in a ready horn other than the out one (see ArtemisFamiliarKeeper.WespeWaiting).</summary>
    private bool WespeWaiting(IPlayerCharacter player)
    {
        System.Span<bool> usable = stackalloc bool[ArtemisBattlehornState.SlotCount];
        System.Span<uint> beasts = stackalloc uint[ArtemisBattlehornState.SlotCount];
        for (var i = 0; i < usable.Length; i++)
        {
            var horn = BSTActions.Battlehorns[i];
            beasts[i] = _battlehorns[i].PactNameId;
            usable[i] = !_battlehorns[i].IsEmpty
                && ActionAvailability.MeetsLevelAndLearned(player.Level, ActionService, horn)
                && ActionService.IsActionReady(horn.ActionId);
        }

        return ArtemisFamiliarKeeper.WespeWaiting(_battlehorns.IsPopulatedFromGame, _lastOutHorn, beasts, usable);
    }

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
        _lastOutHorn = ArtemisFamiliarKeeper.LastOutBetweenFights(
            _lastOutHorn, Daedalus.Services.Beastmaster.CrucibleTracker.Current.OnBoard, inCombat);
        if (!inCombat)
        {
            _instinct.Reset();
            _capture.Reset();
            _pairing.Reset();
        }

        if (RotationServices.Condition?[Dalamud.Game.ClientState.Conditions.ConditionFlag.InCombat] == true)
        {
            _pullHoldSinceUtc = System.DateTime.MinValue;
            _pullHoldReleased = false;
            _prePullSettled = false;
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

        // On a board before the fight: Borrow a skin from a beast on the horns, then open on the first horn.
        if (TryPreBorrow(context, isMoving, inCombat))
            return;

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
    /// The pre-fight Borrow on a Crucible board (user, 2026-11-08; fuurie / BMRV D3 clears, TPLemont): between fights,
    /// call a Beastkin (else Vilekin) from the horns after the first, Borrow its skin, then call the first horn back so
    /// the fight opens as planned. The card waits on Beast Mode (its 90 s is halted while a familiar is out) and
    /// DamageModule keeps it until it is used. A horn swap out of combat costs no horn cooldown (fuurie still calls the
    /// pre-borrowed Behemoth second, ~10 s into Pas de Seul). Returns true when something was sent this frame.
    /// </summary>
    private bool TryPreBorrow(IArtemisContext context, bool isMoving, bool inCombat)
    {
        if (inCombat)
        {
            _preBorrowGaveUp = false;
            _preBorrowPressedUtc = System.DateTime.MinValue;
            _preBorrowWaitLogged = "";
            return false;
        }

        var player = context.Player;
        var now = System.DateTime.UtcNow;
        var cfg = Configuration.Beastmaster;
        var wanted = Daedalus.Services.Beastmaster.CrucibleTracker.Current.OnBoard
            && cfg.EnableBorrow && cfg.KeepFamiliarOut && _battlehorns.IsPopulatedFromGame
            && ActionAvailability.MeetsLevelAndLearned(player.Level, ActionService, BSTActions.Borrow);
        // The gap after a horn or a Borrow: nothing is pressed inside it, but what is already done is still read.
        var gapOver = (now - _lastHornTryUtc).TotalSeconds >= ArtemisFamiliarKeeper.PreBorrowGapSeconds;
        _prePullSettled = !wanted && context.HasFamiliar; // no Borrow to make: the first beast is all the pull waits for
        if (!wanted || (context.HasFamiliar && context.FamiliarBeast == null))
            return false; // a familiar not yet identified: its horn is unknown

        // A card beast's battle (the Banemite's Geshunpest): Borrow is saved for it — pressed here, its 30 s recast
        // would still be running when that beast comes out.
        if (Daedalus.Services.Beastmaster.CrucibleTracker.Current.Enemy is { } foe
            && CrucibleDegreeTeams.CardBeast(Daedalus.Services.Beastmaster.CrucibleHints.Degree, foe.Battle) is { } cardBeast)
        {
            _prePullSettled = context.HasFamiliar;
            if (context.HasFamiliar)
                LogPreBorrowWait($"no Borrow before this fight: it is kept for {BstFamiliars.ByBestiaryNo((uint)cardBeast)?.Name ?? "a beast"}'s card");
            return false;
        }

        System.Span<BeastClassification> kins = stackalloc BeastClassification[ArtemisBattlehornState.SlotCount];
        var outSlot = ArtemisFamiliarKeeper.None;
        for (var i = 0; i < kins.Length; i++)
        {
            var no = _battlehorns[i].PactNameId;
            kins[i] = _battlehorns[i].IsEmpty
                ? BeastClassification.Unknown
                : BstFamiliars.ByBestiaryNo(no)?.Classification ?? BeastClassification.Unknown;
            if (context.FamiliarBeast is { } beast && (uint)beast.BestiaryNo == no)
                outSlot = i;
        }

        // The plan's own pick for this battle (the video plan names one for every fight), else the kin rule.
        var planBeast = Daedalus.Services.Beastmaster.CrucibleTracker.Current.Enemy is { } planFoe
            ? CrucibleDegreeTeams.PreBorrowBeast(Daedalus.Services.Beastmaster.CrucibleHints.Degree, planFoe.Battle)
            : null;
        var planSlot = ArtemisFamiliarKeeper.None;
        for (var i = 0; planBeast != null && i < kins.Length; i++)
        {
            if (!_battlehorns[i].IsEmpty && _battlehorns[i].PactNameId == (uint)planBeast.Value)
                planSlot = i;
        }

        var preSlot = planSlot != ArtemisFamiliarKeeper.None ? planSlot : ArtemisFamiliarKeeper.PickPreBorrowSlot(kins);
        if (preSlot == ArtemisFamiliarKeeper.None)
        {
            _prePullSettled = context.HasFamiliar;
            if (context.HasFamiliar)
                LogPreBorrowWait($"nothing to borrow: no Beastkin or Vilekin on the second or third horn ({HornNames()})");
            return false;
        }

        var card = planSlot != ArtemisFamiliarKeeper.None
            ? ArtemisCooldownRules.KinshipCard(kins[preSlot])
            : ArtemisFamiliarKeeper.PreBorrowCard(kins[preSlot]);
        var modeId = ActionService.GetAdjustedActionId(BSTActions.BeastMode.ActionId);
        var cardHeld = ArtemisFamiliarKeeper.HoldsCardWorthKeeping(modeId);
        if (cardHeld && modeId == card)
            _damage.PreBorrowedCardId = card;

        // A Borrow that did not take within a retry gap is not pressed again before this fight.
        if (!cardHeld && _preBorrowPressedUtc != System.DateTime.MinValue
            && (now - _preBorrowPressedUtc).TotalSeconds >= ArtemisFamiliarKeeper.RetrySeconds)
        {
            _preBorrowGaveUp = true;
            _preBorrowPressedUtc = System.DateTime.MinValue;
            RotationServices.DebugLog?.Log(
                Daedalus.Services.Debug.DebugLogCategory.Action, Daedalus.Services.Debug.DebugLogSeverity.Warning,
                "[Crucible] pre-borrow: Borrow did not take; opening without it");
        }

        var mounted = RotationServices.Condition?[
            Dalamud.Game.ClientState.Conditions.ConditionFlag.Mounted] == true;
        var pressable = ArtemisFamiliarKeeper.CanPressHornNow(
            ActionService.AnimationLockRemaining, ActionService.SecondsSinceLastAction, ActionService.GcdRemaining);
        var canAct = !player.IsDead && player.CurrentHp > 0 && !isMoving && !mounted && !player.IsCasting
            && !HornGame.IsBestiaryOpen() && pressable;
        // With no beast out the game refuses Borrow whatever its recast: then only the recast says whether the Borrow
        // beast is worth calling — and it comes out first, with no detour through the first horn.
        var borrowReady = context.HasFamiliar
            ? ActionService.GetActionStatusCode(BSTActions.Borrow.ActionId, player.GameObjectId) == 0
            : ActionService.IsActionReady(BSTActions.Borrow.ActionId);
        var step = ArtemisFamiliarKeeper.NextPreBorrowStep(
            true, canAct, preSlot, outSlot, cardHeld || _preBorrowGaveUp, borrowReady);
        // The card is in hand (or the Borrow was given up) and the beast out is not the one it came from: ready.
        // So is a Borrow still on its recast with none pressed: there is nothing to wait for but a beast.
        var borrowOnRecast = _preBorrowPressedUtc == System.DateTime.MinValue
                             && !ActionService.IsActionReady(BSTActions.Borrow.ActionId);
        // Borrowed from the first horn itself, it is already the beast the fight opens on.
        _prePullSettled = step == ArtemisFamiliarKeeper.PreBorrowStep.None && (cardHeld || _preBorrowGaveUp || borrowOnRecast)
                          && context.HasFamiliar && (outSlot != preSlot || preSlot == 0);
        if (!gapOver)
            return false;
        if (step == ArtemisFamiliarKeeper.PreBorrowStep.None && !cardHeld && !_preBorrowGaveUp)
        {
            LogPreBorrowWait(ArtemisFamiliarKeeper.HornWait(
                    player.IsDead || player.CurrentHp == 0, isMoving, mounted, player.IsCasting, HornGame.IsBestiaryOpen(),
                    pressable, anyHornReady: true) is { } why
                ? $"waiting: {why}"
                : outSlot == preSlot ? "waiting: Borrow is not ready" : "waiting: the horn is not ready");
        }

        var name = BstFamiliars.ByBestiaryNo(_battlehorns[preSlot].PactNameId)?.Name ?? "the beast";
        var cardName = card == BSTActions.BeastskinId ? "Beastskin"
            : card == BSTActions.VileskinId ? "Vileskin"
            : card == BSTActions.ScaleskinId ? "Scaleskin"
            : card == BSTActions.SoulCrushId ? "Soul Crush"
            : card == BSTActions.QuellingWaveId ? "Quelling Wave"
            : "card";
        switch (step)
        {
            case ArtemisFamiliarKeeper.PreBorrowStep.SummonBeast:
                return BlowHornForPreBorrow(player, preSlot, $"[Crucible] pre-borrow: calling {name} for its {cardName}", now);
            case ArtemisFamiliarKeeper.PreBorrowStep.Borrow:
                // Pressed and waiting for the card to show: not again (it is given up after a retry gap, above). The
                // first horn follows as soon as the card is held, with no gap of its own.
                if (_preBorrowPressedUtc != System.DateTime.MinValue)
                    return false;
                if (!ActionService.ExecuteOgcd(BSTActions.Borrow, player.GameObjectId))
                    return false;
                _preBorrowPressedUtc = now;
                _damage.PreBorrowedCardId = card;
                _artemisDebugState.PlannedAction = $"Borrow ({name}, before the fight)";
                RotationServices.DebugLog?.Log(
                    Daedalus.Services.Debug.DebugLogCategory.Action, Daedalus.Services.Debug.DebugLogSeverity.Info,
                    $"[Crucible] pre-borrow: Borrow from {name} ({cardName})");
                return true;
            case ArtemisFamiliarKeeper.PreBorrowStep.SummonOpener:
                return BlowHornForPreBorrow(player, 0, "[Crucible] pre-borrow: card held, calling the first horn back", now);
            default:
                return false;
        }
    }

    /// <summary>Blow horn <paramref name="slot"/> for the pre-borrow when the game says it is ready.</summary>
    private bool BlowHornForPreBorrow(IPlayerCharacter player, int slot, string log, System.DateTime now)
    {
        var horn = BSTActions.Battlehorns[slot];
        if (!ActionAvailability.MeetsLevelAndLearned(player.Level, ActionService, horn)
            || ActionService.GetActionStatusCode(horn.ActionId, player.GameObjectId) != 0)
            return false;

        _lastHornTryUtc = now;
        if (!ActionService.ExecuteOgcd(horn, player.GameObjectId))
            return false;

        _artemisDebugState.PlannedAction = horn.Name;
        _artemisDebugState.DamageState = "Pre-fight Borrow";
        RotationServices.DebugLog?.Log(
            Daedalus.Services.Debug.DebugLogCategory.Action, Daedalus.Services.Debug.DebugLogSeverity.Info, log);
        return true;
    }

    /// <summary>
    /// Blow a Battlehorn when no familiar is out. Returns true when a horn was actually sent this frame.
    /// </summary>
    private bool TryKeepFamiliarOut(IArtemisContext context, bool isMoving)
    {
        var now = System.DateTime.UtcNow;
        var player = context.Player;

        if (context.HasFamiliar)
        {
            var nameNow = context.FamiliarBeast?.Name ?? "a familiar";
            if (_familiarGoneSinceUtc != System.DateTime.MinValue && context.InCombat)
                LogFamiliar($"{nameNow} is out after {(now - _familiarGoneSinceUtc).TotalSeconds:0.0}s with no familiar");
            _lastFamiliarName = nameNow;
            _hornRefusals = 0;
            _hornWaitLogged = "";
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

            return TrySwapOutFalling(context, isMoving, now) || TrySpareForBuster(context, isMoving, now);
        }

        if (_familiarGoneSinceUtc == System.DateTime.MinValue)
        {
            _familiarGoneSinceUtc = now;
            if (context.InCombat)
                LogFamiliar($"{(_lastFamiliarName.Length > 0 ? _lastFamiliarName : "the familiar")} is gone; horns: {HornNames()}");
        }

        // The bomb push: no familiar until the worm is caught (it would push the bomb off line). From the last frame.
        if (_damage.HoldsFamiliarForBombPush)
        {
            _artemisDebugState.DamageState = "Bomb push — no familiar until the worm is caught";
            return false;
        }

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
        var pressable = ArtemisFamiliarKeeper.CanPressHornNow(
            ActionService.AnimationLockRemaining, ActionService.SecondsSinceLastAction, ActionService.GcdRemaining);
        var canAct = !player.IsDead && player.CurrentHp > 0 && !isMoving && !mounted && !player.IsCasting
            && !HornGame.IsBestiaryOpen() && pressable;
        if (context.InCombat && Configuration.Beastmaster.KeepFamiliarOut
            && ArtemisFamiliarKeeper.HornWait(
                player.IsDead || player.CurrentHp == 0, isMoving, mounted, player.IsCasting, HornGame.IsBestiaryOpen(),
                pressable, AnyReady(usable)) is { } wait
            && wait != _hornWaitLogged)
        {
            _hornWaitLogged = wait;
            LogFamiliar($"no familiar for {(now - _familiarGoneSinceUtc).TotalSeconds:0.0}s, the horn waits: {wait}");
        }

        var slot = ArtemisFamiliarKeeper.PickHorn(
            Configuration.Beastmaster.KeepFamiliarOut, context.HasFamiliar, canAct,
            (now - _familiarGoneSinceUtc).TotalSeconds, (now - _lastHornTryUtc).TotalSeconds,
            // On a Crucible board before the fight, the first horn: its order is set per battle (Legendary: poison first
            // or Mantis first), and the beast last out belonged to the previous battle.
            Daedalus.Services.Beastmaster.CrucibleTracker.Current.OnBoard && !context.InCombat
                ? ArtemisFamiliarKeeper.None
                : _lastOutHorn,
            usable);
        if (slot == ArtemisFamiliarKeeper.None)
            return false;

        _lastHornTryUtc = now;
        var chosen = BSTActions.Battlehorns[slot];
        var gone = (now - _familiarGoneSinceUtc).TotalSeconds;
        if (!ActionService.ExecuteOgcd(chosen, player.GameObjectId))
        {
            // The game said ready and then refused the press. Once is most likely a press that met another on its way
            // out, so the usual gap; twice in a row is something the rule cannot see, and each refusal is an error
            // toast — so back right off.
            _hornRefusals++;
            var backoff = ArtemisFamiliarKeeper.RefusedBackoffSeconds(_hornRefusals);
            _lastHornTryUtc = now.AddSeconds(backoff - ArtemisFamiliarKeeper.RetrySeconds);
            if (context.InCombat)
                LogFamiliar($"{chosen.Name} ({HornName(slot)}) refused by the game, {gone:0.0}s with no familiar "
                            + $"(lock {ActionService.AnimationLockRemaining:0.00}, GCD {ActionService.GcdRemaining:0.00}): "
                            + $"next try in {backoff:0}s", warning: true);
            return false;
        }

        _artemisDebugState.PlannedAction = chosen.Name;
        _artemisDebugState.DamageState = "No familiar — calling one back";
        if (context.InCombat)
            LogFamiliar($"{chosen.Name} ({HornName(slot)}) pressed, {gone:0.0}s after the last one left");
        return true;
    }

    /// <summary>
    /// A falling familiar whose Parting Blow is on cooldown (DamageModule.SwapOutWanted, from the last frame): blow the
    /// next ready horn, which switches it out before it is knocked out. Returns true when a horn was sent.
    /// </summary>
    private bool TrySwapOutFalling(IArtemisContext context, bool isMoving, System.DateTime now)
    {
        if (!_damage.SwapOutWanted || !_battlehorns.IsPopulatedFromGame)
            return false;

        var player = context.Player;
        System.Span<bool> usable = stackalloc bool[ArtemisBattlehornState.SlotCount];
        for (var i = 0; i < usable.Length; i++)
        {
            var horn = BSTActions.Battlehorns[i];
            usable[i] = !_battlehorns[i].IsEmpty
                && ActionAvailability.MeetsLevelAndLearned(player.Level, ActionService, horn)
                && ActionService.IsActionReady(horn.ActionId);
        }

        var pressable = ArtemisFamiliarKeeper.CanPressHornNow(
            ActionService.AnimationLockRemaining, ActionService.SecondsSinceLastAction, ActionService.GcdRemaining);
        var canAct = !player.IsDead && player.CurrentHp > 0 && !isMoving && !player.IsCasting && pressable;
        var slot = ArtemisFamiliarKeeper.PickSwapHorn(
            true, canAct, (now - _lastHornTryUtc).TotalSeconds, _lastOutHorn, usable);
        if (slot == ArtemisFamiliarKeeper.None)
            return false;

        _lastHornTryUtc = now;
        var chosen = BSTActions.Battlehorns[slot];
        var outName = context.FamiliarBeast?.Name ?? "the familiar";
        if (!ActionService.ExecuteOgcd(chosen, player.GameObjectId))
        {
            LogFamiliar($"{outName} falling: {chosen.Name} ({HornName(slot)}) refused by the game "
                        + $"(lock {ActionService.AnimationLockRemaining:0.00}, GCD {ActionService.GcdRemaining:0.00})", warning: true);
            return false;
        }

        _artemisDebugState.PlannedAction = $"{chosen.Name} (swap {outName} out — falling, Parting Blow on cooldown)";
        RotationServices.DebugLog?.Log(
            Daedalus.Services.Debug.DebugLogCategory.Action, Daedalus.Services.Debug.DebugLogSeverity.Info,
            $"[Crucible] {outName} falling and Parting Blow on cooldown: {chosen.Name} ({HornName(slot)}) swaps it out");
        return true;
    }

    private static bool AnyReady(System.ReadOnlySpan<bool> horns)
    {
        foreach (var ready in horns)
        {
            if (ready)
                return true;
        }

        return false;
    }

    /// <summary>
    /// A tankbuster is coming while the beast out is kept for a later battle and the beastmaster is hurt
    /// (DamageModule.SpareForBusterWanted, from the last frame): blow the horn of a beast no later battle needs. It
    /// swaps in, tanks as a beast that can be spent does, and is Snarled into the hit. Returns true when a horn was sent.
    /// </summary>
    private bool TrySpareForBuster(IArtemisContext context, bool isMoving, System.DateTime now)
    {
        if (!_damage.SpareForBusterWanted || !_battlehorns.IsPopulatedFromGame
            || !Daedalus.Services.Beastmaster.CrucibleHints.BeastsNeededLaterKnown)
            return false;

        var player = context.Player;
        var neededLater = Daedalus.Services.Beastmaster.CrucibleHints.BeastsNeededLater;
        System.Span<bool> usable = stackalloc bool[ArtemisBattlehornState.SlotCount];
        System.Span<bool> spare = stackalloc bool[ArtemisBattlehornState.SlotCount];
        for (var i = 0; i < usable.Length; i++)
        {
            var horn = BSTActions.Battlehorns[i];
            usable[i] = !_battlehorns[i].IsEmpty
                && ActionAvailability.MeetsLevelAndLearned(player.Level, ActionService, horn)
                && ActionService.IsActionReady(horn.ActionId);
            spare[i] = !_battlehorns[i].IsEmpty
                && !System.Linq.Enumerable.Contains(neededLater, (int)_battlehorns[i].PactNameId);
        }

        var pressable = ArtemisFamiliarKeeper.CanPressHornNow(
            ActionService.AnimationLockRemaining, ActionService.SecondsSinceLastAction, ActionService.GcdRemaining);
        var canAct = !player.IsDead && player.CurrentHp > 0 && !isMoving && !player.IsCasting && pressable;
        var slot = ArtemisFamiliarKeeper.PickSpareHorn(
            true, canAct, (now - _lastHornTryUtc).TotalSeconds, _lastOutHorn, usable, spare);
        if (slot == ArtemisFamiliarKeeper.None)
            return false;

        _lastHornTryUtc = now;
        var chosen = BSTActions.Battlehorns[slot];
        var outName = context.FamiliarBeast?.Name ?? "the familiar";
        var hp = player.MaxHp > 0 ? (float)player.CurrentHp / player.MaxHp : 0f;
        if (!ActionService.ExecuteOgcd(chosen, player.GameObjectId))
        {
            LogFamiliar($"a tankbuster is coming: {chosen.Name} ({HornName(slot)}) refused by the game "
                        + $"(lock {ActionService.AnimationLockRemaining:0.00}, GCD {ActionService.GcdRemaining:0.00})", warning: true);
            return false;
        }

        _artemisDebugState.PlannedAction = $"{chosen.Name} ({HornName(slot)} takes the tankbuster — {outName} is kept)";
        RotationServices.DebugLog?.Log(
            Daedalus.Services.Debug.DebugLogCategory.Action, Daedalus.Services.Debug.DebugLogSeverity.Info,
            $"[Crucible] a tankbuster is coming and you are at {hp:P0}: {chosen.Name} ({HornName(slot)}) comes out to take it, "
            + $"{outName} is kept for a later battle");
        return true;
    }

    /// <summary>The beast on a horn, by name.</summary>
    private string HornName(int slot)
        => _battlehorns[slot].IsEmpty ? "empty"
            : BstFamiliars.ByBestiaryNo(_battlehorns[slot].PactNameId)?.Name ?? $"#{_battlehorns[slot].PactNameId}";

    /// <summary>The three horns in order, for a log line.</summary>
    private string HornNames()
        => string.Join(" > ", System.Linq.Enumerable.Select(System.Linq.Enumerable.Range(0, ArtemisBattlehornState.SlotCount), HornName));

    private static void LogFamiliar(string message, bool warning = false)
        => RotationServices.DebugLog?.Log(
            Daedalus.Services.Debug.DebugLogCategory.Action,
            warning ? Daedalus.Services.Debug.DebugLogSeverity.Warning : Daedalus.Services.Debug.DebugLogSeverity.Info,
            "[Familiar] " + message);

    /// <summary>Why the pre-fight Borrow is not happening, once per reason between fights.</summary>
    private void LogPreBorrowWait(string why)
    {
        if (why == _preBorrowWaitLogged)
            return;
        _preBorrowWaitLogged = why;
        RotationServices.DebugLog?.Log(
            Daedalus.Services.Debug.DebugLogCategory.Action, Daedalus.Services.Debug.DebugLogSeverity.Info,
            "[Crucible] pre-borrow: " + why);
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
