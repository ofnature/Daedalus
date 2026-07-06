using System;
using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs.FFXIV.Client.Game;
using Daedalus.Data;
using Daedalus.Models.Action;
using Daedalus.Rotation.Common.Helpers;
using Daedalus.Rotation.HermesCore.Context;
using Daedalus.Services;
using Daedalus.Services.Action;

namespace Daedalus.Rotation.HermesCore.Helpers;

internal enum HermesMudraStepResult
{
    WaitingForGcd,
    WaitingForAcknowledge,
    PressedMudra,
    ExecutedNinjutsu,
    AbortRabbit,
}

/// <summary>
/// RSR DoSuiton / DoRaiton slot-step execution — one mudra per GCD, no AdvanceSequence.
/// </summary>
internal static class HermesNinjutsuMudraExecutor
{
    /// <summary>Mudra recast is 0.5s; the game rejects/garbles a sign submitted before it elapses.</summary>
    internal const double MinSecondsBetweenMudraPresses = 0.5;

    private static ulong _collectFrameNumber;
    private static int _callsThisCollectFrame;

    public static bool IsRabbitFailureSlot(IHermesContext context)
        => HermesNinjutsuSlotProbe.IsRabbitFailureSlot(context.ActionService);

    /// <summary>Call once at the start of NinjutsuModule.CollectCandidates to detect multi-call same frame.</summary>
    public static void BeginCollectFrame(ulong frameNumber)
    {
        if (_collectFrameNumber != frameNumber)
        {
            _collectFrameNumber = frameNumber;
            _callsThisCollectFrame = 0;
        }
    }

    internal readonly struct StepIntent
    {
        public ActionDefinition? MudraToPress { get; init; }
        public ActionDefinition? NinjutsuToExecute { get; init; }
        public bool IsRabbitFailure { get; init; }
        public bool IsUnknownSlot { get; init; }
    }

    public static StepIntent ResolveStep(NINActions.NinjutsuType aim, uint slotId, byte level, bool hasKassatsu)
    {
        if (HermesNinjutsuSlotProbe.IsRabbitMediumCurrent(slotId))
            return new StepIntent { IsRabbitFailure = true };

        return aim switch
        {
            NINActions.NinjutsuType.Suiton => ResolveSuiton(slotId),
            NINActions.NinjutsuType.Raiton => ResolveRaiton(slotId),
            NINActions.NinjutsuType.Katon => ResolveKaton(slotId),
            NINActions.NinjutsuType.FumaShuriken => ResolveFuma(slotId),
            NINActions.NinjutsuType.Doton => ResolveDoton(slotId),
            NINActions.NinjutsuType.Huton => ResolveHuton(slotId),
            NINActions.NinjutsuType.Hyoton => ResolveHyoton(slotId),
            NINActions.NinjutsuType.GokaMekkyaku => ResolveGoka(slotId, level, hasKassatsu),
            NINActions.NinjutsuType.HyoshoRanryu => ResolveHyosho(slotId, level, hasKassatsu),
            _ => new StepIntent { IsUnknownSlot = true }
        };
    }

    public static bool IsStepBlockedOnOpenGcd(IHermesContext context, NINActions.NinjutsuType aim)
    {
        var slotId = HermesNinjutsuSlotProbe.GetSlotAdjustedId(context.ActionService);
        var intent = ResolveStep(aim, slotId, context.Player.Level, context.HasKassatsu);
        if (intent.IsRabbitFailure || intent.IsUnknownSlot)
            return false;

        if (intent.NinjutsuToExecute != null)
            return !context.ActionService.CanExecuteActionId(
                context.ActionService.GetAdjustedActionId(NINActions.Ninjutsu.ActionId));

        if (intent.MudraToPress == null)
            return false;

        var mudra = intent.MudraToPress;
        var adjustedId = context.ActionService.GetAdjustedActionId(mudra.ActionId);
        if (context.ActionService.WasLastAction(mudra.ActionId)
            || context.ActionService.WasLastAction(adjustedId))
            return false;

        return !CanPressMudraUsedUp(context, adjustedId);
    }

