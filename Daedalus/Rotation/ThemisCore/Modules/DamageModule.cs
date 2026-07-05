using Dalamud.Game.ClientState.Objects.SubKinds;
using Daedalus.Data;
using Daedalus.Models.Action;
using Daedalus.Rotation.ApolloCore.Helpers;
using Daedalus.Rotation.Common;
using Daedalus.Rotation.Common.Helpers;
using Daedalus.Rotation.Common.Scheduling;
using Daedalus.Rotation.ThemisCore.Abilities;
using Daedalus.Rotation.ThemisCore.Context;
using Daedalus.Services.Training;
using ThemisRotation = Daedalus.Rotation.Themis;

namespace Daedalus.Rotation.ThemisCore.Modules;

/// <summary>
/// Handles the Paladin DPS rotation.
/// Scheduler-driven: pushes candidates with priorities; scheduler evaluates gates and dispatches.
/// </summary>
public sealed class DamageModule : IThemisModule
{
    public int Priority => 30;
    public string Name => "Damage";

    public bool TryExecute(IThemisContext context, bool isMoving) => false;

    public void UpdateDebugState(IThemisContext context)
    {
        // Debug state updated during CollectCandidates
    }

    #region CollectCandidates (scheduler path)

    public void CollectCandidates(IThemisContext context, RotationScheduler scheduler, bool isMoving)
    {
        if (!context.Configuration.Tank.EnableDamage)
        {
            context.Debug.DamageState = "Disabled";
            return;
        }

        if (!context.InCombat)
        {
            context.Debug.DamageState = "Not in combat";
            return;
        }

        if (context.TargetingService.IsDamageTargetingPaused())
        {
            // W2W transit: mobs still chasing us get the ranged filler (see TryPushTransitRangedFiller).
            TryPushTransitRangedFiller(context, scheduler);
            context.Debug.DamageState = "Paused (no target)";
            return;
        }

        var player = context.Player;

        var pack = EnemyPackDebugHelper.Count(context.TargetingService, JobAoERadiusYalms.Tank, player);
        EnemyPackDebugHelper.Apply(context.Debug, pack);

        var enemyStrategy = TankTargetingHelper.ResolveEnemyStrategy(
            context.Configuration.Tank,
            context.Configuration.Targeting.EnemyStrategy,
            context.PartyHelper?.FindCoTank(player) != null);

        var target = context.TargetingService.FindEnemyForAction(
            enemyStrategy,
            PLDActions.FastBlade.ActionId,
            player);

        // FindEnemyForAction can return a sticky/hard target far outside melee (W2W transit stuck
        // state — the module then pushes combo GCDs that fail range at dispatch forever). Demote
        // beyond-melee targets to the out-of-melee branch so gap-close + Shield Lob run instead.
        if (target != null && !TankTargetingHelper.IsWithinMeleeReach(player, target))
            target = null;

        var engageTarget = target ?? context.TargetingService.FindEnemy(
            enemyStrategy,
            20f,
            player);

        if (engageTarget == null)
        {
            TryPushTransitRangedFiller(context, scheduler);
            context.Debug.DamageState = "No target";
            return;
        }

        // Out-of-melee branch: push Intervene (oGCD gap close) + Shield Lob (ranged filler)
        if (target == null)
        {
            // Gap closer ONLY to snap aggro back (mob peeled to someone else) — never as a travel
            // tool: dashing at far targets mid-W2W fights AutoDuty's pathing (user rule; DRK run
            // showed Shadowstride x3 during the gather). Shield Lob covers the damage at range.
            var lostToOther = context.EnmityService?.HasLostAggroToOther(engageTarget, player.EntityId) == true;
            if (lostToOther && !context.Configuration.Tank.SuppressGapCloserOnLostMob)
            {
                var gapCloseBlocked = context.TargetingService.GapCloserSafety.ShouldBlockGapCloser(engageTarget, player);
                if (gapCloseBlocked)
                    context.Debug.DamageState = $"Intervene blocked: {context.TargetingService.GapCloserSafety.LastBlockReason}";
                else
                    TryPushIntervene(context, scheduler, engageTarget.GameObjectId);
            }
            TryPushShieldLob(context, scheduler, engageTarget.GameObjectId);
            // Below Shield Lob (Lv15, pre-PLD Gladiator) there is nothing to push here — the base
            // rotation's pre-ranged walk-in closes the distance instead. Name the state so the
            // empty GCD doesn't read as a stall in Why Stuck.
            if (player.Level < PLDActions.ShieldLob.MinLevel)
                context.Debug.DamageState = "Out of melee — walking in (no ranged GCD until Lv15)";
            return;
        }

        // PLD's AoE (Total Eclipse / Prominence / Holy Circle) is a player-centered PBAoE, so count
        // enemies around the PLAYER, not the hard target. Counting around the target undercounts when
        // the pack is spread around the player (e.g. a third mob behind you), which made the rotation
        // stay single-target with adds clearly in PBAoE range.
        var enemyCount = context.TargetingService.CountEnemiesInRange(5f, player);

        // oGCD pushes
        TryPushCircleOfScorn(context, scheduler, player.GameObjectId, target.GameObjectId);
        TryPushExpiacion(context, scheduler, target.GameObjectId);
        TryPushBladeOfHonor(context, scheduler, target.GameObjectId);
        if (!context.TargetingService.GapCloserSafety.ShouldBlockGapCloser(target, player))
            TryPushIntervene(context, scheduler, target.GameObjectId);

        // GCD pushes — magic phase and burst spenders fire over basic combo
        TryPushConfiteorChain(context, scheduler, target.GameObjectId);
        TryPushHolyPhaseSpenders(context, scheduler, target.GameObjectId, player.GameObjectId, enemyCount);
        TryPushDivineMightSpenders(context, scheduler, target.GameObjectId, player.GameObjectId, enemyCount);
        TryPushAtonementChain(context, scheduler, target.GameObjectId);
        TryPushGoringBlade(context, scheduler, target.GameObjectId);
        TryPushMovingAddTag(context, scheduler, isMoving, player);
        TryPushBasicCombo(context, scheduler, target.GameObjectId, player.GameObjectId, enemyCount);
    }

