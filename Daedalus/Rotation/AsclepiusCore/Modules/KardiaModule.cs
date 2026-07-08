using System;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Daedalus.Config;
using Daedalus.Data;
using Daedalus.Models.Action;
using Daedalus.Rotation.AsclepiusCore.Abilities;
using Daedalus.Rotation.AsclepiusCore.Context;
using Daedalus.Rotation.AsclepiusCore.Helpers;
using Daedalus.Rotation.Common.Helpers;
using Daedalus.Rotation.Common.Scheduling;
using Daedalus.Services.Training;

namespace Daedalus.Rotation.AsclepiusCore.Modules;

/// <summary>
/// Handles Kardia / Soteria / Philosophia for Sage (scheduler-driven).
/// Push priorities are 0-3 so Kardia placement wins against Resurrection (1-2).
/// </summary>
public sealed class KardiaModule : IAsclepiusModule
{
    public int Priority => 3;
    public string Name => "Kardia";

    public bool TryExecute(IAsclepiusContext context, bool isMoving) => false;

    public void CollectCandidates(IAsclepiusContext context, RotationScheduler scheduler, bool isMoving)
    {
        // Kardia always dispatches directly through TryExecuteKardia so the recast gate
        // cannot be bypassed by the scheduler (which calls ExecuteOgcd without that check).
        TryDispatchKardia(context);

        if (!context.InCombat)
            return;

        TryPushSoteria(context, scheduler);
        TryPushPhilosophia(context, scheduler);
    }

    public void UpdateDebugState(IAsclepiusContext context)
    {
        var player = context.Player;
        var tank = context.PartyHelper.FindTankInParty(player);
        var kardiaTargetId = ResolveKardionBearerId(context, player, tank);
        var kardiaTargetName = ResolveTargetName(context, kardiaTargetId);

        context.Debug.KardiaTargetGameObjectId = kardiaTargetId;
        context.Debug.KardiaTargetName = kardiaTargetName;
        context.Debug.TankGameObjectId = tank?.GameObjectId ?? 0;
        context.Debug.TankTargetName = tank?.Name?.TextValue ?? "None";
        context.Debug.TankHasKardion = tank != null
            && AsclepiusStatusHelper.TankHasKardion(
                player, tank, context.ObjectTable, context.PartyList, kardiaTargetId);
        context.Debug.KardiaTarget = kardiaTargetId != 0
            ? $"{kardiaTargetName} ({kardiaTargetId})"
            : "None";

        context.Debug.KardiaState = context.HasKardiaPlaced ? "Kardion active" : "No Kardion";
        context.Debug.SoteriaStacks = context.KardiaManager.GetSoteriaStacks(context.Player);
        context.Debug.SoteriaState = context.HasSoteria ? "Active" : "Idle";
        context.Debug.PhilosophiaState = context.HasPhilosophia ? "Active" : "Idle";
    }

