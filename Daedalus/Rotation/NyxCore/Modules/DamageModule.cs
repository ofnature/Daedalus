using Daedalus.Data;
using Daedalus.Rotation.ApolloCore.Helpers;
using Daedalus.Rotation.Common.Helpers;
using Daedalus.Rotation.Common.Scheduling;
using Daedalus.Rotation.NyxCore.Abilities;
using Daedalus.Rotation.NyxCore.Context;
using Daedalus.Services.Training;

namespace Daedalus.Rotation.NyxCore.Modules;

/// <summary>
/// Handles the Dark Knight damage rotation (scheduler-driven).
/// </summary>
public sealed class DamageModule : INyxModule
{
    public int Priority => 30;
    public string Name => "Damage";

    public bool TryExecute(INyxContext context, bool isMoving) => false;
    public void UpdateDebugState(INyxContext context) { }

    public void CollectCandidates(INyxContext context, RotationScheduler scheduler, bool isMoving)
    {
        if (!context.Configuration.Tank.EnableDamage) { context.Debug.DamageState = "Disabled"; return; }
        if (!context.InCombat) { context.Debug.DamageState = "Not in combat"; return; }
        if (context.TargetingService.IsDamageTargetingPaused()) { context.Debug.DamageState = "Paused (no target)"; return; }

        var player = context.Player;
        var enemyStrategy = TankTargetingHelper.ResolveEnemyStrategy(
            context.Configuration.Tank,
            context.Configuration.Targeting.EnemyStrategy,
            context.PartyHelper?.FindCoTank(player) != null);

        var target = context.TargetingService.FindEnemyForAction(
            enemyStrategy, DRKActions.HardSlash.ActionId, player);
        var engageTarget = target ?? context.TargetingService.FindEnemy(
            enemyStrategy, 20f, player);

        if (engageTarget == null) { context.Debug.DamageState = "No target"; return; }

        // Out-of-melee
        if (target == null)
        {
            // Ranged-pull: when enabled, stay put and pull with Unmend instead of dashing in.
            // Lost-mob: when the mob slipped to another player, don't dash after it — Provoke (25y,
            // auto-fired by EnmityModule) + Unmend reclaim it in place.
            var lostToOther = context.Configuration.Tank.SuppressGapCloserOnLostMob
                              && context.EnmityService?.HasLostAggroToOther(engageTarget, player.EntityId) == true;
            if (!context.Configuration.Tank.PullRangedMobsWithRangedAttack && !lostToOther)
                TryPushShadowstrideGapClose(context, scheduler, engageTarget.GameObjectId, engageTarget);
            TryPushUnmend(context, scheduler, engageTarget.GameObjectId);
            return;
        }

        var pack = EnemyPackDebugHelper.Count(context.TargetingService, 5f, player);
        EnemyPackDebugHelper.Apply(context.Debug, pack);
        var enemyCount = pack.AoeRange;
        TryPushDarkArtsProc(context, scheduler, target.GameObjectId, enemyCount);
        TryPushShadowbringer(context, scheduler, target.GameObjectId);
        TryPushSaltedEarth(context, scheduler, player.GameObjectId);
        TryPushSaltAndDarkness(context, scheduler, player.GameObjectId);
        TryPushDarksideMaintenance(context, scheduler, target.GameObjectId, enemyCount);
        TryPushCarveAndSpit(context, scheduler, target.GameObjectId);
        TryPushAbyssalDrain(context, scheduler, target.GameObjectId, enemyCount);
        TryPushShadowstrideWeave(context, scheduler, target.GameObjectId, target);

        // GCDs
        TryPushDeliriumCombo(context, scheduler, target.GameObjectId, enemyCount);
        TryPushDisesteem(context, scheduler, target.GameObjectId);
        TryPushBloodSpender(context, scheduler, target.GameObjectId, enemyCount);
        TryPushMovingAddTag(context, scheduler, isMoving);
        TryPushCombo(context, scheduler, target.GameObjectId, enemyCount);
    }