    /// <summary>
    /// True when a newly started sequence (MudraCount == 0) cannot press its first mudra due to charge CD.
    /// Distinct from <see cref="IsStepBlockedOnOpenGcd"/> which also returns true while waiting on ninjutsu finish.
    /// </summary>
    public static bool IsFirstMudraBlockedOnCharge(IHermesContext context, NINActions.NinjutsuType aim)
    {
        if (context.MudraHelper.MudraCount > 0)
            return false;

        if (!context.CanExecuteGcd)
            return false;

        var slotId = HermesNinjutsuSlotProbe.GetEffectiveSlotId(context.ActionService);
        var intent = ResolveStep(aim, slotId, context.Player.Level, context.HasKassatsu);
        if (intent.IsRabbitFailure || intent.IsUnknownSlot || intent.NinjutsuToExecute != null)
            return false;

        if (intent.MudraToPress == null)
            return false;

        var mudra = intent.MudraToPress;
        var adjustedId = context.ActionService.GetAdjustedActionId(mudra.ActionId);
        if (context.ActionService.WasLastAction(mudra.ActionId)
            || context.ActionService.WasLastAction(adjustedId))
            return false;

        return !CanPressMudraUsedUp(context, adjustedId);
    }

    /// <summary>
    /// Mudra was pressed but the Ninjutsu slot has not advanced — re-pressing is blocked by WasLastAction.
    /// This stall must count toward abort; otherwise only the 7s sequence timeout fires and the rotation idles.
    /// </summary>
    public static bool IsWaitingForSlotAcknowledge(IHermesContext context, NINActions.NinjutsuType aim)
    {
        if (!context.CanExecuteGcd)
            return false;

        var slotFromService = HermesNinjutsuSlotProbe.GetSlotAdjustedId(context.ActionService);
        var slotFromManager = HermesNinjutsuSlotProbe.GetSlotFromActionManager();
        var slotId = slotFromManager != 0 ? slotFromManager : slotFromService;

        var intent = ResolveStep(aim, slotId, context.Player.Level, context.HasKassatsu);
        if (intent.IsRabbitFailure || intent.MudraToPress == null)
            return false;

        return WasLastMudra(context, intent.MudraToPress);
    }

    /// <summary>
    /// True when NinjutsuModule will attempt a mudra press or ninjutsu finish on the open GCD.
    /// Used to block combo so both do not fire in the same collect frame.
    /// </summary>
    public static bool WillConsumeOpenGcdForMudraStep(IHermesContext context, NINActions.NinjutsuType aim)
    {
        if (!context.CanExecuteGcd)
            return false;

        var slotFromService = HermesNinjutsuSlotProbe.GetSlotAdjustedId(context.ActionService);
        var slotFromManager = HermesNinjutsuSlotProbe.GetSlotFromActionManager();
        var slotId = slotFromManager != 0 ? slotFromManager : slotFromService;

        var intent = ResolveStep(aim, slotId, context.Player.Level, context.HasKassatsu);
        if (intent.IsRabbitFailure || intent.IsUnknownSlot)
            return false;

        if (intent.NinjutsuToExecute != null)
        {
            return context.ActionService.CanExecuteActionId(
                context.ActionService.GetAdjustedActionId(NINActions.Ninjutsu.ActionId));
        }

        if (intent.MudraToPress == null)
            return false;

        if (WasLastMudra(context, intent.MudraToPress))
            return false;

        var adjustedId = context.ActionService.GetAdjustedActionId(intent.MudraToPress.ActionId);
        return CanPressMudraUsedUp(context, adjustedId);
    }

    internal static bool IsTenPressable(IHermesContext context)
        => TenMudraChargeTracker.GetSnapshot(context.ActionService, context.Player.Level).IsPressable;