    private void TryDispatchKardia(IAsclepiusContext context)
    {
        var config = context.Configuration.Sage;
        var player = context.Player;

        if (!config.AutoKardia) return;
        if (player.Level < SGEActions.Kardia.MinLevel) return;

        // After a zone change, wait for the new party/tank to spawn before placing Kardia so
        // the buff lands on the real tank instead of being wasted mid-zoning. Combat overrides
        // the grace period so an active pull is never left without Kardia.
        if (!context.InCombat && context.KardiaManager.IsPostZoneWarmupActive)
        {
            context.Debug.KardiaState = "Waiting (zoning)";
            return;
        }

        var tank = context.PartyHelper.FindTankInParty(player);
        var (desiredTarget, isSwap, swapReason) = ResolveKardiaDispatchTarget(context, tank);
        if (desiredTarget == null)
        {
            context.Debug.KardiaState = "No target";
            UpdateKardiaDebugTargets(context, tank, 0, "None");
            return;
        }

        if (!isSwap)
        {
            PrimeTankKardionLatch(context, player, tank, desiredTarget);

            var resolvedTargetId = ResolveKardionBearerId(context, player, tank);
            if (ShouldSuppressKardiaRecast(context, player, tank, desiredTarget, resolvedTargetId))
            {
                var resolvedTargetName = ResolveTargetName(context, resolvedTargetId);
                UpdateKardiaDebugTargets(context, tank, resolvedTargetId, resolvedTargetName);
                SyncResolvedBearer(context, resolvedTargetId, desiredTarget);
                context.Debug.PlannedAction = string.Empty;
                context.Debug.KardiaState = DescribeSuppressedKardiaState(context, tank, desiredTarget);
                return;
            }

            var resolvedName = ResolveTargetName(context, resolvedTargetId);
            UpdateKardiaDebugTargets(context, tank, resolvedTargetId, resolvedName);
        }
        else
        {
            // Intentional swap: the tank/bearer-keyed suppression must not veto it — the whole point
            // is moving Kardion OFF the current bearer. Only skip when the destination already bears.
            if (context.KardiaManager.IsKardionOnTarget(
                    player, desiredTarget, context.ObjectTable, context.PartyList, tank))
            {
                context.Debug.KardiaState = "Kardion active";
                return;
            }

            UpdateKardiaDebugTargets(
                context, tank, desiredTarget.GameObjectId, desiredTarget.Name?.TextValue ?? "Unknown");
        }

        if (context.InCombat && !context.CanExecuteOgcd)
        {
            context.Debug.KardiaState = "Waiting (weave)";
            return;
        }

        if (!context.ActionService.IsActionReady(SGEActions.Kardia.ActionId))
        {
            context.Debug.KardiaState = "Waiting (CD/status)";
            return;
        }

        var onTank = tank != null && desiredTarget.EntityId == tank.EntityId;
        var decision = isSwap ? (onTank ? "SwapHome" : "SwapLowHp") : "Place";
        var reason = isSwap
            ? swapReason
            : context.InCombat
                ? onTank ? "Kardia not on tank, moving back" : "Placing Kardia"
                : "Pre-pull Kardia";

        TryExecuteKardia(context, desiredTarget, decision, reason, isSwap);
    }

    /// <summary>
    /// Resolves where Kardia should sit this frame.
    /// Home target: the tank (player, trust, or duty support) → highest-DPS ally → self.
    /// With a live bearer, the smart swap (gated on <c>KardiaSwapEnabled</c> + the 5s swap
    /// cooldown) can (a) move Kardion to the most injured ally below <c>KardiaSwapThreshold</c>
    /// during combat, and (b) return it home once that ally recovers. When no swap fires the
    /// bearer is held — never silently re-homed, which would bypass the swap cooldown.
    /// </summary>
    private static (IBattleChara? target, bool isSwap, string reason) ResolveKardiaDispatchTarget(
        IAsclepiusContext context,
        IBattleChara? tank)
    {
        var config = context.Configuration.Sage;
        var home = tank ?? FindKardiaFallbackTarget(context);
        var bearer = FindKardiaTargetById(context, context.KardiaTargetId);

        if (bearer == null || !context.HasKardiaPlaced)
            return (home, false, "placing");

        if (config.KardiaSwapEnabled && context.CanSwapKardia)
        {
            var bearerHp = HpPercent(bearer);

            // Swap TO the most injured ally below the threshold (combat only — OOC regen covers it).
            if (context.InCombat)
            {
                var candidate = FindLowestHpAllyBelowThreshold(context, config.KardiaSwapThreshold, bearer);
                if (candidate != null
                    && context.KardiaManager.ShouldSwapKardia(
                        bearerHp, HpPercent(candidate), config.KardiaSwapThreshold))
                {
                    return (candidate, true, $"ally at {HpPercent(candidate):P0}, moving Kardia");
                }
            }

            // Swap BACK home once an off-home bearer has recovered (in or out of combat).
            if (home != null
                && bearer.EntityId != home.EntityId
                && context.KardiaManager.ShouldSwapKardia(
                    bearerHp, HpPercent(home), config.KardiaSwapThreshold, newTargetIsTank: true))
            {
                return (home, true, "bearer recovered, returning home");
            }
        }

        // No swap fired — keep Kardion where it is (suppression no-ops the dispatch).
        return (bearer, false, "holding");
    }