    #endregion

    #region Out-of-melee

    private void TryPushIntervene(IThemisContext context, RotationScheduler scheduler, ulong targetId)
    {
        if (!context.Configuration.Tank.EnableIntervene) return;
        if (context.Player.Level < PLDActions.Intervene.MinLevel) return;
        if (!context.ActionService.IsActionReady(PLDActions.Intervene.ActionId)) return;

        // RSR parity (PLD_Reborn.AttackAbility): Intervene is a forward dash — don't fire it while
        // moving, since the dash can throw you out of position (RSR gate: !IsMoving).
        if (context.IsMoving) return;

        scheduler.PushOgcd(ThemisAbilities.Intervene, targetId, priority: 4,
            onDispatched: _ =>
            {
                context.Debug.PlannedAction = PLDActions.Intervene.Name;
                context.Debug.DamageState = "Intervene (gap close)";
                TrainingHelper.Decision(context.TrainingService)
                    .Action(PLDActions.Intervene.ActionId, PLDActions.Intervene.Name)
                    .AsTankDamage()
                    .Reason(
                        "Intervene used as gap closer.",
                        "Intervene is a gap closer that also deals damage.")
                    .Factors("Target out of melee range", "Intervene charge available")
                    .Alternatives("Shield Lob (ranged GCD, slower)")
                    .Tip("Intervene has 2 charges. Don't hold both charges.")
                    .Concept(PldConcepts.Intervene)
                    .Record();
                context.TrainingService?.RecordConceptApplication(PldConcepts.Intervene, wasSuccessful: true);
            });
    }

    /// <summary>
    /// Wall-to-wall add tag: while moving in a duty, ranged-pull the nearest mob within 25y that isn't on
    /// us yet (including not-yet-aggroed packs we're walking toward) so the pull gathers and nothing is
    /// left behind. Only fires while moving, in an instanced duty, and between combos (never breaks one).
    /// </summary>
    private void TryPushMovingAddTag(IThemisContext context, RotationScheduler scheduler, bool isMoving, IPlayerCharacter player)
    {
        if (!isMoving) return;
        if (!context.Configuration.Tank.TagAddsWhileMovingWithRangedAttack) return;
        if (!PlayerSafetyHelper.IsInInstancedDuty()) return;
        if (player.Level < PLDActions.ShieldLob.MinLevel) return;
        if (!context.ActionService.IsActionReady(PLDActions.ShieldLob.ActionId)) return;

        // Never interrupt a combo in progress — only tag between combos (no active combo timer).
        if (context.ComboTimeRemaining > 0f)
            return;

        var stray = context.TargetingService.FindNearestTaggableEnemy(25f, player);
        if (stray == null) return;

        scheduler.PushGcd(ThemisAbilities.ShieldLob, stray.GameObjectId, priority: 6,
            onDispatched: _ =>
            {
                context.Debug.PlannedAction = PLDActions.ShieldLob.Name;
                context.Debug.DamageState = "Shield Lob (tag add — moving)";
                TrainingHelper.Decision(context.TrainingService)
                    .Action(PLDActions.ShieldLob.ActionId, PLDActions.ShieldLob.Name)
                    .AsTankDamage()
                    .Reason(
                        "Tagging a stray add with Shield Lob while moving.",
                        "During a wall-to-wall pull, ranged-tag adds that aren't on you yet so none are left behind.")
                    .Factors("Moving", "Add not aggroed to us", "Not mid-combo")
                    .Alternatives("Ignore the add (it gets left behind)")
                    .Tip("Shield Lob stray adds while running between packs.")
                    .Concept(PldConcepts.BurstWindow)
                    .Record();
                context.TrainingService?.RecordConceptApplication(PldConcepts.BurstWindow, wasSuccessful: true);
            });
    }

    /// <summary>
    /// W2W transit filler: ranged GCD at the nearest in-combat enemy (chasing pack) while we have no
    /// hard target / nothing in engage range. FindNearbyEnemy bypasses the no-target damage pause by
    /// design and only returns enemies already fighting us.
    /// </summary>
    private void TryPushTransitRangedFiller(IThemisContext context, RotationScheduler scheduler)
    {
        if (!context.Configuration.Tank.TransitRangedFiller) return;
        var player = context.Player;
        if (player.Level < PLDActions.ShieldLob.MinLevel) return;
        var chasing = context.TargetingService.FindNearbyEnemy(25f, player);
        if (chasing == null) return;
        TryPushShieldLob(context, scheduler, chasing.GameObjectId);
    }

