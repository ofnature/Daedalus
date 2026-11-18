using System.Collections.Generic;
using System.Numerics;

namespace Daedalus.Services.Targeting;

/// <summary>
/// Which enemy a targeted circle AoE is aimed at, and how many it is credited with hitting.
/// <para>
/// Only enemies in OUR fight may be the anchor, and only they count. The scan used to take every attackable
/// hostile in range: in Eureka Pagos (2026-11-17) an Astrologian's Gravity and a Pictomancer's AoE went to
/// whichever cluster of mobs was densest, pulled or not, and each cast brought a fresh pack onto the group.
/// </para>
/// </summary>
internal static class AoEAnchorPolicy
{
    internal readonly record struct Candidate(ulong Id, Vector3 Position, bool InOurFight);

    /// <summary>The anchor with the most enemies of our fight inside <paramref name="radius"/> of it (itself included); (0, 0) when none.</summary>
    internal static (ulong Id, int Hits) BestCircle(IReadOnlyList<Candidate> candidates, float radius)
    {
        ulong bestId = 0;
        var bestHits = 0;
        var radiusSquared = radius * radius;

        foreach (var anchor in candidates)
        {
            if (!anchor.InOurFight)
                continue;

            var hits = 0;
            foreach (var other in candidates)
            {
                if (other.InOurFight && Vector3.DistanceSquared(anchor.Position, other.Position) <= radiusSquared)
                    hits++;
            }

            if (hits > bestHits)
            {
                bestHits = hits;
                bestId = anchor.Id;
            }
        }

        return (bestId, bestHits);
    }
}
