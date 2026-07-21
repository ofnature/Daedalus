using Dalamud.Game.ClientState.Objects.Types;
using Daedalus.Data;
using Daedalus.Models.Action;
using Daedalus.Rotation.ApolloCore.Helpers;
using Daedalus.Rotation.Common.Helpers;
using Daedalus.Rotation.Common.RoleActionHelpers;
using Daedalus.Rotation.Common.Scheduling;
using Daedalus.Rotation.NikeCore.Abilities;
using Daedalus.Rotation.NikeCore.Context;
using Daedalus.Services;
using Daedalus.Services.Targeting;
using Daedalus.Services.Training;

namespace Daedalus.Rotation.NikeCore.Modules;

/// <summary>
/// Handles the Samurai damage rotation (scheduler-driven).
/// Manages combos, Iaijutsu, Kaeshi, Ogi Namikiri, Kenki spenders.
/// </summary>
public sealed class DamageModule : INikeModule
{
    public int Priority => 30;
    public string Name => "Damage";

    private readonly IBurstWindowService? _burstWindowService;
    private readonly ISmartAoEService? _smartAoEService;

    public DamageModule(IBurstWindowService? burstWindowService = null, ISmartAoEService? smartAoEService = null)
    {
        _burstWindowService = burstWindowService;
        _smartAoEService = smartAoEService;
    }

    private bool ShouldHoldForBurst(float thresholdSeconds = 8f) =>
        BurstHoldHelper.ShouldHoldForBurst(_burstWindowService, thresholdSeconds);


    public bool TryExecute(INikeContext context, bool isMoving) => false;

    public void UpdateDebugState(INikeContext context) { }

    public void CollectCandidates(INikeContext context, RotationScheduler scheduler, bool isMoving)
    {
        if (!context.InCombat)
        {
            context.Debug.DamageState = "Not in combat";
            return;
        }
        if (context.TargetingService.IsDamageTargetingPaused())
        {
            context.Debug.DamageState = "Paused (no target)";
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
            SAMActions.Hakaze.ActionId,
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
                scheduler.PushGcd(NikeAbilities.Hakaze, engageTarget.GameObjectId, priority: 6,
                    onDispatched: _ =>
                    {
                        context.Debug.PlannedAction = SAMActions.Hakaze.Name;
                        context.Debug.DamageState = "Opening on hard target (automation)";
                    });
                context.Debug.DamageState = "Automation engage — closing to melee";
                return;
            }
            context.Debug.DamageState = "No target";
            return;
        }

        var aoeEnabled = context.Configuration.Samurai.EnableAoERotation;
        var aoeThreshold = context.Configuration.Samurai.AoEMinTargets;
        var pack = EnemyPackDebugHelper.Count(context.TargetingService, 5f, player);
        EnemyPackDebugHelper.Apply(context.Debug, pack);
        var enemyCount = aoeEnabled ? pack.AoeRange : 0;
        var useAoE = aoeEnabled && pack.AoeRange >= aoeThreshold;

        // oGCDs
        TryPushFeint(context, scheduler, target);
        TryPushSecondWind(context, scheduler);
        TryPushBloodbath(context, scheduler);
        TryPushKenkiSpender(context, scheduler, target, useAoE);

        // GCDs (priority order)
        TryPushKaeshiNamikiri(context, scheduler, target);
        TryPushTsubameGaeshi(context, scheduler, target);