    /// <summary>
    /// Combo must not steal this GCD — that desyncs the sequence into Rabbit Medium.
    /// </summary>
    public static bool IsPendingNinjutsuFinishStep(IHermesContext context, NINActions.NinjutsuType aim)
    {
        if (!context.CanExecuteGcd)
            return false;

        var slotFromService = HermesNinjutsuSlotProbe.GetSlotAdjustedId(context.ActionService);
        var slotFromManager = HermesNinjutsuSlotProbe.GetSlotFromActionManager();
        var slotId = slotFromManager != 0 ? slotFromManager : slotFromService;

        var intent = ResolveStep(aim, slotId, context.Player.Level, context.HasKassatsu);
        if (intent.IsRabbitFailure || intent.IsUnknownSlot || intent.NinjutsuToExecute == null)
            return false;

        return !context.ActionService.CanExecuteActionId(
            context.ActionService.GetAdjustedActionId(NINActions.Ninjutsu.ActionId));
    }

    public static string GetExpectedStepName(NINActions.NinjutsuType aim, uint slotId, byte level, bool hasKassatsu)
    {
        var intent = ResolveStep(aim, slotId, level, hasKassatsu);
        if (intent.IsRabbitFailure) return "Rabbit Medium";
        if (intent.NinjutsuToExecute != null) return intent.NinjutsuToExecute.Name;
        if (intent.MudraToPress != null) return intent.MudraToPress.Name;
        return "";
    }

    public static bool CanCurrentStepExecute(IHermesContext context, NINActions.NinjutsuType aim, uint slotId)
    {
        var intent = ResolveStep(aim, slotId, context.Player.Level, context.HasKassatsu);
        if (intent.IsRabbitFailure || intent.IsUnknownSlot)
            return false;

        if (intent.NinjutsuToExecute != null)
            return context.ActionService.CanExecuteActionId(
                context.ActionService.GetAdjustedActionId(NINActions.Ninjutsu.ActionId));

        if (intent.MudraToPress == null)
            return false;

        var adjustedId = context.ActionService.GetAdjustedActionId(intent.MudraToPress.ActionId);
        if (context.ActionService.WasLastAction(intent.MudraToPress.ActionId)
            || context.ActionService.WasLastAction(adjustedId))
            return false;

        return CanPressMudraUsedUp(context, adjustedId);
    }

    /// <summary>
    /// RSR CanUse(usedUp: true) parity for charge-based mudras — not GetActionStatus == 0.
    /// </summary>
    internal static bool CanPressMudraUsedUp(IHermesContext context, uint adjustedId)
    {
        var actionService = context.ActionService;
        if (actionService.GetCurrentCharges(adjustedId) > 0)
            return true;

        return actionService.GetCooldownRemaining(adjustedId) <= actionService.GcdRemaining;
    }

