using Dalamud.Game.ClientState.Objects.Types;
using Daedalus.Config.DPS;
using Daedalus.Data;
using Daedalus.Models.Action;
using Daedalus.Rotation.Common.Helpers;
using Daedalus.Rotation.Common.Scheduling;
using Daedalus.Rotation.IrisCore.Abilities;
using Daedalus.Rotation.IrisCore.Context;
using Daedalus.Rotation.IrisCore.Helpers;
using Daedalus.Services;
using Daedalus.Services.Targeting;
using Daedalus.Services.Training;

namespace Daedalus.Rotation.IrisCore.Modules;

/// <summary>
/// Handles Pictomancer GCD rotation (scheduler-driven).
/// Includes pre-pull motif painting (out-of-combat) and in-combat damage rotation.
/// </summary>
public sealed class DamageModule : IIrisModule
{
    public int Priority => 50;
    public string Name => "Damage";

    private readonly IBurstWindowService? _burstWindowService;
    private readonly ISmartAoEService? _smartAoEService;

    public DamageModule(IBurstWindowService? burstWindowService = null, ISmartAoEService? smartAoEService = null)
    {
        _burstWindowService = burstWindowService;
        _smartAoEService = smartAoEService;
    }

    public bool TryExecute(IIrisContext context, bool isMoving) => false;

    public void UpdateDebugState(IIrisContext context) { }

    public void CollectCandidates(IIrisContext context, RotationScheduler scheduler, bool isMoving)
    {
        // Pre-pull motif painting (out-of-combat)
        if (!context.InCombat)
        {
            TryPushPrepaintMotif(context, scheduler);
            return;
        }

        if (context.IsCasting && !context.CanSlidecast) return;

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
        var target = context.TargetingService.FindEnemy(
            context.Configuration.Targeting.EnemyStrategy,
            FFXIVConstants.CasterTargetingRange,
            player);
        if (target == null)
        {
            context.Debug.DamageState = "No target";
            return;
        }

        // Hyperphantasia treats GCDs as instant — disable movement-only branches
        if (context.HasHyperphantasia) isMoving = false;

        // Addle (party mit utility)
        TryPushAddle(context, scheduler, target);

        TryPushMotifDuringInspiration(context, scheduler, isMoving);
        TryPushStarPrism(context, scheduler, target);
        TryPushRainbowDrip(context, scheduler, target, isMoving);
        TryPushHammerCombo(context, scheduler, target);
        TryPushCometInBlack(context, scheduler, target);
        TryPushHolyInWhite(context, scheduler, target, isMoving);
        TryPushSubtractiveCombo(context, scheduler, target);
        TryPushBaseCombo(context, scheduler, target);
        if (!isMoving) TryPushRepaintMotif(context, scheduler);
    }

    private ulong ResolveTargetId(IIrisContext context, IBattleChara target, uint actionId, bool useAoeBranch) =>
        IrisSmartAoEHelper.ResolveGcdTargetId(_smartAoEService, context, target, actionId, useAoeBranch);

    private void TryPushAddle(IIrisContext context, RotationScheduler scheduler, IBattleChara target)
    {
        var player = context.Player;
        if (player.Level < RoleActions.Addle.MinLevel) return;
        if (!context.ActionService.IsActionReady(RoleActions.Addle.ActionId)) return;

        var partyCoord = context.PartyCoordinationService;
        var coordConfig = context.Configuration.PartyCoordination;
        if (coordConfig.EnableCooldownCoordination &&
            partyCoord?.WasActionUsedByOther(RoleActions.Addle.ActionId, 15f) == true)
        {
            return;
        }

        scheduler.PushOgcd(IrisAbilities.Addle, target.GameObjectId, priority: 7,
            onDispatched: _ =>
            {
                context.Debug.PlannedAction = RoleActions.Addle.Name;
                partyCoord?.OnCooldownUsed(RoleActions.Addle.ActionId, 90_000);
            });
    }

    #region Pre-pull / Inspiration motif painting