    private void TryPushShieldLob(IThemisContext context, RotationScheduler scheduler, ulong targetId)
    {
        if (context.Player.Level < PLDActions.ShieldLob.MinLevel) return;
        if (!context.ActionService.IsActionReady(PLDActions.ShieldLob.ActionId)) return;

        scheduler.PushGcd(ThemisAbilities.ShieldLob, targetId, priority: 10,
            onDispatched: _ =>
            {
                context.Debug.PlannedAction = PLDActions.ShieldLob.Name;
                context.Debug.DamageState = "Shield Lob (ranged)";
                TrainingHelper.Decision(context.TrainingService)
                    .Action(PLDActions.ShieldLob.ActionId, PLDActions.ShieldLob.Name)
                    .AsTankDamage()
                    .Reason(
                        "Shield Lob used as ranged attack while out of melee.",
                        "Shield Lob is a 20y ranged GCD. Maintains uptime when you can't reach the target.")
                    .Factors("Target out of melee range", "Intervene not available")
                    .Alternatives("Intervene (faster gap close)", "Hold GCD (DPS loss)")
                    .Tip("Shield Lob is a filler GCD for ranged situations.")
                    .Concept(PldConcepts.BurstWindow)
                    .Record();
                context.TrainingService?.RecordConceptApplication(PldConcepts.BurstWindow, wasSuccessful: true);
            });
    }

    #endregion

    #region oGCD Damage

    private void TryPushCircleOfScorn(IThemisContext context, RotationScheduler scheduler, ulong selfId, ulong targetId)
    {
        if (!context.Configuration.Tank.EnableCircleOfScorn) return;
        if (context.Player.Level < PLDActions.CircleOfScorn.MinLevel) return;
        if (!context.ActionService.IsActionReady(PLDActions.CircleOfScorn.ActionId)) return;
        // RSR parity (ActionConfig.TimeToKill): don't apply the AoE DoT to a target about to die.
        if (ShouldSkipForTtk(context, targetId)) return;

        // RSR parity (PLD_Reborn.AttackAbility): Circle of Scorn only fires once Fight or
        // Flight AND Requiescat/Imperator are on cooldown, so the FoF -> Requiescat opener
        // double-weave is never robbed of a weave slot. RSR gate:
        //   FightOrFlightPvE.Cooldown.IsCoolingDown && Imperator.Cooldown.IsCoolingDown
        if (!BurstOpenerCommitted(context)) return;

        scheduler.PushOgcd(ThemisAbilities.CircleOfScorn, selfId, priority: 3,
            onDispatched: _ =>
            {
                context.Debug.PlannedAction = PLDActions.CircleOfScorn.Name;
                context.Debug.DamageState = "Circle of Scorn";
                TrainingHelper.Decision(context.TrainingService)
                    .Action(PLDActions.CircleOfScorn.ActionId, PLDActions.CircleOfScorn.Name)
                    .AsTankDamage()
                    .Reason(
                        "Circle of Scorn used on cooldown for AoE DoT damage.",
                        "Circle of Scorn applies a DoT to all enemies in range.")
                    .Factors("Circle of Scorn off cooldown")
                    .Alternatives("Delay for alignment (usually not worth it)")
                    .Tip("Circle of Scorn is a 25s cooldown AoE DoT. Use on cooldown.")
                    .Concept(PldConcepts.CircleOfScorn)
                    .Record();
                context.TrainingService?.RecordConceptApplication(PldConcepts.CircleOfScorn, wasSuccessful: true);
            });
    }

    private void TryPushExpiacion(IThemisContext context, RotationScheduler scheduler, ulong targetId)
    {
        if (!context.Configuration.Tank.EnableSpiritsWithin) return;

        var level = context.Player.Level;
        var behavior = level >= PLDActions.Expiacion.MinLevel ? ThemisAbilities.Expiacion : ThemisAbilities.SpiritsWithin;
        var action = level >= PLDActions.Expiacion.MinLevel ? PLDActions.Expiacion : PLDActions.SpiritsWithin;

        if (level < action.MinLevel) return;
        if (!context.ActionService.IsActionReady(action.ActionId)) return;

        // RSR parity (PLD_Reborn.AttackAbility): Expiacion/Spirits Within are held until the
        // FoF -> Requiescat opener oGCDs are committed, matching RSR's
        //   FightOrFlightPvE.Cooldown.IsCoolingDown && Imperator.Cooldown.IsCoolingDown gate.
        if (!BurstOpenerCommitted(context)) return;

        var hasFof = context.HasFightOrFlight;

        scheduler.PushOgcd(behavior, targetId, priority: 3,
            onDispatched: _ =>
            {
                context.Debug.PlannedAction = action.Name;
                context.Debug.DamageState = action.Name;
                TrainingHelper.Decision(context.TrainingService)
                    .Action(action.ActionId, action.Name)
                    .AsTankDamage()
                    .Reason(
                        $"{action.Name} used on cooldown for single-target oGCD damage.",
                        $"{action.Name} is a high-potency single-target oGCD.")
                    .Factors($"{action.Name} off cooldown", hasFof ? "Inside Fight or Flight" : "Outside burst window")
                    .Alternatives("Delay for burst (minor benefit)")
                    .Tip($"Use {action.Name} on cooldown. Align with Fight or Flight when possible.")
                    .Concept(PldConcepts.Expiacion)
                    .Record();
                context.TrainingService?.RecordConceptApplication(PldConcepts.Expiacion, wasSuccessful: true);
            });
    }

