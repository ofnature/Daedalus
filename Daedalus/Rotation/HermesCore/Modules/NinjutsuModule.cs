using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs.FFXIV.Client.Game;
using Daedalus.Data;
using Daedalus.Models.Action;
using Daedalus.Rotation.Common;
using Daedalus.Rotation.Common.Helpers;
using Daedalus.Rotation.Common.Scheduling;
using Daedalus.Rotation.HermesCore.Context;
using Daedalus.Rotation.HermesCore.Helpers;
using Daedalus.Services;
using Daedalus.Services.Training;

namespace Daedalus.Rotation.HermesCore.Modules;

/// <summary>
/// Handles Ninja mudra sequences and Ninjutsu execution (scheduler-driven).
/// Bypasses the scheduler queue for mudra/ninjutsu/TCJ dispatch because UseAction
/// rejects the replacement action IDs the ninjutsu chain produces — fires raw via
/// ActionManager from CollectCandidates. Other modules push to scheduler normally.
/// </summary>
public sealed class NinjutsuModule : IHermesModule
{
    private const int MudraStepStuckFrameThreshold = 45;
    private const int NinjutsuAbortCooldownFrames = 45;

    private int _mudraCountZeroStuckFrames;
    private int _ninjutsuAbortCooldownFrames;
    private readonly IHermesNinjutsuExecutor _executor;

    public int Priority => 10;
    public string Name => "Ninjutsu";

    public NinjutsuModule(IHermesNinjutsuExecutor? executor = null)
    {
        _executor = executor ?? new HermesNinjutsuExecutor();
    }

    public bool TryExecute(IHermesContext context, bool isMoving) => false;

    public void UpdateDebugState(IHermesContext context) { }

