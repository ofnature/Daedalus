using Dalamud.Game.ClientState.Objects.Types;
using Daedalus.Config.DPS;
using Daedalus.Data;
using Daedalus.Models.Action;
using Daedalus.Rotation.ApolloCore.Helpers;
using Daedalus.Rotation.Common.Helpers;
using Daedalus.Rotation.Common.RoleActionHelpers;
using Daedalus.Rotation.Common.Scheduling;
using Daedalus.Rotation.KratosCore.Abilities;
using Daedalus.Rotation.KratosCore.Context;
using Daedalus.Rotation.KratosCore.Helpers;
using Daedalus.Services;
using Daedalus.Services.Action;
using Daedalus.Services.Positional;
using Daedalus.Services.Targeting;
using Daedalus.Services.Training;

namespace Daedalus.Rotation.KratosCore.Modules;

/// <summary>
/// Handles the Monk damage rotation (scheduler-driven).
/// Manages form cycling, positional optimization, Chakra spending, and burst windows.
/// </summary>
public sealed class DamageModule : IKratosModule
{
    /// <summary>Self-centered AoE radius for Arm of the Destroyer / Rockbreaker / Four-point Fury.</summary>
    private const float AoERadiusYalms = 5f;

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

    public bool TryExecute(IKratosContext context, bool isMoving) => false;

    public void UpdateDebugState(IKratosContext context) { }

    public void CollectCandidates(IKratosContext context, RotationScheduler scheduler, bool isMoving)
    {
        var player = context.Player;
        var level = player.Level;

        // Pre-combat: Meditation if Chakra below 5 (downtime build-up).
        if (!context.InCombat)
        {
            if (level >= MNKActions.Meditation.MinLevel && context.Chakra < 5)
            {
                scheduler.PushGcd(KratosAbilities.Meditation, player.GameObjectId, priority: 10,
                    onDispatched: _ =>
                    {
                        context.Debug.PlannedAction = MNKActions.Meditation.Name;
                        context.Debug.DamageState = $"Meditation ({context.Chakra}/5 Chakra, downtime)";
                    });
            }
            else
            {
                context.Debug.DamageState = "Not in combat";
            }
            return;
        }

        if (context.Configuration.Targeting.SuppressDamageOnForcedMovement
            && PlayerSafetyHelper.IsForcedMovementActive(context.Player))
        {
            context.Debug.DamageState = "Paused (forced movement)";
            return;
        }

        var target = context.TargetingService.FindEnemyForAction(
            context.Configuration.Targeting.EnemyStrategy,
            MNKActions.Bootshine.ActionId,
            player);
        if (target == null)
        {
            // Out of melee range — gap close, ranged Chakra spend, Meditation filler
            var farTarget = context.TargetingService.FindNearbyEnemy(25f, player);
            if (farTarget != null)
            {
                TryPushThunderclap(context, scheduler, farTarget);
                TryPushRangedFiller(context, scheduler, farTarget);
                context.Debug.DamageState = "Out of melee range";
                if (level >= MNKActions.Meditation.MinLevel && context.Chakra < 5)
                {
                    scheduler.PushGcd(KratosAbilities.Meditation, player.GameObjectId, priority: 10,
                        onDispatched: _ =>
                        {
                            context.Debug.PlannedAction = MNKActions.Meditation.Name;
                            context.Debug.DamageState = $"Meditation ({context.Chakra}/5 Chakra, out of range)";
                        });
                }
            }
            else
            {
                context.Debug.DamageState = "No target";
            }
            return;
        }

        var aoeEnabled = context.Configuration.Monk.EnableAoERotation;
        var aoeThreshold = context.Configuration.Monk.AoEMinTargets;
        var pack = EnemyPackDebugHelper.Count(context.TargetingService, AoERadiusYalms, player);
        EnemyPackDebugHelper.Apply(context.Debug, pack);
        var useAoE = aoeEnabled && pack.AoeRange >= aoeThreshold;

        // oGCDs
        TryPushFeint(context, scheduler, target);
        TryPushSecondWind(context, scheduler);
        TryPushBloodbath(context, scheduler);
        TryPushChakraSpender(context, scheduler, target, useAoE);
        TryPushThunderclap(context, scheduler, target);

        // GCDs (priority order: Blitz > Procs > PB GCD > Form rotation > Six-Sided Star > Ranged filler)
        TryPushMasterfulBlitz(context, scheduler, target, useAoE);
        TryPushFiresReply(context, scheduler, target);
        TryPushWindsReply(context, scheduler, target);
        TryPushPerfectBalanceAction(context, scheduler, target, useAoE);
        TryPushFormRotation(context, scheduler, target, useAoE);
        TryPushSixSidedStar(context, scheduler, target, isMoving);
        TryPushRangedFiller(context, scheduler, target);
    }

    private static bool ShouldSkipMnkPositional(
        IKratosContext context,
        bool correctPositional,
        string positionalName,
        ActionDefinition action)
    {
        if (correctPositional || context.HasTrueNorth || context.TargetHasPositionalImmunity)
            return false;

        if (!PositionalRequirementHelper.ShouldApply(context.Debug.EngagedEnemies))
            return false;

        if (!context.Configuration.Monk.EnforcePositionals
            && context.ActionService.CanExecuteActionId(action.ActionId))
            return false;

        if (!context.Configuration.Monk.EnforcePositionals)
            return false;

        if (context.Configuration.Monk.AllowPositionalLoss)
            return false;

        var current = context.IsAtRear ? "rear" : context.IsAtFlank ? "flank" : "front";
        context.Debug.PlannedAction = action.Name;
        context.Debug.DamageState = $"Moving to {positionalName} (detected {current})";
        return true;
    }