    private static float HpPercent(IBattleChara chara) =>
        chara.MaxHp > 0 ? (float)chara.CurrentHp / chara.MaxHp : 1f;

    /// <summary>
    /// Most injured living ally below the swap threshold, excluding the current bearer.
    /// </summary>
    private static IBattleChara? FindLowestHpAllyBelowThreshold(
        IAsclepiusContext context,
        float threshold,
        IBattleChara bearer)
    {
        IBattleChara? lowest = null;
        var lowestHp = threshold;
        foreach (var member in context.PartyHelper.GetAllPartyMembers(context.Player))
        {
            if (member.CurrentHp == 0 || member.EntityId == bearer.EntityId)
                continue;

            var hp = HpPercent(member);
            if (hp < lowestHp)
            {
                lowestHp = hp;
                lowest = member;
            }
        }

        return lowest;
    }

    private static void PrimeTankKardionLatch(
        IAsclepiusContext context,
        IPlayerCharacter player,
        IBattleChara? tank,
        IBattleChara desiredTarget)
    {
        if (context.KardiaManager.IsTankKardionLatched(desiredTarget.EntityId))
            return;

        if (tank == null || desiredTarget.EntityId != tank.EntityId)
            return;

        // Inference yields to a tracked bearer: when Kardion is known to sit on someone else
        // (post smart-swap), do NOT re-latch the tank — that would fake a placement and
        // suppress the real return-home cast.
        if (context.KardiaTargetId != 0 && context.KardiaTargetId != tank.GameObjectId)
            return;

        if (!AsclepiusStatusHelper.HasKardia(player))
            return;

        if (AsclepiusStatusHelper.InferKardionOnTank(
                player, tank, context.ObjectTable, context.PartyList))
        {
            context.KardiaManager.ConfirmTankKardion(desiredTarget);
        }
    }

    private static string DescribeSuppressedKardiaState(
        IAsclepiusContext context,
        IBattleChara? tank,
        IBattleChara desiredTarget)
    {
        if (tank != null && desiredTarget.EntityId == tank.EntityId)
            return context.InCombat ? "Kardion on tank" : "Kardion on tank (pre-pull)";

        return context.InCombat ? "Kardion active" : "Kardion active (pre-pull)";
    }

    private static bool ShouldSuppressKardiaRecast(
        IAsclepiusContext context,
        IPlayerCharacter player,
        IBattleChara? tank,
        IBattleChara target,
        ulong resolvedTargetId)
    {
        if (!context.InCombat
            && tank != null
            && target.EntityId == tank.EntityId
            && (context.KardiaManager.IsTankKardionLatched(tank.EntityId)
                || resolvedTargetId == tank.GameObjectId
                || AsclepiusStatusHelper.HasKardia(player)))
        {
            context.KardiaManager.ConfirmTankKardion(tank);
            return true;
        }

        if (tank != null && context.KardiaManager.IsTankKardionLatched(tank.EntityId))
            return true;

        if (!context.InCombat
            && tank != null
            && target.EntityId == tank.EntityId
            && context.Debug.TankHasKardion)
        {
            context.KardiaManager.ConfirmTankKardion(tank);
            return true;
        }

        if (context.KardiaManager.IsTankKardionLatched(target.EntityId))
            return true;

        if (HealingPanelShowsKardionOnTank(context, player, tank, resolvedTargetId))
            return true;

        return context.KardiaManager.ShouldBlockKardiaRecast(
            player, target, context.ObjectTable, context.PartyList, tank);
    }