    private void TryPushPrepaintMotif(IIrisContext context, RotationScheduler scheduler)
    {
        if (!context.Configuration.Pictomancer.PrepaintMotifs) return;
        var player = context.Player;
        var level = player.Level;
        if (context.IsCasting) return;

        var prepaintOption = context.Configuration.Pictomancer.PrepaintOption;

        var paintLandscape = prepaintOption == PrepaintOption.All || prepaintOption == PrepaintOption.LandscapeOnly;
        if (paintLandscape && CanPaintStarrySkyMotif(context) && context.NeedsLandscapeMotif
            && level >= PCTActions.StarrySkyMotif.MinLevel
            && IrisActionProbes.IsStarrySkyMotifReady(context.ActionService))
        {
            scheduler.PushGcd(IrisAbilities.StarrySkyMotif, player.GameObjectId, priority: 1,
                onDispatched: _ =>
                {
                    context.Debug.PlannedAction = PCTActions.StarrySkyMotif.Name;
                    context.Debug.DamageState = "Painting Starry Sky";
                });
            return;
        }

        var paintCreature = prepaintOption == PrepaintOption.All || prepaintOption == PrepaintOption.CreatureOnly;
        if (paintCreature && context.Configuration.Pictomancer.EnableCreatureMotif && context.NeedsCreatureMotif
            && level >= PCTActions.CreatureMotif.MinLevel)
        {
            var motif = GetCreatureMotifOrdered(context, level);
            if (IsCreatureMotifEnabled(context, motif))
            {
                var ability = MapCreatureMotifAbility(motif);
                scheduler.PushGcd(ability, player.GameObjectId, priority: 1,
                    onDispatched: _ =>
                    {
                        context.Debug.PlannedAction = motif.Name;
                        context.Debug.DamageState = $"Painting {motif.Name}";
                    });
                return;
            }
        }

        var paintWeapon = prepaintOption == PrepaintOption.All || prepaintOption == PrepaintOption.WeaponOnly;
        if (paintWeapon && CanPaintHammerMotif(context) && context.NeedsWeaponMotif
            && level >= PCTActions.WeaponMotif.MinLevel
            && IrisActionProbes.IsHammerMotifReady(context.ActionService))
        {
            scheduler.PushGcd(IrisAbilities.HammerMotif, player.GameObjectId, priority: 1,
                onDispatched: _ =>
                {
                    context.Debug.PlannedAction = PCTActions.HammerMotif.Name;
                    context.Debug.DamageState = "Painting Hammer";
                });
        }
    }

    private void TryPushMotifDuringInspiration(IIrisContext context, RotationScheduler scheduler, bool isMoving)
    {
        if (isMoving) return;
        if (!context.HasInspiration) return;
        if (context.IsCasting && !context.CanSlidecast) return;
        var player = context.Player;
        var level = player.Level;

        if (CanPaintStarrySkyMotif(context) && context.NeedsLandscapeMotif
            && level >= PCTActions.StarrySkyMotif.MinLevel
            && IrisActionProbes.IsStarrySkyMotifReady(context.ActionService))
        {
            if (MechanicCastGate.ShouldBlock(context, PCTActions.StarrySkyMotif.CastTime))
            {
                context.Debug.DamageState = MechanicCastGate.FormatBlockedState(context);
                return;
            }
            scheduler.PushGcd(IrisAbilities.StarrySkyMotif, player.GameObjectId, priority: 0,
                onDispatched: _ =>
                {
                    context.Debug.PlannedAction = PCTActions.StarrySkyMotif.Name;
                    context.Debug.DamageState = "Inspiration: Painting Starry Sky";
                });
            return;
        }

        if (context.Configuration.Pictomancer.EnableCreatureMotif && context.NeedsCreatureMotif
            && level >= PCTActions.CreatureMotif.MinLevel)
        {
            var motif = GetCreatureMotifOrdered(context, level);
            if (IsCreatureMotifEnabled(context, motif))
            {
                if (MechanicCastGate.ShouldBlock(context, motif.CastTime))
                {
                    context.Debug.DamageState = MechanicCastGate.FormatBlockedState(context);
                    return;
                }
                var ability = MapCreatureMotifAbility(motif);
                scheduler.PushGcd(ability, player.GameObjectId, priority: 0,
                    onDispatched: _ =>
                    {
                        context.Debug.PlannedAction = motif.Name;
                        context.Debug.DamageState = $"Inspiration: Painting {motif.Name}";
                    });
                return;
            }
        }

        if (CanPaintHammerMotif(context) && context.NeedsWeaponMotif
            && level >= PCTActions.WeaponMotif.MinLevel
            && IrisActionProbes.IsHammerMotifReady(context.ActionService))
        {
            if (MechanicCastGate.ShouldBlock(context, PCTActions.HammerMotif.CastTime))
            {
                context.Debug.DamageState = MechanicCastGate.FormatBlockedState(context);
                return;
            }
            scheduler.PushGcd(IrisAbilities.HammerMotif, player.GameObjectId, priority: 0,
                onDispatched: _ =>
                {
                    context.Debug.PlannedAction = PCTActions.HammerMotif.Name;
                    context.Debug.DamageState = "Inspiration: Painting Hammer";
                });
        }
    }

