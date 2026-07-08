using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Daedalus.Services.Targeting;

/// <summary>One enemy considered for a Split assignment.</summary>
public readonly record struct SplitEnemy(ulong Id, float Hp, Vector3 Position);

/// <summary>One DPS toon participating in a Split assignment.</summary>
public readonly record struct SplitToon(string SenderId, float Dps, Vector3 Position, bool IsMelee);

/// <summary>
/// Pure, deterministic Split assignment: spreads DPS toons across live enemies so estimated kill
/// times converge (the pack dies together), weighting toon DPS against enemy HP. A melee locality
/// tiebreak keeps a melee on a nearby mob when balance is otherwise equal, so nobody is sent
/// sprinting across the arena. Every box feeds identical inputs (shared enemy GameObjectIds, the
/// shared roster, observed per-toon DPS and ~2s-stale positions) and therefore computes the same
/// map, so each box can act on its own slot with no negotiation round and no collisions.
/// </summary>
public static class SplitTargetAssigner
{
    private const float Epsilon = 0.001f;

    /// <summary>Enemies whose "remaining TTK" is within this ratio of the best are treated as tied,
    /// so the melee locality tiebreak can pick the nearer one.</summary>
    private const float TieRatio = 0.95f;

    /// <summary>
    /// Assigns each toon to an enemy id. Returns an empty map when there is nothing to assign.
    /// Greedy under-served-first: the strongest toons go to the enemy with the highest current
    /// remaining TTK (HP / assigned-DPS), which fills every mob before doubling up and converges
    /// kill times. Deterministic: inputs are sorted by id / sender id before iterating.
    /// </summary>
    public static Dictionary<string, ulong> Assign(
        IReadOnlyList<SplitEnemy> enemies,
        IReadOnlyList<SplitToon> toons)
    {
        var result = new Dictionary<string, ulong>(StringComparer.Ordinal);
        if (enemies.Count == 0 || toons.Count == 0)
            return result;

        var sortedEnemies = enemies.OrderBy(e => e.Id).ToList();
        var sortedToons = toons
            .OrderByDescending(t => t.Dps)
            .ThenBy(t => t.SenderId, StringComparer.Ordinal)
            .ToList();

        // Assigned DPS per enemy so far.
        var load = sortedEnemies.ToDictionary(e => e.Id, _ => 0f);

        foreach (var toon in sortedToons)
        {
            var enemyId = PickEnemy(sortedEnemies, load, toon);
            result[toon.SenderId] = enemyId;
            load[enemyId] += MathF.Max(Epsilon, toon.Dps);
        }

        return result;
    }

    private static ulong PickEnemy(
        List<SplitEnemy> enemies,
        Dictionary<ulong, float> load,
        SplitToon toon)
    {
        // Remaining TTK if no more DPS is added: HP / current load (load 0 => effectively infinite,
        // so empty mobs are always served first).
        var maxTtk = float.NegativeInfinity;
        foreach (var e in enemies)
        {
            var ttk = e.Hp / MathF.Max(Epsilon, load[e.Id]);
            if (ttk > maxTtk)
                maxTtk = ttk;
        }

        var threshold = maxTtk * TieRatio;
        ulong best = 0;
        var bestKey = float.MaxValue;
        var found = false;
        foreach (var e in enemies)
        {
            var ttk = e.Hp / MathF.Max(Epsilon, load[e.Id]);
            if (ttk < threshold)
                continue;

            // Within the tied top group: melee prefers the nearer mob; everyone breaks ties by id
            // so the choice is identical on every box.
            var key = toon.IsMelee ? Vector3.Distance(toon.Position, e.Position) : 0f;
            if (!found || key < bestKey || (Math.Abs(key - bestKey) < Epsilon && e.Id < best))
            {
                bestKey = key;
                best = e.Id;
                found = true;
            }
        }

        return best;
    }
}
