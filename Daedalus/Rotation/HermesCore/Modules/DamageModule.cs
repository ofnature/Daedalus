using Dalamud.Game.ClientState.Objects.Types;
using Daedalus.Data;
using Daedalus.Models.Action;
using Daedalus.Rotation.ApolloCore.Helpers;
using Daedalus.Rotation.Common.Helpers;
using Daedalus.Rotation.Common.RoleActionHelpers;
using Daedalus.Rotation.Common.Scheduling;
using Daedalus.Rotation.HermesCore.Abilities;
using Daedalus.Rotation.HermesCore.Context;
using Daedalus.Rotation.HermesCore.Helpers;
using Daedalus.Services;
using Daedalus.Services.Targeting;
using Daedalus.Services.Training;

namespace Daedalus.Rotation.HermesCore.Modules;

/// <summary>
/// Handles the Ninja damage rotation (scheduler-driven).
/// Combo GCDs, Ninki spenders, Raiju, Phantom Kamaitachi.
/// Mudras and Ninjutsu execution live in NinjutsuModule (raw ActionManager).
/// </summary>
public sealed class DamageModule : IHermesModule
{
    public int Priority => 30;
    public string Name => "Damage";

    private const int NinkiForceDumpThreshold = 100;

    private readonly IBurstWindowService? _burstWindowService;
    private readonly ISmartAoEService? _smartAoEService;
    private readonly IHermesNinjutsuExecutor _executor;

    public DamageModule(
        IBurstWindowService? burstWindowService = null,
        ISmartAoEService? smartAoEService = null,
        IHermesNinjutsuExecutor? executor = null)
    {
        _burstWindowService = burstWindowService;
        _smartAoEService = smartAoEService;
        _executor = executor ?? new HermesNinjutsuExecutor();
    }

    private bool ShouldHoldForBurst(float thresholdSeconds = 8f) =>
        BurstHoldHelper.ShouldHoldForBurst(_burstWindowService, thresholdSeconds);

    public bool TryExecute(IHermesContext context, bool isMoving) => false;

    public void UpdateDebugState(IHermesContext context) { }

    public void CollectCandidates(IHermesContext context, RotationScheduler scheduler, bool isMoving)
    {
        context.Debug.DamageState = "";

        if (!context.InCombat)
        {
            context.Debug.DamageState = "Not in combat";
            return;
        }
        if (context.Configuration.Targeting.SuppressDamageOnForcedMovement
            && PlayerSafetyHelper.IsForcedMovementActive(context.Player))
        {
            context.Debug.DamageState = "Paused (forced movement)";
            return;
        }

        var player = context.Player;
        var target = context.TargetingService.FindEnemyForAction(
            context.Configuration.Targeting.EnemyStrategy,
            NINActions.SpinningEdge.ActionId,
            player);
        if (target == null)
        {
            // Automation engage beyond melee (melee audit 2026-07-18, the MNK Henchman lesson):
            // a PASSIVE hard-targeted mark is invisible to the engaged-enemy scans, so this
            // branch pushed nothing and the driver waited forever. Keep the combo starter
            // QUEUED at the override's hard target — the dispatch range-gate holds it until
            // the driver walks us into reach, then the opener fires. Manual play unchanged.
            if (context.Configuration.ExternalCombatOverride
                && context.TargetingService.GetUserEnemyTarget() is { IsDead: false } engageTarget)
            {
                scheduler.PushGcd(HermesAbilities.SpinningEdge, engageTarget.GameObjectId, priority: 6,
                    onDispatched: _ =>
                    {
                        context.Debug.PlannedAction = NINActions.SpinningEdge.Name;
                        context.Debug.DamageState = "Opening on hard target (automation)";
                    });
                context.Debug.DamageState = "Automation engage — closing to melee";
                return;
            }
            context.Debug.DamageState = "No target";
            return;
        }

        var aoeEnabled = context.Configuration.Ninja.EnableAoERotation;
        var aoeThreshold = context.Configuration.Ninja.AoEMinTargets;
        var pack = EnemyPackDebugHelper.Count(context.TargetingService, 5f, player);
        EnemyPackDebugHelper.Apply(context.Debug, pack);
        var enemyCount = aoeEnabled ? pack.AoeRange : 0;

        // Role oGCDs are safe during mudra — RSR blocks them at base layer, but Feint/Second Wind
        // live in DamageModule and must not interrupt hand-sign sequences (BuffModule owns burst oGCDs).
        TryPushFeint(context, scheduler, target);
        TryPushSecondWind(context, scheduler);
        TryPushBloodbath(context, scheduler);

        context.Debug.IsTcjStepPending = _executor.IsTcjStepPending;
        if (_executor.IsTcjStepPending)
        {
            context.Debug.DamageState = "Paused (TCJ step pending)";
            return;
        }

        // RSR 496 gate + reserve GCD when mudra/ninjutsu step is ready (NinjutsuModule runs first).
        // Allow combo filler only while Ten/mudra is on CD during a queued sequence (ABB).
        if (HermesMudraGate.ShouldBlockComboGcds(context))
        {
            context.Debug.DamageState = "Stalled (mudra status)";
            return;
        }

        if (HermesBurstPrepHelper.ShouldHoldComboGcds(context, context.MudraHelper, _burstWindowService))
        {
            context.Debug.DamageState = "Burst prep — holding GCD for Kunai's Bane";
            return;
        }

        TryPushNinkiSpender(context, scheduler, target, enemyCount);

        // GCDs
        TryPushRaiju(context, scheduler, target);
        TryPushPhantomKamaitachi(context, scheduler, target);
        TryPushComboRotation(context, scheduler, target, enemyCount);
    }