        // Cast-time GCDs (Ogi Namikiri / Iaijutsu, 1.3s bar) never fire while moving — starting
        // the cast mid-step just cancels it (RSR blocks moving casts the same way). isMoving
        // includes Daedalus's own vNav paths (anchor hops/drifts), so the toon finishes the step,
        // plants, and casts next cycle; combo/filler GCDs below keep the GCD rolling meanwhile.
        if (!isMoving)
        {
            TryPushOgiNamikiri(context, scheduler, target);
            TryPushIaijutsu(context, scheduler, target, useAoE, pack.AoeRange);
        }
        TryPushMeikyoFinisher(context, scheduler, target, useAoE);
        TryPushComboRotation(context, scheduler, target, enemyCount, useAoE);
    }

    #region oGCDs

    private void TryPushFeint(INikeContext context, RotationScheduler scheduler, IBattleChara target)
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

        scheduler.PushOgcd(NikeAbilities.Feint, target.GameObjectId, priority: 7,
            onDispatched: _ =>
            {
                context.Debug.PlannedAction = RoleActions.Feint.Name;
                partyCoord?.OnCooldownUsed(RoleActions.Feint.ActionId, 90_000);
            });
    }

    private void TryPushSecondWind(INikeContext context, RotationScheduler scheduler)
    {
        if (!context.Configuration.MeleeShared.EnableSecondWind) return;

        RoleActionPushers.TryPushSecondWind(
            context, scheduler, NikeAbilities.SecondWind,
            hpThresholdPct: context.Configuration.MeleeShared.SecondWindHpThreshold,
            priority: 6,
            onDispatched: _ => context.Debug.PlannedAction = RoleActions.SecondWind.Name);
    }

    private void TryPushBloodbath(INikeContext context, RotationScheduler scheduler)
    {
        if (!context.Configuration.MeleeShared.EnableBloodbath) return;

        RoleActionPushers.TryPushBloodbath(
            context, scheduler, NikeAbilities.Bloodbath,
            hpThresholdPct: context.Configuration.MeleeShared.BloodbathHpThreshold,
            priority: 6,
            onDispatched: _ => context.Debug.PlannedAction = RoleActions.Bloodbath.Name);
    }

    private void TryPushKenkiSpender(INikeContext context, RotationScheduler scheduler, IBattleChara target, bool useAoE)
    {
        var player = context.Player;
        var level = player.Level;
        if (context.Kenki < context.Configuration.Samurai.KenkiMinGauge) return;
        if (context.Configuration.Samurai.EnableBurstPooling && ShouldHoldForBurst(8f) && context.Kenki < context.Configuration.Samurai.KenkiReserveForBurst) return;

        var shouldSpend = context.Kenki >= context.Configuration.Samurai.KenkiOvercapThreshold || context.Kenki >= 50;
        if (!shouldSpend) return;

        if (useAoE && level >= SAMActions.Kyuten.MinLevel)
        {
            if (!context.Configuration.Samurai.EnableKyuten) return;
            if (!context.ActionService.IsActionReady(SAMActions.Kyuten.ActionId)) return;

            scheduler.PushOgcd(NikeAbilities.Kyuten, player.GameObjectId, priority: 5,
                onDispatched: _ =>
                {
                    context.Debug.PlannedAction = SAMActions.Kyuten.Name;
                    context.Debug.DamageState = $"Kyuten (AoE)";

                    TrainingHelper.Decision(context.TrainingService)
                        .Action(SAMActions.Kyuten.ActionId, SAMActions.Kyuten.Name)
                        .AsAoE(0)
                        .Target("AoE group")
                        .Reason($"Spending 25 Kenki on Kyuten",
                            "Kyuten is the AoE Kenki spender. Use to prevent Kenki overcap. " +
                            "Prioritize Senei/Guren on cooldown, then use Kyuten/Shinten for excess.")
                        .Factors(new[] { $"Kenki: {context.Kenki}", "AoE situation", "Avoiding overcap" })
                        .Alternatives(new[] { "Use Shinten (less total damage)", "Hold for Senei/Guren (if soon)" })
                        .Tip("Don't sit at max Kenki. Spend regularly on Shinten/Kyuten.")
                        .Concept("sam_kenki_gauge")
                        .Record();
                    context.TrainingService?.RecordConceptApplication("sam_kenki_gauge", true, "AoE Kenki spending");
                });
            return;
        }

        if (level >= SAMActions.Shinten.MinLevel)
        {
            if (!context.Configuration.Samurai.EnableShinten) return;
            if (!context.ActionService.IsActionReady(SAMActions.Shinten.ActionId)) return;

            scheduler.PushOgcd(NikeAbilities.Shinten, target.GameObjectId, priority: 5,
                onDispatched: _ =>
                {
                    context.Debug.PlannedAction = SAMActions.Shinten.Name;
                    context.Debug.DamageState = "Shinten";

                    TrainingHelper.Decision(context.TrainingService)
                        .Action(SAMActions.Shinten.ActionId, SAMActions.Shinten.Name)
                        .AsMeleeResource("Kenki", context.Kenki)
                        .Target(target.Name?.TextValue ?? "Target")
                        .Reason($"Spending 25 Kenki on Shinten",
                            "Shinten is your primary single-target Kenki spender. " +
                            "Use to avoid overcapping Kenki (100 max). Keep some reserve for Zanshin (50).")
                        .Factors(new[] { $"Kenki: {context.Kenki}", "Avoiding overcap", "ST damage filler" })
                        .Alternatives(new[] { "Wait for Senei (if soon)", "Overcap Kenki (wastes gauge)" })
                        .Tip("Shinten is filler damage. Spend Kenki before it caps.")
                        .Concept("sam_kenki_gauge")
                        .Record();
                    context.TrainingService?.RecordConceptApplication("sam_kenki_gauge", true, "ST Kenki spending");
                });
        }
    }

    #endregion

    #region GCDs

    private void TryPushKaeshiNamikiri(INikeContext context, RotationScheduler scheduler, IBattleChara target)
    {
        var player = context.Player;
        if (player.Level < SAMActions.KaeshiNamikiri.MinLevel) return;
        if (!context.KaeshiNamikiriReady) return;

        scheduler.PushGcd(NikeAbilities.KaeshiNamikiri, target.GameObjectId, priority: 1,
            onDispatched: _ =>
            {
                context.Debug.PlannedAction = SAMActions.KaeshiNamikiri.Name;
                context.Debug.DamageState = "Kaeshi: Namikiri";

                TrainingHelper.Decision(context.TrainingService)
                    .Action(SAMActions.KaeshiNamikiri.ActionId, SAMActions.KaeshiNamikiri.Name)
                    .AsMeleeBurst()
                    .Target(target.Name?.TextValue ?? "Target")
                    .Reason("Following up Ogi Namikiri with Kaeshi: Namikiri",
                        "Kaeshi: Namikiri is a guaranteed follow-up after Ogi Namikiri. " +
                        "It has a short window so use it immediately. High potency burst damage.")
                    .Factors(new[] { "Kaeshi: Namikiri Ready buff active", "Ogi Namikiri just used", "Burst window active" })
                    .Alternatives(new[] { "Miss the window (buff expires)" })
                    .Tip("Always use Kaeshi: Namikiri immediately after Ogi Namikiri.")
                    .Concept("sam_iaijutsu")
                    .Record();
                context.TrainingService?.RecordConceptApplication("sam_iaijutsu", true, "Kaeshi: Namikiri follow-up");
            });
    }

    private void TryPushTsubameGaeshi(INikeContext context, RotationScheduler scheduler, IBattleChara target)
    {
        if (!context.Configuration.Samurai.EnableTsubamegaeshi) return;
        var player = context.Player;
        if (player.Level < SAMActions.TsubameGaeshi.MinLevel) return;
        if (!context.TsubameGaeshiActionReady) return;

        var kaeshiAction = SAMActions.GetTsubameKaeshiAction(context.ActionService);
        if (kaeshiAction == null) return;

        var ability = ResolveTsubameKaeshiBehavior(kaeshiAction);
        if (ability == null) return;

        scheduler.PushGcd(ability, target.GameObjectId, priority: 1,
            onDispatched: _ =>
            {
                context.Debug.PlannedAction = kaeshiAction.Name;
                context.Debug.DamageState = kaeshiAction.Name;

                TrainingHelper.Decision(context.TrainingService)
                    .Action(kaeshiAction.ActionId, kaeshiAction.Name)
                    .AsMeleeBurst()
                    .Target(target.Name?.TextValue ?? "Target")
                    .Reason($"Following up Iaijutsu with {kaeshiAction.Name}",
                        "Tsubame-gaeshi repeats your last Iaijutsu. " +
                        "Use immediately after Iaijutsu - the window is short. " +
                        "Kaeshi: Setsugekka is highest potency, Kaeshi: Goken for AoE.")
                    .Factors(new[] { "Tsubame-gaeshi slot active", $"Slot: {kaeshiAction.Name}", "Burst window active" })
                    .Alternatives(new[] { "Miss the window (buff expires)", "Wrong Iaijutsu order" })
                    .Tip("Iaijutsu → Tsubame-gaeshi is your core burst combo. Never skip it.")
                    .Concept("sam_iaijutsu")
                    .Record();
                context.TrainingService?.RecordConceptApplication("sam_iaijutsu", true, "Tsubame-gaeshi follow-up");
            });
    }

    private static AbilityBehavior? ResolveTsubameKaeshiBehavior(ActionDefinition action) =>
        action.ActionId switch
        {
            var id when id == SAMActions.KaeshiHiganbana.ActionId => NikeAbilities.KaeshiHiganbana,
            var id when id == SAMActions.KaeshiGoken.ActionId => NikeAbilities.KaeshiGoken,
            var id when id == SAMActions.KaeshiSetsugekka.ActionId => NikeAbilities.KaeshiSetsugekka,
            var id when id == SAMActions.TendoKaeshiGoken.ActionId => NikeAbilities.TendoKaeshiGoken,
            var id when id == SAMActions.TendoKaeshiSetsugekka.ActionId => NikeAbilities.TendoKaeshiSetsugekka,
            _ => null,
        };

    private void TryPushOgiNamikiri(INikeContext context, RotationScheduler scheduler, IBattleChara target)
    {
        if (!context.Configuration.Samurai.EnableOgiNamikiri) return;
        var player = context.Player;
        if (player.Level < SAMActions.OgiNamikiri.MinLevel) return;
        if (!context.HasOgiNamikiriReady) return;
        // RSR parity: Ogi only fires with BOTH damage buffs up (HasFugetsuAndFuka) — a 1000-potency
        // cast without Fugetsu's +13% is a straight loss, and the Ready buff lasts 30s so there is
        // always time to re-establish the buffs first (the combo does it naturally).
        if (!context.HasFugetsu || !context.HasFuka) return;
        // Block re-dispatch during the server RTT window after Ogi Namikiri fires: status 2959
        // stays visible on the client for 2–3 frames before the remove-packet arrives, but
        // KaeshiNamikiriReady (2960) isn't granted yet — without this guard a second Ogi fires
        // before Kaeshi can be seen. WasLastAction resets the moment Kaeshi is dispatched.
        if (context.ActionService.WasLastAction(SAMActions.OgiNamikiri.ActionId)) return;

        scheduler.PushGcd(NikeAbilities.OgiNamikiri, target.GameObjectId, priority: 2,
            onDispatched: _ =>
            {
                context.Debug.PlannedAction = SAMActions.OgiNamikiri.Name;
                context.Debug.DamageState = "Ogi Namikiri";

                TrainingHelper.Decision(context.TrainingService)
                    .Action(SAMActions.OgiNamikiri.ActionId, SAMActions.OgiNamikiri.Name)
                    .AsMeleeBurst()
                    .Target(target.Name?.TextValue ?? "Target")
                    .Reason("Using Ogi Namikiri (granted by Ikishoten)",
                        "Ogi Namikiri is SAM's highest potency GCD. Granted by Ikishoten. " +
                        "Always follow with Kaeshi: Namikiri. Use during burst windows for maximum effect.")
                    .Factors(new[] { "Ogi Namikiri Ready buff active", "From Ikishoten", "Burst window active" })
                    .Alternatives(new[] { "Hold for raid buffs (if close)", "Use outside burst (wastes damage)" })
                    .Tip("Ogi Namikiri → Kaeshi: Namikiri → Zanshin is your biggest burst sequence.")
                    .Concept("sam_burst_window")
                    .Record();
                context.TrainingService?.RecordConceptApplication("sam_burst_window", true, "Ogi Namikiri burst");
            });
    }

    private void TryPushIaijutsu(INikeContext context, RotationScheduler scheduler, IBattleChara target, bool useAoE, int enemyCount)
    {
        if (!context.Configuration.Samurai.EnableIaijutsu) return;
        var player = context.Player;
        var level = player.Level;
        if (context.SenCount == 0) return;

        switch (context.SenCount)
        {
            case 1:
                if (!context.Configuration.Samurai.MaintainHiganbana) return;
                if (level < SAMActions.Higanbana.MinLevel) return;
                // Skip Higanbana at 2+ targets — DoT on one mob is a DPS loss vs AoE filler
                if (enemyCount >= 2) return;
                // RSR parity: the 60s DoT SNAPSHOTS the damage buffs at cast — never apply it
                // without both Fugetsu and Fuka up (a full minute of unbuffed ticks otherwise).
                if (!context.HasFugetsu || !context.HasFuka) return;
                if (context.HasHiganbanaOnTarget && context.HiganbanaRemaining > context.Configuration.Samurai.HiganbanaRefreshThreshold) return;
                var targetHpPercent = target.MaxHp > 0 ? (float)target.CurrentHp / target.MaxHp : 1f;
                if (targetHpPercent < context.Configuration.Samurai.HiganbanaMinTargetHp) return;
                PushIaijutsu(context, scheduler, target, SAMActions.Higanbana, NikeAbilities.Higanbana, SAMActions.IaijutsuType.Higanbana);
                break;

            case 2:
                if (level < SAMActions.TenkaGoken.MinLevel) return;
                if (!useAoE) return;
                PushIaijutsu(context, scheduler, target, SAMActions.TenkaGoken, NikeAbilities.TenkaGoken, SAMActions.IaijutsuType.TenkaGoken);
                break;

            case 3:
                if (level < SAMActions.MidareSetsugekka.MinLevel) return;
                PushIaijutsu(context, scheduler, target, SAMActions.MidareSetsugekka, NikeAbilities.MidareSetsugekka, SAMActions.IaijutsuType.MidareSetsugekka);
                break;
        }
    }

    private void PushIaijutsu(INikeContext context, RotationScheduler scheduler, IBattleChara target,
                              ActionDefinition action, AbilityBehavior ability, SAMActions.IaijutsuType type)
    {
        scheduler.PushGcd(ability, target.GameObjectId, priority: 3,
            onDispatched: _ =>
            {
                context.Debug.PlannedAction = action.Name;
                context.Debug.DamageState = $"{action.Name} ({context.SenCount} Sen)";
                context.Debug.LastIaijutsu = type;

                var (description, explanation, tip, conceptId) = GetIaijutsuTrainingInfo(type, context);
                TrainingHelper.Decision(context.TrainingService)
                    .Action(action.ActionId, action.Name)
                    .AsMeleeDamage()
                    .Target(target.Name?.TextValue ?? "Target")
                    .Reason(description, explanation)
                    .Factors(new[] { $"Sen count: {context.SenCount}", GetSenState(context), "Iaijutsu ready" })
                    .Alternatives(new[] { "Continue building Sen", "Use wrong Sen count" })
                    .Tip(tip)
                    .Concept(conceptId)
                    .Record();
                context.TrainingService?.RecordConceptApplication("sam_sen_system", true, $"Iaijutsu: {action.Name}");
            });
    }

    private static (string description, string explanation, string tip, string conceptId) GetIaijutsuTrainingInfo(SAMActions.IaijutsuType type, INikeContext context)
    {
        return type switch
        {
            SAMActions.IaijutsuType.Higanbana => (
                $"Applying Higanbana DoT ({(context.HasHiganbanaOnTarget ? $"{context.HiganbanaRemaining:F1}s remaining" : "not on target")})",
                "Higanbana is SAM's 60s DoT. Apply once and refresh when <5s remains. " +
                "Don't use in AoE situations. The full duration deals more damage than Midare.",
                "Keep Higanbana up 100% of the time on single-target fights.",
                "sam_sen_system"),
            SAMActions.IaijutsuType.TenkaGoken => (
                "Using Tenka Goken (2 Sen AoE Iaijutsu)",
                "Tenka Goken is the 2-Sen AoE Iaijutsu. Use instead of Midare when hitting 3+ targets. " +
                "Still triggers Tsubame-gaeshi for Kaeshi: Goken follow-up.",
                "In AoE, use Tenka Goken at 2 Sen rather than building to 3.",
                "sam_aoe_rotation"),
            SAMActions.IaijutsuType.MidareSetsugekka => (
                "Using Midare Setsugekka (3 Sen burst)",
                "Midare Setsugekka is SAM's primary burst GCD. Requires all 3 Sen. " +
                "Always follow with Tsubame-gaeshi for Kaeshi: Setsugekka.",
                "Midare → Kaeshi: Setsugekka is your bread-and-butter burst combo.",
                "sam_iaijutsu"),
            _ => ("Using Iaijutsu", "Iaijutsu abilities consume Sen.", "Build Sen through combos.", "sam_iaijutsu")
        };
    }

    private static string GetSenState(INikeContext context)
    {
        var sen = new System.Collections.Generic.List<string>();
        if (context.HasSetsu) sen.Add("Setsu");
        if (context.HasGetsu) sen.Add("Getsu");
        if (context.HasKa) sen.Add("Ka");
        return sen.Count > 0 ? string.Join("+", sen) : "No Sen";
    }

    private void TryPushMeikyoFinisher(INikeContext context, RotationScheduler scheduler, IBattleChara target, bool useAoE)
    {
        if (!context.HasMeikyoShisui || context.MeikyoStacks <= 0) return;

        if (useAoE) TryPushAoeMeikyoFinisher(context, scheduler, target);
        else TryPushSingleTargetMeikyoFinisher(context, scheduler, target);
    }

    private void TryPushSingleTargetMeikyoFinisher(INikeContext context, RotationScheduler scheduler, IBattleChara target)
    {
        var level = context.Player.Level;

        if (!context.HasGetsu && level >= SAMActions.Gekko.MinLevel)
        {
            bool correctPositional = context.IsAtRear || context.HasTrueNorth || context.TargetHasPositionalImmunity;
            scheduler.PushGcd(NikeAbilities.Gekko, target.GameObjectId, priority: 4,
                onDispatched: _ =>
                {
                    context.Debug.PlannedAction = SAMActions.Gekko.Name;
                    context.Debug.DamageState = $"Meikyo Gekko {(correctPositional ? "(rear)" : "(WRONG)")}";

                    TrainingHelper.Decision(context.TrainingService)
                        .Action(SAMActions.Gekko.ActionId, SAMActions.Gekko.Name)
                        .AsPositional(correctPositional, "rear")
                        .Target(target.Name?.TextValue ?? "Target")
                        .Reason($"Meikyo Gekko for Getsu Sen",
                            "Gekko grants Getsu (Moon) Sen and has a rear positional for bonus damage. " +
                            "During Meikyo Shisui, you can use finishers directly without combos.")
                        .Factors(new[] { "Meikyo Shisui active", "Need Getsu Sen", correctPositional ? "At rear" : "Not at rear" })
                        .Alternatives(new[] { "Use Kasha instead (need Ka)", "Use Yukikaze (need Setsu)" })
                        .Tip("Gekko = rear, Kasha = flank. Use True North when you can't position.")
                        .Concept("sam_positionals")
                        .Record();
                    context.TrainingService?.RecordConceptApplication("sam_positionals", correctPositional, "Gekko rear positional");
                });
            return;
        }

        if (!context.HasKa && level >= SAMActions.Kasha.MinLevel)
        {
            bool correctPositional = context.IsAtFlank || context.HasTrueNorth || context.TargetHasPositionalImmunity;
            scheduler.PushGcd(NikeAbilities.Kasha, target.GameObjectId, priority: 4,
                onDispatched: _ =>
                {
                    context.Debug.PlannedAction = SAMActions.Kasha.Name;
                    context.Debug.DamageState = $"Meikyo Kasha {(correctPositional ? "(flank)" : "(WRONG)")}";

                    TrainingHelper.Decision(context.TrainingService)
                        .Action(SAMActions.Kasha.ActionId, SAMActions.Kasha.Name)
                        .AsPositional(correctPositional, "flank")
                        .Target(target.Name?.TextValue ?? "Target")
                        .Reason($"Meikyo Kasha for Ka Sen",
                            "Kasha grants Ka (Flower) Sen and has a flank positional for bonus damage. " +
                            "During Meikyo Shisui, you can use finishers directly without combos.")
                        .Factors(new[] { "Meikyo Shisui active", "Need Ka Sen", correctPositional ? "At flank" : "Not at flank" })
                        .Alternatives(new[] { "Use Gekko instead (need Getsu)", "Use Yukikaze (need Setsu)" })
                        .Tip("Kasha = flank, Gekko = rear. Use True North when you can't position.")
                        .Concept("sam_positionals")
                        .Record();
                    context.TrainingService?.RecordConceptApplication("sam_positionals", correctPositional, "Kasha flank positional");
                });
            return;
        }

        if (!context.HasSetsu && level >= SAMActions.Yukikaze.MinLevel)
        {
            scheduler.PushGcd(NikeAbilities.Yukikaze, target.GameObjectId, priority: 4,
                onDispatched: _ =>
                {
                    context.Debug.PlannedAction = SAMActions.Yukikaze.Name;
                    context.Debug.DamageState = "Meikyo Yukikaze";

                    TrainingHelper.Decision(context.TrainingService)
                        .Action(SAMActions.Yukikaze.ActionId, SAMActions.Yukikaze.Name)
                        .AsMeleeDamage()
                        .Target(target.Name?.TextValue ?? "Target")
                        .Reason("Meikyo Yukikaze for Setsu Sen",
                            "Yukikaze grants Setsu (Snow) Sen during Meikyo Shisui. " +
                            "Use it to build the missing Setsu Sen without a combo chain.")
                        .Factors(new[] { "Meikyo Shisui active", "Need Setsu Sen", "Combo skip" })
                        .Alternatives(new[] { "Use Gekko (need Getsu)", "Use Kasha (need Ka)" })
                        .Tip("Meikyo → Gekko → Kasha → Yukikaze builds all 3 Sen instantly.")
                        .Concept("sam_meikyo_shisui")
                        .Record();
                    context.TrainingService?.RecordConceptApplication("sam_meikyo_shisui", true, "Meikyo Yukikaze Setsu");
                });
            return;
        }

        // Overflow — all Sen held: prefer Gekko
        if (level >= SAMActions.Gekko.MinLevel)
        {
            scheduler.PushGcd(NikeAbilities.Gekko, target.GameObjectId, priority: 4,
                onDispatched: _ =>
                {
                    context.Debug.PlannedAction = SAMActions.Gekko.Name;
                    context.Debug.DamageState = "Meikyo Gekko (overflow)";

                    TrainingHelper.Decision(context.TrainingService)
                        .Action(SAMActions.Gekko.ActionId, SAMActions.Gekko.Name)
                        .AsMeleeDamage()
                        .Target(target.Name?.TextValue ?? "Target")
                        .Reason("Meikyo Gekko (overflow — all Sen held)",
                            "All Sen are already held. Gekko consumes a Meikyo stack and still deals combo-level damage. " +
                            "Prefer using Meikyo stacks to build missing Sen rather than overflowing.")
                        .Factors(new[] { "Meikyo Shisui active", "All Sen already held", "Using stack to avoid waste" })
                        .Alternatives(new[] { "Delay Meikyo until Sen are spent (better timing)" })
                        .Tip("Activate Meikyo Shisui before spending Sen so all stacks build toward Iaijutsu.")
                        .Concept("sam_meikyo_shisui")
                        .Record();
                    context.TrainingService?.RecordConceptApplication("sam_meikyo_shisui", true, "Meikyo overflow Gekko");
                });
        }
    }

    private void TryPushAoeMeikyoFinisher(INikeContext context, RotationScheduler scheduler, IBattleChara target)
    {
        var player = context.Player;
        var level = player.Level;

        if (!context.HasGetsu && level >= SAMActions.Mangetsu.MinLevel)
        {
            scheduler.PushGcd(NikeAbilities.Mangetsu, player.GameObjectId, priority: 4,
                onDispatched: _ =>
                {
                    context.Debug.PlannedAction = SAMActions.Mangetsu.Name;
                    context.Debug.DamageState = "Meikyo Mangetsu";

                    TrainingHelper.Decision(context.TrainingService)
                        .Action(SAMActions.Mangetsu.ActionId, SAMActions.Mangetsu.Name)
                        .AsAoE(0)
                        .Target("Nearby enemies")
                        .Reason("Meikyo Mangetsu for Getsu Sen (AoE)",
                            "Mangetsu grants Getsu (Moon) Sen and refreshes Fugetsu buff in AoE. " +
                            "During Meikyo Shisui, use it directly without a combo chain.")
                        .Factors(new[] { "Meikyo Shisui active", "Need Getsu Sen", "AoE situation" })
                        .Alternatives(new[] { "Use Oka (need Ka)", "Use ST finishers (fewer enemies)" })
                        .Tip("In AoE, use Mangetsu (Getsu) and Oka (Ka) under Meikyo Shisui.")
                        .Concept("sam_meikyo_shisui")
                        .Record();
                    context.TrainingService?.RecordConceptApplication("sam_meikyo_shisui", true, "AoE Meikyo Mangetsu");
                });
            return;
        }

        if (!context.HasKa && level >= SAMActions.Oka.MinLevel)
        {
            scheduler.PushGcd(NikeAbilities.Oka, player.GameObjectId, priority: 4,
                onDispatched: _ =>
                {
                    context.Debug.PlannedAction = SAMActions.Oka.Name;
                    context.Debug.DamageState = "Meikyo Oka";

                    TrainingHelper.Decision(context.TrainingService)
                        .Action(SAMActions.Oka.ActionId, SAMActions.Oka.Name)
                        .AsAoE(0)
                        .Target("Nearby enemies")
                        .Reason("Meikyo Oka for Ka Sen (AoE)",
                            "Oka grants Ka (Flower) Sen and refreshes Fuka buff in AoE. " +
                            "During Meikyo Shisui, use it directly without a combo chain.")
                        .Factors(new[] { "Meikyo Shisui active", "Need Ka Sen", "AoE situation" })
                        .Alternatives(new[] { "Use Mangetsu (need Getsu)", "Use ST finishers (fewer enemies)" })
                        .Tip("In AoE, use Mangetsu (Getsu) and Oka (Ka) under Meikyo Shisui.")
                        .Concept("sam_meikyo_shisui")
                        .Record();
                    context.TrainingService?.RecordConceptApplication("sam_meikyo_shisui", true, "AoE Meikyo Oka");
                });
            return;
        }

        if (level >= SAMActions.Mangetsu.MinLevel)
        {
            scheduler.PushGcd(NikeAbilities.Mangetsu, player.GameObjectId, priority: 4,
                onDispatched: _ =>
                {
                    context.Debug.PlannedAction = SAMActions.Mangetsu.Name;
                    context.Debug.DamageState = "Meikyo Mangetsu (overflow)";

                    TrainingHelper.Decision(context.TrainingService)
                        .Action(SAMActions.Mangetsu.ActionId, SAMActions.Mangetsu.Name)
                        .AsAoE(0)
                        .Target("Nearby enemies")
                        .Reason("Meikyo Mangetsu (overflow — Getsu and Ka already held)",
                            "Both Getsu and Ka Sen are held. Mangetsu consumes a Meikyo stack while still dealing AoE damage.")
                        .Factors(new[] { "Meikyo Shisui active", "Getsu and Ka already held", "Using stack to avoid waste" })
                        .Alternatives(new[] { "Delay Meikyo until Sen spent (better timing)" })
                        .Tip("Use Meikyo Shisui after spending Sen so stacks build toward Tenka Goken.")
                        .Concept("sam_meikyo_shisui")
                        .Record();
                    context.TrainingService?.RecordConceptApplication("sam_meikyo_shisui", true, "AoE Meikyo overflow");
                });
        }
    }

    private void TryPushComboRotation(INikeContext context, RotationScheduler scheduler, IBattleChara target, int enemyCount, bool useAoE)
    {
        var player = context.Player;
        var level = player.Level;

        // Out of melee range: throw Enpi to keep uptime instead of idling until we walk back in
        // (NIN Throwing Dagger parity). Position-based (rather than the native range check) so the
        // gate fails open in range — it must never divert a valid melee combo to a weak ranged toss
        // when we are actually in melee. Higher-priority GCDs (Iaijutsu/Ogi/Kaeshi/Tsubame) are pushed
        // earlier in CollectCandidates, so they still take precedence when their own gates open.
        var meleeReach = SAMActions.Hakaze.Range + target.HitboxRadius + player.HitboxRadius;
        if (level >= SAMActions.Enpi.MinLevel
            && !DistanceHelper.IsInRange(player.Position, target.Position, meleeReach))
        {
            TryPushEnpi(context, scheduler, target);
            return;
        }

        // Continue an in-progress AoE combo even if the enemy count dropped below threshold mid-chain.
        if (!useAoE && enemyCount > 0 && IsInAoeCombo(context))
            useAoE = true;

        if (useAoE) TryPushAoeCombo(context, scheduler, target);
        else TryPushSingleTargetCombo(context, scheduler, target);
    }

    private void TryPushEnpi(INikeContext context, RotationScheduler scheduler, IBattleChara target)
    {
        if (!context.ActionService.IsActionReady(SAMActions.Enpi.ActionId)) return;

        scheduler.PushGcd(NikeAbilities.Enpi, target.GameObjectId, priority: 7,
            onDispatched: _ =>
            {
                var enhanced = context.StatusHelper.HasEnhancedEnpi(context.Player);
                context.Debug.PlannedAction = SAMActions.Enpi.Name;
                context.Debug.DamageState = enhanced ? "Enpi (Enhanced — out of melee range)" : "Enpi (out of melee range)";

                TrainingHelper.Decision(context.TrainingService)
                    .Action(SAMActions.Enpi.ActionId, SAMActions.Enpi.Name)
                    .AsRangedDamage()
                    .Target(target.Name?.TextValue ?? "Target")
                    .Reason("Enpi — target out of melee range",
                        "Keep GCD uptime with the ranged attack until back in melee instead of idling. " +
                        "Enhanced Enpi (from Hissatsu: Yaten) hits far harder — use the proc if it is up.")
                    .Factors(new[] { "Target beyond melee range", enhanced ? "Enhanced Enpi proc active" : "No Enpi proc" })
                    .Alternatives(new[] { "Close the gap (movement)" })
                    .Tip("Enpi is weak filler — return to melee as soon as possible.")
                    .Concept("sam_combo_rotation")
                    .Record();
            });
    }

    private static bool IsInAoeCombo(INikeContext context)
    {
        if (context.ComboStep == 0) return false;
        return context.LastComboAction == SAMActions.Fuga.ActionId ||
               context.LastComboAction == SAMActions.Fuko.ActionId;
    }

    private void TryPushSingleTargetCombo(INikeContext context, RotationScheduler scheduler, IBattleChara target)
    {
        var level = context.Player.Level;
        var comboStep = context.ComboStep;
        var comboStarter = SAMActions.GetComboStarter((byte)level, context.ActionService);
        var starterAbility = comboStarter == SAMActions.Gyofu ? NikeAbilities.Gyofu : NikeAbilities.Hakaze;
        // Dual-source starter detection — same resilience as the AoE path. Guards against the gauge
        // combo-step read lagging after Gyofu/Hakaze, which would otherwise drop the step-2 push and
        // re-issue the (blocked) starter, stalling the rotation. WasLastGcd (not WasLastAction) so an
        // oGCD weave (Shinten/Feint/etc.) between the starter and step 2 doesn't mask the starter.
        var justFiredStarter = context.ActionService.WasLastGcd(comboStarter.ActionId)
                               || context.ActionService.WasLastGcd(SAMActions.Hakaze.ActionId)
                               || context.ActionService.WasLastGcd(SAMActions.Gyofu.ActionId);
        // Symmetric dual-source detection for the step2->finisher transition. Same failure mode as the
        // starter->step2 case: the gauge combo-step read can lag or desync (notably right after a target
        // swap), leaving comboStep stuck at 1 / LastComboAction on the starter. Without this, the Gekko/
        // Kasha finisher push (which needs comboStep==2 && LastComboAction==Jinpu/Shifu) is dropped and the
        // onStarter block re-issues Jinpu every GCD forever — the "Jinpu lock" seen on the 2nd pack mob.
        // Treating "we just cast Jinpu/Shifu" as equivalent guarantees the finisher fires the next GCD, and
        // it suppresses the onStarter step-2 re-push so the two don't collide at the same priority.
        var justFiredJinpu = context.ActionService.WasLastGcd(SAMActions.Jinpu.ActionId);
        var justFiredShifu = context.ActionService.WasLastGcd(SAMActions.Shifu.ActionId);
        var onStarter = ((comboStep == 1 &&
                         (context.LastComboAction == comboStarter.ActionId ||
                          context.LastComboAction == SAMActions.Hakaze.ActionId))
                        || justFiredStarter)
                        && !justFiredJinpu && !justFiredShifu;

        // Step 2 finishers at p6 — no early return; starter at p7 is ActionStatus fallback (PLD parity)
        if (((comboStep == 2 && context.LastComboAction == SAMActions.Jinpu.ActionId) || justFiredJinpu) &&
            level >= SAMActions.Gekko.MinLevel)
        {
            bool correctPositional = context.IsAtRear || context.HasTrueNorth || context.TargetHasPositionalImmunity;
            scheduler.PushGcd(NikeAbilities.Gekko, target.GameObjectId, priority: 6,
                onDispatched: _ =>
                {
                    context.Debug.PlannedAction = SAMActions.Gekko.Name;
                    context.Debug.DamageState = $"Gekko {(correctPositional ? "(rear)" : "(WRONG)")}";

                    TrainingHelper.Decision(context.TrainingService)
                        .Action(SAMActions.Gekko.ActionId, SAMActions.Gekko.Name)
                        .AsPositional(correctPositional, "rear")
                        .Target(target.Name?.TextValue ?? "Target")
                        .Reason("Combo finisher Gekko for Getsu Sen",
                            "Gekko is the finisher after Jinpu. Grants Getsu (Moon) Sen and refreshes Fugetsu buff. " +
                            "Has a rear positional for bonus damage and extra Kenki.")
                        .Factors(new[] { "Combo step 2 (after Jinpu)", correctPositional ? "At rear" : "Not at rear", "Grants Getsu Sen" })
                        .Alternatives(new[] { "Break combo (miss finisher)", "Wrong positional (less damage)" })
                        .Tip("Gekko = rear. Position before finisher or use True North.")
                        .Concept("sam_positionals")
                        .Record();
                    context.TrainingService?.RecordConceptApplication("sam_positionals", correctPositional, "Gekko combo rear");
                });
        }

        if (((comboStep == 2 && context.LastComboAction == SAMActions.Shifu.ActionId) || justFiredShifu) &&
            level >= SAMActions.Kasha.MinLevel)
        {
            bool correctPositional = context.IsAtFlank || context.HasTrueNorth || context.TargetHasPositionalImmunity;
            scheduler.PushGcd(NikeAbilities.Kasha, target.GameObjectId, priority: 6,
                onDispatched: _ =>
                {
                    context.Debug.PlannedAction = SAMActions.Kasha.Name;
                    context.Debug.DamageState = $"Kasha {(correctPositional ? "(flank)" : "(WRONG)")}";

                    TrainingHelper.Decision(context.TrainingService)
                        .Action(SAMActions.Kasha.ActionId, SAMActions.Kasha.Name)
                        .AsPositional(correctPositional, "flank")
                        .Target(target.Name?.TextValue ?? "Target")
                        .Reason("Combo finisher Kasha for Ka Sen",
                            "Kasha is the finisher after Shifu. Grants Ka (Flower) Sen and refreshes Fuka buff. " +
                            "Has a flank positional for bonus damage and extra Kenki.")
                        .Factors(new[] { "Combo step 2 (after Shifu)", correctPositional ? "At flank" : "Not at flank", "Grants Ka Sen" })
                        .Alternatives(new[] { "Break combo (miss finisher)", "Wrong positional (less damage)" })
                        .Tip("Kasha = flank. Position before finisher or use True North.")
                        .Concept("sam_positionals")
                        .Record();
                    context.TrainingService?.RecordConceptApplication("sam_positionals", correctPositional, "Kasha combo flank");
                });
        }

        // Step 1 at p6 — no early return
        if (onStarter)
        {
            // Ends-first: refresh whichever buff expires sooner so neither drops.
            var fugetsuUrgent = (!context.HasFugetsu || context.FugetsuRemaining < 10f) && level >= SAMActions.Jinpu.MinLevel;
            var fukaUrgent    = (!context.HasFuka    || context.FukaRemaining    < 10f) && level >= SAMActions.Shifu.MinLevel;
            var fugetsuFirst  = fugetsuUrgent && (!fukaUrgent || context.FugetsuRemaining <= context.FukaRemaining);

            if (fugetsuFirst)
            {
                PushComboStep2(context, scheduler, target, SAMActions.Jinpu, NikeAbilities.Jinpu,
                    $"Jinpu to refresh Fugetsu ({(context.HasFugetsu ? $"{context.FugetsuRemaining:F1}s left" : "missing")})",
                    "Jinpu refreshes Fugetsu (+13% damage up). Keep this buff active at all times. " +
                    "Follow with Gekko to grant Getsu Sen and deal positional damage.",
                    new[] { "Combo step 2 (after Hakaze/Gyofu)", context.HasFugetsu ? $"Fugetsu {context.FugetsuRemaining:F1}s" : "Fugetsu missing", "Refreshing buff" },
                    "Keep both Fugetsu and Fuka up. They expire in 40s — refresh before 10s.",
                    "sam_fugetsu_fuka", "Fugetsu refresh via Jinpu");
            }
            else if (fukaUrgent)
            {
                PushComboStep2(context, scheduler, target, SAMActions.Shifu, NikeAbilities.Shifu,
                    $"Shifu to refresh Fuka ({(context.HasFuka ? $"{context.FukaRemaining:F1}s left" : "missing")})",
                    "Shifu refreshes Fuka (+13% haste). Keep this buff active at all times. " +
                    "Follow with Kasha to grant Ka Sen and deal positional damage.",
                    new[] { "Combo step 2 (after Hakaze/Gyofu)", context.HasFuka ? $"Fuka {context.FukaRemaining:F1}s" : "Fuka missing", "Refreshing buff" },
                    "Fuka increases GCD speed. Letting it drop costs DPS.",
                    "sam_fugetsu_fuka", "Fuka refresh via Shifu");
            }
            else if (!context.HasSetsu && level >= SAMActions.Yukikaze.MinLevel)
            {
                PushComboStep2(context, scheduler, target, SAMActions.Yukikaze, NikeAbilities.Yukikaze,
                    "Yukikaze for Setsu Sen (missing)",
                    "Yukikaze grants Setsu (Snow) Sen when used as a combo finisher. " +
                    "You need all 3 Sen for Midare Setsugekka. No positional required.",
                    new[] { "Combo step 2", "Setsu Sen missing", "No positional needed" },
                    "Yukikaze is the easiest Sen to build — no positional requirement.",
                    "sam_sen_system", "Setsu Sen via Yukikaze");
            }
            else if (!context.HasGetsu && level >= SAMActions.Jinpu.MinLevel)
            {
                PushComboStep2(context, scheduler, target, SAMActions.Jinpu, NikeAbilities.Jinpu,
                    "Jinpu to build Getsu Sen (missing)",
                    "Jinpu leads to Gekko which grants Getsu (Moon) Sen. " +
                    "Also refreshes Fugetsu buff. Has a rear positional for bonus Kenki.",
                    new[] { "Combo step 2", "Getsu Sen missing", "Fugetsu maintained" },
                    "Jinpu → Gekko (rear) builds Getsu and refreshes Fugetsu.",
                    "sam_sen_system", "Getsu path via Jinpu");
            }
            else if (!context.HasKa && level >= SAMActions.Shifu.MinLevel)
            {
                PushComboStep2(context, scheduler, target, SAMActions.Shifu, NikeAbilities.Shifu,
                    "Shifu to build Ka Sen (missing)",
                    "Shifu leads to Kasha which grants Ka (Flower) Sen. " +
                    "Also refreshes Fuka buff. Has a flank positional for bonus Kenki.",
                    new[] { "Combo step 2", "Ka Sen missing", "Fuka maintained" },
                    "Shifu → Kasha (flank) builds Ka and refreshes Fuka.",
                    "sam_sen_system", "Ka path via Shifu");
            }
            else if (level >= SAMActions.Jinpu.MinLevel)
            {
                PushComboStep2(context, scheduler, target, SAMActions.Jinpu, NikeAbilities.Jinpu,
                    "Jinpu (combo step 2 — default path)",
                    "Jinpu leads to Gekko for Getsu Sen and refreshes Fugetsu. " +
                    "Used as the default mid-combo step when no specific Sen or buff is urgent.",
                    new[] { "Combo step 2", "Buffs maintained", "Default rotation" },
                    "Rotate Jinpu and Shifu paths evenly to maintain both Fugetsu and Fuka.",
                    "sam_fugetsu_fuka", "Default Jinpu path");
            }
        }

        // Combo starter / fallback at p7
        if (level >= comboStarter.MinLevel)
        {
            scheduler.PushGcd(starterAbility, target.GameObjectId, priority: 7,
                onDispatched: _ =>
                {
                    context.Debug.PlannedAction = comboStarter.Name;
                    context.Debug.DamageState = $"{comboStarter.Name} (Combo 1)";

                    TrainingHelper.Decision(context.TrainingService)
                        .Action(comboStarter.ActionId, comboStarter.Name)
                        .AsMeleeDamage()
                        .Target(target.Name?.TextValue ?? "Target")
                        .Reason($"{comboStarter.Name} — starting single-target combo",
                            $"{comboStarter.Name} is SAM's combo opener. It leads to Jinpu, Shifu, or Yukikaze. " +
                            "Always follow up — breaking combo wastes potency.")
                        .Factors(new[] { "Combo step 1", "No active combo", "Starting rotation" })
                        .Alternatives(new[] { "Use Meikyo to skip combo (if active)" })
                        .Tip($"After {comboStarter.Name}: Jinpu → Gekko (rear) or Shifu → Kasha (flank) or Yukikaze.")
                        .Concept("sam_combo_rotation")
                        .Record();
                    context.TrainingService?.RecordConceptApplication("sam_combo_rotation", true, $"Combo starter {comboStarter.Name}");
                });
        }
    }

    private static void PushComboStep2(INikeContext context, RotationScheduler scheduler, IBattleChara target,
                                        ActionDefinition action, AbilityBehavior ability,
                                        string reason, string explanation, string[] factors, string tip,
                                        string concept, string conceptApplication)
    {
        scheduler.PushGcd(ability, target.GameObjectId, priority: 6,
            onDispatched: _ =>
            {
                context.Debug.PlannedAction = action.Name;
                context.Debug.DamageState = $"{action.Name} (Combo 2)";

                TrainingHelper.Decision(context.TrainingService)
                    .Action(action.ActionId, action.Name)
                    .AsMeleeDamage()
                    .Target(target.Name?.TextValue ?? "Target")
                    .Reason(reason, explanation)
                    .Factors(factors)
                    .Alternatives(new[] { "Wait for buff alignment", "Switch combo path" })
                    .Tip(tip)
                    .Concept(concept)
                    .Record();
                context.TrainingService?.RecordConceptApplication(concept, true, conceptApplication);
            });
    }

    private void TryPushAoeCombo(INikeContext context, RotationScheduler scheduler, IBattleChara target)
    {
        var player = context.Player;
        var level = player.Level;
        var comboStep = context.ComboStep;
        var aoeStarter = SAMActions.GetAoeComboStarter((byte)level, context.ActionService);
        var starterAbility = aoeStarter == SAMActions.Fuko ? NikeAbilities.Fuko : NikeAbilities.Fuga;
        // Dual-source starter detection. The gauge combo-step read can lag (or briefly read 0) for a
        // frame or two after Fuko/Fuga fires; if we rely on it alone, onStarter is false, the finisher
        // is never pushed, and the p7 starter re-push gets blocked by ShouldBlockRepeatGcd (Fuko was the
        // last GCD) — the "Fuko lockup". Treating "we just dispatched the starter" as equivalent keeps the
        // finisher flowing. Use WasLastGcd, NOT WasLastAction: an oGCD weave (Shinten/Kyuten/Feint) after
        // the starter overwrites LastAction but combos only advance on GCDs, so WasLastGcd still points at
        // the starter. Clears as soon as the finisher GCD dispatches, so it can't double-fire.
        var justFiredStarter = context.ActionService.WasLastGcd(aoeStarter.ActionId)
                               || context.ActionService.WasLastGcd(SAMActions.Fuga.ActionId)
                               || context.ActionService.WasLastGcd(SAMActions.Fuko.ActionId);
        var onStarter = (comboStep == 1 &&
                         (context.LastComboAction == aoeStarter.ActionId ||
                          context.LastComboAction == SAMActions.Fuga.ActionId))
                        || justFiredStarter;

        // Step 2 finishers at p6 — no early return; starter at p7 is fallback (PLD parity)
        if (onStarter)
        {
            if ((!context.HasFugetsu || !context.HasGetsu) && level >= SAMActions.Mangetsu.MinLevel)
            {
                scheduler.PushGcd(NikeAbilities.Mangetsu, player.GameObjectId, priority: 6,
                    onDispatched: _ =>
                    {
                        context.Debug.PlannedAction = SAMActions.Mangetsu.Name;
                        context.Debug.DamageState = "Mangetsu (AoE 2)";

                        TrainingHelper.Decision(context.TrainingService)
                            .Action(SAMActions.Mangetsu.ActionId, SAMActions.Mangetsu.Name)
                            .AsAoE(0)
                            .Target("Nearby enemies")
                            .Reason($"Mangetsu for Getsu Sen{(!context.HasFugetsu ? " and Fugetsu" : "")} (AoE combo step 2)",
                                "Mangetsu grants Getsu (Moon) Sen and refreshes Fugetsu buff in AoE. " +
                                "Follow Fuko/Fuga with Mangetsu or Oka based on which Sen and buffs are needed.")
                            .Factors(new[] { "AoE combo step 2", context.HasGetsu ? "Fugetsu missing" : "Getsu Sen missing", "Multiple enemies" })
                            .Alternatives(new[] { "Oka (if Ka or Fuka more urgent)" })
                            .Tip("AoE combo: Fuko/Fuga → Mangetsu (Getsu) or Oka (Ka). Alternate for both.")
                            .Concept("sam_aoe_rotation")
                            .Record();
                        context.TrainingService?.RecordConceptApplication("sam_aoe_rotation", true, "AoE Mangetsu combo");
                    });
            }
            else if ((!context.HasFuka || !context.HasKa) && level >= SAMActions.Oka.MinLevel)
            {
                scheduler.PushGcd(NikeAbilities.Oka, player.GameObjectId, priority: 6,
                    onDispatched: _ =>
                    {
                        context.Debug.PlannedAction = SAMActions.Oka.Name;
                        context.Debug.DamageState = "Oka (AoE 2)";

                        TrainingHelper.Decision(context.TrainingService)
                            .Action(SAMActions.Oka.ActionId, SAMActions.Oka.Name)
                            .AsAoE(0)
                            .Target("Nearby enemies")
                            .Reason($"Oka for Ka Sen{(!context.HasFuka ? " and Fuka" : "")} (AoE combo step 2)",
                                "Oka grants Ka (Flower) Sen and refreshes Fuka buff in AoE. " +
                                "Alternate with Mangetsu to maintain both Fugetsu and Fuka.")
                            .Factors(new[] { "AoE combo step 2", context.HasKa ? "Fuka missing" : "Ka Sen missing", "Multiple enemies" })
                            .Alternatives(new[] { "Mangetsu (if Getsu or Fugetsu more urgent)" })
                            .Tip("AoE combo: Fuko/Fuga → Mangetsu (Getsu) or Oka (Ka). Alternate for both.")
                            .Concept("sam_aoe_rotation")
                            .Record();
                        context.TrainingService?.RecordConceptApplication("sam_aoe_rotation", true, "AoE Oka combo");
                    });
            }
            else if (level >= SAMActions.Mangetsu.MinLevel)
            {
                scheduler.PushGcd(NikeAbilities.Mangetsu, player.GameObjectId, priority: 6,
                    onDispatched: _ =>
                    {
                        context.Debug.PlannedAction = SAMActions.Mangetsu.Name;
                        context.Debug.DamageState = "Mangetsu (default)";

                        TrainingHelper.Decision(context.TrainingService)
                            .Action(SAMActions.Mangetsu.ActionId, SAMActions.Mangetsu.Name)
                            .AsAoE(0)
                            .Target("Nearby enemies")
                            .Reason("Mangetsu (AoE combo step 2 — default)",
                                "Default AoE combo finisher. Refreshes Fugetsu and grants Getsu Sen.")
                            .Factors(new[] { "AoE combo step 2", "Default finisher", "Multiple enemies" })
                            .Alternatives(new[] { "Oka (if Ka or Fuka more urgent)" })
                            .Tip("Keep alternating Mangetsu and Oka to maintain Fugetsu, Fuka, and both Sen.")
                            .Concept("sam_aoe_rotation")
                            .Record();
                        context.TrainingService?.RecordConceptApplication("sam_aoe_rotation", true, "AoE default Mangetsu");
                    });
            }
        }

        // AoE combo starter / fallback at p7
        if (level >= aoeStarter.MinLevel)
        {
            scheduler.PushGcd(starterAbility, player.GameObjectId, priority: 7,
                onDispatched: _ =>
                {
                    context.Debug.PlannedAction = aoeStarter.Name;
                    context.Debug.DamageState = $"{aoeStarter.Name} (AoE 1)";

                    TrainingHelper.Decision(context.TrainingService)
                        .Action(aoeStarter.ActionId, aoeStarter.Name)
                        .AsAoE(0)
                        .Target("Nearby enemies")
                        .Reason($"{aoeStarter.Name} — starting AoE combo",
                            $"{aoeStarter.Name} is the AoE combo opener. Follow with Mangetsu (Getsu) or Oka (Ka). " +
                            "Use instead of single-target combo at 3+ enemies.")
                        .Factors(new[] { "AoE combo step 1", "Multiple enemies", "Starting AoE rotation" })
                        .Alternatives(new[] { "Use Hakaze/Gyofu (fewer enemies)" })
                        .Tip($"AoE opener: {aoeStarter.Name} → Mangetsu or Oka → Tenka Goken.")
                        .Concept("sam_aoe_rotation")
                        .Record();
                    context.TrainingService?.RecordConceptApplication("sam_aoe_rotation", true, $"AoE starter {aoeStarter.Name}");
                });
        }
    }

    #endregion
}