    private void TryPushRepaintMotif(IIrisContext context, RotationScheduler scheduler)
    {
        if (IrisBurstHelper.ShouldHoldRepaint(context, _burstWindowService))
        {
            context.Debug.DamageState = "Holding repaint for burst";
            return;
        }

        var player = context.Player;
        var level = player.Level;
        if (context.IsCasting) return;

        // Paint motifs IN COMBAT, timed to each muse's cooldown (RSR parity). Priority 6 puts these ABOVE
        // the color combo (7/8): a motif paint enables a Living/Steel/Scenic Muse (portrait + Hammer + the
        // Starry burst), all far higher value than a filler color spell. Previously this ran at priority 9
        // (below the combo), so the combo always preempted it and — in back-to-back AutoDuty pulls with no
        // pre-pull downtime — the canvases never got painted and the entire muse/Hammer/Starry system never
        // fired. Each motif is gated so it's painted only when its muse is up or about to be (no over-paint).
        var svc = context.ActionService;
        const int motifPriority = 6;

        // Landscape (Starry Sky Motif): paint ~15s before Scenic Muse so the canvas is ready for the burst.
        if (CanPaintStarrySkyMotif(context) && context.NeedsLandscapeMotif
            && level >= PCTActions.StarrySkyMotif.MinLevel
            && !context.HasStarryMuse && !context.HasHyperphantasia
            && IrisActionProbes.IsStarrySkyMotifReady(svc)
            && svc.GetCooldownRemaining(PCTActions.ScenicMuse.ActionId) <= 15f)
        {
            if (MechanicCastGate.ShouldBlock(context, PCTActions.StarrySkyMotif.CastTime))
            {
                context.Debug.DamageState = MechanicCastGate.FormatBlockedState(context);
                return;
            }
            scheduler.PushGcd(IrisAbilities.StarrySkyMotif, player.GameObjectId, priority: motifPriority,
                onDispatched: _ =>
                {
                    context.Debug.PlannedAction = PCTActions.StarrySkyMotif.Name;
                    context.Debug.DamageState = "Painting Starry Sky (Scenic soon)";
                });
            return;
        }

        // Creature: paint when Living Muse has a charge or is about to (so it's consumed promptly).
        if (context.Configuration.Pictomancer.EnableCreatureMotif && context.NeedsCreatureMotif
            && level >= PCTActions.CreatureMotif.MinLevel
            && (svc.IsActionReady(PCTActions.LivingMuse.ActionId)
                || svc.GetCooldownRemaining(PCTActions.LivingMuse.ActionId) <= PCTActions.CreatureMotif.CastTime * 1.7f))
        {
            var motif = GetCreatureMotifOrdered(context, level);
            if (IsCreatureMotifEnabled(context, motif))
            {
                if (MechanicCastGate.ShouldBlock(context, motif.CastTime))
                {
                    context.Debug.DamageState = MechanicCastGate.FormatBlockedState(context);
                    return;
                }
                var ability = MapCreatureMotifAbility(motif);
                scheduler.PushGcd(ability, player.GameObjectId, priority: motifPriority,
                    onDispatched: _ =>
                    {
                        context.Debug.PlannedAction = motif.Name;
                        context.Debug.DamageState = $"Painting {motif.Name} (Living Muse soon)";
                    });
                return;
            }
        }

        // Weapon (Hammer Motif): paint when Steel Muse has a charge or is about to.
        if (CanPaintHammerMotif(context) && context.NeedsWeaponMotif
            && level >= PCTActions.WeaponMotif.MinLevel
            && IrisActionProbes.IsHammerMotifReady(svc)
            && (svc.IsActionReady(PCTActions.SteelMuse.ActionId)
                || svc.GetCooldownRemaining(PCTActions.SteelMuse.ActionId) <= PCTActions.WeaponMotif.CastTime))
        {
            if (MechanicCastGate.ShouldBlock(context, PCTActions.HammerMotif.CastTime))
            {
                context.Debug.DamageState = MechanicCastGate.FormatBlockedState(context);
                return;
            }
            scheduler.PushGcd(IrisAbilities.HammerMotif, player.GameObjectId, priority: motifPriority,
                onDispatched: _ =>
                {
                    context.Debug.PlannedAction = PCTActions.HammerMotif.Name;
                    context.Debug.DamageState = "Painting Hammer (Steel Muse soon)";
                });
        }
    }