    #region Ninki spender

    private void TryPushNinkiSpender(IHermesContext context, RotationScheduler scheduler, IBattleChara target, int enemyCount)
    {
        var player = context.Player;
        var level = player.Level;
        var ninkiMinGauge = context.Configuration.Ninja.NinkiMinGauge;
        var ninkiOvercapThreshold = context.Configuration.Ninja.NinkiOvercapThreshold;

        if (context.Ninki < ninkiMinGauge) return;
        if (context.Configuration.Ninja.EnableBurstPooling
            && context.Configuration.Ninja.SaveNinkiForBurst
            && ShouldHoldForBurst(8f)
            && context.Ninki < ninkiOvercapThreshold)
            return;

        // RSR: (!InMug || InTrickAttack) — skip when not pooling so Ninki is spent on CD.
        // Force dump at 100 Ninki regardless to prevent overcap.
        if (context.Ninki < NinkiForceDumpThreshold
            && context.InMug && !context.InTrickAttack
            && HermesBurnHelper.ShouldPoolForRaidBurst(context))
            return;

        var aoeThreshold = context.Configuration.Ninja.AoEMinTargets;

        if (enemyCount >= aoeThreshold && level >= NINActions.HellfrogMedium.MinLevel)
        {
            if (!context.Configuration.Ninja.EnableHellfrogMedium) return;
            var aoeAction = NINActions.GetAoeNinkiSpender((byte)level, context.HasMeisui, context.ActionService);
            if (!context.ActionService.IsActionReady(aoeAction.ActionId)) return;
            var ability = aoeAction == NINActions.DeathfrogMedium ? HermesAbilities.DeathfrogMedium : HermesAbilities.HellfrogMedium;

            scheduler.PushOgcd(ability, target.GameObjectId, priority: 1,
                onDispatched: _ =>
                {
                    context.Debug.PlannedAction = aoeAction.Name;
                    context.Debug.DamageState = $"{aoeAction.Name} ({enemyCount} enemies)";
                    TrainingHelper.Decision(context.TrainingService)
                        .Action(aoeAction.ActionId, aoeAction.Name)
                        .AsAoE(enemyCount).Target($"{enemyCount} enemies")
                        .Reason($"Spending 50 Ninki on {aoeAction.Name}",
                            $"{aoeAction.Name} is the AoE Ninki spender.")
                        .Factors($"Ninki >= {ninkiMinGauge}", $"{enemyCount} enemies")
                        .Alternatives("Use Bhavacakra")
                        .Tip("In AoE, prefer Hellfrog Medium at 3+ targets.")
                        .Concept(NinConcepts.NinkiGauge)
                        .Record();
                    context.TrainingService?.RecordConceptApplication(NinConcepts.NinkiGauge, true, "AoE Ninki spending");
                });
            return;
        }

        if (level >= NINActions.HellfrogMedium.MinLevel)
        {
            if (!context.Configuration.Ninja.EnableBhavacakra) return;
            var stAction = NINActions.GetNinkiSpender((byte)level, context.HasMeisui, context.ActionService);
            if (!context.ActionService.IsActionReady(stAction.ActionId)) return;
            var ability = stAction == NINActions.ZeshoMeppo ? HermesAbilities.ZeshoMeppo : HermesAbilities.Bhavacakra;

            scheduler.PushOgcd(ability, target.GameObjectId, priority: 1,
                onDispatched: _ =>
                {
                    context.Debug.PlannedAction = stAction.Name;
                    context.Debug.DamageState = stAction.Name;
                    var meisuiNote = context.HasMeisui ? " (enhanced by Meisui)" : "";
                    TrainingHelper.Decision(context.TrainingService)
                        .Action(stAction.ActionId, stAction.Name)
                        .AsMeleeResource("Ninki", context.Ninki).Target(target.Name?.TextValue ?? "Target")
                        .Reason($"Spending 50 Ninki on {stAction.Name}{meisuiNote}",
                            $"{stAction.Name} is your primary single-target Ninki spender.")
                        .Factors($"Ninki >= {ninkiMinGauge}", context.HasMeisui ? "Meisui buff active" : "Standard potency")
                        .Alternatives("Save for Bunshin")
                        .Tip("Spend Ninki before capping. Bunshin > Bhavacakra in priority.")
                        .Concept(NinConcepts.Bhavacakra)
                        .Record();
                    context.TrainingService?.RecordConceptApplication(NinConcepts.Bhavacakra, true, "ST Ninki spending");
                });
        }
    }

