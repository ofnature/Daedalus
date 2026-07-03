using Daedalus.Config.DPS;
using Daedalus.Data;
using Daedalus.Rotation.Common.Helpers;
using Daedalus.Rotation.Common.Scheduling;
using Daedalus.Rotation.ProteusCore.Abilities;
using Daedalus.Rotation.ProteusCore.Context;

namespace Daedalus.Rotation.ProteusCore.Modules;

/// <summary>
/// Blue Mage GCD damage. Priority: Rose of Destruction (30s CD) → Song of Torment (DoT upkeep) →
/// Plaincracker (AoE, 6y SELF-anchored count) → role filler (Goblin Punch instant melee for tank,
/// Sonic Boom otherwise) → Water Cannon fallback. Unslotted spells are rejected at dispatch and
/// fall through to the next candidate, so a thin loadout degrades instead of stalling.
/// </summary>
public sealed class DamageModule : IProteusModule
{
    public int Priority => 30;
    public string Name => "Damage";

    public bool TryExecute(IProteusContext context, bool isMoving) => false;
    public void UpdateDebugState(IProteusContext context) { }

    public void CollectCandidates(IProteusContext context, RotationScheduler scheduler, bool isMoving)
    {
        if (!context.InCombat) { context.Debug.DamageState = "Not in combat"; return; }
        if (context.HasDiamondback) { context.Debug.DamageState = "Diamondback (locked)"; return; }
        if (context.TargetingService.IsDamageTargetingPaused())
        {
            context.Debug.DamageState = "Paused (no target)";
            return;
        }

        var player = context.Player;
        var target = context.TargetingService.FindEnemy(
            context.Configuration.Targeting.EnemyStrategy,
            FFXIVConstants.CasterTargetingRange,
            player);
        if (target == null) { context.Debug.DamageState = "No target"; return; }

        var cfg = context.Configuration.BlueMage;

        // Rose of Destruction — own 30s recast group (safe to cooldown-check, NOT group 57).
        if (cfg.EnableRoseOfDestruction
            && context.IsSpellUsable(BLUActions.TheRoseOfDestruction.ActionId)
            && context.ActionService.IsActionReady(BLUActions.TheRoseOfDestruction.ActionId)
            && !MechanicCastGate.ShouldBlock(context, BLUActions.TheRoseOfDestruction.CastTime)
            && !isMoving)
        {
            scheduler.PushGcd(ProteusAbilities.TheRoseOfDestruction, target.GameObjectId, priority: 5,
                onDispatched: _ =>
                {
                    context.Debug.PlannedAction = BLUActions.TheRoseOfDestruction.Name;
                    context.Debug.DamageState = "Rose of Destruction";
                });
        }

        // Song of Torment — 30s Bleeding, FindEnemyNeedingDot pattern (source-aware, all ranks).
        if (cfg.EnableSongOfTorment
            && context.IsSpellUsable(BLUActions.SongOfTorment.ActionId)
            && !isMoving)
        {
            var dotTarget = context.TargetingService.FindEnemyNeedingDot(
                BLUActions.StatusIds.Bleeding, 3f, BLUActions.SongOfTorment.Range, player);
            if (dotTarget != null && !MechanicCastGate.ShouldBlock(context, BLUActions.SongOfTorment.CastTime))
            {
                scheduler.PushGcd(ProteusAbilities.SongOfTorment, dotTarget.GameObjectId, priority: 6,
                    onDispatched: _ =>
                    {
                        context.Debug.PlannedAction = BLUActions.SongOfTorment.Name;
                        context.Debug.DamageState = "Song of Torment (DoT)";
                    });
            }
        }

        // Plaincracker — point-blank SELF AoE: count MUST be player-anchored (the Dyskrasia rule).
        var packCount = context.TargetingService.CountEnemiesInRange(BLUActions.Plaincracker.Radius, player);
        context.Debug.AoeRangeEnemies = packCount;
        if (cfg.EnableAoERotation
            && packCount >= cfg.AoEMinTargets
            && context.IsSpellUsable(BLUActions.Plaincracker.ActionId)
            && !isMoving
            && !MechanicCastGate.ShouldBlock(context, BLUActions.Plaincracker.CastTime))
        {
            var capturedCount = packCount;
            scheduler.PushGcd(ProteusAbilities.Plaincracker, player.GameObjectId, priority: 7,
                onDispatched: _ =>
                {
                    context.Debug.PlannedAction = BLUActions.Plaincracker.Name;
                    context.Debug.DamageState = $"Plaincracker ({capturedCount} enemies)";
                });
        }

        // Tank filler: Goblin Punch — INSTANT, 3y. Also the movement filler for any role when in
        // melee reach (the only GCD castable on the move). Range-rejected at dispatch when far.
        if ((context.Role == BluRole.Tank || isMoving) && context.IsSpellUsable(BLUActions.GoblinPunch.ActionId))
        {
            scheduler.PushGcd(ProteusAbilities.GoblinPunch, target.GameObjectId, priority: 8,
                onDispatched: _ =>
                {
                    context.Debug.PlannedAction = BLUActions.GoblinPunch.Name;
                    context.Debug.DamageState = "Goblin Punch";
                });
        }

        // Primary filler: Sonic Boom (1.0s cast, 25y).
        if (!isMoving && !MechanicCastGate.ShouldBlock(context, BLUActions.SonicBoom.CastTime))
        {
            if (context.IsSpellUsable(BLUActions.SonicBoom.ActionId))
            {
                scheduler.PushGcd(ProteusAbilities.SonicBoom, target.GameObjectId, priority: 9,
                    onDispatched: _ =>
                    {
                        context.Debug.PlannedAction = BLUActions.SonicBoom.Name;
                        context.Debug.DamageState = "Sonic Boom";
                    });
            }

            // Fallback when Sonic Boom isn't slotted: the starter spell always exists.
            scheduler.PushGcd(ProteusAbilities.WaterCannon, target.GameObjectId, priority: 10,
                onDispatched: _ =>
                {
                    context.Debug.PlannedAction = BLUActions.WaterCannon.Name;
                    context.Debug.DamageState = "Water Cannon (fallback)";
                });
        }
    }
}