    /// <summary>
    /// Wall-to-wall add tag: while moving in a duty, ranged-pull (Unmend) the nearest mob within 25y that
    /// isn't on us yet — including idle packs we're walking toward — so the pull gathers. Only fires while
    /// moving, in an instanced duty, and between combos (never breaks one).
    /// </summary>
    private void TryPushMovingAddTag(INyxContext context, RotationScheduler scheduler, bool isMoving)
    {
        if (!isMoving) return;
        if (!context.Configuration.Tank.TagAddsWhileMovingWithRangedAttack) return;
        if (!PlayerSafetyHelper.IsInInstancedDuty()) return;
        var player = context.Player;
        if (player.Level < DRKActions.Unmend.MinLevel) return;
        if (!context.ActionService.IsActionReady(DRKActions.Unmend.ActionId)) return;
        if (context.ComboTimeRemaining > 0f) return;

        var stray = context.TargetingService.FindNearestTaggableEnemy(25f, player);
        if (stray == null) return;

        scheduler.PushGcd(NyxAbilities.Unmend, stray.GameObjectId, priority: 6,
            onDispatched: _ =>
            {
                context.Debug.PlannedAction = DRKActions.Unmend.Name;
                context.Debug.DamageState = "Unmend (tag add — moving)";
            });
    }

    // --- Out-of-melee ---

    private void TryPushShadowstrideGapClose(INyxContext context, RotationScheduler scheduler, ulong targetId,
                                             Dalamud.Game.ClientState.Objects.Types.IBattleChara target)
    {
        if (!context.Configuration.Tank.EnableShadowstride) return;
        var player = context.Player;
        if (player.Level < DRKActions.Shadowstride.MinLevel) return;
        if (!context.ActionService.IsActionReady(DRKActions.Shadowstride.ActionId)) return;
        if (context.TargetingService.GapCloserSafety.ShouldBlockGapCloser(target, player))
        {
            context.Debug.DamageState = $"Shadowstride blocked: {context.TargetingService.GapCloserSafety.LastBlockReason}";
            return;
        }

        scheduler.PushOgcd(NyxAbilities.Shadowstride, targetId, priority: 4,
            onDispatched: _ =>
            {
                context.Debug.PlannedAction = DRKActions.Shadowstride.Name;
                context.Debug.DamageState = "Shadowstride (gap close)";
            });
    }

    private void TryPushUnmend(INyxContext context, RotationScheduler scheduler, ulong targetId)
    {
        var player = context.Player;
        if (player.Level < DRKActions.Unmend.MinLevel) return;
        if (!context.ActionService.IsActionReady(DRKActions.Unmend.ActionId)) return;

        scheduler.PushGcd(NyxAbilities.Unmend, targetId, priority: 10,
            onDispatched: _ =>
            {
                context.Debug.PlannedAction = DRKActions.Unmend.Name;
                context.Debug.DamageState = "Unmend (ranged)";
            });
    }

    // --- oGCDs ---

    private void TryPushDarkArtsProc(INyxContext context, RotationScheduler scheduler, ulong targetId, int enemyCount)
    {
        if (!context.HasDarkArts) return;
        var level = context.Player.Level;
        var action = context.Configuration.Tank.EnableAoEDamage && enemyCount >= context.Configuration.Tank.GetEffectiveAoEMinTargets(JobRegistry.DarkKnight)
            ? DRKActions.GetFloodAction(level, context.ActionService) : DRKActions.GetEdgeAction(level, context.ActionService);
        if (!context.ActionService.IsActionReady(action.ActionId)) return;

        var isAoe = context.Configuration.Tank.EnableAoEDamage && enemyCount >= context.Configuration.Tank.GetEffectiveAoEMinTargets(JobRegistry.DarkKnight);
        var behavior = isAoe ? NyxAbilities.FloodOfShadow : NyxAbilities.EdgeOfShadow;
        scheduler.PushOgcd(behavior, targetId, priority: 1,
            onDispatched: _ =>
            {
                context.Debug.PlannedAction = action.Name;
                context.Debug.DamageState = "Dark Arts proc!";
                TrainingHelper.Decision(context.TrainingService)
                    .Action(action.ActionId, action.Name).AsTankBurst()
                    .Reason("Dark Arts proc.", "Free Edge/Flood from TBN shield break.")
                    .Factors("Dark Arts active")
                    .Alternatives("Delay (expires)")
                    .Tip("Never let Dark Arts expire.")
                    .Concept(DrkConcepts.DarkArts).Record();
                context.TrainingService?.RecordConceptApplication(DrkConcepts.DarkArts, true);
            });
    }