    private static bool HealingPanelShowsKardionOnTank(
        IAsclepiusContext context,
        IPlayerCharacter player,
        IBattleChara? tank,
        ulong resolvedTargetId)
    {
        if (tank == null)
            return false;

        if (AsclepiusStatusHelper.TankHasKardion(
                player, tank, context.ObjectTable, context.PartyList, resolvedTargetId))
        {
            context.KardiaManager.ConfirmTankKardion(tank);
            return true;
        }

        return false;
    }

    private static bool IsKardiaPlacementSatisfied(
        IAsclepiusContext context,
        IPlayerCharacter player,
        IBattleChara? tank,
        IBattleChara target)
    {
        return context.KardiaManager.ShouldBlockKardiaRecast(
            player, target, context.ObjectTable, context.PartyList, tank);
    }

    private static void SyncResolvedBearer(
        IAsclepiusContext context,
        ulong resolvedTargetId,
        IBattleChara desiredTarget)
    {
        if (resolvedTargetId != 0)
        {
            context.KardiaManager.SyncDetectedBearer(resolvedTargetId);
            return;
        }

        if (context.KardiaManager.IsKardionOnTarget(
                context.Player, desiredTarget, context.ObjectTable, context.PartyList))
        {
            context.KardiaManager.SyncDetectedBearer(desiredTarget.GameObjectId);
        }
    }

    private static ulong ResolveKardionBearerId(
        IAsclepiusContext context,
        IPlayerCharacter player,
        IBattleChara? tank)
    {
        var knownBearerId = context.KardiaTargetId;
        var liveId = AsclepiusStatusHelper.FindKardionTargetId(
            player,
            context.ObjectTable,
            context.PartyList,
            context.PartyHelper.GetAllPartyMembers(player),
            tank,
            knownBearerId);

        if (liveId != 0)
            return liveId;

        // Inference yields to a tracked bearer — never report "on the tank" while memory says
        // Kardion sits on someone else (post smart-swap).
        if (tank != null
            && (knownBearerId == 0 || knownBearerId == tank.GameObjectId)
            && AsclepiusStatusHelper.InferKardionOnTank(player, tank, context.ObjectTable, context.PartyList))
        {
            return tank.GameObjectId;
        }

        return knownBearerId;
    }

    private static string ResolveTargetName(IAsclepiusContext context, ulong gameObjectId)
    {
        if (gameObjectId == 0)
            return "None";

        foreach (var member in context.PartyHelper.GetAllPartyMembers(context.Player))
        {
            if (member.GameObjectId == gameObjectId)
                return member.Name?.TextValue ?? $"ID:{gameObjectId}";
        }

        var obj = context.ObjectTable.SearchById(gameObjectId);
        return obj?.Name.TextValue ?? $"ID:{gameObjectId}";
    }

    private static void UpdateKardiaDebugTargets(
        IAsclepiusContext context,
        IBattleChara? tank,
        ulong kardiaTargetId,
        string kardiaTargetName)
    {
        context.Debug.KardiaTargetGameObjectId = kardiaTargetId;
        context.Debug.KardiaTargetName = kardiaTargetName;
        context.Debug.TankGameObjectId = tank?.GameObjectId ?? 0;
        context.Debug.TankTargetName = tank?.Name?.TextValue ?? "None";
        context.Debug.TankHasKardion = tank != null
            && AsclepiusStatusHelper.TankHasKardion(
                context.Player, tank, context.ObjectTable, context.PartyList, kardiaTargetId);
        context.Debug.KardiaTarget = kardiaTargetId != 0
            ? $"{kardiaTargetName} ({kardiaTargetId})"
            : "None";
    }

