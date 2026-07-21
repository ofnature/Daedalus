using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.ClientState.Party;
using Dalamud.Plugin.Services;
using Daedalus.Data;
using Daedalus.Rotation.Common;
using Daedalus.Services;
using Daedalus.Services.Action;
using Daedalus.Services.Debuff;
using Daedalus.Services.Positional;
using Daedalus.Services.Positional.Navigation;
using Daedalus.Services.Prediction;
using Daedalus.Services.Stats;
using Daedalus.Services.Targeting;

namespace Daedalus.Rotation.Base;

/// <summary>
/// Base class for melee DPS rotation implementations.
/// Provides IMeleeDpsRotationContext interface implementation and melee-specific services.
/// </summary>
/// <typeparam name="TContext">The melee DPS job-specific context type.</typeparam>
/// <typeparam name="TModule">The melee DPS job-specific module interface type.</typeparam>
public abstract class BaseMeleeDpsRotation<TContext, TModule> : BaseRotation<TContext, TModule>, IHasPositionals
    where TContext : IMeleeDpsRotationContext
    where TModule : IRotationModule<TContext>
{
    #region Melee DPS-Specific Services

    protected readonly IPositionalService PositionalService;

    /// <summary>
    /// Optional service for computing optimal directional AoE facing.
    /// </summary>
    protected readonly ISmartAoEService? SmartAoEService;

    /// <summary>
    /// Optional service for coordinating anticipatory vNav positional movement.
    /// </summary>
    protected readonly IPositionalMovementService? PositionalMovementService;

    #endregion

    #region Combo Tracking

    /// <summary>
    /// Current combo step (1-3, or 0 for no combo).
    /// </summary>
    protected int ComboStep { get; set; }

    /// <summary>
    /// Last combo action ID.
    /// </summary>
    protected uint LastComboAction { get; set; }

    /// <summary>
    /// Time remaining on current combo chain.
    /// </summary>
    protected float ComboTimeRemaining { get; set; }

    #endregion

    #region Positional State

    /// <summary>
    /// Whether the player is at the target's rear.
    /// </summary>
    protected bool IsAtRear { get; set; }

    /// <summary>
    /// Whether the player is at the target's flank.
    /// </summary>
    protected bool IsAtFlank { get; set; }

    /// <summary>
    /// Whether the target has positional immunity.
    /// </summary>
    protected bool TargetHasPositionalImmunity { get; set; }

    /// <summary>
    /// Current positional snapshot — updated every frame by UpdatePositionalState.
    /// Exposed publicly via IHasPositionals for MainWindow display.
    /// </summary>
    public PositionalSnapshot Positionals { get; private set; }

    /// <summary>
    /// Current enemy target used for positional queries and movement.
    /// </summary>
    protected IBattleChara? PositionalTarget { get; private set; }

    #endregion

    #region Debug State

    // Melee DPS rotations typically have both job-specific and common debug states
    protected readonly DebugState CommonDebugState = new();

    // Cached list to avoid per-frame heap allocation when passing player ID to DamageTrendService
    private readonly System.Collections.Generic.List<uint> _damageTrendIds = new(1);

    #endregion

    #region Constructor

    protected BaseMeleeDpsRotation(
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
        IBurstWindowService? burstWindowService = null,
        IErrorMetricsService? errorMetrics = null,
        ISmartAoEService? smartAoEService = null,
        IPositionalMovementService? positionalMovementService = null,
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
            errorMetrics,
            burstWindowService: burstWindowService,
            tinctureDispatcher: tinctureDispatcher,
            pullIntentService: pullIntentService)
    {
        PositionalService = positionalService;
        SmartAoEService = smartAoEService;
        PositionalMovementService = positionalMovementService;
    }

    #endregion

    #region Abstract Methods

    /// <summary>
    /// Returns the action ID used for positional range checking via game API.
    /// Override in derived classes to use the job's own basic melee GCD.
    /// Defaults to Heavy Swing (WAR, ID 31) — valid for any 3y melee action.
    /// </summary>
    protected virtual uint GetMeleeRangeActionId() => WARActions.HeavySwing.ActionId;

    /// <summary>
    /// Reads the job-specific gauge value(s).
    /// Must be implemented by each melee DPS job.
    /// </summary>
    protected abstract void ReadGaugeValues();

    /// <summary>
    /// Determines the current combo step based on the last combo action and timer.
    /// Must be implemented by each melee DPS job since combo chains vary.
    /// </summary>
    protected abstract int DetermineComboStep(uint comboAction, float comboTimer);

    /// <summary>
    /// Syncs melee DPS-specific debug state to common debug state for UI compatibility.
    /// Override in derived classes to map job-specific fields.
    /// </summary>
    protected abstract void SyncDebugState(TContext context);

    #endregion

    #region Combo State Management

    /// <summary>
    /// Updates combo state from game memory.
    /// </summary>
    protected virtual void UpdateComboState()
    {
        LastComboAction = SafeGameAccess.GetComboAction(ErrorMetrics);
        ComboTimeRemaining = SafeGameAccess.GetComboTimer(ErrorMetrics);
        ComboStep = DetermineComboStep(LastComboAction, ComboTimeRemaining);
    }

    #endregion

    #region Positional State Management

    /// <summary>
    /// Updates positional state based on current target.
    /// </summary>
    protected virtual void UpdatePositionalState(IPlayerCharacter player)
    {
        // Find current target using game API range check for maximum accuracy
        var target = TargetingService.FindEnemyForAction(
            Configuration.Targeting.EnemyStrategy,
            GetMeleeRangeActionId(),
            player);

        if (target == null)
        {
            PositionalTarget = null;
            IsAtRear = false;
            IsAtFlank = false;
            TargetHasPositionalImmunity = false;
            Positionals = new PositionalSnapshot { HasTarget = false };
            return;
        }

        PositionalTarget = target;

        // Check positional using the service
        var positional = PositionalService.GetPositional(player, target);
        IsAtRear = positional == PositionalType.Rear;
        IsAtFlank = positional == PositionalType.Flank;
        TargetHasPositionalImmunity = PositionalService.HasPositionalImmunity(target);
        // True North (status 1250) suppresses positional requirements
        var hasTrueNorth = HasStatusEffect(player, 1250);

        Positionals = new PositionalSnapshot
        {
            IsAtRear = IsAtRear,
            IsAtFlank = IsAtFlank,
            TargetHasImmunity = TargetHasPositionalImmunity,
            HasTarget = true,
            RequiredPositional = (TargetHasPositionalImmunity || hasTrueNorth) ? null : ResolveNextRequiredPositional(player),
            // Boundary camping live → BMR gets DesiredPositional=Any (it keeps range/dodges, Daedalus
            // owns the angle). Deliberately WITHOUT the single-enemy check so BMR ownership doesn't
            // flap per pack.
            BoundaryCampingActive = IsBoundaryCampingEnabled
                && IsAutoMovementAllowed() && PositionalBoundaryBiasRadians > 0f,
        };
    }

    /// <summary>
    /// Positional enforcement, overlay, and vNav reposition only apply with one enemy in melee.
    /// </summary>
    protected bool ShouldApplyPositionalRequirements(IPlayerCharacter player)
        => PositionalRequirementHelper.ShouldApply(
            TargetingService.CountEngagedEnemies(PositionalRequirementHelper.EngagedScanYalms, player));

    /// <summary>
    /// Returns the upcoming positional when single-target; null on multi-enemy packs.
    /// </summary>
    protected PositionalType? ResolveNextRequiredPositional(IPlayerCharacter player)
    {
        if (!ShouldApplyPositionalRequirements(player))
            return null;

        return GetNextRequiredPositional();
    }

    /// <summary>
    /// Override in job rotations to report what positional the next GCD requires.
    /// Used by DrawCanvas to highlight where the player should stand.
    /// </summary>
    protected virtual PositionalType? GetNextRequiredPositional() => null;

    /// <summary>
    /// Job-specific anticipation provider for vNav positional movement. Default null (disabled).
    /// </summary>
    protected virtual IPositionalAnticipationProvider? GetPositionalAnticipationProvider() => null;

    /// <summary>
    /// Whether anticipatory vNav positional movement is enabled for this job.
    /// </summary>
    protected virtual bool IsPositionalMovementEnabled() => false;

    /// <summary>
    /// When > 0, stand points target the flank/rear boundary ± this margin instead of the arc center
    /// ("boundary camping" — a flank↔rear swap becomes a short chord hop). Global user tuning via the
    /// Nav Control slider; 0 = center of arc (pre-camping behavior).
    /// </summary>
    protected virtual float PositionalBoundaryBiasRadians
        => Configuration.Nav.PositionalBoundaryBiasDegrees * System.MathF.PI / 180f;

    /// <summary>
    /// Per-job validation gate for the restored positional arc queue. The user-facing per-job
    /// EnablePositionalMovement toggles are persisted true everywhere, so re-enabling the arc block
    /// would otherwise flip arcs on for every melee at once — flip this per job after its Trust
    /// validation pass. NIN first (back-to-back finisher swaps, no filler GCD to travel in).
    /// </summary>
    protected virtual bool IsPositionalArcRolloutEnabled => false;

    /// <summary>
    /// Boundary camping is live for this job: global experimental switch (default OFF) AND the per-job
    /// rollout gate AND the per-job positional-movement toggle. Gates both the vNav hop arcs and the
    /// BMR DesiredPositional=Any handoff.
    /// </summary>
    protected bool IsBoundaryCampingEnabled
        => Configuration.Nav.EnableBoundaryCamping
            && IsPositionalArcRolloutEnabled
            && IsPositionalMovementEnabled();

    /// <summary>
    /// Whether vNav-driven auto movement (positional reposition, burst approach) is permitted right
    /// now: requires the global master toggle AND a real party. Solo play never auto-moves —
    /// positional uptime barely matters against overworld / solo-duty trash and pathing around every
    /// target looks botty. PartyList.Length &gt; 0 includes trust/duty NPCs, so dungeons still move.
    /// </summary>
    protected bool IsAutoMovementAllowed() => Configuration.EnableAutoMovement && PartyList.Length > 0;

    /// <summary>
    /// Builds anticipation inputs shared by all melee jobs. Override to add job-specific fields.
    /// </summary>
    protected virtual PositionalAnticipationContext CreatePositionalAnticipationContext(IPlayerCharacter player)
    {
        return new PositionalAnticipationContext(
            LastComboAction,
            player.Level,
            HasStatusEffect(player, 1250),
            TargetHasPositionalImmunity,
            IsAtRear,
            IsAtFlank);
    }

    private static bool HasStatusEffect(Dalamud.Game.ClientState.Objects.Types.IBattleChara player, uint statusId)
    {
        foreach (var s in player.StatusList)
            if (s.StatusId == statusId) return true;
        return false;
    }

    /// <summary>
    /// Queues or skips anticipatory vNav reposition when a job provider predicts a finisher.
    /// </summary>
    protected virtual void UpdatePositionalMovement(IPlayerCharacter player, bool inCombat)
    {
        if (PositionalMovementService == null)
            return;

        // Use the melee-range positional target when available; then the player's RAW hard
        // target (probe-free — the attackability probe false-negatives at range and parked
        // melee outside walk-in distance); finally a wider strategy search so vNav can path
        // toward a target that's currently out of melee range.
        var resolvedTarget = PositionalTarget
            ?? TargetingService.GetRawEnemyHardTarget() as IBattleChara
            ?? TargetingService.FindEnemy(
                Configuration.Targeting.EnemyStrategy, 25f, player) as IBattleChara;

        PositionalMovementTarget? movementTarget = resolvedTarget is { } t
            ? new PositionalMovementTarget(
                t.Position,
                t.HitboxRadius,
                t.Rotation,
                PositionalService.HasPositionalImmunity(t))
            : null;

        var provider = GetPositionalAnticipationProvider();
        var anticipationContext = CreatePositionalAnticipationContext(player);
        var engagedEnemies = TargetingService.CountEngagedEnemies(
            PositionalRequirementHelper.EngagedScanYalms, player);
        var singleTargetOk = PositionalRequirementHelper.ShouldApply(engagedEnemies);

        var request = new PositionalMovementUpdateRequest(
            AnticipationProvider: provider,
            AnticipationContext: anticipationContext,
            PlayerPosition: player.Position,
            PlayerHitboxRadius: player.HitboxRadius,
            Target: movementTarget,
            ActionService: ActionService,
            InCombat: inCombat,
            EnableMovement: IsBoundaryCampingEnabled
                && IsAutoMovementAllowed() && singleTargetOk,
            AllowMovementDuringActionLock: true,
            MaintainMaxMelee: IsMaxMeleeMaintenanceAllowed(),
            MaxMeleeTarget: ResolveMaxMeleeTarget(player, out var maxMeleeFollowsPlayer),
            MaxMeleeTargetFollowsPlayer: maxMeleeFollowsPlayer,
            VNavFlex: Configuration.Nav.VNavFlex,
            PositionalBoundaryBiasRadians: PositionalBoundaryBiasRadians);

        PositionalMovementService.Update(in request);

        // Anchor gate-chain diagnostics for the Nav Control window (static-backed — see class doc).
        PositionalAnchorDiagnostics.JobName = Name;
        PositionalAnchorDiagnostics.UpdatedUtc = System.DateTime.UtcNow;
        PositionalAnchorDiagnostics.CampingSwitchOn = Configuration.Nav.EnableBoundaryCamping;
        PositionalAnchorDiagnostics.RolloutEnabled = IsPositionalArcRolloutEnabled;
        PositionalAnchorDiagnostics.JobToggleOn = IsPositionalMovementEnabled();
        PositionalAnchorDiagnostics.AutoMovementOn = Configuration.EnableAutoMovement;
        PositionalAnchorDiagnostics.HasParty = PartyList.Length > 0;
        PositionalAnchorDiagnostics.SingleTargetOk = singleTargetOk;
        PositionalAnchorDiagnostics.EngagedEnemies = engagedEnemies;
        PositionalAnchorDiagnostics.HasTarget = movementTarget.HasValue;
        PositionalAnchorDiagnostics.BiasDegrees = Configuration.Nav.PositionalBoundaryBiasDegrees;
        PositionalAnchorDiagnostics.Anticipation =
            provider?.GetAnticipatedPositional(anticipationContext) is { } a
                ? $"{a.Required} (next GCD)"
                : "none";
        var state = PositionalMovementService.State;
        PositionalAnchorDiagnostics.ServiceState = state.SkipReason is { Length: > 0 } reason
            ? $"{state.Phase} — {reason}"
            : state.Phase.ToString();
    }

    /// <summary>
    /// Whether to keep the character at the outer melee edge (back off when hugging the target). Unlike
    /// positional/burst movement this is pure range-keeping, so it runs solo too — gated only by the global
    /// master toggle and the dedicated <see cref="Configuration.MaintainMaxMelee"/> switch.
    /// </summary>
    protected bool IsMaxMeleeMaintenanceAllowed()
    {
        if (!Configuration.EnableAutoMovement || !Configuration.MaintainMaxMelee)
            return false;

        // Solo Position Lock disables max-melee positioning when solo (no party members).
        if (Configuration.Nav.SoloPositionLock && PartyList.Length == 0)
            return false;

        return true;
    }

    /// <summary>
    /// Snapshot of the player's <em>current</em> (hard) target for max-melee range-keeping, so maintenance
    /// only follows the mob we're actually attacking — never a strategy-selected or merely-aggroed enemy.
    /// Returns null when maintenance is disabled or there is no current enemy target. <paramref name="followsPlayer"/>
    /// is set when that mob is targeting the player (solo / self-tanked), which suppresses the back-off.
    /// </summary>
    protected PositionalMovementTarget? ResolveMaxMeleeTarget(IPlayerCharacter player, out bool followsPlayer)
    {
        followsPlayer = false;

        if (!IsMaxMeleeMaintenanceAllowed())
            return null;

        // Probe-passing target preferred (includes the sticky-target continuity), but fall back
        // to the raw hard target: the attackability probe false-negatives while the mob is out
        // of range — exactly when max-melee maintenance needs to walk IN.
        if ((TargetingService.GetUserEnemyTarget() ?? TargetingService.GetRawEnemyHardTarget())
            is not IBattleChara current)
            return null;

        followsPlayer = current.TargetObjectId == player.GameObjectId;

        return new PositionalMovementTarget(
            current.Position,
            current.HitboxRadius,
            current.Rotation,
            PositionalService.HasPositionalImmunity(current));
    }

    #endregion

    #region Override Base Methods

    /// <summary>
    /// Updates melee DPS-specific state (gauge, combo, positionals).
    /// </summary>
    protected override void UpdateJobSpecificServices(IPlayerCharacter player, bool inCombat)
    {
        // Read job gauge
        ReadGaugeValues();

        // Read combo state
        UpdateComboState();

        // Update positional state
        UpdatePositionalState(player);

        // Anticipatory vNav reposition (job-specific provider)
        UpdatePositionalMovement(player, inCombat);

        // Update burst window tracking (pass current target for raid debuff detection)
        BurstWindowService?.Update(player, TargetingService.GetUserEnemyTarget(), inCombat);

        // Update damage trend service with player entity ID (melee track their own damage for self-healing considerations)
        if (inCombat)
        {
            _damageTrendIds.Clear();
            _damageTrendIds.Add(player.EntityId);
            DamageTrendService.Update(1f / 60f, _damageTrendIds);
        }
    }

    /// <summary>
    /// Override to sync debug state after module updates.
    /// </summary>
    protected override void UpdateModuleDebugStates(TContext context)
    {
        base.UpdateModuleDebugStates(context);

        // Sync melee DPS debug state to common state for UI
        if (Configuration.IsDebugWindowOpen)
        {
            SyncDebugState(context);
        }
    }

    #endregion
}