    public static HermesMudraStepResult TryExecuteStep(
        IHermesContext context,
        IBattleChara? target,
        NINActions.NinjutsuType aim,
        out string debugState,
        out ActionDefinition? executedNinjutsu)
    {
        debugState = "";
        executedNinjutsu = null;

        _callsThisCollectFrame++;
        var callIndex = _callsThisCollectFrame;

        var canExecuteGcd = context.CanExecuteGcd;
        var slotFromService = HermesNinjutsuSlotProbe.GetSlotAdjustedId(context.ActionService);
        var slotFromManager = HermesNinjutsuSlotProbe.GetSlotFromActionManager();
        var slotId = slotFromManager != 0 ? slotFromManager : slotFromService;
        var slotBranch = HermesNinjutsuSlotProbe.GetMatchedSlotBranch(slotId);
        var wasLastTen = WasLastMudra(context, NINActions.Ten);
        var wasLastChi = WasLastMudra(context, NINActions.Chi);
        var wasLastJin = WasLastMudra(context, NINActions.Jin);

        PopulateStepDebug(
            context,
            callIndex,
            canExecuteGcd,
            slotId,
            slotFromService,
            slotFromManager,
            slotBranch,
            wasLastTen,
            wasLastChi,
            wasLastJin);

        var intent = ResolveStep(aim, slotId, context.Player.Level, context.HasKassatsu);

        if (intent.IsRabbitFailure)
        {
            debugState = FormatDebugState(
                callIndex, canExecuteGcd, slotId, slotBranch, wasLastTen, wasLastChi, wasLastJin,
                "Rabbit Medium (failed sequence)", result: "AbortRabbit");
            return HermesMudraStepResult.AbortRabbit;
        }

        if (!canExecuteGcd)
        {
            debugState = FormatDebugState(
                callIndex, canExecuteGcd, slotId, slotBranch, wasLastTen, wasLastChi, wasLastJin,
                "Waiting for GCD", result: "WaitingForGcd");
            return HermesMudraStepResult.WaitingForGcd;
        }

        // Inter-press pacing: the 0.5s mudra GCD sits inside the queue window, so CanExecuteGcd
        // reads true almost immediately after a press — a same-slot fast acknowledge then let the
        // next sign submit 0.30s later, racing the game's combo-state write into a duplicate sign
        // and Rabbit Medium (two rabbits in three Lv60 pulls, both at pull open; clean sequences
        // space 0.7-0.8s). Hold the next step until the mudra recast has actually elapsed.
        if (context.MudraHelper.SecondsSinceLastPress < MinSecondsBetweenMudraPresses)
        {
            debugState = FormatDebugState(
                callIndex, canExecuteGcd, slotId, slotBranch, wasLastTen, wasLastChi, wasLastJin,
                $"Pacing ({context.MudraHelper.SecondsSinceLastPress:0.00}s since last sign)",
                result: "WaitingForAcknowledge");
            return HermesMudraStepResult.WaitingForAcknowledge;
        }

        if (intent.NinjutsuToExecute != null)
        {
            var result = TryExecuteNinjutsu(context, target, intent.NinjutsuToExecute, slotId, callIndex,
                canExecuteGcd, slotBranch, wasLastTen, wasLastChi, wasLastJin, out debugState, out executedNinjutsu);
            return result;
        }

        if (intent.MudraToPress != null)
        {
            var result = TryPressMudra(context, intent.MudraToPress, slotId, callIndex,
                canExecuteGcd, slotBranch, wasLastTen, wasLastChi, wasLastJin, out debugState);
            return result;
        }

        debugState = FormatDebugState(
            callIndex, canExecuteGcd, slotId, slotBranch, wasLastTen, wasLastChi, wasLastJin,
            $"Waiting for slot (aim {aim})", result: "WaitingForAcknowledge");
        return HermesMudraStepResult.WaitingForAcknowledge;
    }

    private static void PopulateStepDebug(
        IHermesContext context,
        int callIndex,
        bool canExecuteGcd,
        uint slotId,
        uint slotFromService,
        uint slotFromManager,
        string slotBranch,
        bool wasLastTen,
        bool wasLastChi,
        bool wasLastJin)
    {
        var debug = context.Debug;
        debug.MudraStepFrameNumber = _collectFrameNumber;
        debug.MudraStepCallsThisFrame = callIndex;
        debug.MudraStepCanExecuteGcd = canExecuteGcd;
        debug.NinjutsuSlotAdjustedId = slotId;
        debug.NinjutsuSlotFromActionManager = slotFromManager;
        debug.NinjutsuSlotProbe = HermesNinjutsuSlotProbe.DescribeSlot(slotId);
        debug.MudraStepSlotBranch = slotBranch;
        debug.MudraWasLastTen = wasLastTen;
        debug.MudraWasLastChi = wasLastChi;
        debug.MudraWasLastJin = wasLastJin;
        PopulateMudraPressabilityDebug(context, debug);

        if (slotFromManager != 0 && slotFromService != slotFromManager)
        {
            debug.NinjutsuState =
                $"[slot mismatch svc={slotFromService} am={slotFromManager}]";
        }
    }

    private static string FormatDebugState(
        int callIndex,
        bool canExecuteGcd,
        uint slotId,
        string slotBranch,
        bool wasLastTen,
        bool wasLastChi,
        bool wasLastJin,
        string action,
        string result)
    {
        return $"f#{_collectFrameNumber} call#{callIndex} | slot={slotId} ({slotBranch}) | " +
               $"CanExecuteGcd={canExecuteGcd} | WasLast T/C/J={wasLastTen}/{wasLastChi}/{wasLastJin} | " +
               $"{action} → {result}";
    }

