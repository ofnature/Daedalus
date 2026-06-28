using System;
using System.Numerics;
using Daedalus.Models.Action;

namespace Daedalus.Services.Action;

/// <summary>
/// Interface for action execution service, providing GCD/oGCD state and cooldown information.
/// </summary>
public interface IActionService
{
    /// <summary>
    /// Current GCD state (Ready, Rolling, WeaveWindow, Casting, AnimationLock).
    /// </summary>
    GcdState CurrentGcdState { get; }

    /// <summary>
    /// Time remaining on GCD (0 if ready).
    /// </summary>
    float GcdRemaining { get; }

    /// <summary>
    /// Live GCD duration in seconds, scaled by skill speed / haste. Stable while the GCD is
    /// ready (unlike <see cref="GcdRemaining"/>, which reports 0 at that moment); use for
    /// GCD-relative timing such as DoT refresh windows.
    /// </summary>
    float GcdDuration { get; }

    /// <summary>
    /// Seconds since the last action (GCD or oGCD) was dispatched. <see cref="double.MaxValue"/> when
    /// nothing has fired yet. Used to detect a stalled rotation (no action while in combat).
    /// </summary>
    double SecondsSinceLastAction { get; }

    /// <summary>
    /// Why the most recent <c>ExecuteGcd</c> returned false due to an internal guard (repeat-GCD,
    /// submit latch, charge guard, backoff). Null when it succeeded or the game refused the cast (that
    /// case is enriched with range/LoS by the scheduler). Surfaced in Why Stuck.
    /// </summary>
    string? LastGcdRejectReason { get; }

    /// <summary>
    /// Animation lock remaining.
    /// </summary>
    float AnimationLockRemaining { get; }

    /// <summary>
    /// Whether player is currently casting.
    /// </summary>
    bool IsCasting { get; }

    /// <summary>
    /// Whether GCD is ready for a new action.
    /// </summary>
    bool CanExecuteGcd { get; }

    /// <summary>
    /// Whether we can weave an oGCD right now.
    /// </summary>
    bool CanExecuteOgcd { get; }

    /// <summary>
    /// Optional Sage guard: return true to block Kardia (24285) at the action layer.
    /// Set/cleared by Asclepius each rotation frame.
    /// </summary>
    Func<ulong, bool>? KardiaRecastGuard { get; set; }

    /// <summary>
    /// Optional recovery hook invoked with an enemy GameObjectId when a GCD is refused for <em>facing</em>
    /// — either UseAction returned false, or a submit was accepted but never committed. Wired to
    /// <c>TargetingService.EnsureHardTarget</c> so the character re-faces the target (auto-face can't
    /// self-correct because it only turns on a successful cast). Fired at most once per stuck window.
    /// </summary>
    Action<ulong>? FaceTargetOnStuck { get; set; }

    /// <summary>
    /// Checks if a specific action is off cooldown.
    /// </summary>
    bool IsActionReady(uint actionId);

    /// <summary>
    /// Gets cooldown remaining for a specific action.
    /// </summary>
    float GetCooldownRemaining(uint actionId);

    /// <summary>
    /// Seconds since this action was last executed (0 if never / off cooldown).
    /// Used for NIN Mug/Trick burst window detection — mirrors RSR Cooldown.ElapsedAfter.
    /// </summary>
    float GetRecastTimeElapsed(uint actionId);

    /// <summary>
    /// Gets the number of available weave slots before the GCD is ready.
    /// </summary>
    int GetAvailableWeaveSlots();

    /// <summary>
    /// Execute a GCD action immediately.
    /// </summary>
    bool ExecuteGcd(ActionDefinition action, ulong targetId);

    /// <summary>
    /// Execute an oGCD action immediately.
    /// </summary>
    bool ExecuteOgcd(ActionDefinition action, ulong targetId);

    /// <summary>
    /// Execute a ground-targeted oGCD action at a specific position.
    /// </summary>
    bool ExecuteGroundTargetedOgcd(ActionDefinition action, Vector3 targetPosition);