    private static string? DescribeKardiaCastError(
        IAsclepiusContext context,
        IPlayerCharacter player,
        IBattleChara? tank,
        IBattleChara target)
    {
        var resolvedTargetId = ResolveKardionBearerId(context, player, tank);
        if (ShouldSuppressKardiaRecast(context, player, tank, target, resolvedTargetId))
            return "Unexpected cast — recast should have been suppressed";

        if (context.KardiaManager.ShouldBlockKardiaRecast(
                player, target, context.ObjectTable, context.PartyList, tank))
        {
            return "Unexpected cast — KardiaManager recast block missed";
        }

        return null;
    }

    private static bool TryExecuteKardia(
        IAsclepiusContext context,
        IBattleChara target,
        string decision,
        string reason,
        bool isSwap = false)
    {
        var player = context.Player;
        var tank = context.PartyHelper.FindTankInParty(player);

        // Intentional swaps only verify the destination isn't already the bearer; the recast gate
        // is keyed on the CURRENT bearer and would veto every swap by design.
        if (isSwap)
        {
            if (context.KardiaManager.IsKardionOnTarget(
                    player, target, context.ObjectTable, context.PartyList, tank))
            {
                return false;
            }
        }
        else if (context.KardiaManager.ShouldBlockKardiaRecast(
                player, target, context.ObjectTable, context.PartyList, tank))
        {
            return false;
        }

        if (!context.ActionService.IsActionReady(SGEActions.Kardia.ActionId))
            return false;

        if (!isSwap)
        {
            var castError = DescribeKardiaCastError(context, player, tank, target);
            if (castError != null)
            {
                context.Debug.PinKardiaError(castError);
                return false;
            }
        }

        if (!context.ActionService.ExecuteOgcd(SGEActions.Kardia, target.GameObjectId))
            return false;

        context.Debug.KardiaExecutedThisFrame = true;
        context.Debug.KardiaLastCastUtc = DateTime.UtcNow;
        if (!isSwap && DescribeKardiaCastError(context, player, tank, target) is { } postCastError)
            context.Debug.PinKardiaError(postCastError);
        else if (!isSwap
                 && !context.InCombat
                 && tank != null
                 && target.EntityId == tank.EntityId
                 && (context.Debug.TankHasKardion || AsclepiusStatusHelper.HasKardia(player)))
        {
            context.Debug.PinKardiaError("Unexpected OOC recast on tank — Kardion already active");
        }

        context.Debug.PlannedAction = "Kardia (cast)";
        OnKardiaDispatched(context, target, SGEActions.Kardia, decision, reason, tank);
        return true;
    }

    private static void OnKardiaDispatched(
        IAsclepiusContext context,
        IBattleChara target,
        ActionDefinition action,
        string decision,
        string reason,
        IBattleChara? tank = null)
    {
        context.KardiaManager.RecordSwap(target.GameObjectId, target.EntityId);
        tank ??= context.PartyHelper.FindTankInParty(context.Player);
        // Latch the NEW bearer whoever it is (tank, injured ally, DPS fallback, self) so the very
        // next frame suppresses recasts on it — a fallback placement without a latch was one of the
        // chain-cast ingredients when the tank left the zone.
        context.KardiaManager.ConfirmTankKardion(target);
        context.Debug.PlannedAction = "Kardia (cast)";
        context.Debug.PlanningState = decision == "EnsureTank" ? "Kardia -> Tank" : "Placing Kardia";
        context.Debug.KardiaState = "Active";
        context.LogKardiaDecision(target.Name?.TextValue ?? "Unknown", decision, reason);

        if (context.TrainingService?.IsTrainingEnabled != true)
            return;

        var targetName = target.Name?.TextValue ?? "Unknown";
        var isTank = context.PartyHelper.FindTankInParty(context.Player)?.GameObjectId == target.GameObjectId;

        context.TrainingService.RecordDecision(new ActionExplanation
        {
            Timestamp = DateTime.UtcNow,
            ActionId = action.ActionId,
            ActionName = "Kardia",
            Category = "Healing",
            TargetName = targetName,
            ShortReason = $"Kardia placed on {targetName}" + (isTank ? " (tank)" : ""),
            DetailedReason = $"Kardia placed on {targetName}. Kardia is SGE's signature ability - every time you deal damage, the Kardia target receives a 170 potency heal!",
            Factors = new[]
            {
                isTank ? "Target: Tank (primary damage taker)" : "Target: Lowest HP party member",
                "170 potency heal per damaging GCD",
                "No cooldown, instant swap",
            },
            Alternatives = new[] { "No alternatives - Kardia should ALWAYS be placed" },
            Tip = "Keep Kardia on the tank before pulls so your first Dosis heals them passively.",
            ConceptId = SgeConcepts.KardiaManagement,
            Priority = ExplanationPriority.High,
        });
    }