    private static bool WasLastMudra(IHermesContext context, ActionDefinition mudra)
    {
        var adjustedId = context.ActionService.GetAdjustedActionId(mudra.ActionId);
        return context.ActionService.WasLastAction(mudra.ActionId)
               || context.ActionService.WasLastAction(adjustedId);
    }

    internal static void PopulateTenChargeDebug(IHermesContext context, HermesDebugState debug)
    {
        var snapshot = TenMudraChargeTracker.GetSnapshot(context.ActionService, context.Player.Level);
        debug.MudraTenCharges = snapshot.CurrentCharges;
        debug.MudraTenMaxCharges = snapshot.MaxCharges;
        debug.MudraTenCooldownRemaining = snapshot.NextChargeRemaining;
        debug.MudraTenChargeRecastTotal = snapshot.ChargeRecastTotal;
        debug.MudraTenIsPressable = snapshot.IsPressable;
        debug.MudraTenSecondsUntilPressable = snapshot.SecondsUntilPressable;

        unsafe
        {
            var actionManager = SafeGameAccess.GetActionManager(null);
            if (actionManager == null)
                return;

            debug.MudraTenActionStatus = actionManager->GetActionStatus(ActionType.Action, snapshot.AdjustedActionId);
            debug.MudraTenEventStatus = actionManager->GetActionStatus(ActionType.EventAction, snapshot.AdjustedActionId);
        }
    }

    private static void PopulateMudraPressabilityDebug(IHermesContext context, HermesDebugState debug)
        => PopulateTenChargeDebug(context, debug);

    private static string DescribeMudraPressability(IHermesContext context, uint adjustedId)
    {
        var charges = context.ActionService.GetCurrentCharges(adjustedId);
        var cdRem = context.ActionService.GetCooldownRemaining(adjustedId);

        unsafe
        {
            var actionManager = SafeGameAccess.GetActionManager(null);
            if (actionManager == null)
                return $"charges={charges} cd={cdRem:F2}s";

            var statusAction = actionManager->GetActionStatus(ActionType.Action, adjustedId);
            var statusEvent = actionManager->GetActionStatus(ActionType.EventAction, adjustedId);
            return $"GetActionStatus(Action)={statusAction} Event={statusEvent} charges={charges} cd={cdRem:F2}s";
        }
    }

    private static StepIntent ResolveSuiton(uint slotId)
    {
        if (HermesNinjutsuSlotProbe.IsSuitonCurrent(slotId))
            return new StepIntent { NinjutsuToExecute = NINActions.Suiton };
        if (HermesNinjutsuSlotProbe.IsRaitonCurrent(slotId))
            return new StepIntent { MudraToPress = NINActions.Jin };
        if (HermesNinjutsuSlotProbe.IsFumaShurikenCurrent(slotId))
            return new StepIntent { MudraToPress = NINActions.Chi };
        if (HermesNinjutsuSlotProbe.IsNoActiveNinjutsu(slotId))
            return new StepIntent { MudraToPress = NINActions.Ten };
        return new StepIntent { IsUnknownSlot = true };
    }

    private static StepIntent ResolveRaiton(uint slotId)
    {
        if (HermesNinjutsuSlotProbe.IsRaitonCurrent(slotId))
            return new StepIntent { NinjutsuToExecute = NINActions.Raiton };
        if (HermesNinjutsuSlotProbe.IsFumaShurikenCurrent(slotId))
            return new StepIntent { MudraToPress = NINActions.Chi };
        if (HermesNinjutsuSlotProbe.IsNoActiveNinjutsu(slotId))
            return new StepIntent { MudraToPress = NINActions.Ten };
        return new StepIntent { IsUnknownSlot = true };
    }

    private static StepIntent ResolveKaton(uint slotId)
    {
        if (HermesNinjutsuSlotProbe.IsKatonCurrent(slotId))
            return new StepIntent { NinjutsuToExecute = NINActions.Katon };
        if (HermesNinjutsuSlotProbe.IsFumaShurikenCurrent(slotId))
            return new StepIntent { MudraToPress = NINActions.Ten };
        if (HermesNinjutsuSlotProbe.IsNoActiveNinjutsu(slotId))
            return new StepIntent { MudraToPress = NINActions.Chi };
        return new StepIntent { IsUnknownSlot = true };
    }