    /// <summary>
    /// True once the Fight or Flight → Requiescat/Imperator burst-opener oGCDs are committed
    /// (on cooldown) or simply unavailable (disabled / under level). Mirrors RSR's
    /// "FightOrFlightPvE.Cooldown.IsCoolingDown && Imperator.Cooldown.IsCoolingDown"
    /// gate that stops Circle of Scorn / Expiacion stealing an opener weave slot.
    /// Requiescat resolves to Imperator at L96+ via GetAdjustedActionId.
    /// </summary>
    private static bool BurstOpenerCommitted(IThemisContext context)
    {
        var level = context.Player.Level;
        var svc = context.ActionService;

        var fofInPlan = context.Configuration.Tank.EnableFightOrFlight
                        && level >= PLDActions.FightOrFlight.MinLevel;
        if (fofInPlan && svc.IsActionReady(PLDActions.FightOrFlight.ActionId))
            return false;

        var magicInPlan = context.Configuration.Tank.EnableRequiescat
                          && level >= PLDActions.Requiescat.MinLevel;
        if (magicInPlan && ThemisRotation.IsMagicPhaseActionReady(context))
            return false;

        return true;
    }

    /// <summary>
    /// RSR-style time-to-kill gate. When enabled, suppresses DoT (re)application on
    /// targets whose estimated TTK is below the configured threshold. Defaults to
    /// inert: off by config, and never skips until enough HP samples exist
    /// (GetTtkSeconds returns float.MaxValue while data is insufficient).
    /// </summary>
    private static bool ShouldSkipForTtk(IThemisContext context, ulong targetId)
    {
        if (!context.Configuration.Tank.EnableTimeToKillCheck) return false;
        var ttkSvc = context.TimeToKillService;
        if (ttkSvc is null) return false;
        return ttkSvc.GetTtkSeconds(targetId) < context.Configuration.Tank.TimeToKillThresholdSeconds;
    }

    #endregion

    #region Magic Phase (Confiteor chain + Holy Spirit/Circle)

    private void TryPushConfiteorChain(IThemisContext context, RotationScheduler scheduler, ulong targetId)
    {
        if (!context.HasRequiescat) return;
        var level = context.Player.Level;
        if (level < PLDActions.Confiteor.MinLevel) return;
        if (context.ConfiteorStep == 0) return;

        AbilityBehavior behavior;
        ActionDefinition action;
        int step = context.ConfiteorStep;

        switch (step)
        {
            case 1: behavior = ThemisAbilities.Confiteor; action = PLDActions.Confiteor; break;
            case 2: behavior = ThemisAbilities.BladeOfFaith; action = PLDActions.BladeOfFaith; break;
            case 3: behavior = ThemisAbilities.BladeOfTruth; action = PLDActions.BladeOfTruth; break;
            case 4: behavior = ThemisAbilities.BladeOfValor; action = PLDActions.BladeOfValor; break;
            default: return;
        }

        if (level < action.MinLevel) return;

        scheduler.PushGcd(behavior, targetId, priority: 2,
            onDispatched: _ =>
            {
                context.Debug.PlannedAction = action.Name;
                context.Debug.DamageState = $"Confiteor chain ({step}/4)";
                var conceptId = step == 1 ? PldConcepts.Confiteor : PldConcepts.BladeCombo;
                TrainingHelper.Decision(context.TrainingService)
                    .Action(action.ActionId, action.Name)
                    .AsTankBurst()
                    .Reason(
                        $"Confiteor chain step {step}/4 during Requiescat phase.",
                        "Confiteor → Blade of Faith → Truth → Valor is your highest-potency burst combo.")
                    .Factors("Requiescat active", $"Confiteor step: {step}/4")
                    .Alternatives("Holy Spirit instead (lower potency)")
                    .Tip("Always complete the full Confiteor combo during Requiescat.")
                    .Concept(conceptId)
                    .Record();
                context.TrainingService?.RecordConceptApplication(conceptId, wasSuccessful: true);
            });
    }