    private void TryPushSoteria(IAsclepiusContext context, RotationScheduler scheduler)
    {
        var config = context.Configuration.Sage;
        var player = context.Player;

        if (!config.EnableSoteria) return;
        if (player.Level < SGEActions.Soteria.MinLevel) return;
        if (context.HasSoteria) return;
        if (!context.HasKardiaPlaced) return;
        if (!context.ActionService.IsActionReady(SGEActions.Soteria.ActionId)) return;

        var kardiaTarget = FindKardiaTargetById(context, context.KardiaTargetId);
        if (kardiaTarget == null) return;

        var hpPercent = kardiaTarget.MaxHp > 0 ? (float)kardiaTarget.CurrentHp / kardiaTarget.MaxHp : 1f;
        if (hpPercent > config.SoteriaThreshold) return;

        var capturedTarget = kardiaTarget;
        var capturedHpPercent = hpPercent;
        var action = SGEActions.Soteria;

        scheduler.PushOgcd(AsclepiusAbilities.Soteria, player.GameObjectId, priority: 1,
            onDispatched: _ =>
            {
                context.Debug.PlannedAction = action.Name;
                context.Debug.PlanningState = "Soteria";
                context.LogKardiaDecision(capturedTarget.Name?.TextValue ?? "Unknown", "Soteria", $"HP {capturedHpPercent:P0}");

                if (context.TrainingService?.IsTrainingEnabled == true)
                {
                    var targetName = capturedTarget.Name?.TextValue ?? "Unknown";
                    context.TrainingService.RecordDecision(new ActionExplanation
                    {
                        Timestamp = DateTime.UtcNow,
                        ActionId = action.ActionId,
                        ActionName = "Soteria",
                        Category = "Healing",
                        TargetName = targetName,
                        ShortReason = $"Soteria - boosting Kardia heals (target at {capturedHpPercent:P0})",
                        DetailedReason = $"Soteria activated with Kardia target {targetName} at {capturedHpPercent:P0} HP. Soteria increases Kardia healing potency by 50% for 15 seconds (4 stacks consumed by your attacks).",
                        Factors = new[]
                        {
                            $"Kardia target HP: {capturedHpPercent:P0}",
                            $"Threshold: {config.SoteriaThreshold:P0}",
                            "50% Kardia potency boost (170 -> 255 per hit)",
                            "4 stacks over 15s",
                            "90s cooldown",
                        },
                        Alternatives = new[] { "Druochole (direct heal)", "Taurochole (heal + mit for tanks)", "Swap Kardia + continue DPS" },
                        Tip = "Soteria is FREE extra healing! It boosts Kardia by 50% for 4 attacks. Use it when your Kardia target is taking sustained damage.",
                        ConceptId = SgeConcepts.SoteriaUsage,
                        Priority = ExplanationPriority.Normal,
                    });
                }
            });
    }