    private void TryPushShadowbringer(INyxContext context, RotationScheduler scheduler, ulong targetId)
    {
        if (!context.Configuration.Tank.EnableShadowbringer) return;
        var player = context.Player;
        if (player.Level < DRKActions.Shadowbringer.MinLevel) return;
        if (!context.ActionService.IsActionReady(DRKActions.Shadowbringer.ActionId)) return;

        scheduler.PushOgcd(NyxAbilities.Shadowbringer, targetId, priority: 2,
            onDispatched: _ =>
            {
                context.Debug.PlannedAction = DRKActions.Shadowbringer.Name;
                context.Debug.DamageState = "Shadowbringer";
            });
    }

    private void TryPushSaltedEarth(INyxContext context, RotationScheduler scheduler, ulong selfId)
    {
        if (!context.Configuration.Tank.EnableSaltedEarth) return;
        var player = context.Player;
        if (player.Level < DRKActions.SaltedEarth.MinLevel) return;
        if (context.HasSaltedEarth) return;
        if (!context.ActionService.IsActionReady(DRKActions.SaltedEarth.ActionId)) return;

        scheduler.PushOgcd(NyxAbilities.SaltedEarth, selfId, priority: 3,
            onDispatched: _ =>
            {
                context.Debug.PlannedAction = DRKActions.SaltedEarth.Name;
                context.Debug.DamageState = "Salted Earth";
            });
    }

    private void TryPushSaltAndDarkness(INyxContext context, RotationScheduler scheduler, ulong selfId)
    {
        if (!context.Configuration.Tank.EnableSaltedEarth) return;
        var player = context.Player;
        if (player.Level < DRKActions.SaltAndDarkness.MinLevel) return;
        if (!context.HasSaltedEarth) return;
        if (!context.ActionService.IsActionReady(DRKActions.SaltAndDarkness.ActionId)) return;

        scheduler.PushOgcd(NyxAbilities.SaltAndDarkness, selfId, priority: 3,
            onDispatched: _ =>
            {
                context.Debug.PlannedAction = DRKActions.SaltAndDarkness.Name;
                context.Debug.DamageState = "Salt and Darkness";
            });
    }

    /// <summary>
    /// Edge/Flood (Darkside maintenance + MP spend) decision — RSR <c>CheckDarkSide</c> parity. Refreshes
    /// Darkside before it lapses (within ~3 GCDs), then spends MP above a floor that reserves 3000 for The
    /// Blackest Night (unless TBN is disabled). In the burst window (Delirium/Blood Weapon up) it spends
    /// down to that reserve to fit the extra Edges (5/2 plan); outside burst it only dumps near cap (8500)
    /// so natural MP regen isn't wasted. (Dark Arts free Edge is handled separately at priority 1.)
    /// </summary>
    internal static (bool spend, string reason) ResolveDarksideSpend(
        bool hasDarkside, float darksideRemaining, int currentMp, bool keepTbnReserve, bool inBurst, float gcdDuration)
    {
        if (!hasDarkside) return (true, "Darkside activate");
        if (darksideRemaining > 0f && BaseStatusHelper.WillStatusEndInGcds(darksideRemaining, 3, gcdDuration))
            return (true, $"Darkside refresh ({darksideRemaining:F1}s)");

        // 3000 to cast Edge/Flood, plus 3000 banked for TBN when the reserve is on.
        var reserve = keepTbnReserve ? 6000 : 3000;
        if (inBurst && currentMp >= reserve + 100) return (true, "MP dump (burst)");
        if (currentMp >= 8500 && currentMp >= reserve) return (true, "MP dump");
        return (false, string.Empty);
    }