    private static StepIntent ResolveFuma(uint slotId)
    {
        if (HermesNinjutsuSlotProbe.IsFumaShurikenCurrent(slotId))
            return new StepIntent { NinjutsuToExecute = NINActions.FumaShuriken };
        if (HermesNinjutsuSlotProbe.IsNoActiveNinjutsu(slotId))
            return new StepIntent { MudraToPress = NINActions.Ten };
        return new StepIntent { IsUnknownSlot = true };
    }

    private static StepIntent ResolveDoton(uint slotId)
    {
        if (HermesNinjutsuSlotProbe.IsDotonCurrent(slotId))
            return new StepIntent { NinjutsuToExecute = NINActions.Doton };
        if (HermesNinjutsuSlotProbe.IsHyotonCurrent(slotId))
            return new StepIntent { MudraToPress = NINActions.Chi };
        if (HermesNinjutsuSlotProbe.IsFumaShurikenCurrent(slotId))
            return new StepIntent { MudraToPress = NINActions.Jin };
        if (HermesNinjutsuSlotProbe.IsNoActiveNinjutsu(slotId))
            return new StepIntent { MudraToPress = NINActions.Ten };
        return new StepIntent { IsUnknownSlot = true };
    }

    private static StepIntent ResolveHuton(uint slotId)
    {
        if (HermesNinjutsuSlotProbe.IsHutonCurrent(slotId))
            return new StepIntent { NinjutsuToExecute = NINActions.Huton };
        if (HermesNinjutsuSlotProbe.IsHyotonCurrent(slotId))
            return new StepIntent { MudraToPress = NINActions.Ten };
        if (HermesNinjutsuSlotProbe.IsFumaShurikenCurrent(slotId))
            return new StepIntent { MudraToPress = NINActions.Jin };
        if (HermesNinjutsuSlotProbe.IsNoActiveNinjutsu(slotId))
            return new StepIntent { MudraToPress = NINActions.Chi };
        return new StepIntent { IsUnknownSlot = true };
    }

    private static StepIntent ResolveHyoton(uint slotId)
    {
        if (HermesNinjutsuSlotProbe.IsHyotonCurrent(slotId))
            return new StepIntent { NinjutsuToExecute = NINActions.Hyoton };
        if (HermesNinjutsuSlotProbe.IsFumaShurikenCurrent(slotId))
            return new StepIntent { MudraToPress = NINActions.Jin };
        if (HermesNinjutsuSlotProbe.IsNoActiveNinjutsu(slotId))
            return new StepIntent { MudraToPress = NINActions.Chi };
        return new StepIntent { IsUnknownSlot = true };
    }

    private static StepIntent ResolveGoka(uint slotId, byte level, bool hasKassatsu)
    {
        var execute = GetNinjutsuAction(NINActions.NinjutsuType.GokaMekkyaku, hasKassatsu, level);
        if (execute == null)
            return new StepIntent { IsUnknownSlot = true };

        if (HermesNinjutsuSlotProbe.IsGokaMekkyakuCurrent(slotId))
            return new StepIntent { NinjutsuToExecute = execute };
        if (HermesNinjutsuSlotProbe.IsFumaShurikenCurrent(slotId))
            return new StepIntent { MudraToPress = NINActions.Ten };
        if (HermesNinjutsuSlotProbe.IsNoActiveNinjutsu(slotId))
            return new StepIntent { MudraToPress = NINActions.Chi };
        return new StepIntent { IsUnknownSlot = true };
    }

    private static StepIntent ResolveHyosho(uint slotId, byte level, bool hasKassatsu)
    {
        var execute = GetNinjutsuAction(NINActions.NinjutsuType.HyoshoRanryu, hasKassatsu, level);
        if (execute == null)
            return new StepIntent { IsUnknownSlot = true };

        if (HermesNinjutsuSlotProbe.IsHyoshoRanryuCurrent(slotId))
            return new StepIntent { NinjutsuToExecute = execute };
        if (HermesNinjutsuSlotProbe.IsFumaShurikenCurrent(slotId))
            return new StepIntent { MudraToPress = NINActions.Jin };
        if (HermesNinjutsuSlotProbe.IsNoActiveNinjutsu(slotId))
            return new StepIntent { MudraToPress = NINActions.Chi };
        return new StepIntent { IsUnknownSlot = true };
    }