    private void TryPushHolyPhaseSpenders(IThemisContext context, RotationScheduler scheduler, ulong targetId, ulong selfId, int enemyCount)
    {
        if (!context.HasRequiescat) return;
        var level = context.Player.Level;
        var minAoE = context.Configuration.Tank.GetEffectiveAoEMinTargets(JobRegistry.Paladin);

        // Holy Circle (AoE) if threshold met and L72+
        if (level >= PLDActions.HolyCircle.MinLevel &&
            enemyCount >= minAoE &&
            context.Configuration.Tank.EnableAoEDamage)
        {
            // Cast-time gate: instant when Requiescat stacks > 0 or Swiftcast
            var holyCircleCastTime = context.RequiescatStacks > 0 || context.HasSwiftcast
                ? 0f
                : PLDActions.HolyCircle.CastTime;
            if (!MechanicCastGate.ShouldBlock(context, holyCircleCastTime))
            {
                var stacks = context.RequiescatStacks;
                scheduler.PushGcd(ThemisAbilities.HolyCircle, selfId, priority: 3,
                    onDispatched: _ =>
                    {
                        context.Debug.PlannedAction = PLDActions.HolyCircle.Name;
                        context.Debug.DamageState = $"Holy Circle ({enemyCount} targets)";
                        TrainingHelper.Decision(context.TrainingService)
                            .Action(PLDActions.HolyCircle.ActionId, PLDActions.HolyCircle.Name)
                            .AsTankBurst()
                            .Reason(
                                $"Holy Circle during Requiescat with {enemyCount} targets.",
                                "Holy Circle is free and instant during Requiescat. Outperforms Holy Spirit in AoE.")
                            .Factors($"Requiescat stacks: {stacks}", $"Enemy count: {enemyCount} (>= {minAoE})")
                            .Alternatives($"Holy Spirit ({enemyCount} targets, lower total potency)")
                            .Tip("During Requiescat in multi-target, use Holy Circle.")
                            .Concept(PldConcepts.MagicPhase)
                            .Record();
                        context.TrainingService?.RecordConceptApplication(PldConcepts.MagicPhase, wasSuccessful: true);
                    });
                return;
            }
        }

        // Holy Spirit (single target) L64+
        if (level >= PLDActions.HolySpirit.MinLevel)
        {
            var holySpiritCastTime = context.RequiescatStacks > 0 || context.HasDivineMight || context.HasSwiftcast
                ? 0f
                : PLDActions.HolySpirit.CastTime;
            if (MechanicCastGate.ShouldBlock(context, holySpiritCastTime)) return;

            var stacks = context.RequiescatStacks;
            scheduler.PushGcd(ThemisAbilities.HolySpirit, targetId, priority: 3,
                onDispatched: _ =>
                {
                    context.Debug.PlannedAction = PLDActions.HolySpirit.Name;
                    context.Debug.DamageState = $"Holy Spirit ({stacks} stacks)";
                    TrainingHelper.Decision(context.TrainingService)
                        .Action(PLDActions.HolySpirit.ActionId, PLDActions.HolySpirit.Name)
                        .AsTankBurst()
                        .Reason(
                            $"Holy Spirit during Requiescat ({stacks} stacks remaining).",
                            "Holy Spirit is your primary single-target magic GCD during Requiescat.")
                        .Factors($"Requiescat stacks: {stacks}", $"Single target scenario ({enemyCount} enemies)")
                        .Alternatives("Holy Circle (better with 3+ targets)", "Confiteor chain (use when 0 stacks remain)")
                        .Tip("Spend all Requiescat stacks with Holy Spirit before the buff expires.")
                        .Concept(PldConcepts.HolySpirit)
                        .Record();
                    context.TrainingService?.RecordConceptApplication(PldConcepts.HolySpirit, wasSuccessful: true);
                });
        }
    }

    /// <summary>
    /// Holy Spirit/Circle during filler when Divine Might is active but Requiescat is not.
    /// RSR parity (PLD_Reborn GeneralGCD ~401–419).
    /// </summary>
    private void TryPushDivineMightSpenders(IThemisContext context, RotationScheduler scheduler, ulong targetId, ulong selfId, int enemyCount)
    {
        if (!context.HasDivineMight || context.HasRequiescat) return;

        var level = context.Player.Level;
        var minAoE = context.Configuration.Tank.GetEffectiveAoEMinTargets(JobRegistry.Paladin);

        if (level >= PLDActions.HolyCircle.MinLevel &&
            enemyCount >= minAoE &&
            context.Configuration.Tank.EnableAoEDamage)
        {
            if (!MechanicCastGate.ShouldBlock(context, 0f))
            {
                scheduler.PushGcd(ThemisAbilities.HolyCircle, selfId, priority: 4,
                    onDispatched: _ =>
                    {
                        context.Debug.PlannedAction = PLDActions.HolyCircle.Name;
                        context.Debug.DamageState = $"Holy Circle (Divine Might, {enemyCount} targets)";
                        TrainingHelper.Decision(context.TrainingService)
                            .Action(PLDActions.HolyCircle.ActionId, PLDActions.HolyCircle.Name)
                            .AsTankDamage()
                            .Reason(
                                "Holy Circle with Divine Might during filler.",
                                "Divine Might makes Holy Circle instant outside Requiescat.")
                            .Factors("Divine Might active", $"Enemy count: {enemyCount} (>= {minAoE})")
                            .Alternatives("Holy Spirit (single target)", "Spend Atonement chain first")
                            .Tip("Spend Divine Might before Royal Authority refreshes it.")
                            .Concept(PldConcepts.HolySpirit)
                            .Record();
                        context.TrainingService?.RecordConceptApplication(PldConcepts.HolySpirit, wasSuccessful: true);
                    });
                return;
            }
        }

        if (level >= PLDActions.HolySpirit.MinLevel &&
            !MechanicCastGate.ShouldBlock(context, 0f))
        {
            scheduler.PushGcd(ThemisAbilities.HolySpirit, targetId, priority: 4,
                onDispatched: _ =>
                {
                    context.Debug.PlannedAction = PLDActions.HolySpirit.Name;
                    context.Debug.DamageState = "Holy Spirit (Divine Might)";
                    TrainingHelper.Decision(context.TrainingService)
                        .Action(PLDActions.HolySpirit.ActionId, PLDActions.HolySpirit.Name)
                        .AsTankDamage()
                        .Reason(
                            "Holy Spirit with Divine Might during filler.",
                            "Divine Might from Royal Authority makes Holy Spirit instant.")
                        .Factors("Divine Might active", "Outside Requiescat window")
                        .Alternatives("Continue Atonement chain", "Royal Authority (wastes proc)")
                        .Tip("Priority: Sepulchre > Supplication > Holy Spirit > Atonement.")
                        .Concept(PldConcepts.HolySpirit)
                        .Record();
                    context.TrainingService?.RecordConceptApplication(PldConcepts.HolySpirit, wasSuccessful: true);
                });
        }
    }

    #endregion

    #region Atonement Chain