    private static ActionDefinition GetCreatureMotifOrdered(IIrisContext context, byte level)
    {
        var motif = IrisActionProbes.GetNextCreatureMotif(context.ActionService, level, context.LivingMuseCharges);
        if (IsCreatureMotifEnabled(context, motif))
            return motif;

        foreach (var candidate in new[] { PCTActions.PomMotif, PCTActions.WingMotif, PCTActions.ClawMotif, PCTActions.MawMotif })
        {
            if (level < candidate.MinLevel) continue;
            if (!IsCreatureMotifEnabled(context, candidate)) continue;
            if (IsCreatureMotifProbeReady(context, candidate))
                return candidate;
        }

        if (context.Configuration.Pictomancer.CreatureMotifOrder == CreatureOrder.MawClawWingPom)
            return PCTActions.GetCreatureMotif(level, context.LivingMuseCharges ^ 1, context.ActionService);

        return PCTActions.GetCreatureMotif(level, context.LivingMuseCharges, context.ActionService);
    }

    private static bool IsCreatureMotifProbeReady(IIrisContext context, ActionDefinition motif)
    {
        if (motif.ActionId == PCTActions.PomMotif.ActionId) return IrisActionProbes.IsPomMotifReady(context.ActionService);
        if (motif.ActionId == PCTActions.WingMotif.ActionId) return IrisActionProbes.IsWingMotifReady(context.ActionService);
        if (motif.ActionId == PCTActions.ClawMotif.ActionId) return IrisActionProbes.IsClawMotifReady(context.ActionService);
        if (motif.ActionId == PCTActions.MawMotif.ActionId) return IrisActionProbes.IsMawMotifReady(context.ActionService);
        return true;
    }

    private static bool CanPaintHammerMotif(IIrisContext context) =>
        context.Configuration.Pictomancer.EnableWeaponMotif
        && context.Configuration.Pictomancer.EnableHammerMotif;

    private static bool CanPaintStarrySkyMotif(IIrisContext context) =>
        context.Configuration.Pictomancer.EnableLandscapeMotif
        && context.Configuration.Pictomancer.EnableStarrySkyMotif;

    private static bool ShouldUseSubtractiveRoute(IIrisContext context)
    {
        if (!context.Configuration.Pictomancer.EnableSubtractiveCombo)
            return false;

        return context.HasSubtractivePalette
               || context.HasSubtractiveSpectrum
               || context.IsInSubtractiveCombo;
    }