    private void TryPushDarksideMaintenance(INyxContext context, RotationScheduler scheduler, ulong targetId, int enemyCount)
    {
        if (!context.HasEnoughMpForEdge)
        {
            context.Debug.DamageState = "Low MP, can't maintain Darkside";
            return;
        }

        var level = context.Player.Level;
        var isAoe = context.Configuration.Tank.EnableAoEDamage && enemyCount >= context.Configuration.Tank.GetEffectiveAoEMinTargets(JobRegistry.DarkKnight);
        var action = isAoe ? DRKActions.GetFloodAction(level, context.ActionService) : DRKActions.GetEdgeAction(level, context.ActionService);
        var behavior = isAoe ? NyxAbilities.FloodOfShadow : NyxAbilities.EdgeOfShadow;

        bool inBurst = context.HasDelirium || context.HasBloodWeapon;
        bool keepTbnReserve = context.Configuration.Tank.EnableTheBlackestNight;
        var (spend, reason) = ResolveDarksideSpend(context.HasDarkside, context.DarksideRemaining,
            context.CurrentMp, keepTbnReserve, inBurst, context.ActionService.GcdDuration);
        if (!spend) return;
        if (!context.ActionService.IsActionReady(action.ActionId)) return;

        var darksideRem = context.DarksideRemaining;
        var mp = context.CurrentMp;
        var noDarkside = !context.HasDarkside;

        scheduler.PushOgcd(behavior, targetId, priority: 5,
            onDispatched: _ =>
            {
                context.Debug.PlannedAction = action.Name;
                context.Debug.DamageState = reason;
                TrainingHelper.Decision(context.TrainingService)
                    .Action(action.ActionId, action.Name).AsTankResource(mp)
                    .Reason(reason, "Edge/Flood extends Darkside and spends 3000 MP.")
                    .Factors($"Darkside: {darksideRem:F1}s", $"MP: {mp}")
                    .Alternatives("Let Darkside fall")
                    .Tip("Keep Darkside active always.")
                    .Concept(noDarkside ? DrkConcepts.Darkside : DrkConcepts.DarksideMaintenance).Record();
                context.TrainingService?.RecordConceptApplication(DrkConcepts.DarksideMaintenance, true);
            });
    }

    private void TryPushCarveAndSpit(INyxContext context, RotationScheduler scheduler, ulong targetId)
    {
        if (!context.Configuration.Tank.EnableCarveAndSpit) return;
        var player = context.Player;
        if (player.Level < DRKActions.CarveAndSpit.MinLevel) return;
        if (!context.ActionService.IsActionReady(DRKActions.CarveAndSpit.ActionId)) return;

        scheduler.PushOgcd(NyxAbilities.CarveAndSpit, targetId, priority: 6,
            onDispatched: _ =>
            {
                context.Debug.PlannedAction = DRKActions.CarveAndSpit.Name;
                context.Debug.DamageState = "Carve and Spit";
            });
    }

    private void TryPushAbyssalDrain(INyxContext context, RotationScheduler scheduler, ulong targetId, int enemyCount)
    {
        if (!context.Configuration.Tank.EnableAbyssalDrain) return;
        var player = context.Player;
        if (player.Level < DRKActions.AbyssalDrain.MinLevel) return;
        if (enemyCount < 2) return;
        if (!context.ActionService.IsActionReady(DRKActions.AbyssalDrain.ActionId)) return;

        scheduler.PushOgcd(NyxAbilities.AbyssalDrain, targetId, priority: 6,
            onDispatched: _ =>
            {
                context.Debug.PlannedAction = DRKActions.AbyssalDrain.Name;
                context.Debug.DamageState = $"Abyssal Drain ({enemyCount} enemies)";
            });
    }

    private void TryPushShadowstrideWeave(INyxContext context, RotationScheduler scheduler, ulong targetId,
                                          Dalamud.Game.ClientState.Objects.Types.IBattleChara target)
    {
        if (!context.Configuration.Tank.EnableShadowstride) return;
        var player = context.Player;
        if (player.Level < DRKActions.Shadowstride.MinLevel) return;
        if (!context.ActionService.IsActionReady(DRKActions.Shadowstride.ActionId)) return;
        if (context.TargetingService.GapCloserSafety.ShouldBlockGapCloser(target, player)) return;

        scheduler.PushOgcd(NyxAbilities.Shadowstride, targetId, priority: 7,
            onDispatched: _ =>
            {
                context.Debug.PlannedAction = DRKActions.Shadowstride.Name;
                context.Debug.DamageState = "Shadowstride (weave)";
            });
    }

    // --- GCDs ---