    private void TryPushAtonementChain(IThemisContext context, RotationScheduler scheduler, ulong targetId)
    {
        var level = context.Player.Level;
        if (level < PLDActions.Atonement.MinLevel) return;

        // Finish Total Eclipse → Prominence before spending Atonement (Dawntrail AoE combo lock-in).
        if (ThemisRotation.IsInAoECombo(context.ActionService, context.LastComboAction, context.ComboTimeRemaining))
            return;

        var step = context.AtonementStep;
        if (step == 0) return;

        AbilityBehavior behavior;
        ActionDefinition action;

        switch (step)
        {
            case 1: behavior = ThemisAbilities.Atonement; action = PLDActions.Atonement; break;
            case 2: behavior = ThemisAbilities.Supplication; action = PLDActions.Supplication; break;
            case 3: behavior = ThemisAbilities.Sepulchre; action = PLDActions.Sepulchre; break;
            default: return;
        }

        var hasFof = context.HasFightOrFlight;
        var fofRem = context.FightOrFlightRemaining;
        var chainPriority = ThemisRotation.AtonementChainPriority(step);

        scheduler.PushGcd(behavior, targetId, priority: chainPriority,
            onDispatched: _ =>
            {
                context.Debug.PlannedAction = action.Name;
                context.Debug.DamageState = $"Atonement chain ({step}/3)";
                if (hasFof)
                {
                    TrainingHelper.Decision(context.TrainingService)
                        .Action(action.ActionId, action.Name)
                        .AsTankBurst()
                        .Reason(
                            $"Atonement chain during Fight or Flight ({fofRem:F1}s remaining)",
                            "Atonement → Supplication → Sepulchre is a high-potency chain unlocked by Royal Authority.")
                        .Factors($"Fight or Flight active ({fofRem:F1}s)", $"Chain position: {step}/3")
                        .Alternatives("Save stacks for later burst", "Use main combo instead (lower potency)")
                        .Tip("Always complete the Atonement chain during Fight or Flight.")
                        .Concept(PldConcepts.AtonementChain)
                        .Record();
                }
                else
                {
                    TrainingHelper.Decision(context.TrainingService)
                        .Action(action.ActionId, action.Name)
                        .AsTankDamage()
                        .Reason(
                            $"Atonement chain step {step}/3 - spending proc before expiry.",
                            "Atonement → Supplication → Sepulchre is unlocked by Royal Authority.")
                        .Factors($"Chain position: {step}/3")
                        .Alternatives("Hold for Fight or Flight", "Use main combo (resets combo, wastes stacks)")
                        .Tip("Don't let Atonement stacks expire.")
                        .Concept(PldConcepts.AtonementChain)
                        .Record();
                }
                context.TrainingService?.RecordConceptApplication(PldConcepts.AtonementChain, wasSuccessful: true);
            });
    }

    #endregion

    #region Goring Blade / Blade of Honor

    private void TryPushBladeOfHonor(IThemisContext context, RotationScheduler scheduler, ulong targetId)
    {
        if (!context.Configuration.Tank.EnableDamage) return;
        if (context.Player.Level < PLDActions.BladeOfHonor.MinLevel) return;
        // Proc gate (RSR parity): only when "Blade of Honor Ready" is active, detected via button
        // replacement in ThemisContext.HasBladeOfHonor. This is the fix for the per-frame requeue spam.
        if (!context.HasBladeOfHonor) return;

        scheduler.PushOgcd(ThemisAbilities.BladeOfHonor, targetId, priority: 1,
            onDispatched: _ =>
            {
                context.Debug.PlannedAction = PLDActions.BladeOfHonor.Name;
                context.Debug.DamageState = "Blade of Honor";
                TrainingHelper.Decision(context.TrainingService)
                    .Action(PLDActions.BladeOfHonor.ActionId, PLDActions.BladeOfHonor.Name)
                    .AsTankBurst()
                    .Reason(
                        "Blade of Honor — burst finisher after the Confiteor combo (Blade of Valor).",
                        "Highest-potency oGCD finisher; fires once per Requiescat/Imperator burst window.")
                    .Factors("Blade of Honor Ready proc active")
                    .Alternatives("Weave another oGCD if the proc is not yet available")
                    .Tip("Blade of Honor is granted by Blade of Valor and consumed once per burst.")
                    .Concept(PldConcepts.GoringBlade)
                    .Record();
                context.TrainingService?.RecordConceptApplication(PldConcepts.GoringBlade, wasSuccessful: true);
            });
    }

    private void TryPushGoringBlade(IThemisContext context, RotationScheduler scheduler, ulong targetId)
    {
        var level = context.Player.Level;
        if (level < PLDActions.GoringBlade.MinLevel) return;

        if (ThemisRotation.IsInAoECombo(context.ActionService, context.LastComboAction, context.ComboTimeRemaining))
            return;

        // RSR parity (PaladinRotation.ModifyGoringBladePvE: StatusNeed = GoringBladeReady).
        // Dawntrail removed the Goring Blade DoT/combo — it is now a Fight-or-Flight proc weaponskill,
        // so gate purely on the proc. (No DoT-remaining refresh and no TimeToKill skip: it is direct
        // damage, always worth spending while the proc is up.)
        if (!context.HasGoringBladeReady) return;

        var hasFof = context.HasFightOrFlight;
        scheduler.PushGcd(ThemisAbilities.GoringBlade, targetId, priority: 3,
            onDispatched: _ =>
            {
                context.Debug.PlannedAction = PLDActions.GoringBlade.Name;
                context.Debug.DamageState = "Goring Blade (proc)";
                TrainingHelper.Decision(context.TrainingService)
                    .Action(PLDActions.GoringBlade.ActionId, PLDActions.GoringBlade.Name)
                    .AsTankBurst()
                    .Reason(
                        "Goring Blade — Fight or Flight proc weaponskill (700 potency).",
                        "Granted by Fight or Flight; spend it within the proc window during burst.")
                    .Factors("Goring Blade Ready proc active", hasFof ? "Inside Fight or Flight" : "Proc window")
                    .Alternatives("Continue the main combo if the proc is not up")
                    .Tip("Use Goring Blade once per Fight or Flight, early in the burst.")
                    .Concept(PldConcepts.GoringBlade)
                    .Record();
                context.TrainingService?.RecordConceptApplication(PldConcepts.GoringBlade, wasSuccessful: true);
            });
    }