    private static bool IsCreatureMotifEnabled(IIrisContext context, ActionDefinition motif)
    {
        var config = context.Configuration.Pictomancer;
        if (motif.ActionId == PCTActions.PomMotif.ActionId) return config.EnablePomMotif;
        if (motif.ActionId == PCTActions.WingMotif.ActionId) return config.EnableWingMotif;
        if (motif.ActionId == PCTActions.ClawMotif.ActionId) return config.EnableClawMotif;
        if (motif.ActionId == PCTActions.MawMotif.ActionId) return config.EnableMawMotif;
        return true;
    }

    private static AbilityBehavior MapCreatureMotifAbility(ActionDefinition motif)
    {
        if (motif.ActionId == PCTActions.PomMotif.ActionId) return IrisAbilities.PomMotif;
        if (motif.ActionId == PCTActions.WingMotif.ActionId) return IrisAbilities.WingMotif;
        if (motif.ActionId == PCTActions.ClawMotif.ActionId) return IrisAbilities.ClawMotif;
        if (motif.ActionId == PCTActions.MawMotif.ActionId) return IrisAbilities.MawMotif;
        return IrisAbilities.PomMotif;
    }

    #endregion

    #region GCDs

    private void TryPushStarPrism(IIrisContext context, RotationScheduler scheduler, IBattleChara target)
    {
        if (!context.Configuration.Pictomancer.EnableStarPrism) return;
        if (context.Player.Level < PCTActions.StarPrism.MinLevel) return;
        if (!context.HasStarstruck) return;

        scheduler.PushGcd(IrisAbilities.StarPrism, target.GameObjectId, priority: 2,
            onDispatched: _ =>
            {
                context.Debug.PlannedAction = PCTActions.StarPrism.Name;
                context.Debug.DamageState = "Star Prism";
                TrainingHelper.Decision(context.TrainingService)
                    .Action(PCTActions.StarPrism.ActionId, PCTActions.StarPrism.Name)
                    .AsCasterBurst().Target(target.Name?.TextValue)
                    .Reason("Star Prism - burst finisher",
                        "Star Prism is your highest potency single-target GCD, available during Starstruck buff.")
                    .Factors("Starstruck active")
                    .Alternatives("None - must use before buff expires")
                    .Tip("Star Prism is your biggest hit. Never let Starstruck expire without using it.")
                    .Concept(PctConcepts.FinisherPriority)
                    .Record();
                context.TrainingService?.RecordConceptApplication(PctConcepts.FinisherPriority, true, "Star Prism finisher used");
            });
    }

    private void TryPushRainbowDrip(IIrisContext context, RotationScheduler scheduler, IBattleChara target, bool isMoving)
    {
        if (!context.Configuration.Pictomancer.EnableRainbowDrip) return;
        if (context.Player.Level < PCTActions.RainbowDrip.MinLevel) return;

        if (!context.HasRainbowBright)
        {
            if (isMoving) return;
            if (!context.IsInBurstWindow) return;
            if (MechanicCastGate.ShouldBlock(context, PCTActions.RainbowDrip.CastTime))
            {
                context.Debug.DamageState = MechanicCastGate.FormatBlockedState(context);
                return;
            }
        }

        scheduler.PushGcd(IrisAbilities.RainbowDrip, target.GameObjectId, priority: 3,
            onDispatched: _ =>
            {
                context.Debug.PlannedAction = PCTActions.RainbowDrip.Name;
                context.Debug.DamageState = context.HasRainbowBright ? "Rainbow Drip (instant)" : "Rainbow Drip (hardcast)";
            });
    }

