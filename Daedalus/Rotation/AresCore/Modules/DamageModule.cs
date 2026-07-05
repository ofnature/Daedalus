using Daedalus.Data;
using Daedalus.Rotation.AresCore.Abilities;
using Daedalus.Rotation.AresCore.Context;
using Daedalus.Rotation.ApolloCore.Helpers;
using Daedalus.Rotation.Common.Helpers;
using Daedalus.Rotation.Common.Scheduling;
using Daedalus.Services.Action;
using Daedalus.Services.Training;

namespace Daedalus.Rotation.AresCore.Modules;

/// <summary>
/// Handles the Warrior damage rotation (scheduler-driven).
/// </summary>
public sealed class DamageModule : IAresModule
{
    public int Priority => 30;
    public string Name => "Damage";

    public bool TryExecute(IAresContext context, bool isMoving) => false;

    public void UpdateDebugState(IAresContext context) { }

    public void CollectCandidates(IAresContext context, RotationScheduler scheduler, bool isMoving)
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
            // No hard target = damage paused. During W2W transit the mobs are still ON us — give
            // them the ranged filler (FindNearbyEnemy only returns in-combat enemies, so nothing new
            // is ever pulled). This pause was a 12-14% uptime hole on tank W2W validation runs.
            TryPushTransitRangedFiller(context, scheduler);
            context.Debug.DamageState = "Paused (no target)";
            return;
        }

        var player = context.Player;

        var enemyStrategy = TankTargetingHelper.ResolveEnemyStrategy(
            context.Configuration.Tank,
            context.Configuration.Targeting.EnemyStrategy,
            context.PartyHelper?.FindCoTank(player) != null);

        var target = context.TargetingService.FindEnemyForAction(
            enemyStrategy,
            WARActions.HeavySwing.ActionId,
            player);

        // FindEnemyForAction can return a sticky/hard target far outside melee (W2W transit stuck
        // state — the module then pushes combo GCDs that fail range at dispatch forever). Demote
        // beyond-melee targets to the out-of-melee branch so gap-close + Tomahawk run instead.
        if (target != null && !TankTargetingHelper.IsWithinMeleeReach(player, target))
            target = null;

        var engageTarget = target ?? context.TargetingService.FindEnemy(
            enemyStrategy,
            20f,
            player);

        if (engageTarget == null)
        {
            // Nothing within engage range — mobs chasing from further out still get the filler.
            TryPushTransitRangedFiller(context, scheduler);
            context.Debug.DamageState = "No target";
            return;
        }

        // Out-of-melee: Onslaught (gap close) + Tomahawk (ranged filler)
        if (target == null)
        {
            // Gap closer ONLY to snap aggro back (mob peeled to someone else) — never as a travel
            // tool: dashing at far targets mid-W2W fights AutoDuty's pathing (user rule; DRK run
            // showed Shadowstride x3 during the gather). Tomahawk covers the damage at range.
            var lostToOther = context.EnmityService?.HasLostAggroToOther(engageTarget, player.EntityId) == true;
            if (lostToOther && !context.Configuration.Tank.SuppressGapCloserOnLostMob)
                TryPushOnslaughtGapClose(context, scheduler, engageTarget.GameObjectId, engageTarget);
            TryPushTomahawk(context, scheduler, engageTarget.GameObjectId, engageTarget);
            // Without Tomahawk (Lv15, pre-WAR Marauder — level+learned, matching the base
            // rotation's walk-in gate) there is nothing to push here — the pre-ranged walk-in
            // closes the distance instead. Name the state so the empty GCD doesn't read as a stall.
            if (!ActionAvailability.MeetsLevelAndLearned(player.Level, context.ActionService, WARActions.Tomahawk))
                context.Debug.DamageState = "Out of melee — walking in (no ranged GCD until Lv15)";
            return;
        }

        var pack = EnemyPackDebugHelper.Count(context.TargetingService, 5f, player);
        EnemyPackDebugHelper.Apply(context.Debug, pack);
        var enemyCount = pack.AoeRange;

        // oGCD pushes
        TryPushPrimalWrath(context, scheduler, target.GameObjectId, enemyCount);
        TryPushUpheaval(context, scheduler, target.GameObjectId, enemyCount);
        TryPushOrogeny(context, scheduler, target.GameObjectId, enemyCount);
        TryPushOnslaughtWeave(context, scheduler, target.GameObjectId, target);

        // GCD pushes — burst procs before IR spenders, Primal chain after stacks spent, combo fallback last
        TryPushInnerChaos(context, scheduler, target.GameObjectId, enemyCount);
        TryPushGaugeSpender(context, scheduler, target.GameObjectId, enemyCount);
        TryPushPrimalRend(context, scheduler, target.GameObjectId);
        TryPushPrimalRuination(context, scheduler, target.GameObjectId);
        TryPushMovingAddTag(context, scheduler, isMoving);
        TryPushCombo(context, scheduler, target.GameObjectId, enemyCount);
    }

    /// <summary>
    /// Wall-to-wall add tag: while moving in a duty, ranged-pull (Tomahawk) the nearest mob within 25y
    /// that isn't on us yet — including idle packs we're walking toward — so the pull gathers. Only fires
    /// while moving, in an instanced duty, and between combos (never breaks one).
    /// </summary>
    private void TryPushMovingAddTag(IAresContext context, RotationScheduler scheduler, bool isMoving)
    {
        if (!isMoving) return;
        if (!context.Configuration.Tank.TagAddsWhileMovingWithRangedAttack) return;
        if (!PlayerSafetyHelper.IsInInstancedDuty()) return;
        var player = context.Player;
        if (player.Level < WARActions.Tomahawk.MinLevel) return;
        if (!context.ActionService.IsActionReady(WARActions.Tomahawk.ActionId)) return;
        if (context.ComboTimeRemaining > 0f) return;

        var stray = context.TargetingService.FindNearestTaggableEnemy(25f, player);
        if (stray == null) return;

        scheduler.PushGcd(AresAbilities.Tomahawk, stray.GameObjectId, priority: 6,
            onDispatched: _ =>
            {
                context.Debug.PlannedAction = WARActions.Tomahawk.Name;
                context.Debug.DamageState = "Tomahawk (tag add — moving)";
            });
    }

    #region Out-of-melee

    private void TryPushOnslaughtGapClose(IAresContext context, RotationScheduler scheduler, ulong targetId,
                                          Dalamud.Game.ClientState.Objects.Types.IBattleChara target)
    {
        if (!context.Configuration.Tank.EnableOnslaught) return;
        var player = context.Player;
        if (player.Level < WARActions.Onslaught.MinLevel) return;
        if (!context.ActionService.IsActionReady(WARActions.Onslaught.ActionId)) return;
        if (context.TargetingService.GapCloserSafety.ShouldBlockGapCloser(target, player))
        {
            context.Debug.DamageState = $"Onslaught blocked: {context.TargetingService.GapCloserSafety.LastBlockReason}";
            return;
        }

        scheduler.PushOgcd(AresAbilities.Onslaught, targetId, priority: 4,
            onDispatched: _ =>
            {
                context.Debug.PlannedAction = WARActions.Onslaught.Name;
                context.Debug.DamageState = "Onslaught (gap close)";
            });
    }

    /// <summary>
    /// W2W transit filler: ranged GCD at the nearest in-combat enemy (chasing pack) while we have no
    /// hard target / nothing in engage range. FindNearbyEnemy bypasses the no-target damage pause by
    /// design and only returns enemies already fighting us.
    /// </summary>
    private void TryPushTransitRangedFiller(IAresContext context, RotationScheduler scheduler)
    {
        if (!context.Configuration.Tank.TransitRangedFiller) return;
        var player = context.Player;
        if (player.Level < WARActions.Tomahawk.MinLevel) return;
        var chasing = context.TargetingService.FindNearbyEnemy(25f, player);
        if (chasing == null) return;
        TryPushTomahawk(context, scheduler, chasing.GameObjectId, chasing);
    }

    private void TryPushTomahawk(IAresContext context, RotationScheduler scheduler, ulong targetId,
                                 Dalamud.Game.ClientState.Objects.Types.IBattleChara target)
    {
        var player = context.Player;
        if (player.Level < WARActions.Tomahawk.MinLevel) return;
        if (!context.ActionService.IsActionReady(WARActions.Tomahawk.ActionId)) return;
        if (DistanceHelper.IsActionInRange(WARActions.HeavySwing.ActionId, player, target)) return;

        scheduler.PushGcd(AresAbilities.Tomahawk, targetId, priority: 10,
            onDispatched: _ =>
            {
                context.Debug.PlannedAction = WARActions.Tomahawk.Name;
                context.Debug.DamageState = "Tomahawk (ranged)";
            });
    }

    #endregion

    #region oGCDs

    private void TryPushPrimalWrath(IAresContext context, RotationScheduler scheduler, ulong targetId, int enemyCount)
    {
        if (!context.Configuration.Tank.EnablePrimalWrath) return;
        if (context.Player.Level < WARActions.PrimalWrath.MinLevel) return;
        if (!context.PrimalWrathReady) return;

        scheduler.PushOgcd(AresAbilities.PrimalWrath, targetId, priority: 1,
            onDispatched: _ =>
            {
                context.Debug.PlannedAction = WARActions.PrimalWrath.Name;
                context.Debug.DamageState = $"Primal Wrath ({enemyCount} enemies)";
            });
    }

    private void TryPushUpheaval(IAresContext context, RotationScheduler scheduler, ulong targetId, int enemyCount)
    {
        if (!context.Configuration.Tank.EnableOrogeny) return;
        var level = context.Player.Level;
        if (level < WARActions.Upheaval.MinLevel) return;
        if (!context.HasSurgingTempest) return;

        // Prefer Orogeny for AoE
        if (context.Configuration.Tank.EnableAoEDamage &&
            enemyCount >= context.Configuration.Tank.GetEffectiveAoEMinTargets(JobRegistry.Warrior) &&
            level >= WARActions.Orogeny.MinLevel) return;

        if (!context.ActionService.IsActionReady(WARActions.Upheaval.ActionId)) return;

        scheduler.PushOgcd(AresAbilities.Upheaval, targetId, priority: 2,
            onDispatched: _ =>
            {
                context.Debug.PlannedAction = WARActions.Upheaval.Name;
                context.Debug.DamageState = "Upheaval";
                TrainingHelper.Decision(context.TrainingService)
                    .Action(WARActions.Upheaval.ActionId, WARActions.Upheaval.Name)
                    .AsTankDamage()
                    .Reason("Upheaval on cooldown.", "400-potency oGCD on 30s CD.")
                    .Factors("Upheaval ready", "Single target")
                    .Alternatives("Skip (no reason to hold)")
                    .Tip("Use on cooldown.")
                    .Concept(WarConcepts.Upheaval)
                    .Record();
                context.TrainingService?.RecordConceptApplication(WarConcepts.Upheaval, true, "oGCD burst");
            });
    }

    private void TryPushOrogeny(IAresContext context, RotationScheduler scheduler, ulong targetId, int enemyCount)
    {
        if (!context.Configuration.Tank.EnableOrogeny) return;
        var level = context.Player.Level;
        if (level < WARActions.Orogeny.MinLevel) return;
        if (!context.Configuration.Tank.EnableAoEDamage) return;
        if (enemyCount < context.Configuration.Tank.GetEffectiveAoEMinTargets(JobRegistry.Warrior)) return;
        if (!context.ActionService.IsActionReady(WARActions.Orogeny.ActionId)) return;

        scheduler.PushOgcd(AresAbilities.Orogeny, targetId, priority: 2,
            onDispatched: _ =>
            {
                context.Debug.PlannedAction = WARActions.Orogeny.Name;
                context.Debug.DamageState = $"Orogeny ({enemyCount} enemies)";
                TrainingHelper.Decision(context.TrainingService)
                    .Action(WARActions.Orogeny.ActionId, WARActions.Orogeny.Name)
                    .AsTankDamage()
                    .Reason($"Orogeny AoE oGCD ({enemyCount} enemies).", "Replaces Upheaval in AoE.")
                    .Factors($"{enemyCount} enemies", "AoE threshold met")
                    .Alternatives("Upheaval (ST only)")
                    .Tip("Free AoE damage in pulls.")
                    .Concept(WarConcepts.Orogeny)
                    .Record();
                context.TrainingService?.RecordConceptApplication(WarConcepts.Orogeny, true, "AoE oGCD");
            });
    }

    private void TryPushOnslaughtWeave(IAresContext context, RotationScheduler scheduler, ulong targetId,
                                       Dalamud.Game.ClientState.Objects.Types.IBattleChara target)
    {
        if (!context.Configuration.Tank.EnableOnslaught) return;
        if (!context.Configuration.Tank.AutoOnslaught) return;
        var player = context.Player;
        if (player.Level < WARActions.Onslaught.MinLevel) return;
        // Don't dash mid-movement (RSR YEET !moving) — keeps Onslaught from yanking you out of position.
        if (context.IsMoving) return;
        if (!context.ActionService.IsActionReady(WARActions.Onslaught.ActionId)) return;

        // Burst-align (RSR YEETBurst): weave during Inner Release; otherwise hold a charge and only
        // spend when about to overcap (2+ charges banked).
        var charges = (int)context.ActionService.GetCurrentCharges(WARActions.Onslaught.ActionId);
        if (!context.HasInnerRelease && charges < 2) return;

        if (context.TargetingService.GapCloserSafety.ShouldBlockGapCloser(target, player))
        {
            context.Debug.DamageState = $"Onslaught blocked: {context.TargetingService.GapCloserSafety.LastBlockReason}";
            return;
        }
        if (!TankTargetingHelper.IsWithinMeleeReach(player, target)) return;

        scheduler.PushOgcd(AresAbilities.Onslaught, targetId, priority: 4,
            onDispatched: _ =>
            {
                context.Debug.PlannedAction = WARActions.Onslaught.Name;
                context.Debug.DamageState = "Onslaught (weave)";
                TrainingHelper.Decision(context.TrainingService)
                    .Action(WARActions.Onslaught.ActionId, WARActions.Onslaught.Name)
                    .AsTankDamage()
                    .Reason("Onslaught woven as extra damage.", "100 potency, no GCD cost.")
                    .Factors("In melee range", "Charge available")
                    .Alternatives("Hold charges")
                    .Tip("3 charges at Lv.88+.")
                    .Concept(WarConcepts.Onslaught)
                    .Record();
                context.TrainingService?.RecordConceptApplication(WarConcepts.Onslaught, true, "oGCD weave");
            });
    }

    #endregion

    #region Primal chain

    private void TryPushPrimalRend(IAresContext context, RotationScheduler scheduler, ulong targetId)
    {
        if (!context.Configuration.Tank.EnablePrimalRend) return;
        if (!context.Configuration.Tank.AutoPrimalRend) return;
        var level = context.Player.Level;
        if (level < WARActions.PrimalRend.MinLevel) return;
        // RSR: spend all Inner Release stacks on Fell Cleave before Primal Rend
        if (context.InnerReleaseStacks > 0) return;
        if (!context.HasPrimalRendReady) return;

        scheduler.PushGcd(AresAbilities.PrimalRend, targetId, priority: 4,
            onDispatched: _ =>
            {
                context.Debug.PlannedAction = WARActions.PrimalRend.Name;
                context.Debug.DamageState = "Primal Rend";
                TrainingHelper.Decision(context.TrainingService)
                    .Action(WARActions.PrimalRend.ActionId, WARActions.PrimalRend.Name)
                    .AsTankBurst()
                    .Reason("Primal Rend Ready active.", "700-potency ranged GCD, unlocks Primal Ruination.")
                    .Factors("Primal Rend Ready active", "Highest potency GCD")
                    .Alternatives("Use Fell Cleave (wastes proc)")
                    .Tip("Always use Primal Rend immediately when ready.")
                    .Concept(WarConcepts.PrimalRend)
                    .Record();
                context.TrainingService?.RecordConceptApplication(WarConcepts.PrimalRend, true, "Inner Release bonus GCD");
            });
    }

    private void TryPushPrimalRuination(IAresContext context, RotationScheduler scheduler, ulong targetId)
    {
        if (!context.Configuration.Tank.EnablePrimalRuination) return;
        var level = context.Player.Level;
        if (level < WARActions.PrimalRuination.MinLevel) return;
        if (context.InnerReleaseStacks > 0) return;
        if (!context.PrimalRuinationReady) return;

        scheduler.PushGcd(AresAbilities.PrimalRuination, targetId, priority: 4,
            onDispatched: _ =>
            {
                context.Debug.PlannedAction = WARActions.PrimalRuination.Name;
                context.Debug.DamageState = "Primal Ruination";
                TrainingHelper.Decision(context.TrainingService)
                    .Action(WARActions.PrimalRuination.ActionId, WARActions.PrimalRuination.Name)
                    .AsTankBurst()
                    .Reason("Primal Ruination - follow-up to Primal Rend.", "Completes Inner Release burst chain.")
                    .Factors("Primal Ruination Ready", "Burst chain")
                    .Alternatives("Use Fell Cleave (wastes proc)")
                    .Tip("Follows Primal Rend in every Inner Release window.")
                    .Concept(WarConcepts.PrimalRend)
                    .Record();
                context.TrainingService?.RecordConceptApplication(WarConcepts.PrimalRend, true, "Burst chain");
            });
    }

    #endregion

    #region Inner Chaos

    private void TryPushInnerChaos(IAresContext context, RotationScheduler scheduler, ulong targetId, int enemyCount)
    {
        var level = context.Player.Level;

        // AoE path
        if (context.Configuration.Tank.EnableAoEDamage &&
            enemyCount >= context.Configuration.Tank.GetEffectiveAoEMinTargets(JobRegistry.Warrior) &&
            level >= WARActions.ChaoticCyclone.MinLevel &&
            context.ChaoticCycloneReady)
        {
            scheduler.PushGcd(AresAbilities.ChaoticCyclone, targetId, priority: 2,
                onDispatched: _ =>
                {
                    context.Debug.PlannedAction = WARActions.ChaoticCyclone.Name;
                    context.Debug.DamageState = $"Chaotic Cyclone ({enemyCount} enemies)";
                    TrainingHelper.Decision(context.TrainingService)
                        .Action(WARActions.ChaoticCyclone.ActionId, WARActions.ChaoticCyclone.Name)
                        .AsTankBurst()
                        .Reason($"Chaotic Cyclone AoE ({enemyCount}).", "Free guaranteed crit AoE GCD.")
                        .Factors("Nascent Chaos active", $"{enemyCount} enemies")
                        .Alternatives("Inner Chaos (ST only)")
                        .Tip("In AoE, always Chaotic Cyclone over Inner Chaos.")
                        .Concept(WarConcepts.NascentChaos)
                        .Record();
                    context.TrainingService?.RecordConceptApplication(WarConcepts.NascentChaos, true, "Chaotic Cyclone AoE burst");
                });
        }

        // ST path — push alongside AoE candidate; scheduler picks first valid
        if (level >= WARActions.InnerChaos.MinLevel && context.InnerChaosReady)
        {
            scheduler.PushGcd(AresAbilities.InnerChaos, targetId, priority: 2,
            onDispatched: _ =>
            {
                context.Debug.PlannedAction = WARActions.InnerChaos.Name;
                context.Debug.DamageState = "Inner Chaos";
                TrainingHelper.Decision(context.TrainingService)
                    .Action(WARActions.InnerChaos.ActionId, WARActions.InnerChaos.Name)
                    .AsTankBurst()
                    .Reason("Inner Chaos on Nascent Chaos.", "660-potency free guaranteed crit + direct hit.")
                    .Factors("Nascent Chaos active", "Single target")
                    .Alternatives("Fell Cleave (wastes proc)")
                    .Tip("Always use Inner Chaos when Nascent Chaos is active.")
                    .Concept(WarConcepts.InnerChaos)
                    .Record();
                context.TrainingService?.RecordConceptApplication(WarConcepts.InnerChaos, true, "Nascent Chaos burst GCD");
            });
        }
    }

    #endregion

    #region Gauge spenders

    /// <summary>
    /// Surging Tempest guard (RSR parity): outside Inner Release, don't dump Beast Gauge if Surging
    /// Tempest is down or about to fall off — the gauge spender (p3) outranks the Storm's Eye combo
    /// finisher (p6), so without this the rotation can spend forever and let the +damage buff lapse.
    /// Blocking the spender lets the combo run and refresh Surging Tempest via Storm's Eye.
    /// </summary>
    private static bool ShouldRefreshSurgingTempestFirst(IAresContext context)
    {
        if (context.HasInnerRelease) return false; // inside IR we spend stacks; ST was refreshed pre-burst
        if (context.Player.Level < WARActions.StormsEye.MinLevel) return false;
        return !context.HasSurgingTempest
               || BaseStatusHelper.WillStatusEndInGcds(
                      context.SurgingTempestRemaining, 3, context.ActionService.GcdDuration);
    }

    private void TryPushGaugeSpender(IAresContext context, RotationScheduler scheduler, ulong targetId, int enemyCount)
    {
        bool atCap = context.BeastGauge >= context.Configuration.Tank.BeastGaugeCap;
        bool canSpend = (context.HasInnerRelease && context.InnerReleaseStacks > 0 && !context.HasNascentChaos)
                        || context.BeastGauge >= 50
                        || atCap;
        if (!canSpend) return;

        // Refresh Surging Tempest before dumping gauge when it's about to drop (filler only).
        if (ShouldRefreshSurgingTempestFirst(context)) return;

        var level = context.Player.Level;

        // AoE: Decimate
        if (context.Configuration.Tank.EnableAoEDamage &&
            enemyCount >= context.Configuration.Tank.GetEffectiveAoEMinTargets(JobRegistry.Warrior))
        {
            var decimateAction = WARActions.GetDecimateAction(level, context.ActionService);
            if (level < WARActions.SteelCyclone.MinLevel) return;

            var irStacks = context.InnerReleaseStacks;
            var gauge = context.BeastGauge;
            var hasIR = context.HasInnerRelease;
            var behavior = level >= WARActions.Decimate.MinLevel ? AresAbilities.Decimate : AresAbilities.SteelCyclone;

            scheduler.PushGcd(behavior, targetId, priority: 3,
                onDispatched: _ =>
                {
                    context.Debug.PlannedAction = decimateAction.Name;
                    context.Debug.DamageState = hasIR
                        ? $"{decimateAction.Name} ({enemyCount} enemies, IR)"
                        : $"{decimateAction.Name} ({enemyCount} enemies)";
                    if (hasIR)
                    {
                        TrainingHelper.Decision(context.TrainingService)
                            .Action(decimateAction.ActionId, decimateAction.Name)
                            .AsTankBurst()
                            .Reason($"{decimateAction.Name} during Inner Release ({enemyCount}).", "Free guaranteed crit AoE.")
                            .Factors($"IR stacks: {irStacks}", $"{enemyCount} enemies")
                            .Alternatives("Fell Cleave (ST only)")
                            .Tip("In AoE IR, always Decimate.")
                            .Concept(WarConcepts.IRWindow)
                            .Record();
                        context.TrainingService?.RecordConceptApplication(WarConcepts.IRWindow, true, "AoE IR burst");
                    }
                    else
                    {
                        TrainingHelper.Decision(context.TrainingService)
                            .Action(decimateAction.ActionId, decimateAction.Name)
                            .AsTankResource(gauge)
                            .Reason($"{decimateAction.Name} AoE gauge spend ({enemyCount}).", "50 gauge AoE damage.")
                            .Factors($"Gauge: {gauge}", $"{enemyCount} enemies")
                            .Alternatives("Fell Cleave (ST, wastes AoE opportunity)")
                            .Tip("With 3+ enemies, Decimate over Fell Cleave.")
                            .Concept(WarConcepts.BeastGauge)
                            .Record();
                        context.TrainingService?.RecordConceptApplication(WarConcepts.BeastGauge, true, "AoE gauge spending");
                    }
                });
            return;
        }

        // ST: Fell Cleave (or Inner Beast pre-54)
        var fellCleaveAction = WARActions.GetFellCleaveAction(level, context.ActionService);
        if (level < WARActions.FellCleave.MinLevel)
        {
            if (level < WARActions.InnerBeast.MinLevel) return;
            fellCleaveAction = WARActions.InnerBeast;
        }

        var irStacksB = context.InnerReleaseStacks;
        var gaugeB = context.BeastGauge;
        var hasIRB = context.HasInnerRelease;
        var behaviorB = level >= WARActions.FellCleave.MinLevel ? AresAbilities.FellCleave : AresAbilities.InnerBeast;

        scheduler.PushGcd(behaviorB, targetId, priority: 3,
            onDispatched: _ =>
            {
                context.Debug.PlannedAction = fellCleaveAction.Name;
                context.Debug.DamageState = hasIRB
                    ? $"{fellCleaveAction.Name} (IR: {irStacksB} stacks)"
                    : $"{fellCleaveAction.Name} ({gaugeB} gauge)";
                if (hasIRB)
                {
                    TrainingHelper.Decision(context.TrainingService)
                        .Action(fellCleaveAction.ActionId, fellCleaveAction.Name)
                        .AsTankBurst()
                        .Reason($"Fell Cleave during IR ({irStacksB} stacks).", "Free guaranteed crit + direct hit.")
                        .Factors($"IR stacks: {irStacksB}")
                        .Alternatives("Other GCDs (wastes IR stacks)")
                        .Tip("Spam Fell Cleave during IR.")
                        .Concept("war_inner_release")
                        .Record();
                    context.TrainingService?.RecordConceptApplication("war_inner_release", true, "Burst window");
                }
                else
                {
                    TrainingHelper.Decision(context.TrainingService)
                        .Action(fellCleaveAction.ActionId, fellCleaveAction.Name)
                        .AsTankResource(gaugeB)
                        .Reason($"Fell Cleave gauge spend ({gaugeB}).", "50 gauge ST damage.")
                        .Factors($"Gauge: {gaugeB}")
                        .Alternatives("Hold for IR (may overcap)")
                        .Tip("Don't overcap gauge.")
                        .Concept("war_infuriate_gauge")
                        .Record();
                    context.TrainingService?.RecordConceptApplication("war_infuriate_gauge", true, "Gauge spending");
                }
            });
    }

    #endregion

    #region Basic combo

    private void TryPushCombo(IAresContext context, RotationScheduler scheduler, ulong targetId, int enemyCount)
    {
        var level = context.Player.Level;
        var useAoE = context.Configuration.Tank.EnableAoEDamage &&
                     enemyCount >= context.Configuration.Tank.GetEffectiveAoEMinTargets(JobRegistry.Warrior);

        if (useAoE)
        {
            TryPushAoeCombo(context, scheduler, targetId, enemyCount);
        }
        else
        {
            TryPushSingleTargetCombo(context, scheduler, targetId);
        }
    }

    private void TryPushSingleTargetCombo(IAresContext context, RotationScheduler scheduler, ulong targetId)
    {
        var level = context.Player.Level;
        var skipFinisher = context.HasInnerRelease && context.InnerReleaseStacks > 0;

        // Step 3: finisher at p6 — no early return; Heavy Swing at p7 is ActionStatus fallback (PLD parity)
        if (!skipFinisher &&
            context.ComboStep == 2 &&
            context.LastComboAction == WARActions.Maim.ActionId)
        {
            bool needsSurgingTempest = !context.HasSurgingTempest || context.SurgingTempestRemaining < 10f;
            var finisher = WARActions.GetComboFinisher(level, needsSurgingTempest, context.ActionService);
            // Below Storm's Path (Lv26) the resolver falls back to Maim — there IS no step 3;
            // pushing the finisher behavior then dispatches an unlearned id every chain (Heavy
            // Swing caught it, but the candidate was pure rejection noise).
            var hasRealFinisher = finisher.ActionId != WARActions.Maim.ActionId;
            if (hasRealFinisher && level >= finisher.MinLevel)
            {
                var isStormsEye = finisher.ActionId == WARActions.StormsEye.ActionId;
                var behavior = isStormsEye ? AresAbilities.StormsEye : AresAbilities.StormsPath;
                var stempRem = context.SurgingTempestRemaining;

                scheduler.PushGcd(behavior, targetId, priority: 6,
                    onDispatched: _ =>
                    {
                        context.Debug.PlannedAction = finisher.Name;
                        context.Debug.DamageState = $"{finisher.Name} (combo 3)";
                        TrainingHelper.Decision(context.TrainingService)
                            .Action(finisher.ActionId, finisher.Name)
                            .AsTankDamage()
                            .Reason(isStormsEye
                                ? "Storm's Eye refresh Surging Tempest."
                                : "Storm's Path gauge generation.",
                                isStormsEye
                                    ? "Storm's Eye - 10 gauge + Surging Tempest refresh."
                                    : "Storm's Path - 20 gauge.")
                            .Factors("Combo 3", isStormsEye ? $"ST: {stempRem:F1}s (needs refresh)" : $"ST OK ({stempRem:F1}s)")
                            .Alternatives(isStormsEye ? "Storm's Path" : "Storm's Eye")
                            .Tip("Alternate based on Surging Tempest remaining.")
                            .Concept(WarConcepts.SurgingTempest)
                            .Record();
                        context.TrainingService?.RecordConceptApplication(WarConcepts.SurgingTempest, true, "Combo finisher");
                    });
            }
        }

        // Step 2: Maim at p6
        if (context.ComboStep == 1 &&
            context.LastComboAction == WARActions.HeavySwing.ActionId &&
            level >= WARActions.Maim.MinLevel)
        {
            scheduler.PushGcd(AresAbilities.Maim, targetId, priority: 6,
                onDispatched: _ =>
                {
                    context.Debug.PlannedAction = WARActions.Maim.Name;
                    context.Debug.DamageState = "Maim (combo 2)";
                    TrainingHelper.Decision(context.TrainingService)
                        .Action(WARActions.Maim.ActionId, WARActions.Maim.Name)
                        .AsTankDamage()
                        .Reason("Maim combo step 2.", "Grants 10 Beast Gauge.")
                        .Factors("Combo 2")
                        .Alternatives("Break combo (wastes progress)")
                        .Tip("Never break combo chain.")
                        .Concept(WarConcepts.BeastGauge)
                        .Record();
                    context.TrainingService?.RecordConceptApplication(WarConcepts.BeastGauge, true, "Combo gauge");
                });
        }

        // Step 1 / restart / fallback: Heavy Swing at p7
        if (level >= WARActions.HeavySwing.MinLevel)
        {
            scheduler.PushGcd(AresAbilities.HeavySwing, targetId, priority: 7,
                onDispatched: _ =>
                {
                    context.Debug.PlannedAction = WARActions.HeavySwing.Name;
                    context.Debug.DamageState = "Heavy Swing (combo 1)";
                    TrainingHelper.Decision(context.TrainingService)
                        .Action(WARActions.HeavySwing.ActionId, WARActions.HeavySwing.Name)
                        .AsTankDamage()
                        .Reason("Heavy Swing starter.", "Starts ST combo.")
                        .Factors("Combo 1")
                        .Alternatives("Inner Chaos (Nascent Chaos only)", "Fell Cleave (50 gauge)")
                        .Tip("WAR combo flow: HS → Maim → finisher.")
                        .Concept(WarConcepts.BeastGauge)
                        .Record();
                    context.TrainingService?.RecordConceptApplication(WarConcepts.BeastGauge, true, "Combo start");
                });
        }
    }

    private void TryPushAoeCombo(IAresContext context, RotationScheduler scheduler, ulong targetId, int enemyCount)
    {
        var level = context.Player.Level;
        var skipFinisher = context.HasInnerRelease && context.InnerReleaseStacks > 0;

        // Step 2: Mythril Tempest at p6 — no early return; Overpower at p7 is fallback (PLD parity)
        if (!skipFinisher &&
            context.ComboStep == 1 &&
            context.LastComboAction == WARActions.Overpower.ActionId &&
            Daedalus.Services.Action.ActionAvailability.MeetsLevelAndLearned(
                (byte)level, context.ActionService, WARActions.MythrilTempest))
        {
            scheduler.PushGcd(AresAbilities.MythrilTempest, targetId, priority: 6,
                onDispatched: _ =>
                {
                    context.Debug.PlannedAction = WARActions.MythrilTempest.Name;
                    context.Debug.DamageState = $"Mythril Tempest ({enemyCount}, combo 2)";
                    TrainingHelper.Decision(context.TrainingService)
                        .Action(WARActions.MythrilTempest.ActionId, WARActions.MythrilTempest.Name)
                        .AsTankDamage()
                        .Reason($"Mythril Tempest AoE combo ({enemyCount}).", "20 Beast Gauge + Surging Tempest.")
                        .Factors($"{enemyCount} enemies", "Combo 2")
                        .Alternatives("ST combo")
                        .Tip("Complete AoE combo in pulls.")
                        .Concept(WarConcepts.BeastGauge)
                        .Record();
                    context.TrainingService?.RecordConceptApplication(WarConcepts.BeastGauge, true, "AoE combo gauge");
                });
        }

        // Step 1 / starter / fallback: Overpower at p7
        if (level < WARActions.Overpower.MinLevel)
        {
            TryPushSingleTargetCombo(context, scheduler, targetId);
            return;
        }

        scheduler.PushGcd(AresAbilities.Overpower, targetId, priority: 7,
            onDispatched: _ =>
            {
                context.Debug.PlannedAction = WARActions.Overpower.Name;
                context.Debug.DamageState = $"Overpower ({enemyCount}, combo 1)";
                TrainingHelper.Decision(context.TrainingService)
                    .Action(WARActions.Overpower.ActionId, WARActions.Overpower.Name)
                    .AsTankDamage()
                    .Reason($"Overpower AoE combo starter ({enemyCount}).", "AoE cone, sets up Mythril Tempest.")
                    .Factors($"{enemyCount} enemies", "Combo 1")
                    .Alternatives("Heavy Swing (single target)")
                    .Tip("With enough enemies, Overpower starts AoE chain.")
                    .Concept(WarConcepts.BeastGauge)
                    .Record();
                context.TrainingService?.RecordConceptApplication(WarConcepts.BeastGauge, true, "AoE combo start");
            });
    }

    #endregion
}
