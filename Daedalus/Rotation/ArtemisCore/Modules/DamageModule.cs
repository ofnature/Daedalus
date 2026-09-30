using Daedalus.Data;
using Daedalus.Models.Action;
using Daedalus.Rotation.ArtemisCore.Abilities;
using Daedalus.Rotation.ArtemisCore.Context;
using Daedalus.Rotation.Common.Helpers;
using Daedalus.Rotation.Common.Scheduling;
using Daedalus.Services.Action;

namespace Daedalus.Rotation.ArtemisCore.Modules;

/// <summary>
/// The Beastmaster damage rotation.
///
/// <para>
/// Two independent tracks, which is what makes this job unlike every other melee: the 2.5s combo
/// chain occupies the GCD, while the instinctual skills run on their OWN 5s recast — "Instinctual
/// skills do not share a recast timer with any other actions". They are Weaponskills that do not
/// consume the global, so they are pushed as oGCDs and weave alongside the chain. Spending a GCD
/// slot on one would roughly halve the job's throughput, and the mistake would be invisible in a
/// log; it would just look slow.
/// </para>
///
/// <para>
/// TP is never read. The Beastmaster gauge is absent from ClientStructs and the only route to the
/// number is scraping JobHudXBM0's nodes. It is not needed: instinctual skills are unusable below
/// their TP minimum, so the game's own action status code is the gate — if it says the action can
/// fire, there is enough TP. That also covers recast and range in the same check.
/// </para>
/// </summary>
public sealed class DamageModule : IArtemisModule
{
    public int Priority => 30;
    public string Name => "Damage";

    public bool TryExecute(IArtemisContext context, bool isMoving) => false;

    public void UpdateDebugState(IArtemisContext context) { }