    private void TryPushPhilosophia(IAsclepiusContext context, RotationScheduler scheduler)
    {
        var config = context.Configuration.Sage;
        var player = context.Player;

        if (!config.EnablePhilosophia) return;
        if (player.Level < SGEActions.Philosophia.MinLevel) return;
        if (context.HasPhilosophia) return;
        if (!context.ActionService.IsActionReady(SGEActions.Philosophia.ActionId)) return;

        var (avgHp, _, _) = context.PartyHelper.CalculatePartyHealthMetrics(player);
        if (avgHp > config.PhilosophiaThreshold) return;

        var capturedAvgHp = avgHp;
        var action = SGEActions.Philosophia;

        scheduler.PushOgcd(AsclepiusAbilities.Philosophia, player.GameObjectId, priority: 2,
            onDispatched: _ =>
            {
                context.Debug.PlannedAction = action.Name;
                context.Debug.PlanningState = "Philosophia";

                if (context.TrainingService?.IsTrainingEnabled == true)
                {
                    context.TrainingService.RecordDecision(new ActionExplanation
                    {
                        Timestamp = DateTime.UtcNow,
                        ActionId = action.ActionId,
                        ActionName = "Philosophia",
                        Category = "Healing",
                        TargetName = "Party",
                        ShortReason = $"Philosophia - party-wide Kardia (party at {capturedAvgHp:P0})",
                        DetailedReason = $"Philosophia activated with party at {capturedAvgHp:P0} average HP. For 20 seconds, your damaging attacks heal ALL party members for 100 potency (instead of just the Kardia target). This is incredible sustained party healing while you DPS!",
                        Factors = new[]
                        {
                            $"Party avg HP: {capturedAvgHp:P0}",
                            $"Threshold: {config.PhilosophiaThreshold:P0}",
                            "100 potency party heal per damaging attack",
                            "20s duration",
                            "180s cooldown",
                        },
                        Alternatives = new[] { "Kerachole (AoE regen + mit)", "Ixochole (instant AoE heal)", "Physis II (AoE HoT)" },
                        Tip = "Philosophia is AMAZING for sustained party healing! For 20 seconds, every attack you land heals the ENTIRE party.",
                        ConceptId = SgeConcepts.PhilosophiaUsage,
                        Priority = ExplanationPriority.High,
                    });
                }
            });
    }

    /// <summary>
    /// No-tank fallback: the highest-DPS ally — the de-facto aggro holder, so Kardia's passive
    /// healing lands where the damage goes. Ranked by the live parser when an encounter is running;
    /// with no parse data (pre-pull) the first DPS-role ally in party order wins. Tanks are handled
    /// upstream; healers are skipped (poor bearers). Falls back to the Sage itself.
    /// </summary>
    private static IBattleChara? FindKardiaFallbackTarget(IAsclepiusContext context)
    {
        var player = context.Player;
        IBattleChara? best = null;
        var bestDps = -1f;

        foreach (var member in context.PartyHelper.GetAllPartyMembers(player))
        {
            if (member.CurrentHp == 0 || member.GameObjectId == player.GameObjectId)
                continue;

            var jobId = TrustPartyRoleHelper.ResolveJobId(member, context.PartyList);
            if (JobRegistry.IsTank(jobId) || JobRegistry.IsHealer(jobId))
                continue;

            var dps = GetLiveDps(context, member.Name?.TextValue);
            if (best == null || dps > bestDps)
            {
                best = member;
                bestDps = dps;
            }
        }

        return best ?? player;
    }

    private static float GetLiveDps(IAsclepiusContext context, string? name)
    {
        var encounter = context.DpsMeter?.Current;
        if (encounter == null || string.IsNullOrEmpty(name))
            return 0f;

        foreach (var stats in encounter.GetRanked())
        {
            if (stats.Name == name)
                return encounter.GetDps(stats);
        }

        return 0f;
    }

    private static IBattleChara? FindKardiaTargetById(IAsclepiusContext context, ulong targetId)
    {
        if (targetId == 0) return null;
        if (context.Player.GameObjectId == targetId) return context.Player;
        foreach (var member in context.PartyHelper.GetAllPartyMembers(context.Player))
        {
            if (member.GameObjectId == targetId)
                return member.CurrentHp > 0 ? member : null; // dead bearer = Kardion stripped
        }
        return null;
    }
}