    public void CollectCandidates(IHermesContext context, RotationScheduler scheduler, bool isMoving)
    {
        context.Debug.NinjutsuEvaluated = false;
        HermesNinjutsuMudraExecutor.BeginCollectFrame(context.FrameCache?.FrameNumber ?? 0);

        if (!context.InCombat)
        {
            if (context.MudraHelper.IsSequenceActive && context.MudraHelper.MudraCount >= 1)
                AbortMudraSequence(context, "Combat ended");
            context.Debug.NinjutsuState = "Not in combat";
            return;
        }

        var player = context.Player;
        var level = player.Level;
        var target = context.TargetingService.FindEnemy(
            context.Configuration.Targeting.EnemyStrategy,
            20f,
            player);

        if (target == null && !context.MudraHelper.IsSequenceActive)
        {
            context.Debug.NinjutsuState = "No target";
            return;
        }

        var pack = EnemyPackDebugHelper.Count(context.TargetingService, JobAoERadiusYalms.Melee, player);
        EnemyPackDebugHelper.Apply(context.Debug, pack);
        var enemyCount = pack.AoeRange;

        if (TryHandleRabbitFailure(context, target))
            return;

        // Track 1 — RSR DoTenChiJin (priority over MudraHelper; Phase R1)
        if (context.HasTenChiJin)
            _executor.TryExecuteTenChiJin(context, target, enemyCount);
        else
            _executor.ResetTcjTrack();

        if (context.MudraHelper.IsSequenceActive)
        {
            ContinueMudraSequence(context, target);
            UpdateMudraStuckDebug(context);
            var abortReason = GetMudraAbortReason(context, target);
            if (abortReason != null)
            {
                AbortMudraSequence(context, abortReason);
                return;
            }

            if (context.MudraHelper.MudraCount > 0)
            {
                var slotId = HermesNinjutsuSlotProbe.GetEffectiveSlotId(context.ActionService);
                var aimForDesync = context.MudraHelper.TargetNinjutsu;
                if (aimForDesync != NINActions.NinjutsuType.None
                    && HermesNinjutsuSlotProbe.IsNoActiveNinjutsu(slotId)
                    && !HermesNinjutsuMudraExecutor.IsWaitingForSlotAcknowledge(context, aimForDesync))
                {
                    AbortMudraSequence(context, "Mudra slot desync", applyCooldown: false);
                    return;
                }
            }

            // New sequence only — first mudra on charge CD; release combo instead of idling ~20s.
            var aim = context.MudraHelper.TargetNinjutsu;
            if (context.CanExecuteGcd
                && aim != NINActions.NinjutsuType.None
                && context.MudraHelper.MudraCount == 0
                && HermesNinjutsuMudraExecutor.IsFirstMudraBlockedOnCharge(context, aim))
            {
                AbortMudraSequence(context, "Ten on charge CD — combo filler", applyCooldown: false);
                return;
            }

            return;
        }

        if (context.HasTenChiJin)
            return;

        HermesNinjutsuExecutor.ClearTcjDebugProbe(context);

        if (_ninjutsuAbortCooldownFrames > 0)
            _ninjutsuAbortCooldownFrames--;

        context.Debug.NinjutsuEvaluated = true;
        context.Debug.EnableNinjutsu = context.Configuration.Ninja.EnableNinjutsu;
        context.Debug.NeedsSuiton = HermesNinjutsuDiagnostics.EvaluateNeedsSuiton(
            context, level, enemyCount, out var needsSuitonReason);
        context.Debug.NeedsSuitonReason = needsSuitonReason;
        context.Debug.ShouldStartNinjutsu = HermesNinjutsuDiagnostics.EvaluateShouldStartNinjutsu(
            context, level, enemyCount, out var blockReason);
        context.Debug.ShouldStartNinjutsuBlockReason = blockReason;

        context.Debug.NinjutsuAbortCooldownFrames = _ninjutsuAbortCooldownFrames;

        if (context.Debug.ShouldStartNinjutsu && _ninjutsuAbortCooldownFrames == 0)
        {
            if (TryResumeOrphanedMudraSlot(context, target, enemyCount))
                return;

            if (!HermesNinjutsuMudraExecutor.IsTenPressable(context))
            {
                var needsSuiton = NeedsSuiton(context, level, enemyCount);
                var queued = ResolveNinjutsuTarget(context, level, enemyCount, needsSuiton);
                var tenWait = TenMudraChargeTracker.GetSnapshot(context.ActionService, level);
                var waitSummary = TenMudraChargeTracker.FormatWaitSummary(tenWait);
                context.Debug.NinjutsuState = queued == NINActions.NinjutsuType.None
                    ? $"Idle: {waitSummary}"
                    : $"Queued {queued} — {waitSummary}";
                return;
            }

            StartNinjutsuSequence(context, target, enemyCount);
        }
        else if (_ninjutsuAbortCooldownFrames > 0)
        {
            context.Debug.NinjutsuState = $"Backoff after abort ({_ninjutsuAbortCooldownFrames})";
        }
        else if (string.IsNullOrEmpty(context.Debug.NinjutsuState))
        {
            context.Debug.NinjutsuState = $"Idle: {blockReason}";
        }
    }

    private void AbortMudraSequence(IHermesContext context, string reason, bool applyCooldown = true)
    {
        _mudraCountZeroStuckFrames = 0;
        context.MudraHelper.Reset();
        if (applyCooldown)
            _ninjutsuAbortCooldownFrames = NinjutsuAbortCooldownFrames;
        context.Debug.NinjutsuAbortCooldownFrames = _ninjutsuAbortCooldownFrames;
        context.Debug.NinjutsuState = $"Aborted: {reason}";
    }

    private void UpdateMudraStuckCounter(IHermesContext context, HermesMudraStepResult result)
    {
        if (!context.CanExecuteGcd)
        {
            _mudraCountZeroStuckFrames = 0;
            return;
        }

        var aim = context.MudraHelper.TargetNinjutsu;
        if (aim == NINActions.NinjutsuType.None)
        {
            _mudraCountZeroStuckFrames = 0;
            return;
        }

        if (result == HermesMudraStepResult.WaitingForAcknowledge
            || HermesNinjutsuMudraExecutor.IsStepBlockedOnOpenGcd(context, aim))
        {
            _mudraCountZeroStuckFrames++;
            return;
        }

        _mudraCountZeroStuckFrames = 0;
    }