    private void TryPushFeint(IHermesContext context, RotationScheduler scheduler, IBattleChara target)
    {
        var player = context.Player;
        if (player.Level < RoleActions.Feint.MinLevel) return;
        if (!context.ActionService.IsActionReady(RoleActions.Feint.ActionId)) return;

        var partyCoord = context.PartyCoordinationService;
        var coordConfig = context.Configuration.PartyCoordination;
        if (coordConfig.EnableCooldownCoordination &&
            partyCoord?.WasActionUsedByOther(RoleActions.Feint.ActionId, 15f) == true)
        {
            return;
        }

        scheduler.PushOgcd(HermesAbilities.Feint, target.GameObjectId, priority: 7,
            onDispatched: _ =>
            {
                context.Debug.PlannedAction = RoleActions.Feint.Name;
                partyCoord?.OnCooldownUsed(RoleActions.Feint.ActionId, 90_000);
            });
    }

    private void TryPushSecondWind(IHermesContext context, RotationScheduler scheduler)
    {
        if (!context.Configuration.MeleeShared.EnableSecondWind) return;

        RoleActionPushers.TryPushSecondWind(
            context, scheduler, HermesAbilities.SecondWind,
            hpThresholdPct: context.Configuration.MeleeShared.SecondWindHpThreshold,
            priority: 6,
            onDispatched: _ => context.Debug.PlannedAction = RoleActions.SecondWind.Name);
    }

    private void TryPushBloodbath(IHermesContext context, RotationScheduler scheduler)
    {
        if (!context.Configuration.MeleeShared.EnableBloodbath) return;

        RoleActionPushers.TryPushBloodbath(
            context, scheduler, HermesAbilities.Bloodbath,
            hpThresholdPct: context.Configuration.MeleeShared.BloodbathHpThreshold,
            priority: 6,
            onDispatched: _ => context.Debug.PlannedAction = RoleActions.Bloodbath.Name);
    }

    #endregion

    #region GCDs