    private static unsafe HermesMudraStepResult TryPressMudra(
        IHermesContext context,
        ActionDefinition mudra,
        uint slotIdAtEntry,
        int callIndex,
        bool canExecuteGcd,
        string slotBranch,
        bool wasLastTen,
        bool wasLastChi,
        bool wasLastJin,
        out string debugState)
    {
        var baseId = mudra.ActionId;
        var adjustedId = context.ActionService.GetAdjustedActionId(baseId);
        var wasLastThisMudra = context.ActionService.WasLastAction(baseId)
                               || context.ActionService.WasLastAction(adjustedId);

        if (wasLastThisMudra)
        {
            debugState = FormatDebugState(
                callIndex, canExecuteGcd, slotIdAtEntry, slotBranch, wasLastTen, wasLastChi, wasLastJin,
                $"Waiting for {mudra.Name} acknowledge (adj={adjustedId})", result: "WaitingForAcknowledge");
            return HermesMudraStepResult.WaitingForAcknowledge;
        }

        if (!CanPressMudraUsedUp(context, adjustedId))
        {
            var statusDetail = DescribeMudraPressability(context, adjustedId);
            debugState = FormatDebugState(
                callIndex, canExecuteGcd, slotIdAtEntry, slotBranch, wasLastTen, wasLastChi, wasLastJin,
                $"Waiting for {mudra.Name} usedUp (adj={adjustedId}, {statusDetail})", result: "WaitingForAcknowledge");
            return HermesMudraStepResult.WaitingForAcknowledge;
        }

        if (mudra.ActionId == NINActions.Ten.ActionId)
            context.Debug.NinjutsuSlotBeforeTenPress = slotIdAtEntry;

        var actionManager = SafeGameAccess.GetActionManager(null);
        if (actionManager == null)
        {
            debugState = FormatDebugState(
                callIndex, canExecuteGcd, slotIdAtEntry, slotBranch, wasLastTen, wasLastChi, wasLastJin,
                "ActionManager unavailable", result: "WaitingForAcknowledge");
            return HermesMudraStepResult.WaitingForAcknowledge;
        }

        if (!actionManager->UseAction(ActionType.Action, adjustedId, context.Player.GameObjectId))
        {
            debugState = FormatDebugState(
                callIndex, canExecuteGcd, slotIdAtEntry, slotBranch, wasLastTen, wasLastChi, wasLastJin,
                $"UseAction failed {mudra.Name} (adj={adjustedId})", result: "WaitingForAcknowledge");
            return HermesMudraStepResult.WaitingForAcknowledge;
        }

        context.ActionService.NotifyActionExecuted(mudra, adjustedId);
        context.MudraHelper.NotifyMudraPressed();

        var slotAfterPress = actionManager->GetAdjustedActionId(NINActions.Ninjutsu.ActionId);
        if (mudra.ActionId == NINActions.Ten.ActionId)
            context.Debug.NinjutsuSlotAfterTenPress = slotAfterPress;

        context.Debug.PlannedAction = mudra.Name;
        debugState = FormatDebugState(
            callIndex, canExecuteGcd, slotIdAtEntry, slotBranch, wasLastTen, wasLastChi, wasLastJin,
            $"Input {mudra.Name} (adj={adjustedId}, slotAfter={slotAfterPress})", result: "PressedMudra");
        return HermesMudraStepResult.PressedMudra;
    }