    private void UpdateMudraStuckDebug(IHermesContext context)
    {
        var aim = context.MudraHelper.TargetNinjutsu;
        var slotId = HermesNinjutsuSlotProbe.GetSlotAdjustedId(context.ActionService);

        context.Debug.MudraCountZeroStuckFrames = _mudraCountZeroStuckFrames;
        context.Debug.MudraCountZeroStuckThreshold = MudraStepStuckFrameThreshold;
        context.Debug.NinjutsuSlotAdjustedId = slotId;
        context.Debug.NinjutsuSlotProbe = HermesNinjutsuSlotProbe.DescribeSlot(slotId);
        context.Debug.MudraStuckCanExecuteGcd = context.CanExecuteGcd;
        context.Debug.MudraStuckGcdRemaining = context.ActionService.GcdRemaining;
        context.Debug.MudraStuckAnimationLock = context.ActionService.AnimationLockRemaining;
        context.Debug.MudraNextName = HermesNinjutsuMudraExecutor.GetExpectedStepName(
            aim, slotId, context.Player.Level, context.HasKassatsu);
        context.Debug.MudraNextCanExecute = HermesNinjutsuMudraExecutor.CanCurrentStepExecute(context, aim, slotId);
    }

    private string? GetMudraAbortReason(IHermesContext context, IBattleChara? target)
    {
        if (_mudraCountZeroStuckFrames >= MudraStepStuckFrameThreshold)
            return "Stuck waiting for mudra step";

        if (!context.MudraHelper.IsSequenceActive)
            return null;
        if (!context.InCombat)
            return "Combat ended";
        return null;
    }

    private void ContinueMudraSequence(IHermesContext context, IBattleChara? target)
    {
        var mudraHelper = context.MudraHelper;
        var result = HermesNinjutsuMudraExecutor.TryExecuteStep(
            context, target, mudraHelper.TargetNinjutsu, out var stepState, out var executedNinjutsu);
        context.Debug.NinjutsuState = stepState;
        UpdateMudraStuckCounter(context, result);

        switch (result)
        {
            case HermesMudraStepResult.ExecutedNinjutsu when executedNinjutsu != null:
                var targetNinjutsu = mudraHelper.TargetNinjutsu;
                if (targetNinjutsu == NINActions.NinjutsuType.Suiton)
                    context.MudraHelper.MarkSuitonExecuted();
                if (targetNinjutsu == NINActions.NinjutsuType.Doton)
                    context.MudraHelper.MarkDotonExecuted();
                mudraHelper.CompleteSequence();
                RecordNinjutsuTraining(context, target, targetNinjutsu, executedNinjutsu);
                break;
            case HermesMudraStepResult.AbortRabbit:
                AbortMudraSequence(context, "Rabbit Medium", applyCooldown: false);
                break;
        }
    }

    private void RecordNinjutsuTraining(
        IHermesContext context,
        IBattleChara? target,
        NINActions.NinjutsuType targetNinjutsu,
        Models.Action.ActionDefinition ninjutsuAction)
    {
        var ninjutsuType = GetNinjutsuDescription(targetNinjutsu, context.HasKassatsu);
        var conceptId = GetNinjutsuConceptId(targetNinjutsu);
        TrainingHelper.Decision(context.TrainingService)
            .Action(ninjutsuAction.ActionId, ninjutsuAction.Name)
            .AsMeleeDamage()
            .Target(target?.Name?.TextValue ?? "Self")
            .Reason($"Executing {ninjutsuAction.Name} ({ninjutsuType})",
                GetNinjutsuExplanation(targetNinjutsu, context.HasKassatsu))
            .Factors(GetNinjutsuFactors(targetNinjutsu, context))
            .Alternatives(GetNinjutsuAlternatives(targetNinjutsu))
            .Tip(GetNinjutsuTip(targetNinjutsu))
            .Concept(conceptId)
            .Record();
        context.TrainingService?.RecordConceptApplication(conceptId, true, ninjutsuType);
    }

    private static bool NeedsSuiton(IHermesContext context, byte level, int enemyCount)
        => HermesNinjutsuDiagnostics.EvaluateNeedsSuiton(context, level, enemyCount, out _);

    private static bool HasDotonActive(IHermesContext context)
        => HermesNinjutsuDiagnostics.HasDotonGroundDoT(context);

    private static NINActions.NinjutsuType ResolveNinjutsuTarget(
        IHermesContext context, byte level, int enemyCount, bool needsSuiton)
    {
        if (needsSuiton && level >= NINActions.Suiton.MinLevel)
            return NINActions.NinjutsuType.Suiton;

        return MudraHelper.GetRecommendedNinjutsu(
            level, context.HasKassatsu, needsSuiton, enemyCount,
            context.Configuration.Ninja.UseDotonForAoE,
            context.Configuration.Ninja.DotonMinTargets,
            HasDotonActive(context));
    }