    #endregion

    #region Basic Combo (ST / AoE)

    private void TryPushBasicCombo(IThemisContext context, RotationScheduler scheduler, ulong targetId, ulong selfId, int enemyCount)
    {
        var level = context.Player.Level;
        var minAoE = context.Configuration.Tank.GetEffectiveAoEMinTargets(JobRegistry.Paladin);
        var prominenceAvailable = ThemisRotation.IsProminenceAvailable(context.ActionService, level);
        var inAoECombo = prominenceAvailable
            && ThemisRotation.IsInAoECombo(context.ActionService, context.LastComboAction, context.ComboTimeRemaining);

        var stuckTeWithoutProminence = context.LastComboAction == PLDActions.TotalEclipse.ActionId
            && context.ComboTimeRemaining > 0
            && !prominenceAvailable;

        // A real pack (>= minAoE in PBAoE range) breaks an in-progress single-target combo to AoE
        // immediately. The old !inSingleTargetCombo guard delayed AoE by up to 2 GCDs while finishing the
        // 1-2-3 — fine normally, but in wall-to-wall pulls AutoDuty only parks on each pack for ~2 GCDs,
        // so that delay meant the pack was never tagged before it moved on. At 2+ targets AoE is the
        // breakeven gain anyway (RSR parity), so interrupting ST is correct, not just convenient.
        var useFullAoECombo = context.Configuration.Tank.EnableAoEDamage
                              && level >= PLDActions.TotalEclipse.MinLevel
                              && prominenceAvailable
                              && (inAoECombo || (enemyCount >= minAoE && !stuckTeWithoutProminence));

        if (useFullAoECombo)
        {
            TryPushAoECombo(context, scheduler, selfId, enemyCount, minAoE);
            return;
        }

        var useTeFiller = context.Configuration.Tank.EnableAoEDamage
                          && level >= PLDActions.TotalEclipse.MinLevel
                          && !prominenceAvailable
                          && enemyCount >= minAoE
                          && !stuckTeWithoutProminence;

        if (useTeFiller)
        {
            TryPushTotalEclipseFiller(context, scheduler, selfId, enemyCount, minAoE);
            return;
        }

        TryPushSingleTargetCombo(context, scheduler, targetId);
    }

    private void TryPushAoECombo(IThemisContext context, RotationScheduler scheduler, ulong selfId, int enemyCount, int minAoE)
    {
        var level = context.Player.Level;

        // Mid-combo: only Prominence is valid — do not fall through to Total Eclipse (ActionStatus deadlocks).
        if (ThemisRotation.IsInAoECombo(context.ActionService, context.LastComboAction, context.ComboTimeRemaining))
        {
            if (level >= PLDActions.Prominence.MinLevel)
            {
                scheduler.PushGcd(ThemisAbilities.Prominence, selfId, priority: 2,
                    onDispatched: _ =>
                    {
                        context.Debug.PlannedAction = PLDActions.Prominence.Name;
                        context.Debug.DamageState = $"AoE 2/2 ({enemyCount} targets)";
                        TrainingHelper.Decision(context.TrainingService)
                            .Action(PLDActions.Prominence.ActionId, PLDActions.Prominence.Name)
                            .AsTankDamage()
                            .Reason(
                                $"AoE combo finisher: Prominence ({enemyCount} targets).",
                                "Total Eclipse → Prominence is PLD's AoE combo.")
                            .Factors($"Enemy count: {enemyCount} (>= {minAoE})", "AoE combo step 2", "AoE damage enabled")
                            .Alternatives("Single target combo (better for 1-2 targets)")
                            .Tip("Switch to AoE rotation at 3+ targets.")
                            .Concept(PldConcepts.MagicPhase)
                            .Record();
                        context.TrainingService?.RecordConceptApplication(PldConcepts.MagicPhase, wasSuccessful: true);
                    });
            }
            return;
        }

        if (ThemisRotation.HasUnspentFillerProcs(context))
            return;

        PushTotalEclipse(context, scheduler, selfId, enemyCount, minAoE, comboStepLabel: "AoE 1/2");
    }

    /// <summary>
    /// Total Eclipse without Prominence (job quest not done): standalone AoE filler only.
    /// </summary>
    private void TryPushTotalEclipseFiller(IThemisContext context, RotationScheduler scheduler, ulong selfId, int enemyCount, int minAoE)
    {
        if (ThemisRotation.HasUnspentFillerProcs(context))
            return;

        PushTotalEclipse(context, scheduler, selfId, enemyCount, minAoE, comboStepLabel: "AoE filler");
    }