    #region oGCDs

    private void TryPushFeint(IKratosContext context, RotationScheduler scheduler, IBattleChara target)
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

        scheduler.PushOgcd(KratosAbilities.Feint, target.GameObjectId, priority: 7,
            onDispatched: _ =>
            {
                context.Debug.PlannedAction = RoleActions.Feint.Name;
                partyCoord?.OnCooldownUsed(RoleActions.Feint.ActionId, 90_000);
            });
    }

    private void TryPushSecondWind(IKratosContext context, RotationScheduler scheduler)
    {
        if (!context.Configuration.MeleeShared.EnableSecondWind) return;

        RoleActionPushers.TryPushSecondWind(
            context, scheduler, KratosAbilities.SecondWind,
            hpThresholdPct: context.Configuration.MeleeShared.SecondWindHpThreshold,
            priority: 6,
            onDispatched: _ => context.Debug.PlannedAction = RoleActions.SecondWind.Name);
    }

    private void TryPushBloodbath(IKratosContext context, RotationScheduler scheduler)
    {
        if (!context.Configuration.MeleeShared.EnableBloodbath) return;

        RoleActionPushers.TryPushBloodbath(
            context, scheduler, KratosAbilities.Bloodbath,
            hpThresholdPct: context.Configuration.MeleeShared.BloodbathHpThreshold,
            priority: 6,
            onDispatched: _ => context.Debug.PlannedAction = RoleActions.Bloodbath.Name);
    }

    private void TryPushChakraSpender(IKratosContext context, RotationScheduler scheduler, IBattleChara target, bool useAoE)
    {
        if (!context.Configuration.Monk.EnableChakraSpenders) return;
        var player = context.Player;
        var level = player.Level;
        if (context.Chakra < context.Configuration.Monk.ChakraMinGauge) return;
        if (context.Configuration.Monk.EnableBurstPooling && ShouldHoldForBurst(8f) && context.Chakra < 45) return;

        // Hold Chakra when Brotherhood is imminent — BH grants Chakra generation,
        // so spending at 4-5 right before BH wastes the incoming Chakra.
        if (level >= MNKActions.Brotherhood.MinLevel && context.Chakra < 5
            && context.ActionService.GetCooldownRemaining(MNKActions.Brotherhood.ActionId) is > 0f and < 10f)
            return;

        if (useAoE && level >= MNKActions.HowlingFist.MinLevel)
        {
            var aoeAction = MNKActions.GetAoeChakraSpender((byte)level, context.ActionService);
            var ability = level >= MNKActions.Enlightenment.MinLevel ? KratosAbilities.Enlightenment : KratosAbilities.HowlingFist;
            if (!context.ActionService.IsActionReady(aoeAction.ActionId)) return;

            // Priority 4: below the burst cooldowns (RoF 1 / Brotherhood 2 / Perfect Balance 3). With Chakra
            // capped at 5/5 the spender is eligible every weave; at priority 1 it starved Perfect Balance out
            // of every slot, so PB never fired and no Blitz happened. It still spends promptly in the many
            // weave slots the burst cooldowns don't use.
            scheduler.PushOgcd(ability, target.GameObjectId, priority: 4,
                onDispatched: _ =>
                {
                    context.Debug.PlannedAction = aoeAction.Name;
                    context.Debug.DamageState = $"{aoeAction.Name} (AoE)";

                    TrainingHelper.Decision(context.TrainingService)
                        .Action(aoeAction.ActionId, aoeAction.Name)
                        .AsAoE(0)
                        .Target("AoE group")
                        .Reason($"Spending 5 Chakra on {aoeAction.Name}",
                            "Enlightenment/Howling Fist is the AoE Chakra spender. " +
                            "Use at 5 Chakra stacks to avoid overcapping. High oGCD priority.")
                        .Factors(new[] { "5 Chakra stacks", "AoE situation", "Avoiding overcap" })
                        .Alternatives(new[] { "Use Forbidden Chakra (fewer enemies)", "Hold for burst (risky)" })
                        .Tip("Always spend Chakra at 5 stacks. AoE at 3+ enemies.")
                        .Concept("mnk_chakra_gauge")
                        .Record();
                    context.TrainingService?.RecordConceptApplication("mnk_chakra_gauge", true, "AoE Chakra spending");
                });
            return;
        }

        if (level >= MNKActions.SteelPeak.MinLevel)
        {
            var stAction = MNKActions.GetChakraSpender((byte)level, context.ActionService);
            var ability = level >= MNKActions.TheForbiddenChakra.MinLevel ? KratosAbilities.TheForbiddenChakra : KratosAbilities.SteelPeak;
            if (!context.ActionService.IsActionReady(stAction.ActionId)) return;

            // Priority 4: below the burst cooldowns so it never starves Perfect Balance (see AoE branch).
            scheduler.PushOgcd(ability, target.GameObjectId, priority: 4,
                onDispatched: _ =>
                {
                    context.Debug.PlannedAction = stAction.Name;
                    context.Debug.DamageState = stAction.Name;

                    TrainingHelper.Decision(context.TrainingService)
                        .Action(stAction.ActionId, stAction.Name)
                        .AsMeleeResource("Chakra", context.Chakra)
                        .Target(target.Name?.TextValue ?? "Target")
                        .Reason($"Spending 5 Chakra on {stAction.Name}",
                            "Forbidden Chakra is MNK's ST Chakra spender. " +
                            "Use at 5 stacks to avoid overcapping. Weave during burst windows.")
                        .Factors(new[] { "5 Chakra stacks", "ST damage filler", "Avoiding overcap" })
                        .Alternatives(new[] { "Use Enlightenment (3+ enemies)", "Hold for burst (risky)" })
                        .Tip("Chakra generates passively and from crits. Spend at 5 stacks.")
                        .Concept("mnk_chakra_gauge")
                        .Record();
                    context.TrainingService?.RecordConceptApplication("mnk_chakra_gauge", true, "ST Chakra spending");
                });
        }
    }

    private void TryPushThunderclap(IKratosContext context, RotationScheduler scheduler, IBattleChara target)
    {
        if (!context.Configuration.Monk.EnableThunderclap) return;
        var player = context.Player;
        if (player.Level < MNKActions.Thunderclap.MinLevel) return;
        if (DistanceHelper.IsActionInRange(MNKActions.Bootshine.ActionId, player, target)) return;

        var dx = player.Position.X - target.Position.X;
        var dz = player.Position.Z - target.Position.Z;
        var distance = (float)System.Math.Sqrt(dx * dx + dz * dz);
        if (distance > 20f) return;
        if (!context.ActionService.IsActionReady(MNKActions.Thunderclap.ActionId)) return;

        if (context.TargetingService.GapCloserSafety.ShouldBlockGapCloser(target, player))
        {
            context.Debug.DamageState = $"Thunderclap blocked: {context.TargetingService.GapCloserSafety.LastBlockReason}";
            return;
        }

        scheduler.PushOgcd(KratosAbilities.Thunderclap, target.GameObjectId, priority: 2,
            onDispatched: _ =>
            {
                context.Debug.PlannedAction = MNKActions.Thunderclap.Name;
                context.Debug.DamageState = "Thunderclap (gap close)";

                TrainingHelper.Decision(context.TrainingService)
                    .Action(MNKActions.Thunderclap.ActionId, MNKActions.Thunderclap.Name)
                    .AsMeleeDamage()
                    .Target(target.Name?.TextValue ?? "Target")
                    .Reason("Thunderclap to close gap and re-enter melee range",
                        "Thunderclap is MNK's gap closer. Use when out of melee range to quickly return to the target.")
                    .Factors(new[] { "Out of melee range", "Target within 20y", "Thunderclap ready" })
                    .Alternatives(new[] { "Sprint + run in (slower)", "Wait for target to move closer" })
                    .Tip("Thunderclap keeps you in melee range. Use freely when the gap opens.")
                    .Concept("mnk_positionals")
                    .Record();
                context.TrainingService?.RecordConceptApplication("mnk_positionals", true, "Gap close");
            });
    }

    #endregion

    #region GCDs

    private void TryPushMasterfulBlitz(IKratosContext context, RotationScheduler scheduler, IBattleChara target, bool useAoE)
    {
        if (!context.Configuration.Monk.EnableMasterfulBlitz) return;
        var player = context.Player;
        var level = player.Level;
        if (level < MNKActions.ElixirField.MinLevel) return;
        if (context.BeastChakraCount < 3) return;

        // RoF gate: only Blitz during RoF or within 42s of RoF ending (covers odd-minute windows).
        // Without this, Blitz fires immediately and drifts out of burst alignment.
        if (level >= MNKActions.RiddleOfFire.MinLevel
            && !context.HasRiddleOfFire
            && context.ActionService.GetCooldownRemaining(MNKActions.RiddleOfFire.ActionId) > 18f)
            return;

        var blitzAction = MNKActions.GetBlitzAction(
            (byte)level,
            context.HasLunarNadi,
            context.HasSolarNadi,
            (MNKActions.BeastChakraType)context.BeastChakra1,
            (MNKActions.BeastChakraType)context.BeastChakra2,
            (MNKActions.BeastChakraType)context.BeastChakra3);
        if (blitzAction == null) return;

        var ability = blitzAction.ActionId switch
        {
            var id when id == MNKActions.PhantomRush.ActionId => KratosAbilities.PhantomRush,
            var id when id == MNKActions.RisingPhoenix.ActionId => KratosAbilities.RisingPhoenix,
            var id when id == MNKActions.ElixirBurst.ActionId => KratosAbilities.ElixirBurst,
            var id when id == MNKActions.ElixirField.ActionId => KratosAbilities.ElixirField,
            var id when id == MNKActions.FlintStrike.ActionId => KratosAbilities.FlintStrike,
            var id when id == MNKActions.CelestialRevolution.ActionId => KratosAbilities.CelestialRevolution,
            _ => KratosAbilities.ElixirField,
        };

        scheduler.PushGcd(ability, target.GameObjectId, priority: 1,
            onDispatched: _ =>
            {
                context.Debug.PlannedAction = blitzAction.Name;
                context.Debug.DamageState = $"{blitzAction.Name} (Blitz)";

                TrainingHelper.Decision(context.TrainingService)
                    .Action(blitzAction.ActionId, blitzAction.Name)
                    .AsMeleeBurst()
                    .Target(target.Name?.TextValue ?? "Target")
                    .Reason($"Executing {blitzAction.Name} (3 Beast Chakra accumulated)",
                        $"Blitz attack from 3 Beast Chakra. Nadi state: Lunar={context.HasLunarNadi}, Solar={context.HasSolarNadi}.")
                    .Factors(new[] { "3 Beast Chakra ready", "Blitz available" })
                    .Alternatives(new[] { "Can't - must use Blitz when available" })
                    .Tip("Use Perfect Balance to build Beast Chakra quickly. Phantom Rush requires both Nadi.")
                    .Concept("mnk_beast_chakra")
                    .Record();
                context.TrainingService?.RecordConceptApplication("mnk_beast_chakra", true, $"Blitz: {blitzAction.Name}");
            });
    }

    private void TryPushFiresReply(IKratosContext context, RotationScheduler scheduler, IBattleChara target)
    {
        if (!context.Configuration.Monk.EnableFiresReply) return;
        var player = context.Player;
        if (player.Level < MNKActions.FiresReply.MinLevel) return;
        if (!context.HasFiresRumination) return;
        if (context.HasPerfectBalance) return;
        if (context.HasFormlessFist) return;
        if (context.CurrentForm != MonkForm.OpoOpo && context.CurrentForm != MonkForm.None) return;

        scheduler.PushGcd(KratosAbilities.FiresReply, target.GameObjectId, priority: 2,
            onDispatched: _ =>
            {
                context.Debug.PlannedAction = MNKActions.FiresReply.Name;
                context.Debug.DamageState = "Fire's Reply";

                TrainingHelper.Decision(context.TrainingService)
                    .Action(MNKActions.FiresReply.ActionId, MNKActions.FiresReply.Name)
                    .AsMeleeDamage()
                    .Target(target.Name?.TextValue ?? "Target")
                    .Reason("Using Fire's Reply proc (from Riddle of Fire)",
                        "Fire's Reply is a high-potency proc that appears after Riddle of Fire. " +
                        "Has a 30s window to use. Use before it expires for free damage.")
                    .Factors(new[] { "Fire's Rumination proc active", "High potency GCD", "Free damage" })
                    .Alternatives(new[] { "Let it expire (wastes damage)" })
                    .Tip("Fire's Reply is free damage. Use it before the 30s window expires.")
                    .Concept("mnk_riddle_of_fire")
                    .Record();
                context.TrainingService?.RecordConceptApplication("mnk_riddle_of_fire", true, "Fire's Reply proc");
            });
    }

    private void TryPushWindsReply(IKratosContext context, RotationScheduler scheduler, IBattleChara target)
    {
        if (!context.Configuration.Monk.EnableWindsReply) return;
        var player = context.Player;
        if (player.Level < MNKActions.WindsReply.MinLevel) return;
        if (!context.HasWindsRumination) return;

        scheduler.PushGcd(KratosAbilities.WindsReply, target.GameObjectId, priority: 2,
            onDispatched: _ =>
            {
                context.Debug.PlannedAction = MNKActions.WindsReply.Name;
                context.Debug.DamageState = "Wind's Reply";

                TrainingHelper.Decision(context.TrainingService)
                    .Action(MNKActions.WindsReply.ActionId, MNKActions.WindsReply.Name)
                    .AsMeleeDamage()
                    .Target(target.Name?.TextValue ?? "Target")
                    .Reason("Using Wind's Reply proc (from Riddle of Wind)",
                        "Wind's Reply is a high-potency proc that appears after Riddle of Wind. " +
                        "Has a 30s window to use. Use before it expires for free damage.")
                    .Factors(new[] { "Wind's Rumination proc active", "High potency GCD", "Free damage" })
                    .Alternatives(new[] { "Let it expire (wastes damage)" })
                    .Tip("Wind's Reply is free damage. Use it before the 30s window expires.")
                    .Concept("mnk_riddle_of_wind")
                    .Record();
                context.TrainingService?.RecordConceptApplication("mnk_riddle_of_wind", true, "Wind's Reply proc");
            });
    }

    private void TryPushPerfectBalanceAction(IKratosContext context, RotationScheduler scheduler, IBattleChara target, bool useAoE)
    {
        if (!context.HasPerfectBalance) return;
        if (context.PerfectBalanceStacks <= 0) return;

        var action = GetPerfectBalanceAction(context, useAoE);
        if (action == null) return;

        var ability = MapToAbility(action);

        scheduler.PushGcd(ability, target.GameObjectId, priority: 3,
            onDispatched: _ =>
            {
                context.Debug.PlannedAction = action.Name;
                context.Debug.DamageState = $"{action.Name} (PB: {context.PerfectBalanceStacks} stacks)";

                var pbInfo = GetPerfectBalanceExplanation(context);
                TrainingHelper.Decision(context.TrainingService)
                    .Action(action.ActionId, action.Name)
                    .AsMeleeBurst()
                    .Target(target.Name?.TextValue ?? "Target")
                    .Reason($"Perfect Balance GCD: {action.Name} (building {pbInfo.target})",
                        pbInfo.explanation)
                    .Factors(new[] { $"Perfect Balance ({context.PerfectBalanceStacks} stacks)", pbInfo.beastChakraState, $"Building {pbInfo.target}" })
                    .Alternatives(new[] { "Wrong GCD (misbuilds Blitz)" })
                    .Tip(pbInfo.tip)
                    .Concept("mnk_perfect_balance")
                    .Record();
                context.TrainingService?.RecordConceptApplication("mnk_perfect_balance", true, $"PB GCD: {action.Name}");
            });
    }

    private void TryPushFormRotation(IKratosContext context, RotationScheduler scheduler, IBattleChara target, bool useAoE)
    {
        var form = context.CurrentForm;

        // MNK GCDs are NOT rejected by UseAction for wrong form — the game accepts them
        // but without the form bonus. Must select the correct form GCD based on status.
        // Formless Fist / no form → default to Opo-opo (RSR pattern).
        if (context.HasFormlessFist || form == MonkForm.Formless || form == MonkForm.None)
        {
            TryPushOpoOpo(context, scheduler, target, useAoE);
            return;
        }

        switch (form)
        {
            case MonkForm.OpoOpo: TryPushOpoOpo(context, scheduler, target, useAoE); break;
            case MonkForm.Raptor: TryPushRaptor(context, scheduler, target, useAoE); break;
            case MonkForm.Coeurl: TryPushCoeurl(context, scheduler, target, useAoE); break;
            default: TryPushOpoOpo(context, scheduler, target, useAoE); break;
        }
    }

    private void TryPushOpoOpo(IKratosContext context, RotationScheduler scheduler, IBattleChara target, bool useAoE)
    {
        var level = context.Player.Level;

        if (useAoE)
        {
            var aoeAction = level >= MNKActions.ShadowOfTheDestroyer.MinLevel
                ? MNKActions.ShadowOfTheDestroyer : MNKActions.ArmOfTheDestroyer;
            var ability = level >= MNKActions.ShadowOfTheDestroyer.MinLevel
                ? KratosAbilities.ShadowOfTheDestroyer : KratosAbilities.ArmOfTheDestroyer;

            scheduler.PushGcd(ability, context.Player.GameObjectId, priority: 5,
                onDispatched: _ =>
                {
                    context.Debug.PlannedAction = aoeAction.Name;
                    context.Debug.DamageState = $"{aoeAction.Name} (Opo-opo)";

                    TrainingHelper.Decision(context.TrainingService)
                        .Action(aoeAction.ActionId, aoeAction.Name)
                        .AsAoE(0)
                        .Target("AoE group")
                        .Reason($"Opo-opo AoE: {aoeAction.Name}",
                            "Shadow/Arm of the Destroyer is the Opo-opo AoE GCD.")
                        .Factors(new[] { "Opo-opo form", "AoE situation", "Form rotation" })
                        .Alternatives(new[] { "Single-target Bootshine/Dragon Kick (fewer enemies)" })
                        .Tip("Use AoE GCDs when 3+ enemies are in melee range.")
                        .Concept("mnk_positionals")
                        .Record();
                    context.TrainingService?.RecordConceptApplication("mnk_positionals", true, "AoE Opo-opo GCD");
                });
            return;
        }

        var action = GetOpoOpoAction(context, (uint)level);

        bool isRearPositional = action == MNKActions.Bootshine || action == MNKActions.LeapingOpo;
        string positional = isRearPositional ? "(rear)" : "(flank)";
        string positionalName = isRearPositional ? "rear" : "flank";
        bool correctPositional = isRearPositional
            ? (context.IsAtRear || context.HasTrueNorth || context.TargetHasPositionalImmunity)
            : (context.IsAtFlank || context.HasTrueNorth || context.TargetHasPositionalImmunity);
        if (ShouldSkipMnkPositional(context, correctPositional, positionalName, action)) return;

        var stAbility = MapToAbility(action);
        scheduler.PushGcd(stAbility, target.GameObjectId, priority: 5,
            onDispatched: _ =>
            {
                context.Debug.PlannedAction = action.Name;
                context.Debug.DamageState = $"{action.Name} {positional}";

                var procStatus = context.HasLeadenFist ? "Leaden Fist active" : (context.HasOpooposFury ? "Opo-opo's Fury active" : "No proc");
                TrainingHelper.Decision(context.TrainingService)
                    .Action(action.ActionId, action.Name)
                    .AsPositional(correctPositional, positionalName)
                    .Target(target.Name?.TextValue ?? "Target")
                    .Reason($"Opo-opo form: {action.Name} {(correctPositional ? positional : "(WRONG)")}",
                        action == MNKActions.DragonKick
                            ? "Dragon Kick (flank) grants Leaden Fist buff for your next Bootshine."
                            : "Bootshine/Leaping Opo (rear) consumes Leaden Fist for bonus damage.")
                    .Factors(new[] { "Opo-opo form", procStatus, correctPositional ? $"At {positionalName}" : $"Not at {positionalName}" })
                    .Alternatives(new[] { "Wrong positional (less damage)", "Wrong GCD (miss proc)" })
                    .Tip("Opo-opo: Dragon Kick (flank) → Bootshine (rear) alternation for Leaden Fist.")
                    .Concept("mnk_positionals")
                    .Record();
                context.TrainingService?.RecordConceptApplication("mnk_positionals", correctPositional, $"Opo-opo {positionalName}");
            });
    }

    private void TryPushRaptor(IKratosContext context, RotationScheduler scheduler, IBattleChara target, bool useAoE)
    {
        var level = context.Player.Level;

        if (useAoE && ActionAvailability.MeetsLevelAndLearned(level, context.ActionService, MNKActions.FourPointFury))
        {
            context.Debug.PlannedAction = MNKActions.FourPointFury.Name;
            context.Debug.DamageState = "Four-point Fury (Raptor)";

            scheduler.PushGcd(KratosAbilities.FourPointFury, context.Player.GameObjectId, priority: 5,
                onDispatched: _ =>
                {
                    TrainingHelper.Decision(context.TrainingService)
                        .Action(MNKActions.FourPointFury.ActionId, MNKActions.FourPointFury.Name)
                        .AsAoE(0)
                        .Target("AoE group")
                        .Reason("Raptor AoE: Four-point Fury",
                            "Four-point Fury is the Raptor form AoE GCD. Also refreshes Disciplined Fist.")
                        .Factors(new[] { "Raptor form", "AoE situation", "Refreshes Disciplined Fist" })
                        .Alternatives(new[] { "Single-target True Strike/Twin Snakes (fewer enemies)" })
                        .Tip("Four-point Fury refreshes Disciplined Fist while doing AoE damage.")
                        .Concept("mnk_positionals")
                        .Record();
                    context.TrainingService?.RecordConceptApplication("mnk_positionals", true, "AoE Raptor GCD");
                });
            return;
        }

        var action = GetRaptorAction(context, (uint)level);

        bool isRearPositional = action == MNKActions.TrueStrike || action == MNKActions.RisingRaptor;
        string positional = isRearPositional ? "(rear)" : "(flank)";
        string positionalName = isRearPositional ? "rear" : "flank";
        bool correctPositional = isRearPositional
            ? (context.IsAtRear || context.HasTrueNorth || context.TargetHasPositionalImmunity)
            : (context.IsAtFlank || context.HasTrueNorth || context.TargetHasPositionalImmunity);
        if (ShouldSkipMnkPositional(context, correctPositional, positionalName, action)) return;

        var stAbility = MapToAbility(action);
        scheduler.PushGcd(stAbility, target.GameObjectId, priority: 5,
            onDispatched: _ =>
            {
                context.Debug.PlannedAction = action.Name;
                context.Debug.DamageState = $"{action.Name} {positional}";

                var buffStatus = $"Disciplined Fist: {(context.HasDisciplinedFist ? $"{context.DisciplinedFistRemaining:F1}s" : "missing")}";
                TrainingHelper.Decision(context.TrainingService)
                    .Action(action.ActionId, action.Name)
                    .AsPositional(correctPositional, positionalName)
                    .Target(target.Name?.TextValue ?? "Target")
                    .Reason($"Raptor form: {action.Name} {(correctPositional ? positional : "(WRONG)")}",
                        action == MNKActions.TwinSnakes
                            ? "Twin Snakes (flank) refreshes Disciplined Fist (+15% damage buff)."
                            : "True Strike/Rising Raptor (rear) is pure damage when buff is healthy.")
                    .Factors(new[] { "Raptor form", buffStatus, correctPositional ? $"At {positionalName}" : $"Not at {positionalName}" })
                    .Alternatives(new[] { "Let Disciplined Fist drop (lose 15% damage)", "Wrong positional (less damage)" })
                    .Tip("Raptor: Twin Snakes (flank) to refresh buff, True Strike (rear) for damage.")
                    .Concept("mnk_positionals")
                    .Record();
                context.TrainingService?.RecordConceptApplication("mnk_positionals", correctPositional, $"Raptor {positionalName}");
            });
    }

    private void TryPushCoeurl(IKratosContext context, RotationScheduler scheduler, IBattleChara target, bool useAoE)
    {
        var level = context.Player.Level;

        if (useAoE && level >= MNKActions.Rockbreaker.MinLevel)
        {
            scheduler.PushGcd(KratosAbilities.Rockbreaker, context.Player.GameObjectId, priority: 5,
                onDispatched: _ =>
                {
                    context.Debug.PlannedAction = MNKActions.Rockbreaker.Name;
                    context.Debug.DamageState = "Rockbreaker (Coeurl)";

                    TrainingHelper.Decision(context.TrainingService)
                        .Action(MNKActions.Rockbreaker.ActionId, MNKActions.Rockbreaker.Name)
                        .AsAoE(0)
                        .Target("AoE group")
                        .Reason("Coeurl AoE: Rockbreaker",
                            "Rockbreaker is the Coeurl form AoE GCD.")
                        .Factors(new[] { "Coeurl form", "AoE situation", "Form rotation" })
                        .Alternatives(new[] { "Single-target Snap Punch/Demolish (fewer enemies)" })
                        .Tip("Rockbreaker is your Coeurl AoE GCD. Prioritize Demolish on single targets.")
                        .Concept("mnk_positionals")
                        .Record();
                    context.TrainingService?.RecordConceptApplication("mnk_positionals", true, "AoE Coeurl GCD");
                });
            return;
        }

        var action = GetCoeurlAction(context, (uint)level);

        bool isRearPositional = action == MNKActions.Demolish || action == MNKActions.PouncingCoeurl;
        string positional = isRearPositional ? "(rear)" : "(flank)";
        string positionalName = isRearPositional ? "rear" : "flank";
        bool correctPositional = isRearPositional
            ? (context.IsAtRear || context.HasTrueNorth || context.TargetHasPositionalImmunity)
            : (context.IsAtFlank || context.HasTrueNorth || context.TargetHasPositionalImmunity);
        if (ShouldSkipMnkPositional(context, correctPositional, positionalName, action)) return;

        var stAbility = MapToAbility(action);
        scheduler.PushGcd(stAbility, target.GameObjectId, priority: 5,
            onDispatched: _ =>
            {
                context.Debug.PlannedAction = action.Name;
                context.Debug.DamageState = $"{action.Name} {positional}";

                var dotStatus = $"Demolish: {(context.HasDemolishOnTarget ? $"{context.DemolishRemaining:F1}s" : "missing")}";
                TrainingHelper.Decision(context.TrainingService)
                    .Action(action.ActionId, action.Name)
                    .AsPositional(correctPositional, positionalName)
                    .Target(target.Name?.TextValue ?? "Target")
                    .Reason($"Coeurl form: {action.Name} {(correctPositional ? positional : "(WRONG)")}",
                        action == MNKActions.Demolish
                            ? "Demolish (rear) applies/refreshes DoT. Refresh when <3s remains."
                            : "Snap Punch/Pouncing Coeurl (flank) is pure damage when DoT is healthy.")
                    .Factors(new[] { "Coeurl form", dotStatus, correctPositional ? $"At {positionalName}" : $"Not at {positionalName}" })
                    .Alternatives(new[] { "Let Demolish drop (lose DoT damage)", "Wrong positional (less damage)" })
                    .Tip("Coeurl: Demolish (rear) to apply DoT, Snap Punch (flank) for damage.")
                    .Concept("mnk_positionals")
                    .Record();
                context.TrainingService?.RecordConceptApplication("mnk_positionals", correctPositional, $"Coeurl {positionalName}");
            });
    }

    #endregion

    private void TryPushSixSidedStar(IKratosContext context, RotationScheduler scheduler, IBattleChara target, bool isMoving)
    {
        if (!context.Configuration.Monk.EnableSixSidedStar) return;
        var player = context.Player;
        if (player.Level < MNKActions.SixSidedStar.MinLevel) return;
        // Movement-only filler. Six-sided Star does NOT consume Chakra (the old Chakra>=5 gate was wrong and
        // kept it perpetually eligible, so it grabbed every gap the form combo left and spammed). It's a
        // single instant GCD with a move-speed buff — only worth pressing when you're moving and can't keep
        // the melee combo going. Priority 6 keeps it below the form rotation, so it never pre-empts the combo.
        if (!isMoving) return;
        scheduler.PushGcd(KratosAbilities.SixSidedStar, target.GameObjectId, priority: 6,
            onDispatched: _ =>
            {
                context.Debug.PlannedAction = MNKActions.SixSidedStar.Name;
                context.Debug.DamageState = "Six-Sided Star (movement filler)";

                TrainingHelper.Decision(context.TrainingService)
                    .Action(MNKActions.SixSidedStar.ActionId, MNKActions.SixSidedStar.Name)
                    .AsMeleeDamage()
                    .Target(target.Name?.TextValue ?? "Target")
                    .Reason("Six-Sided Star while moving",
                        "Six-Sided Star is a single instant GCD with a movement-speed buff. Use only while " +
                        "moving and unable to keep the melee combo going — it does not consume Chakra.")
                    .Factors(new[] { "Moving", "Melee combo interrupted" })
                    .Alternatives(new[] { "Resume the form combo when stationary" })
                    .Tip("Six-Sided Star is a movement filler, not a Chakra spender. Prefer the form combo.")
                    .Concept("mnk_chakra_gauge")
                    .Record();
                context.TrainingService?.RecordConceptApplication("mnk_chakra_gauge", true, "Six-Sided Star");
            });
    }

    private void TryPushRangedFiller(IKratosContext context, RotationScheduler scheduler, IBattleChara target)
    {
        var player = context.Player;
        var level = player.Level;
        if (level < MNKActions.HowlingFist.MinLevel) return;
        if (DistanceHelper.IsActionInRange(MNKActions.Bootshine.ActionId, player, target)) return;
        if (context.Chakra < 5) return;

        var action = MNKActions.GetAoeChakraSpender((byte)level, context.ActionService);
        if (!context.ActionService.IsActionReady(action.ActionId)) return;

        var ability = level >= MNKActions.Enlightenment.MinLevel
            ? KratosAbilities.Enlightenment : KratosAbilities.HowlingFist;

        scheduler.PushOgcd(ability, target.GameObjectId, priority: 1,
            onDispatched: _ =>
            {
                context.Debug.PlannedAction = action.Name;
                context.Debug.DamageState = $"{action.Name} (ranged filler)";
            });
    }

    #region Action selection helpers

    private static (string target, string explanation, string beastChakraState, string tip) GetPerfectBalanceExplanation(IKratosContext context)
    {
        var chakraStr = $"Beast: {(context.BeastChakra1 != 0 ? "Opo" : "")} {(context.BeastChakra2 != 0 ? "Raptor" : "")} {(context.BeastChakra3 != 0 ? "Coeurl" : "")}".Trim();
        if (string.IsNullOrWhiteSpace(chakraStr)) chakraStr = "None";

        if (context.HasBothNadi)
            return ("Phantom Rush", "Both Nadi active - 3 identical Opo-opo GCDs for Phantom Rush.", chakraStr, "Spam Opo-opo GCDs for the 1500-potency Phantom Rush.");
        if (KratosNadiPolicy.ShouldBuildSolar(context.HasLunarNadi, context.HasSolarNadi))
            return ("Solar Nadi", "Building one of each form (Opo → Raptor → Coeurl) for Rising Phoenix (Solar).", chakraStr, "Use a different form for each of the 3 Perfect Balance GCDs.");
        return ("Lunar Nadi", "Building 3 identical Opo-opo GCDs for Elixir Burst (Lunar).", chakraStr, "Use Opo-opo GCDs x3 for the Lunar Nadi.");
    }

    private ActionDefinition? GetPerfectBalanceAction(IKratosContext context, bool useAoe)
    {
        var level = context.Player.Level;

        var hasOpo = context.BeastChakra1 == 1 || context.BeastChakra2 == 1 || context.BeastChakra3 == 1;
        var hasRaptor = context.BeastChakra1 == 2 || context.BeastChakra2 == 2 || context.BeastChakra3 == 2;
        var hasCoeurl = context.BeastChakra1 == 3 || context.BeastChakra2 == 3 || context.BeastChakra3 == 3;

        // Build the nadi we're MISSING (so we eventually hold both → Phantom Rush). The old logic rebuilt
        // whichever nadi we already had and never reached Phantom Rush. See KratosNadiPolicy.
        var nextForm = KratosNadiPolicy.NextForm(
            context.HasLunarNadi, context.HasSolarNadi, hasOpo, hasRaptor, hasCoeurl);

        return nextForm switch
        {
            MnkPbForm.OpoOpo => useAoe
                ? (level >= MNKActions.ShadowOfTheDestroyer.MinLevel ? MNKActions.ShadowOfTheDestroyer : MNKActions.ArmOfTheDestroyer)
                : GetOpoOpoAction(context, (uint)level),
            MnkPbForm.Raptor => useAoe
                ? ResolveRaptorAoEAction(level, context.ActionService)
                : GetRaptorAction(context, (uint)level),
            MnkPbForm.Coeurl => useAoe
                ? (level >= MNKActions.Rockbreaker.MinLevel ? MNKActions.Rockbreaker : MNKActions.SnapPunch)
                : GetCoeurlAction(context, (uint)level),
            _ => GetOpoOpoAction(context, (uint)level),
        };
    }

    private static ActionDefinition ResolveRaptorAoEAction(byte level, IActionService actionService)
        => ActionAvailability.MeetsLevelAndLearned(level, actionService, MNKActions.FourPointFury)
            ? MNKActions.FourPointFury
            : MNKActions.TwinSnakes;

    private static ActionDefinition GetOpoOpoAction(IKratosContext context, uint level)
    {
        if (context.HasOpooposFury)
        {
            if (level >= MNKActions.LeapingOpo.MinLevel) return MNKActions.LeapingOpo;
            return MNKActions.Bootshine;
        }
        if (context.HasLeadenFist)
        {
            if (level >= MNKActions.LeapingOpo.MinLevel) return MNKActions.LeapingOpo;
            return MNKActions.Bootshine;
        }
        if (level >= MNKActions.DragonKick.MinLevel) return MNKActions.DragonKick;
        return MNKActions.Bootshine;
    }

    private static ActionDefinition GetRaptorAction(IKratosContext context, uint level)
    {
        // Rising Raptor is the Raptor's Fury spender — only usable with the proc.
        if (context.HasRaptorsFury && level >= MNKActions.RisingRaptor.MinLevel)
            return MNKActions.RisingRaptor;

        // Otherwise Twin Snakes is the standard raptor GCD (higher potency than True Strike). Do NOT gate on
        // Disciplined Fist: it never reads active on current patch and RSR's Monk logic doesn't track it.
        // (The old code returned Rising Raptor here with no Fury — an unusable action masked by the dead DF
        // read that would have surfaced the moment DF read true.)
        if (level >= MNKActions.TwinSnakes.MinLevel) return MNKActions.TwinSnakes;
        return MNKActions.TrueStrike;
    }

    private static ActionDefinition GetCoeurlAction(IKratosContext context, uint level)
    {
        if (context.HasCoeurlsFury)
        {
            if (level >= MNKActions.PouncingCoeurl.MinLevel) return MNKActions.PouncingCoeurl;
            if (level >= MNKActions.SnapPunch.MinLevel) return MNKActions.SnapPunch;
        }

        if (!context.HasDemolishOnTarget || context.DemolishRemaining < 3f)
        {
            if (level >= MNKActions.Demolish.MinLevel) return MNKActions.Demolish;
        }
        if (level >= MNKActions.PouncingCoeurl.MinLevel) return MNKActions.PouncingCoeurl;
        if (level >= MNKActions.SnapPunch.MinLevel) return MNKActions.SnapPunch;
        return level >= MNKActions.Demolish.MinLevel ? MNKActions.Demolish : MNKActions.SnapPunch;
    }

    private static AbilityBehavior MapToAbility(ActionDefinition action)
    {
        if (action == MNKActions.DragonKick) return KratosAbilities.DragonKick;
        if (action == MNKActions.Bootshine) return KratosAbilities.Bootshine;
        if (action == MNKActions.LeapingOpo) return KratosAbilities.LeapingOpo;
        if (action == MNKActions.TwinSnakes) return KratosAbilities.TwinSnakes;
        if (action == MNKActions.TrueStrike) return KratosAbilities.TrueStrike;
        if (action == MNKActions.RisingRaptor) return KratosAbilities.RisingRaptor;
        if (action == MNKActions.Demolish) return KratosAbilities.Demolish;
        if (action == MNKActions.SnapPunch) return KratosAbilities.SnapPunch;
        if (action == MNKActions.PouncingCoeurl) return KratosAbilities.PouncingCoeurl;
        if (action == MNKActions.ArmOfTheDestroyer) return KratosAbilities.ArmOfTheDestroyer;
        if (action == MNKActions.ShadowOfTheDestroyer) return KratosAbilities.ShadowOfTheDestroyer;
        if (action == MNKActions.FourPointFury) return KratosAbilities.FourPointFury;
        if (action == MNKActions.Rockbreaker) return KratosAbilities.Rockbreaker;
        return KratosAbilities.Bootshine; // Fallback
    }

    #endregion
}