    /// <summary>
    /// Checks if a specific action can be executed right now.
    /// Resolves <see cref="GetAdjustedActionId"/> on <paramref name="action"/>.ActionId
    /// before querying the action manager (RSR ActionManagerStatusValid parity).
    /// </summary>
    bool CanExecuteAction(ActionDefinition action);

    /// <summary>
    /// Checks whether the action manager reports the given action ID as executable
    /// (<c>GetActionStatus == 0</c>). Used by the GCD scheduler gate after adjusted-ID
    /// resolution.
    /// </summary>
    bool CanExecuteActionId(uint actionId);

    /// <summary>
    /// Raw <c>GetActionStatus</c> code for the action (0 = usable). Non-zero codes are FFXIV
    /// LogMessage IDs (e.g. 565 = not unlocked). Used to explain a rejected dispatch in Why Stuck.
    /// </summary>
    uint GetActionStatusCode(uint actionId);

    /// <summary>
    /// Raw <c>GetActionStatus</c> code for the action evaluated <em>against a specific target</em>
    /// (0 = usable). Unlike the target-less overload, this evaluates target-dependent refusals —
    /// facing, range, and line of sight — so a stuck dispatch can be explained precisely instead of
    /// guessed. Pass the candidate's target id.
    /// </summary>
    uint GetActionStatusCode(uint actionId, ulong targetId);

    /// <summary>
    /// True when the action is unlocked (level + job quest). Uses action-manager status
    /// 565 ("not learned") plus Lumina unlock-link / class-quest completion via UIState.
    /// </summary>
    bool IsActionLearned(uint actionId);

    /// <summary>
    /// Gets the current number of charges available for an action.
    /// For non-charge actions, returns 1 if ready, 0 if on cooldown.
    /// </summary>
    uint GetCurrentCharges(uint actionId);

    /// <summary>
    /// Gets the maximum number of charges for an action at a given level.
    /// </summary>
    ushort GetMaxCharges(uint actionId, uint level);

    /// <summary>
    /// Checks if it's safe to weave an oGCD without clipping the GCD.
    /// Use this before executing oGCDs to prevent DPS loss from GCD delays.
    /// </summary>
    /// <param name="oGcdAnimationLock">Animation lock of the oGCD (default: 0.6s).</param>
    /// <returns>True if the oGCD can be safely weaved without clipping.</returns>
    bool IsSafeToWeave(float oGcdAnimationLock = 0.6f);

    /// <summary>
    /// Checks if a specific oGCD would clip the GCD if used now.
    /// </summary>
    /// <param name="oGcdAnimationLock">Animation lock of the oGCD.</param>
    /// <returns>True if executing the oGCD would cause clipping.</returns>
    bool WouldClipGcd(float oGcdAnimationLock = 0.6f);

    /// <summary>
    /// Gets the WeaveOptimizer for intelligent oGCD timing and prioritization.
    /// </summary>
    IWeaveOptimizer WeaveOptimizer { get; }

    /// <summary>
    /// Execute a GCD targeting the optimal enemy for a directional AoE.
    /// The game auto-faces toward the target, controlling the cone/line direction.
    /// </summary>
    bool ExecuteDirectionalGcd(ActionDefinition action, ulong optimalTargetId);

    /// <summary>
    /// Executes a GCD for a replacement-chain action, bypassing <c>GetActionStatus</c>
    /// and <c>IsGCD</c> pre-flight validation but still performing all per-cycle
    /// accounting (spam guard, <c>_lastExecutedAction</c>, tracker, <c>ActionExecuted</c> event).
    /// </summary>
    /// <param name="action">
    /// The logical action the rotation chose. Used for tracking, analytics, and the
    /// <c>ActionExecuted</c> event — e.g. Raiton (2267) when the rotation decided to cast Raiton.
    /// </param>
    /// <param name="rawDispatchId">
    /// The base action ID that must go to <c>UseAction</c>. Different from
    /// <paramref name="action"/>.ActionId in replacement-chain scenarios — e.g. the
    /// base Ninjutsu ID (2260) that the game replaces with Raiton at execution time.
    /// </param>
    /// <param name="targetId">Target game object ID.</param>
    bool ExecuteGcdRaw(ActionDefinition action, uint rawDispatchId, ulong targetId);