    private void TryPushRaiju(IHermesContext context, RotationScheduler scheduler, IBattleChara target)
    {
        if (!context.Configuration.Ninja.EnableRaiju) return;
        var player = context.Player;
        if (player.Level < NINActions.ForkedRaiju.MinLevel) return;
        if (!context.HasRaijuReady) return;

        ActionDefinition action;
        AbilityBehavior ability;
        if (!DistanceHelper.IsActionInRange(NINActions.SpinningEdge.ActionId, player, target))
        {
            action = NINActions.ForkedRaiju;
            ability = HermesAbilities.ForkedRaiju;
        }
        else
        {
            action = NINActions.FleetingRaiju;
            ability = HermesAbilities.FleetingRaiju;
        }
        if (!context.ActionService.IsActionReady(action.ActionId)) return;

        scheduler.PushGcd(ability, target.GameObjectId, priority: 2,
            onDispatched: _ =>
            {
                context.Debug.PlannedAction = action.Name;
                context.Debug.DamageState = $"{action.Name} (Raiju proc)";
                var isForked = action.ActionId == NINActions.ForkedRaiju.ActionId;
                TrainingHelper.Decision(context.TrainingService)
                    .Action(action.ActionId, action.Name)
                    .AsMeleeDamage().Target(target.Name?.TextValue ?? "Target")
                    .Reason(isForked ? "Using Forked Raiju (gap closer)" : "Using Fleeting Raiju (melee)",
                        "Raiju procs come from using Raiton.")
                    .Factors("Raiju Ready proc active", $"{context.RaijuStacks} stack(s) available")
                    .Alternatives(isForked ? "Walk to target" : "Use Forked for movement")
                    .Tip("Raiju procs are free damage.")
                    .Concept(NinConcepts.RaijuProcs)
                    .Record();
                context.TrainingService?.RecordConceptApplication(NinConcepts.RaijuProcs, true, "Raiju proc usage");
            });
    }

    private void TryPushPhantomKamaitachi(IHermesContext context, RotationScheduler scheduler, IBattleChara target)
    {
        if (!context.Configuration.Ninja.EnablePhantomKamaitachi) return;
        if (context.Player.Level < NINActions.PhantomKamaitachi.MinLevel) return;
        if (!context.HasPhantomKamaitachiReady) return;
        if (!context.ActionService.IsActionReady(NINActions.PhantomKamaitachi.ActionId)) return;

        scheduler.PushGcd(HermesAbilities.PhantomKamaitachi, target.GameObjectId, priority: 3,
            onDispatched: _ =>
            {
                context.Debug.PlannedAction = NINActions.PhantomKamaitachi.Name;
                context.Debug.DamageState = "Phantom Kamaitachi";
                TrainingHelper.Decision(context.TrainingService)
                    .Action(NINActions.PhantomKamaitachi.ActionId, NINActions.PhantomKamaitachi.Name)
                    .AsMeleeDamage().Target(target.Name?.TextValue ?? "Target")
                    .Reason("Using Phantom Kamaitachi (Bunshin proc)",
                        "Phantom Kamaitachi is a high-potency proc from Bunshin.")
                    .Factors("Phantom Kamaitachi Ready proc")
                    .Alternatives("Don't let proc expire")
                    .Tip("Always use Phantom Kamaitachi after Bunshin.")
                    .Concept(NinConcepts.PhantomKamaitachi)
                    .Record();
                context.TrainingService?.RecordConceptApplication(NinConcepts.PhantomKamaitachi, true, "Bunshin follow-up");
            });
    }

    private void TryPushComboRotation(IHermesContext context, RotationScheduler scheduler, IBattleChara target, int enemyCount)
    {
        var player = context.Player;
        var level = player.Level;

        // Out of melee range: throw daggers to keep uptime instead of idling until we walk back in.
        // Gap-closers (Forked Raiju) and ranged ninjutsu are pushed elsewhere at higher priority, so
        // they still take precedence when available; this is the plain-GCD filler. Position-based
        // (rather than the native range check) so the gate fails open in range — it must never divert
        // a valid melee combo to a weak dagger toss when we are actually in melee.
        var meleeReach = NINActions.SpinningEdge.Range + target.HitboxRadius + player.HitboxRadius;
        if (level >= NINActions.ThrowingDagger.MinLevel
            && !DistanceHelper.IsInRange(player.Position, target.Position, meleeReach))
        {
            TryPushThrowingDagger(context, scheduler, target);
            return;
        }

        var aoeThreshold = context.Configuration.Ninja.AoEMinTargets;
        var suppressAoE = HermesBurstPrepHelper.ShouldSuppressAoE(context, enemyCount, aoeThreshold);
        var useAoe = !suppressAoE && enemyCount >= aoeThreshold && level >= NINActions.DeathBlossom.MinLevel;
        if (useAoe) TryPushAoeCombo(context, scheduler, target, enemyCount);
        else TryPushSingleTargetCombo(context, scheduler, target);
    }

