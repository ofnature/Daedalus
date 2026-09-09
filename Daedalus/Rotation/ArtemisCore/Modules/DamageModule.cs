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
        PushInstinctual(context, scheduler, targetId, level);
        PushFamiliarOrders(context, scheduler, targetId, level);
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
        ActionDefinition action, ulong targetId, int priority, string why)
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
        IArtemisContext context, RotationScheduler scheduler, ulong targetId, byte level)
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

        if (cfg.EnableTrick && CanFire(svc, level, BSTActions.Trick, targetId))
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