    public void CollectCandidates(IArtemisContext context, RotationScheduler scheduler, bool isMoving)
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
            BSTActions.SmashAxe.ActionId,
            player);

        if (target == null)
        {
            context.Debug.DamageState = "No target";
            return;
        }

        var targetId = target.GameObjectId;
        var level = player.Level;

        PushComboChain(context, scheduler, targetId, level);

        // With a familiar whose Trick colour is known, Trick and the clockwise axe go out as a pair.
        // Otherwise (no familiar, an unidentified one, or below the levels that make the pair) the
        // axes and Trick run on their own, as before.
        var paired = UsesPairing(context, level, out var answer);
        if (paired)
            PushPaired(context, scheduler, targetId, level, answer!);
        else
            PushInstinctual(context, scheduler, targetId, level);

        PushFamiliarOrders(context, scheduler, targetId, level, trickHandledByPairing: paired);
    }

    /// <summary>
    /// Pair Trick with the axe when: a familiar is out and identified, Trick is learned, and the axe
    /// that answers its colour is learned (Gale Axe, the Volant answer to an Eldritch familiar, only
    /// arrives at level 16).
    /// </summary>
    private static bool UsesPairing(IArtemisContext context, byte level, out ActionDefinition? answer)
    {
        answer = null;
        var cfg = context.Configuration.Beastmaster;
        if (!cfg.EnableTrick || !cfg.EnableInstinctualSkills || !context.HasFamiliar
            || context.FamiliarBeast is not { } beast)
            return false;

        var svc = context.ActionService;
        if (!ActionAvailability.MeetsLevelAndLearned(level, svc, BSTActions.Trick))
            return false;

        answer = BSTActions.InstinctualFor(BSTActions.NextInCycle(beast.TrickAffinity));
        return answer != null && ActionAvailability.MeetsLevelAndLearned(level, svc, answer);
    }

    /// <summary>What the pairing does this frame. Pure, so the rules can be tested without a game.</summary>
    public enum PairingStep
    {
        /// <summary>A Trick is out and its answer is due: press the clockwise axe.</summary>
        AnswerTrick,
        /// <summary>A Trick is out but the familiar has not acted yet: press nothing.</summary>
        WaitForFamiliar,
        /// <summary>Both halves are ready: send Trick; the axe follows once the familiar acts.</summary>
        SendTrick,
        /// <summary>One half is ready and the other is not: hold it for the pair.</summary>
        Hold,
        /// <summary>The axe waited too long for the Trick: send it alone rather than cap TP.</summary>
        AxeAlone,
        /// <summary>The Trick waited too long for the axe: send it alone rather than cap Pet TP.</summary>
        TrickAlone,
        /// <summary>Neither half is ready.</summary>
        Nothing,
    }

    public static PairingStep DecidePairing(
        bool awaitingAnswer, bool readyToAnswer, bool axeReady, bool trickReady, bool holdExpired)
    {
        if (awaitingAnswer)
            return readyToAnswer ? PairingStep.AnswerTrick : PairingStep.WaitForFamiliar;
        if (axeReady && trickReady)
            return PairingStep.SendTrick;
        if (axeReady)
            return holdExpired ? PairingStep.AxeAlone : PairingStep.Hold;
        if (trickReady)
            return holdExpired ? PairingStep.TrickAlone : PairingStep.Hold;
        return PairingStep.Nothing;
    }

    /// <summary>
    /// Trick first, then the clockwise axe once the familiar has acted: an intentional combo finished by
    /// our own skill, which banks Mastered Instinct for Rally. Pressing the axe straight after Trick
    /// resolves it first (the familiar is slow to act) and the combo comes out backwards.
    /// </summary>
    private static void PushPaired(
        IArtemisContext context, RotationScheduler scheduler, ulong targetId, byte level, ActionDefinition answer)
    {
        var svc = context.ActionService;
        var pairing = context.TrickPairing;
        var beast = context.FamiliarBeast!;

        var awaiting = pairing.IsAwaitingAnswer;
        var pendingAnswer = awaiting ? BSTActions.InstinctualFor(pairing.AnswerAffinity) : answer;
        var axeReady = pendingAnswer != null && CanFire(svc, level, pendingAnswer, targetId);
        var trickReady = !awaiting && CanFire(svc, level, BSTActions.Trick, targetId);
        var oneSideReady = !awaiting && axeReady != trickReady;
        var holdExpired = oneSideReady && pairing.HoldExpired();
        if (!oneSideReady)
            pairing.ClearHold();

        var step = DecidePairing(awaiting, pairing.ReadyToAnswer, axeReady, trickReady, holdExpired);
        switch (step)
        {
            case PairingStep.AnswerTrick when axeReady:
                Push(context, scheduler, pendingAnswer!, targetId, priority: 10,
                    why: $"{pairing.PetAffinity} Trick → {pairing.AnswerAffinity}",
                    onDispatched: pairing.OnAnswered);
                break;
            case PairingStep.AnswerTrick:
                context.Debug.InstinctState = $"answer {pairing.AnswerAffinity} not ready";
                break;
            case PairingStep.WaitForFamiliar:
                context.Debug.InstinctState = $"waiting for the {beast.Name} Trick to land";
                break;
            case PairingStep.SendTrick:
            case PairingStep.TrickAlone:
                PushTrick(context, scheduler, targetId, beast.TrickAffinity,
                    step == PairingStep.SendTrick
                        ? $"Trick ({beast.TrickAffinity}) → {answer.Name}"
                        : "Trick alone (axe not ready)");
                break;
            case PairingStep.AxeAlone:
                Push(context, scheduler, answer, targetId, priority: 25, why: "alone (Trick not ready)",
                    onDispatched: pairing.ClearHold);
                break;
            case PairingStep.Hold:
                context.Debug.InstinctState = axeReady ? "holding axe for the Trick" : "holding Trick for the axe";
                break;
            default:
                context.Debug.InstinctState = "none ready";
                break;
        }

        context.Debug.InstinctChain = pairing.Describe();
    }

    private static void PushTrick(
        IArtemisContext context, RotationScheduler scheduler, ulong targetId, InstinctAffinity petAffinity, string why)
    {
        scheduler.PushOgcd(ArtemisAbilities.Trick, targetId, priority: 10, onDispatched: _ =>
        {
            context.Instinct.Record(petAffinity);
            context.TrickPairing.OnTrickDispatched(petAffinity);
            context.Debug.PlannedAction = BSTActions.Trick.Name;
            context.Debug.InstinctState = why;
            context.Debug.InstinctChain = context.TrickPairing.Describe();
        });
    }

    /// <summary>
    /// Smash Axe to Axeblade Bite to Shieldsplitter. Finishers get a stronger priority than the
    /// starter so a live combo is never restarted from the top, and each is gated on combo step
    /// rather than target count — the chain is single-target only, and there is no AoE combo.
    /// </summary>
    private static void PushComboChain(
        IArtemisContext context, RotationScheduler scheduler, ulong targetId, byte level)
    {
        var svc = context.ActionService;

        if (context.ComboStep == 2
            && ActionAvailability.MeetsLevelAndLearned(level, svc, BSTActions.Shieldsplitter))
        {
            scheduler.PushGcd(ArtemisAbilities.Shieldsplitter, targetId, priority: 10,
                onDispatched: _ => context.Debug.PlannedAction = BSTActions.Shieldsplitter.Name);
            return;
        }

        if (context.ComboStep == 1
            && ActionAvailability.MeetsLevelAndLearned(level, svc, BSTActions.AxebladeBite))
        {
            scheduler.PushGcd(ArtemisAbilities.AxebladeBite, targetId, priority: 11,
                onDispatched: _ => context.Debug.PlannedAction = BSTActions.AxebladeBite.Name);
            return;
        }

        scheduler.PushGcd(ArtemisAbilities.SmashAxe, targetId, priority: 20,
            onDispatched: _ => context.Debug.PlannedAction = BSTActions.SmashAxe.Name);
    }

    /// <summary>
    /// Pick one instinctual skill: first the clockwise successor if it will fire (an intentional
    /// combo), otherwise any instinctual skill at all. Taking an off-order spend over dropping the
    /// chain is deliberate — the gauge description states that the chain length itself raises combo
    /// potency, so breaking a long chain to wait for the "right" affinity loses more than it gains.
    /// </summary>
    private static void PushInstinctual(
        IArtemisContext context, RotationScheduler scheduler, ulong targetId, byte level)
    {
        var cfg = context.Configuration.Beastmaster;
        if (!cfg.EnableInstinctualSkills)
            return;

        var svc = context.ActionService;

        if (cfg.PreferIntentionalCombos)
        {
            var wanted = context.Instinct.PreferredNext;
            var preferred = BSTActions.InstinctualFor(wanted);
            if (preferred != null && CanFire(svc, level, preferred, targetId))
            {
                var combo = context.Instinct.ComboFrom(wanted);
                Push(context, scheduler, preferred, targetId, priority: 10, why: $"{wanted} → {combo}");
                return;
            }
        }

        foreach (var affinity in FallbackOrder)
        {
            var action = BSTActions.InstinctualFor(affinity);
            if (action == null || !CanFire(svc, level, action, targetId))
                continue;

            Push(context, scheduler, action, targetId, priority: 25, why: $"{affinity} (off-order)");
            return;
        }

        context.Debug.InstinctState = "none ready";
    }

    /// <summary>Status code 0 means the game accepts it — which subsumes TP, recast and range.</summary>
    private static bool CanFire(IActionService svc, byte level, ActionDefinition action, ulong targetId)
        => ActionAvailability.MeetsLevelAndLearned(level, svc, action)
           && svc.GetActionStatusCode(action.ActionId, targetId) == 0;

    private static readonly InstinctAffinity[] FallbackOrder =
    [
        InstinctAffinity.Volant, InstinctAffinity.Rampant,
        InstinctAffinity.Durant, InstinctAffinity.Eldritch,
    ];

    private static void Push(
        IArtemisContext context, RotationScheduler scheduler,
        ActionDefinition action, ulong targetId, int priority, string why,
        System.Action? onDispatched = null)
    {
        var behavior = ArtemisAbilities.InstinctualFor(BSTActions.AffinityOf(action.ActionId));
        if (behavior == null)
            return;

        scheduler.PushOgcd(behavior, targetId, priority, onDispatched: ctx =>
        {
            // Record what the game ACTUALLY fired, not the button we pressed. At higher levels the
            // base skills substitute into Brutal Rage / Hawkish Talons / Risen Fall / Calamity,
            // whose affinities are Sunstrider and Moonstalker rather than the four Hearts — so
            // recording the base affinity would corrupt the chain the moment the upgrade unlocks.
            var fired = ctx.ActionService.GetAdjustedActionId(action.ActionId);
            var affinity = BSTActions.AffinityOf(fired);
            if (affinity == InstinctAffinity.None)
                affinity = BSTActions.AffinityOf(action.ActionId);

            context.Instinct.Record(affinity);
            onDispatched?.Invoke();
            context.Debug.PlannedAction = action.Name;
            context.Debug.InstinctState = why;
            context.Debug.InstinctChain = context.Instinct.Describe();
        });
    }

    /// <summary>
    /// Trick and Parting Blow both order the familiar, so neither is worth pushing without one out.
    /// <para>
    /// Parting Blow is opt-in. It is the biggest button in the kit — 1,000 potency across 8y — but
    /// the familiar RETREATS afterwards, which silently ends Trick until the player summons again.
    /// That is a trade the player should make, not one the rotation should make for them.
    /// </para>
    /// </summary>
    private static void PushFamiliarOrders(
        IArtemisContext context, RotationScheduler scheduler, ulong targetId, byte level,
        bool trickHandledByPairing)
    {
        if (!context.HasFamiliar)
        {
            context.Debug.DamageState = "No familiar — combo chain only";
            return;
        }

        var svc = context.ActionService;
        var cfg = context.Configuration.Beastmaster;

        if (cfg.EnablePartingBlow && CanFire(svc, level, BSTActions.PartingBlow, targetId))
        {
            scheduler.PushOgcd(ArtemisAbilities.PartingBlow, targetId, priority: 15,
                onDispatched: _ => context.Debug.PlannedAction = BSTActions.PartingBlow.Name);
        }

        if (!trickHandledByPairing && cfg.EnableTrick && CanFire(svc, level, BSTActions.Trick, targetId))
        {
            scheduler.PushOgcd(ArtemisAbilities.Trick, targetId, priority: 30, onDispatched: _ =>
            {
                // The familiar's instinctual skill counts toward the chain — "if either the
                // beastmaster or familiar executes another instinctual skill within seven seconds".
                // Which of the four Hearts it grants depends on the beast and is not knowable from
                // here, so the chain advances without claiming an affinity.
                context.Instinct.RecordUnknown();
                context.Debug.PlannedAction = BSTActions.Trick.Name;
                context.Debug.InstinctChain = context.Instinct.Describe();
            });
        }

        context.Debug.DamageState = "Familiar out";
    }
}