    private void TryPushThrowingDagger(IHermesContext context, RotationScheduler scheduler, IBattleChara target)
    {
        if (!context.ActionService.IsActionReady(NINActions.ThrowingDagger.ActionId)) return;

        scheduler.PushGcd(HermesAbilities.ThrowingDagger, target.GameObjectId, priority: 5,
            onDispatched: _ =>
            {
                context.Debug.PlannedAction = NINActions.ThrowingDagger.Name;
                context.Debug.DamageState = "Throwing Dagger (out of melee range)";
                TrainingHelper.Decision(context.TrainingService)
                    .Action(NINActions.ThrowingDagger.ActionId, NINActions.ThrowingDagger.Name)
                    .AsRangedDamage().Target(target.Name?.TextValue ?? "Target")
                    .Reason("Throwing Dagger — target out of melee range",
                        "Keep GCD uptime with the ranged attack until back in melee instead of idling.")
                    .Factors("Target beyond melee range")
                    .Alternatives("Close the gap (Forked Raiju / movement)")
                    .Tip("Throwing Dagger is weak — return to melee as soon as possible.")
                    .Record();
            });
    }

    private void TryPushSingleTargetCombo(IHermesContext context, RotationScheduler scheduler, IBattleChara target)
    {
        var player = context.Player;
        var level = player.Level;
        var comboStep = context.ComboStep;

        // Step 3 finishers at p6 — no early return (Nike/Themis parity)
        if (comboStep == 2 && context.LastComboAction == NINActions.GustSlash.ActionId)
            TryPushComboFinishers(context, scheduler, target);

        // Step 2 at p5 — no early return
        if (comboStep == 1 && context.LastComboAction == NINActions.SpinningEdge.ActionId
            && level >= NINActions.GustSlash.MinLevel)
        {
            scheduler.PushGcd(HermesAbilities.GustSlash, target.GameObjectId, priority: 5,
                onDispatched: _ =>
                {
                    context.Debug.PlannedAction = NINActions.GustSlash.Name;
                    context.Debug.DamageState = "Gust Slash (Combo 2)";
                    TrainingHelper.Decision(context.TrainingService)
                        .Action(NINActions.GustSlash.ActionId, NINActions.GustSlash.Name)
                        .AsCombo(2).Target(target.Name?.TextValue ?? "Target")
                        .Reason("Gust Slash — combo step 2",
                            "Gust Slash is the second hit in NIN's ST combo.")
                        .Factors("Combo step 2 active")
                        .Alternatives("Restart with Spinning Edge (breaks combo)")
                        .Tip("Maintain your 3-step combo.")
                        .Concept(NinConcepts.ComboBasics)
                        .Record();
                    context.TrainingService?.RecordConceptApplication(NinConcepts.ComboBasics, true, "Combo step 2");
                });
        }

        // Starter / ActionStatus fallback at p7 — always queued
        if (level >= NINActions.SpinningEdge.MinLevel)
        {
            var atComboStep2 = comboStep == 2
                && context.LastComboAction == NINActions.GustSlash.ActionId;

            scheduler.PushGcd(HermesAbilities.SpinningEdge, target.GameObjectId, priority: 7,
                onDispatched: _ =>
                {
                    context.Debug.PlannedAction = NINActions.SpinningEdge.Name;
                    context.Debug.DamageState = atComboStep2
                        ? "Stalled (combo step 2 — no finisher)"
                        : "Spinning Edge (Combo 1)";
                    TrainingHelper.Decision(context.TrainingService)
                        .Action(NINActions.SpinningEdge.ActionId, NINActions.SpinningEdge.Name)
                        .AsCombo(1).Target(target.Name?.TextValue ?? "Target")
                        .Reason(atComboStep2
                            ? "Spinning Edge fallback — finishers blocked at combo step 2"
                            : "Spinning Edge — starting the 3-hit ST combo",
                            atComboStep2
                                ? "When Aeolian Edge and Armor Crush fail ActionStatus, restart combo to preserve GCD uptime."
                                : "Spinning Edge starts NIN's single-target combo.")
                        .Factors(atComboStep2 ? "Combo step 2 stall recovery" : "No higher-priority GCD available")
                        .Alternatives("Use Raiju if proc is active")
                        .Tip("Always complete the full 3-step combo.")
                        .Concept(NinConcepts.ComboBasics)
                        .Record();
                    context.TrainingService?.RecordConceptApplication(NinConcepts.ComboBasics, true,
                        atComboStep2 ? "Combo step 2 fallback" : "Combo step 1");
                });
        }
    }