    private void TryPushHammerCombo(IIrisContext context, RotationScheduler scheduler, IBattleChara target)
    {
        if (!context.Configuration.Pictomancer.EnableWeaponMotif) return;
        var level = context.Player.Level;
        if (level < PCTActions.HammerStamp.MinLevel) return;
        if (!context.HasHammerTime && !context.IsInHammerCombo) return;
        if (IrisBurstHelper.ShouldHoldHammerStart(context, context.ActionService, _burstWindowService)) return;
        if (!context.Configuration.Pictomancer.UseHammerDuringBurst && context.IsInBurstWindow) return;

        if (!IrisActionProbes.CanStartHammerStamp(
                context.ActionService,
                context.HasHammerTime,
                context.HammerTimeStacks,
                context.HammerComboStep))
            return;

        if (context.HammerComboStep == 1 && !IrisActionProbes.IsHammerBrushReady(context.ActionService))
            return;

        if (context.HammerComboStep == 2 && !IrisActionProbes.IsPolishingHammerReady(context.ActionService))
            return;

        var hammerAction = PCTActions.GetHammerComboAction(context.HammerComboStep, level, context.ActionService);
        if (hammerAction == null) return;
        var ability = hammerAction.ActionId switch
        {
            var id when id == PCTActions.HammerStamp.ActionId => IrisAbilities.HammerStamp,
            var id when id == PCTActions.HammerBrush.ActionId => IrisAbilities.HammerBrush,
            var id when id == PCTActions.PolishingHammer.ActionId => IrisAbilities.PolishingHammer,
            _ => IrisAbilities.HammerStamp,
        };

        scheduler.PushGcd(ability, target.GameObjectId, priority: 4,
            onDispatched: _ =>
            {
                context.Debug.PlannedAction = hammerAction.Name;
                context.Debug.DamageState = $"Hammer ({hammerAction.Name})";
                var stepName = context.HammerComboStep switch
                {
                    0 => "Stamp (step 1)",
                    1 => "Brush (step 2)",
                    2 => "Polish (step 3)",
                    _ => "Unknown"
                };
                TrainingHelper.Decision(context.TrainingService)
                    .Action(hammerAction.ActionId, hammerAction.Name)
                    .AsCasterBurst().Target(target.Name?.TextValue)
                    .Reason($"Hammer combo - {stepName}",
                        $"Hammer combo {stepName} is instant cast and high damage. Complete all 3 hits.")
                    .Factors($"Hammer Step: {context.HammerComboStep}", $"Hammer Time Stacks: {context.HammerTimeStacks}")
                    .Alternatives("Don't drop combo")
                    .Tip("Complete the hammer combo before Hammer Time expires.")
                    .Concept(PctConcepts.HammerCombo)
                    .Record();
                context.TrainingService?.RecordConceptApplication(PctConcepts.HammerCombo, true, $"Hammer {stepName} executed");
            });
    }

    private void TryPushCometInBlack(IIrisContext context, RotationScheduler scheduler, IBattleChara target)
    {
        if (!context.Configuration.Pictomancer.EnableCometInBlack) return;
        if (context.Player.Level < PCTActions.CometInBlack.MinLevel) return;
        if (!context.HasBlackPaint) return;

        scheduler.PushGcd(IrisAbilities.CometInBlack, target.GameObjectId, priority: 5,
            onDispatched: _ =>
            {
                context.Debug.PlannedAction = PCTActions.CometInBlack.Name;
                context.Debug.DamageState = "Comet in Black";
                TrainingHelper.Decision(context.TrainingService)
                    .Action(PCTActions.CometInBlack.ActionId, PCTActions.CometInBlack.Name)
                    .AsCasterBurst().Target(target.Name?.TextValue)
                    .Reason("Comet in Black - black paint spender",
                        "Comet in Black consumes Black Paint for high instant damage.")
                    .Factors("Black Paint available")
                    .Alternatives("None - use Black Paint when available")
                    .Tip("Always use Comet in Black when you have Black Paint.")
                    .Concept(PctConcepts.CometInBlack)
                    .Record();
                context.TrainingService?.RecordConceptApplication(PctConcepts.CometInBlack, true, "Black Paint consumed");
            });
    }

    private void TryPushHolyInWhite(IIrisContext context, RotationScheduler scheduler, IBattleChara target, bool isMoving)
    {
        if (!context.Configuration.Pictomancer.EnableHolyInWhite) return;
        if (context.Player.Level < PCTActions.HolyInWhite.MinLevel) return;
        if (!context.HasWhitePaint) return;
        if (!isMoving && context.WhitePaint < 4 && !context.IsInBurstWindow) return;
        if (!isMoving && context.PaletteGauge < context.Configuration.Pictomancer.HolyMinPalette && !context.IsInBurstWindow) return;

        scheduler.PushGcd(IrisAbilities.HolyInWhite, target.GameObjectId, priority: 6,
            onDispatched: _ =>
            {
                context.Debug.PlannedAction = PCTActions.HolyInWhite.Name;
                context.Debug.DamageState = $"Holy in White ({context.WhitePaint - 1} paint)";
            });
    }