    private static void PushTotalEclipse(
        IThemisContext context,
        RotationScheduler scheduler,
        ulong selfId,
        int enemyCount,
        int minAoE,
        string comboStepLabel)
    {
        scheduler.PushGcd(ThemisAbilities.TotalEclipse, selfId, priority: 7,
            onDispatched: _ =>
            {
                context.Debug.PlannedAction = PLDActions.TotalEclipse.Name;
                context.Debug.DamageState = $"{comboStepLabel} ({enemyCount} targets)";
                TrainingHelper.Decision(context.TrainingService)
                    .Action(PLDActions.TotalEclipse.ActionId, PLDActions.TotalEclipse.Name)
                    .AsTankDamage()
                    .Reason(
                        comboStepLabel == "AoE 1/2"
                            ? $"AoE combo starter: Total Eclipse ({enemyCount} targets)."
                            : $"AoE filler: Total Eclipse ({enemyCount} targets).",
                        comboStepLabel == "AoE 1/2"
                            ? "Total Eclipse opens the AoE combo."
                            : "Prominence not unlocked — Total Eclipse only until job quests are done.")
                    .Factors($"Enemy count: {enemyCount} (>= {minAoE})", comboStepLabel)
                    .Alternatives("Single target combo (better for 1-2 targets)")
                    .Tip(comboStepLabel == "AoE 1/2"
                        ? "Total Eclipse → Prominence generates Oath Gauge via Prominence's combo bonus."
                        : "Complete PLD job quests to unlock Prominence and the full AoE combo.")
                    .Concept(PldConcepts.MagicPhase)
                    .Record();
                context.TrainingService?.RecordConceptApplication(PldConcepts.MagicPhase, wasSuccessful: true);
            });
    }

    private void TryPushSingleTargetCombo(IThemisContext context, RotationScheduler scheduler, ulong targetId)
    {
        var level = context.Player.Level;

        // Step 3: Royal Authority / Rage of Halone.
        // Do not return — fall through to Fast Blade at priority 7 as ActionStatus fallback.
        if (context.ComboStep == 3 &&
            context.LastComboAction == PLDActions.RiotBlade.ActionId &&
            !ThemisRotation.HasUnspentFillerProcs(context))
        {
            var finisher = PLDActions.GetComboFinisher(level, context.ActionService);
            var behavior = level >= PLDActions.RoyalAuthority.MinLevel
                ? ThemisAbilities.RoyalAuthority
                : (level >= PLDActions.RageOfHalone.MinLevel ? ThemisAbilities.RageOfHalone : ThemisAbilities.RiotBlade);

            scheduler.PushGcd(behavior, targetId, priority: 6,
                onDispatched: _ =>
                {
                    context.Debug.PlannedAction = finisher.Name;
                    context.Debug.DamageState = "Combo 3/3";
                    TrainingHelper.Decision(context.TrainingService)
                        .Action(finisher.ActionId, finisher.Name)
                        .AsTankDamage()
                        .Reason(
                            "Main combo finisher — grants Sword Oath stacks.",
                            "Fast Blade → Riot Blade → Royal Authority is the foundation of PLD DPS.")
                        .Factors("Combo step 3", context.HasFightOrFlight ? "Inside FoF burst" : "Outside burst")
                        .Alternatives("Atonement chain (higher priority if stacks available)")
                        .Tip("Complete the 1-2-3 combo consistently.")
                        .Concept(PldConcepts.AtonementChain)
                        .Record();
                    context.TrainingService?.RecordConceptApplication(PldConcepts.AtonementChain, wasSuccessful: true);
                });
        }

        // Step 2: Riot Blade.
        // Do not return — fall through to Fast Blade at priority 7 as ActionStatus fallback.
        if (context.ComboStep == 2 &&
            context.LastComboAction == PLDActions.FastBlade.ActionId &&
            level >= PLDActions.RiotBlade.MinLevel)
        {
            scheduler.PushGcd(ThemisAbilities.RiotBlade, targetId, priority: 6,
                onDispatched: _ =>
                {
                    context.Debug.PlannedAction = PLDActions.RiotBlade.Name;
                    context.Debug.DamageState = "Combo 2/3";
                    TrainingHelper.Decision(context.TrainingService)
                        .Action(PLDActions.RiotBlade.ActionId, PLDActions.RiotBlade.Name)
                        .AsTankDamage()
                        .Reason(
                            "Main combo step 2 — Riot Blade.",
                            "Riot Blade restores MP and continues the combo.")
                        .Factors("Combo step 2")
                        .Alternatives("Break combo (wastes progress)")
                        .Tip("Always continue the combo.")
                        .Concept(PldConcepts.BurstWindow)
                        .Record();
                    context.TrainingService?.RecordConceptApplication(PldConcepts.BurstWindow, wasSuccessful: true);
                });
        }

        // Starter / fallback: Fast Blade
        scheduler.PushGcd(ThemisAbilities.FastBlade, targetId, priority: 7,
            onDispatched: _ =>
            {
                context.Debug.PlannedAction = PLDActions.FastBlade.Name;
                context.Debug.DamageState = "Combo 1/3";
                TrainingHelper.Decision(context.TrainingService)
                    .Action(PLDActions.FastBlade.ActionId, PLDActions.FastBlade.Name)
                    .AsTankDamage()
                    .Reason(
                        "Main combo starter — Fast Blade.",
                        "Fast Blade opens the 1-2-3 combo.")
                    .Factors("Combo start")
                    .Alternatives("Atonement chain if Sword Oath stacks available")
                    .Tip("Fast Blade is the foundation of PLD's ST rotation.")
                    .Concept(PldConcepts.BurstWindow)
                    .Record();
                context.TrainingService?.RecordConceptApplication(PldConcepts.BurstWindow, wasSuccessful: true);
            });
    }

    #endregion
}