    private void TryPushComboFinishers(IHermesContext context, RotationScheduler scheduler, IBattleChara target)
    {
        var level = context.Player.Level;
        var kazematoi = context.Kazematoi;

        if (level < NINActions.AeolianEdge.MinLevel)
            return;

        if (level < NINActions.ArmorCrush.MinLevel)
        {
            PushAeolianEdge(context, scheduler, target, priority: 6);
            return;
        }

        if (HermesKazematoiRules.ShouldBuildWithArmorCrush(kazematoi))
        {
            PushArmorCrush(context, scheduler, target, priority: 6);
            return;
        }

        if (HermesKazematoiRules.ShouldSpendWithAeolian(kazematoi))
        {
            PushAeolianEdge(context, scheduler, target, priority: 6);

            if (HermesKazematoiRules.ShouldFallbackArmorCrush(kazematoi))
                PushArmorCrush(context, scheduler, target, priority: 6);
        }
    }

    private static void PushArmorCrush(
        IHermesContext context, RotationScheduler scheduler, IBattleChara target, int priority)
    {
        var correctPositional = context.IsAtFlank || context.HasTrueNorth || context.TargetHasPositionalImmunity;
        if (!HermesPositionalHelper.CanPushPositionalFinisher(context, correctPositional))
            return;

        scheduler.PushGcd(HermesAbilities.ArmorCrush, target.GameObjectId, priority: priority,
            onDispatched: _ =>
            {
                context.Debug.PlannedAction = NINActions.ArmorCrush.Name;
                context.Debug.DamageState = $"Armor Crush {(correctPositional ? "(flank)" : "(WRONG)")}";
                TrainingHelper.Decision(context.TrainingService)
                    .Action(NINActions.ArmorCrush.ActionId, NINActions.ArmorCrush.Name)
                    .AsPositional(correctPositional, "Flank").Target(target.Name?.TextValue ?? "Target")
                    .Reason($"Armor Crush for Kazematoi stacks ({context.Kazematoi} → {context.Kazematoi + 2})",
                        "Armor Crush is a flank positional that grants 2 Kazematoi stacks.")
                    .Factors($"Kazematoi low ({context.Kazematoi})", correctPositional ? "Correct flank" : "MISSED flank")
                    .Alternatives("Use Aeolian Edge")
                    .Tip("Armor Crush builds Kazematoi.")
                    .Concept(NinConcepts.KazematoiManagement)
                    .Record();
                context.TrainingService?.RecordConceptApplication(NinConcepts.KazematoiManagement, correctPositional, "Flank positional");
            });
    }