    /// <summary>
    /// Executes an oGCD for a replacement-chain action — counterpart to
    /// <see cref="ExecuteGcdRaw(ActionDefinition, uint, ulong)"/>. Bypasses
    /// <c>GetActionStatus</c> pre-flight validation but still performs
    /// <c>_ogcdsUsedThisCycle</c> tracking, <c>_lastExecutedAction</c>,
    /// tracker logging, and the <c>ActionExecuted</c> event.
    /// </summary>
    /// <param name="action">The logical action chosen by the rotation (for tracking/events).</param>
    /// <param name="rawDispatchId">The base action ID sent to <c>UseAction</c>.</param>
    /// <param name="targetId">Target game object ID.</param>
    bool ExecuteOgcdRaw(ActionDefinition action, uint rawDispatchId, ulong targetId);

    /// <summary>
    /// Returns the action ID the game will actually use if the player presses
    /// the given base action ID right now (accounts for combos, status-procs,
    /// and level upgrades).
    /// </summary>
    uint GetAdjustedActionId(uint baseActionId);

    /// <summary>
    /// Returns true if the local player currently has the given status effect.
    /// Provided as an action-service-level check so the scheduler's proc gate
    /// is mockable without touching native <c>StatusList</c>.
    /// </summary>
    bool PlayerHasStatus(uint statusId);

    /// <summary>
    /// Execute a consumable item (e.g. combat tincture).
    /// Routes through <c>ActionManager.UseAction(ActionType.Item, ...)</c>.
    /// </summary>
    /// <param name="itemId">NQ item ID. HQ resolution happens internally if <paramref name="preferHq"/> is true.</param>
    /// <param name="preferHq">When true, dispatches the HQ variant (NQ + 1_000_000).</param>
    /// <param name="targetId">Target object ID. Pass 0 for self.</param>
    bool ExecuteItem(uint itemId, bool preferHq, ulong targetId);

    /// <summary>
    /// Action ID of the most recently dispatched oGCD this session (0 if none yet).
    /// Minimal action-history surface for oGCD sequencing (e.g. NIN Trick Attack after Mug).
    /// </summary>
    uint LastOgcdId { get; }

    /// <summary>True if the most recently dispatched GCD matches <paramref name="actionId"/> (false for 0).</summary>
    bool WasLastGcd(uint actionId);

    /// <summary>True if the most recently dispatched oGCD matches <paramref name="actionId"/> (false for 0).</summary>
    bool WasLastOgcd(uint actionId);

    /// <summary>True if the most recent dispatched action matches (RSR IsLastAction parity).</summary>
    bool WasLastAction(uint actionId);

    /// <summary>Records any successful raw ActionManager dispatch by action ID.</summary>
    void RecordActionExecuted(uint actionId);

    /// <summary>Records a successful raw ActionManager GCD dispatch (e.g. TCJ ninjutsu via mudra buttons).</summary>
    void RecordGcdExecuted(uint actionId);

    /// <summary>
    /// Records a successful raw ActionManager dispatch and raises the action feed event.
    /// Does not touch scheduler cycle accounting (<c>_gcdSubmittedThisCycle</c> / oGCD weave slots).
    /// </summary>
    /// <param name="action">Logical action for feed display and tracker.</param>
    /// <param name="recordActionId">
    /// ID stored in action history for <see cref="WasLastAction"/> parity.
    /// When 0, uses <paramref name="action"/>.ActionId.
    /// </param>
    void NotifyActionExecuted(ActionDefinition action, uint recordActionId = 0);
}