    private bool TryHandleRabbitFailure(IHermesContext context, IBattleChara? target)
    {
        if (!HermesNinjutsuMudraExecutor.IsRabbitFailureSlot(context))
            return false;

        if (context.MudraHelper.IsSequenceActive)
        {
            AbortMudraSequence(context, "Rabbit Medium", applyCooldown: false);
            return true;
        }

        if (!context.CanExecuteGcd)
        {
            context.Debug.NinjutsuState = "Clearing Rabbit Medium (waiting for GCD)";
            return true;
        }

        if (TryExecuteRabbitClear(context, target))
        {
            context.Debug.NinjutsuState = "Cleared Rabbit Medium";
            return true;
        }

        context.Debug.NinjutsuState = "Rabbit Medium clear failed — continuing rotation";
        return false;
    }

    private bool TryResumeOrphanedMudraSlot(IHermesContext context, IBattleChara? target, int enemyCount)
    {
        if (context.MudraHelper.IsSequenceActive)
            return false;

        var slotId = HermesNinjutsuSlotProbe.GetEffectiveSlotId(context.ActionService);
        if (HermesNinjutsuSlotProbe.IsNoActiveNinjutsu(slotId)
            || HermesNinjutsuSlotProbe.IsRabbitMediumCurrent(slotId))
            return false;

        if (!context.HasGameMudraStatus && !context.CanExecuteGcd)
            return false;

        var level = context.Player.Level;
        var needsSuiton = NeedsSuiton(context, level, enemyCount);
        var aim = ResolveNinjutsuTarget(context, level, enemyCount, needsSuiton);

        if (aim == NINActions.NinjutsuType.None)
            return false;

        _mudraCountZeroStuckFrames = 0;
        context.MudraHelper.StartSequence(aim);
        context.Debug.NinjutsuState = $"Resuming {aim} from slot ({HermesNinjutsuSlotProbe.DescribeSlot(slotId)})";
        ContinueMudraSequence(context, target);
        return true;
    }

    private static unsafe bool TryExecuteRabbitClear(IHermesContext context, IBattleChara? target)
    {
        var actionManager = SafeGameAccess.GetActionManager(null);
        if (actionManager == null)
            return false;

        var adjustedId = actionManager->GetAdjustedActionId(NINActions.Ninjutsu.ActionId);
        if (!HermesNinjutsuSlotProbe.IsRabbitMediumCurrent(adjustedId))
            return false;

        if (actionManager->GetActionStatus(ActionType.Action, adjustedId) != 0)
            return false;

        var targetId = target?.GameObjectId ?? context.Player.GameObjectId;
        if (!actionManager->UseAction(ActionType.Action, NINActions.Ninjutsu.ActionId, targetId))
            return false;

        context.ActionService.NotifyActionExecuted(NINActions.RabbitMedium, adjustedId);
        return true;
    }

    private void StartNinjutsuSequence(IHermesContext context, IBattleChara? target, int enemyCount)
    {
        if (HermesNinjutsuMudraExecutor.IsRabbitFailureSlot(context))
        {
            context.Debug.NinjutsuState = "Waiting to clear Rabbit Medium";
            return;
        }

        var slotId = HermesNinjutsuSlotProbe.GetEffectiveSlotId(context.ActionService);
        if (!HermesNinjutsuSlotProbe.IsNoActiveNinjutsu(slotId))
        {
            context.Debug.NinjutsuState = "Waiting for ninjutsu slot clear";
            return;
        }

        if (context.HasGameMudraStatus)
        {
            context.Debug.NinjutsuState = "Waiting for mudra status clear";
            return;
        }

        var level = context.Player.Level;
        var needsSuiton = NeedsSuiton(context, level, enemyCount);

        var ninjutsu = ResolveNinjutsuTarget(context, level, enemyCount, needsSuiton);

        if (ninjutsu == NINActions.NinjutsuType.None)
        {
            context.Debug.NinjutsuState = "No recommended Ninjutsu";
            return;
        }

        _mudraCountZeroStuckFrames = 0;
        context.MudraHelper.StartSequence(ninjutsu);
        context.Debug.NinjutsuState = $"Starting {ninjutsu}";
        ContinueMudraSequence(context, target);
    }