    private static void PushAeolianEdge(
        IHermesContext context, RotationScheduler scheduler, IBattleChara target, int priority)
    {
        var correctPositional = context.IsAtRear || context.HasTrueNorth || context.TargetHasPositionalImmunity;
        if (!HermesPositionalHelper.CanPushPositionalFinisher(context, correctPositional))
            return;

        scheduler.PushGcd(HermesAbilities.AeolianEdge, target.GameObjectId, priority: priority,
            onDispatched: _ =>
            {
                context.Debug.PlannedAction = NINActions.AeolianEdge.Name;
                context.Debug.DamageState = $"Aeolian Edge {(correctPositional ? "(rear)" : "(WRONG)")} +Kaze:{context.Kazematoi}";
                TrainingHelper.Decision(context.TrainingService)
                    .Action(NINActions.AeolianEdge.ActionId, NINActions.AeolianEdge.Name)
                    .AsPositional(correctPositional, "Rear").Target(target.Name?.TextValue ?? "Target")
                    .Reason("Aeolian Edge for damage",
                        "Aeolian Edge is a rear positional and your main combo finisher.")
                    .Factors($"Kazematoi available ({context.Kazematoi})", correctPositional ? "Correct rear" : "MISSED rear")
                    .Alternatives("Use Armor Crush if low Kazematoi")
                    .Tip("Aeolian Edge is your bread-and-butter finisher.")
                    .Concept(NinConcepts.Positionals)
                    .Record();
                context.TrainingService?.RecordConceptApplication(NinConcepts.Positionals, correctPositional, "Rear positional");
            });
    }

    private void TryPushAoeCombo(IHermesContext context, RotationScheduler scheduler, IBattleChara target, int enemyCount)
    {
        // Death Blossom is Lv38 (NIN starts at 30) and quest locks count too — without a
        // LEARNED AoE starter the pack runs the single-target combo instead of pushing an
        // uncastable id forever.
        if (!Daedalus.Services.Action.ActionAvailability.MeetsLevelAndLearned(
                context.Player.Level, context.ActionService, NINActions.DeathBlossom))
        {
            TryPushSingleTargetCombo(context, scheduler, target);
            return;
        }

        var level = context.Player.Level;
        var comboStep = context.ComboStep;

        if (comboStep == 1 && context.LastComboAction == NINActions.DeathBlossom.ActionId
            && level >= NINActions.HakkeMujinsatsu.MinLevel
            && !context.ActionService.WasLastGcd(NINActions.HakkeMujinsatsu.ActionId))
        {
            scheduler.PushGcd(HermesAbilities.HakkeMujinsatsu, target.GameObjectId, priority: 5,
                onDispatched: _ =>
                {
                    context.Debug.PlannedAction = NINActions.HakkeMujinsatsu.Name;
                    context.Debug.DamageState = "Hakke Mujinsatsu (AoE 2)";
                    TrainingHelper.Decision(context.TrainingService)
                        .Action(NINActions.HakkeMujinsatsu.ActionId, NINActions.HakkeMujinsatsu.Name)
                        .AsAoE(enemyCount).Target($"{enemyCount} enemies")
                        .Reason("Hakke Mujinsatsu — AoE combo step 2",
                            "Follows Death Blossom in NIN's 2-hit AoE combo.")
                        .Factors($"{enemyCount} enemies nearby")
                        .Alternatives("Single-target combo (fewer enemies)")
                        .Tip("Stick to AoE combo at 3+ targets.")
                        .Concept(NinConcepts.AoeCombo)
                        .Record();
                    context.TrainingService?.RecordConceptApplication(NinConcepts.AoeCombo, true, "AoE combo step 2");
                });
            return;
        }

        scheduler.PushGcd(HermesAbilities.DeathBlossom, target.GameObjectId, priority: 6,
            onDispatched: _ =>
            {
                context.Debug.PlannedAction = NINActions.DeathBlossom.Name;
                context.Debug.DamageState = "Death Blossom (AoE 1)";
                TrainingHelper.Decision(context.TrainingService)
                    .Action(NINActions.DeathBlossom.ActionId, NINActions.DeathBlossom.Name)
                    .AsAoE(enemyCount).Target($"{enemyCount} enemies")
                    .Reason("Death Blossom — starting AoE combo",
                        "Death Blossom is NIN's AoE combo starter.")
                    .Factors($"{enemyCount} enemies nearby")
                    .Alternatives("Use Spinning Edge (1-2 targets)")
                    .Tip("Switch to AoE combo at 3+ enemies.")
                    .Concept(NinConcepts.AoeCombo)
                    .Record();
                context.TrainingService?.RecordConceptApplication(NinConcepts.AoeCombo, true, "AoE combo step 1");
            });
    }

    #endregion
}