    private void TryPushDeliriumCombo(INyxContext context, RotationScheduler scheduler, ulong targetId, int enemyCount)
    {
        var level = context.Player.Level;
        if (level < DRKActions.ScarletDelirium.MinLevel) return;
        if (!context.HasDelirium) return;

        // AoE: Impalement (replaces Quietus during Delirium). Button-replacement, so it fires per stack.
        bool useAoE = context.Configuration.Tank.EnableAoEDamage &&
                      enemyCount >= context.Configuration.Tank.GetEffectiveAoEMinTargets(JobRegistry.DarkKnight) &&
                      level >= DRKActions.Impalement.MinLevel;

        if (useAoE)
        {
            scheduler.PushGcd(NyxAbilities.Impalement, targetId, priority: 2,
                onDispatched: _ =>
                {
                    context.Debug.PlannedAction = DRKActions.Impalement.Name;
                    context.Debug.DamageState = $"Impalement ({enemyCount} enemies)";
                });
            return;
        }

        // ST Delirium combo: Scarlet Delirium → Comeuppance → Torcleaver. Push all three; the
        // AdjustedActionProbe gate dispatches only the step the Bloodspiller slot currently shows, so the
        // chain advances one GCD at a time (Comeuppance/Torcleaver were previously never pushed — the
        // combo broke after step 1, losing the two highest-potency burst GCDs + the Scornful Edge proc).
        PushDeliriumStep(context, scheduler, NyxAbilities.Torcleaver, targetId, DRKActions.Torcleaver.Name);
        PushDeliriumStep(context, scheduler, NyxAbilities.Comeuppance, targetId, DRKActions.Comeuppance.Name);
        PushDeliriumStep(context, scheduler, NyxAbilities.ScarletDelirium, targetId, DRKActions.ScarletDelirium.Name);
    }

    private void PushDeliriumStep(INyxContext context, RotationScheduler scheduler,
                                  Daedalus.Rotation.Common.Scheduling.AbilityBehavior behavior, ulong targetId, string name)
    {
        scheduler.PushGcd(behavior, targetId, priority: 2,
            onDispatched: _ =>
            {
                context.Debug.PlannedAction = name;
                context.Debug.DamageState = $"{name} (Delirium combo)";
                TrainingHelper.Decision(context.TrainingService)
                    .Action(behavior.Action.ActionId, "Delirium Combo").AsTankBurst()
                    .Reason("Delirium combo during burst window.", "Scarlet Delirium → Comeuppance → Torcleaver.")
                    .Factors("Delirium active")
                    .Alternatives("Break combo (wastes burst)")
                    .Tip("Complete Delirium combo in order.")
                    .Concept(DrkConcepts.Delirium).Record();
                context.TrainingService?.RecordConceptApplication(DrkConcepts.Delirium, true);
            });
    }

    private void TryPushDisesteem(INyxContext context, RotationScheduler scheduler, ulong targetId)
    {
        var level = context.Player.Level;
        if (level < DRKActions.Disesteem.MinLevel) return;
        if (!context.HasScornfulEdge) return;
        if (!context.ActionService.IsActionReady(DRKActions.Disesteem.ActionId)) return;

        scheduler.PushGcd(NyxAbilities.Disesteem, targetId, priority: 2,
            onDispatched: _ =>
            {
                context.Debug.PlannedAction = DRKActions.Disesteem.Name;
                context.Debug.DamageState = "Disesteem";
            });
    }

    private void TryPushBloodSpender(INyxContext context, RotationScheduler scheduler, ulong targetId, int enemyCount)
    {
        var level = context.Player.Level;

        // Lv.96+ prefers Scarlet Delirium combo during Delirium — skip Blood spender there
        if (context.HasDelirium && level >= DRKActions.ScarletDelirium.MinLevel) return;

        // Pre-Lv.96 Delirium grants free Bloodspiller
        bool free = context.HasDelirium && level >= DRKActions.Bloodspiller.MinLevel && level < DRKActions.ScarletDelirium.MinLevel;
        bool atCap = context.BloodGauge >= context.Configuration.Tank.BloodGaugeCap;
        if (!free && context.BloodGauge < 50 && !atCap) return;

        // AoE: Quietus
        bool useAoE = context.Configuration.Tank.EnableAoEDamage &&
                      enemyCount >= context.Configuration.Tank.GetEffectiveAoEMinTargets(JobRegistry.DarkKnight) &&
                      level >= DRKActions.Quietus.MinLevel;

        if (useAoE && context.ActionService.IsActionReady(DRKActions.Quietus.ActionId))
        {
            var gauge = context.BloodGauge;
            scheduler.PushGcd(NyxAbilities.Quietus, targetId, priority: 3,
                onDispatched: _ =>
                {
                    context.Debug.PlannedAction = DRKActions.Quietus.Name;
                    context.Debug.DamageState = $"Quietus ({enemyCount}, {gauge} Blood)";
                });
            return;
        }

        if (level < DRKActions.Bloodspiller.MinLevel) return;
        if (!context.ActionService.IsActionReady(DRKActions.Bloodspiller.ActionId)) return;

        var g = context.BloodGauge;
        var isFree = free;
        scheduler.PushGcd(NyxAbilities.Bloodspiller, targetId, priority: 3,
            onDispatched: _ =>
            {
                context.Debug.PlannedAction = DRKActions.Bloodspiller.Name;
                context.Debug.DamageState = isFree ? $"Bloodspiller (free, Delirium)" : $"Bloodspiller ({g} Blood)";
            });
    }