    private static unsafe HermesMudraStepResult TryExecuteNinjutsu(
        IHermesContext context,
        IBattleChara? target,
        ActionDefinition ninjutsuAction,
        uint slotIdAtEntry,
        int callIndex,
        bool canExecuteGcd,
        string slotBranch,
        bool wasLastTen,
        bool wasLastChi,
        bool wasLastJin,
        out string debugState,
        out ActionDefinition? executedNinjutsu)
    {
        executedNinjutsu = ninjutsuAction;
        var targetId = target?.GameObjectId ?? context.Player.GameObjectId;
        if (ninjutsuAction.TargetType == ActionTargetType.Self)
            targetId = context.Player.GameObjectId;

        var actionManager = SafeGameAccess.GetActionManager(null);
        if (actionManager == null)
        {
            debugState = FormatDebugState(
                callIndex, canExecuteGcd, slotIdAtEntry, slotBranch, wasLastTen, wasLastChi, wasLastJin,
                "ActionManager unavailable", result: "WaitingForAcknowledge");
            executedNinjutsu = null;
            return HermesMudraStepResult.WaitingForAcknowledge;
        }

        var adjustedId = actionManager->GetAdjustedActionId(NINActions.Ninjutsu.ActionId);
        if (actionManager->GetActionStatus(ActionType.Action, adjustedId) != 0)
        {
            debugState = FormatDebugState(
                callIndex, canExecuteGcd, slotIdAtEntry, slotBranch, wasLastTen, wasLastChi, wasLastJin,
                $"Waiting for {ninjutsuAction.Name} (adj={adjustedId})", result: "WaitingForAcknowledge");
            executedNinjutsu = null;
            return HermesMudraStepResult.WaitingForAcknowledge;
        }

        if (!actionManager->UseAction(ActionType.Action, NINActions.Ninjutsu.ActionId, targetId))
        {
            debugState = FormatDebugState(
                callIndex, canExecuteGcd, slotIdAtEntry, slotBranch, wasLastTen, wasLastChi, wasLastJin,
                $"UseAction failed {ninjutsuAction.Name}", result: "WaitingForAcknowledge");
            executedNinjutsu = null;
            return HermesMudraStepResult.WaitingForAcknowledge;
        }

        context.ActionService.NotifyActionExecuted(ninjutsuAction, adjustedId);
        context.Debug.PlannedAction = ninjutsuAction.Name;
        debugState = FormatDebugState(
            callIndex, canExecuteGcd, slotIdAtEntry, slotBranch, wasLastTen, wasLastChi, wasLastJin,
            $"Executed {ninjutsuAction.Name} (adj={adjustedId})", result: "ExecutedNinjutsu");
        return HermesMudraStepResult.ExecutedNinjutsu;
    }

    private static ActionDefinition? GetNinjutsuAction(
        NINActions.NinjutsuType ninjutsu, bool hasKassatsu, byte level)
    {
        if (hasKassatsu)
        {
            return ninjutsu switch
            {
                NINActions.NinjutsuType.Katon or NINActions.NinjutsuType.GokaMekkyaku
                    when level >= NINActions.GokaMekkyaku.MinLevel => NINActions.GokaMekkyaku,
                NINActions.NinjutsuType.Hyoton or NINActions.NinjutsuType.HyoshoRanryu
                    when level >= NINActions.HyoshoRanryu.MinLevel => NINActions.HyoshoRanryu,
                NINActions.NinjutsuType.Raiton => NINActions.Raiton,
                _ => GetBaseNinjutsuAction(ninjutsu)
            };
        }

        return GetBaseNinjutsuAction(ninjutsu);
    }

    private static ActionDefinition? GetBaseNinjutsuAction(NINActions.NinjutsuType ninjutsu) => ninjutsu switch
    {
        NINActions.NinjutsuType.FumaShuriken => NINActions.FumaShuriken,
        NINActions.NinjutsuType.Raiton => NINActions.Raiton,
        NINActions.NinjutsuType.Katon => NINActions.Katon,
        NINActions.NinjutsuType.Hyoton => NINActions.Hyoton,
        NINActions.NinjutsuType.Huton => NINActions.Huton,
        NINActions.NinjutsuType.Doton => NINActions.Doton,
        NINActions.NinjutsuType.Suiton => NINActions.Suiton,
        NINActions.NinjutsuType.GokaMekkyaku => NINActions.GokaMekkyaku,
        NINActions.NinjutsuType.HyoshoRanryu => NINActions.HyoshoRanryu,
        _ => null
    };
}