    private static string GetNinjutsuDescription(NINActions.NinjutsuType ninjutsu, bool hasKassatsu)
    {
        if (hasKassatsu)
        {
            return ninjutsu switch
            {
                NINActions.NinjutsuType.Katon or NINActions.NinjutsuType.GokaMekkyaku => "Enhanced AoE fire damage",
                NINActions.NinjutsuType.Hyoton or NINActions.NinjutsuType.HyoshoRanryu => "Enhanced ice burst",
                _ => "Kassatsu-enhanced"
            };
        }
        return ninjutsu switch
        {
            NINActions.NinjutsuType.FumaShuriken => "Ranged damage",
            NINActions.NinjutsuType.Raiton => "Single-target lightning",
            NINActions.NinjutsuType.Katon => "AoE fire damage",
            NINActions.NinjutsuType.Hyoton => "Ice damage + bind",
            NINActions.NinjutsuType.Huton => "Speed buff (obsolete)",
            NINActions.NinjutsuType.Doton => "Ground AoE DoT",
            NINActions.NinjutsuType.Suiton => "Setup for Kunai's Bane",
            _ => "Ninjutsu"
        };
    }

    private static string GetNinjutsuConceptId(NINActions.NinjutsuType ninjutsu) => ninjutsu switch
    {
        NINActions.NinjutsuType.Suiton => NinConcepts.Suiton,
        NINActions.NinjutsuType.Raiton => NinConcepts.RaijuProcs,
        NINActions.NinjutsuType.Katon or NINActions.NinjutsuType.GokaMekkyaku => NinConcepts.AoeNinjutsu,
        NINActions.NinjutsuType.HyoshoRanryu => NinConcepts.Kassatsu,
        NINActions.NinjutsuType.Doton => NinConcepts.AoeNinjutsu,
        _ => NinConcepts.MudraSystem
    };

    private static string GetNinjutsuExplanation(NINActions.NinjutsuType ninjutsu, bool hasKassatsu)
    {
        if (hasKassatsu)
            return "Kassatsu enhances your next Ninjutsu. Hyosho Ranryu (from Hyoton combo) is highest ST damage.";
        return ninjutsu switch
        {
            NINActions.NinjutsuType.Suiton => "Suiton enables Kunai's Bane.",
            NINActions.NinjutsuType.Raiton => "Raiton is your primary ST Ninjutsu. Grants Raiju Ready.",
            NINActions.NinjutsuType.Katon => "Katon is your AoE Ninjutsu.",
            NINActions.NinjutsuType.Doton => "Doton creates a ground AoE DoT.",
            _ => "Ninjutsu are executed by inputting mudra combinations."
        };
    }

    private static string[] GetNinjutsuFactors(NINActions.NinjutsuType ninjutsu, IHermesContext context) => ninjutsu switch
    {
        NINActions.NinjutsuType.Suiton => new[] { "Kunai's Bane ready", "Burst window preparation" },
        NINActions.NinjutsuType.Raiton => new[] { "ST damage priority", "Grants Raiju Ready", context.HasKassatsu ? "Kassatsu active" : "Standard Raiton" },
        NINActions.NinjutsuType.Katon => new[] { "3+ enemies detected", context.HasKassatsu ? "Kassatsu → Goka Mekkyaku" : "Standard Katon" },
        _ => new[] { "Mudra sequence complete", "Ninjutsu ready" }
    };

    private static string[] GetNinjutsuAlternatives(NINActions.NinjutsuType ninjutsu) => ninjutsu switch
    {
        NINActions.NinjutsuType.Suiton => new[] { "Use Raiton (loses burst window)" },
        NINActions.NinjutsuType.Raiton => new[] { "Use Suiton (if burst coming)" },
        _ => new[] { "Different Ninjutsu (situational)" }
    };

    private static string GetNinjutsuTip(NINActions.NinjutsuType ninjutsu) => ninjutsu switch
    {
        NINActions.NinjutsuType.Suiton => "Time Suiton so Kunai's Bane is ready when the buff is applied.",
        NINActions.NinjutsuType.Raiton => "Raiton → Raiju is free damage.",
        NINActions.NinjutsuType.Katon => "With Kassatsu, this becomes Goka Mekkyaku.",
        _ => "Master your mudra sequences."
    };
}