    private void TryPushCombo(INyxContext context, RotationScheduler scheduler, ulong targetId, int enemyCount)
    {
        var level = context.Player.Level;
        bool useAoE = context.Configuration.Tank.EnableAoEDamage &&
                      enemyCount >= context.Configuration.Tank.GetEffectiveAoEMinTargets(JobRegistry.DarkKnight) &&
                      level >= DRKActions.Unleash.MinLevel;

        if (useAoE)
        {
            TryPushAoECombo(context, scheduler, targetId, enemyCount);
        }
        else
        {
            TryPushSingleTargetCombo(context, scheduler, targetId);
        }
    }

    private void TryPushSingleTargetCombo(INyxContext context, RotationScheduler scheduler, ulong targetId)
    {
        var level = context.Player.Level;

        // Step 2: Souleater
        if (context.ComboStep == 2 && level >= DRKActions.Souleater.MinLevel)
        {
            if (!context.ActionService.IsActionReady(DRKActions.Souleater.ActionId)) return;
            scheduler.PushGcd(NyxAbilities.Souleater, targetId, priority: 7,
                onDispatched: _ =>
                {
                    context.Debug.PlannedAction = DRKActions.Souleater.Name;
                    context.Debug.DamageState = "Souleater (combo)";
                });
            return;
        }

        // Step 1: Syphon Strike — requires LastComboAction == HardSlash (ambiguous with AoE step 1 from Unleash)
        if (context.ComboStep == 1 &&
            context.LastComboAction == DRKActions.HardSlash.ActionId &&
            level >= DRKActions.SyphonStrike.MinLevel)
        {
            if (!context.ActionService.IsActionReady(DRKActions.SyphonStrike.ActionId)) return;
            scheduler.PushGcd(NyxAbilities.SyphonStrike, targetId, priority: 7,
                onDispatched: _ =>
                {
                    context.Debug.PlannedAction = DRKActions.SyphonStrike.Name;
                    context.Debug.DamageState = "Syphon Strike (combo)";
                });
            return;
        }

        // Starter: Hard Slash
        if (!context.ActionService.IsActionReady(DRKActions.HardSlash.ActionId)) return;
        scheduler.PushGcd(NyxAbilities.HardSlash, targetId, priority: 7,
            onDispatched: _ =>
            {
                context.Debug.PlannedAction = DRKActions.HardSlash.Name;
                context.Debug.DamageState = "Hard Slash (start)";
            });
    }

    private void TryPushAoECombo(INyxContext context, RotationScheduler scheduler, ulong targetId, int enemyCount)
    {
        var level = context.Player.Level;

        // Step 1: Stalwart Soul — requires LastComboAction == Unleash (ambiguous with ST step 1 from Hard Slash)
        if (context.ComboStep == 1 &&
            context.LastComboAction == DRKActions.Unleash.ActionId &&
            level >= DRKActions.StalwartSoul.MinLevel)
        {
            if (!context.ActionService.IsActionReady(DRKActions.StalwartSoul.ActionId)) return;
            scheduler.PushGcd(NyxAbilities.StalwartSoul, targetId, priority: 7,
                onDispatched: _ =>
                {
                    context.Debug.PlannedAction = DRKActions.StalwartSoul.Name;
                    context.Debug.DamageState = $"Stalwart Soul ({enemyCount} enemies)";
                });
            return;
        }

        // Starter: Unleash
        if (!context.ActionService.IsActionReady(DRKActions.Unleash.ActionId)) return;
        scheduler.PushGcd(NyxAbilities.Unleash, targetId, priority: 7,
            onDispatched: _ =>
            {
                context.Debug.PlannedAction = DRKActions.Unleash.Name;
                context.Debug.DamageState = $"Unleash ({enemyCount} enemies)";
            });
    }
}