    private void TryPushSubtractiveCombo(IIrisContext context, RotationScheduler scheduler, IBattleChara target)
    {
        if (!context.Configuration.Pictomancer.EnableSubtractiveCombo) return;
        if (context.Player.Level < PCTActions.BlizzardInCyan.MinLevel) return;
        if (!context.HasSubtractivePalette && !context.HasSubtractiveSpectrum) return;

        var comboAction = PCTActions.GetSubtractiveComboAction(context.BaseComboStep, context.ShouldUseAoe, context.Player.Level, context.ActionService);
        var ability = MapSubtractiveCombo(comboAction);
        var castTime = context.HasInstantCast ? 0f : comboAction.CastTime;
        if (MechanicCastGate.ShouldBlock(context, castTime))
        {
            context.Debug.DamageState = MechanicCastGate.FormatBlockedState(context);
            return;
        }

        scheduler.PushGcd(ability, ResolveTargetId(context, target, comboAction.ActionId, context.ShouldUseAoe), priority: 7,
            onDispatched: _ =>
            {
                context.Debug.PlannedAction = comboAction.Name;
                context.Debug.DamageState = $"Subtractive ({comboAction.Name})";
            });
    }

    private void TryPushBaseCombo(IIrisContext context, RotationScheduler scheduler, IBattleChara target)
    {
        if (ShouldUseSubtractiveRoute(context))
            return;

        var comboAction = PCTActions.GetBaseComboAction(context.BaseComboStep, context.ShouldUseAoe, context.Player.Level, context.ActionService);
        var ability = MapBaseCombo(comboAction);
        var castTime = context.HasInstantCast ? 0f : comboAction.CastTime;
        if (MechanicCastGate.ShouldBlock(context, castTime))
        {
            context.Debug.DamageState = MechanicCastGate.FormatBlockedState(context);
            return;
        }

        scheduler.PushGcd(ability, ResolveTargetId(context, target, comboAction.ActionId, context.ShouldUseAoe), priority: 8,
            onDispatched: _ =>
            {
                context.Debug.PlannedAction = comboAction.Name;
                context.Debug.DamageState = $"Base Combo ({comboAction.Name})";
            });
    }

    private static AbilityBehavior MapBaseCombo(ActionDefinition action)
    {
        if (action == PCTActions.FireInRed) return IrisAbilities.FireInRed;
        if (action == PCTActions.AeroInGreen) return IrisAbilities.AeroInGreen;
        if (action == PCTActions.WaterInBlue) return IrisAbilities.WaterInBlue;
        if (action == PCTActions.Fire2InRed) return IrisAbilities.Fire2InRed;
        if (action == PCTActions.Aero2InGreen) return IrisAbilities.Aero2InGreen;
        if (action == PCTActions.Water2InBlue) return IrisAbilities.Water2InBlue;
        return IrisAbilities.FireInRed;
    }

    private static AbilityBehavior MapSubtractiveCombo(ActionDefinition action)
    {
        if (action == PCTActions.BlizzardInCyan) return IrisAbilities.BlizzardInCyan;
        if (action == PCTActions.StoneInYellow) return IrisAbilities.StoneInYellow;
        if (action == PCTActions.ThunderInMagenta) return IrisAbilities.ThunderInMagenta;
        if (action == PCTActions.Blizzard2InCyan) return IrisAbilities.Blizzard2InCyan;
        if (action == PCTActions.Stone2InYellow) return IrisAbilities.Stone2InYellow;
        if (action == PCTActions.Thunder2InMagenta) return IrisAbilities.Thunder2InMagenta;
        return IrisAbilities.BlizzardInCyan;
    }

    #endregion
}
