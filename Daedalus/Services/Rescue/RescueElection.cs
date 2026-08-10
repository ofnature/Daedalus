using System;
using System.Collections.Generic;
using System.Linq;

namespace Daedalus.Services.Rescue;

/// <summary>
/// Deterministic multi-healer election (docs/rescue-plan.md). Each healer knows only its OWN
/// eligibility, so there is no shared vote: rank = index among the party's healers sorted by
/// SenderId (every machine derives the same roster order). Rank 0 fires as soon as eligible;
/// rank N waits N backoff steps and fires only if no claim has appeared — an ineligible rank 0
/// (dead, cooldown, unsafe spot) simply never fires and rank 1 covers one step later.
/// </summary>
public static class RescueElection
{
    /// <summary>Per-rank stagger. One step comfortably covers the ~150–400ms signal→pull
    /// budget, so a healthy rank 0 always beats rank 1 to the claim.</summary>
    public const float BackoffStepSeconds = 0.3f;

    /// <summary>
    /// This healer's rank among the party's healer SenderIds (ordinal sort, duplicates folded).
    /// -1 when the local toon is not in the list — a non-healer must never win an election.
    /// </summary>
    public static int Rank(IEnumerable<string> partyHealerSenderIds, string selfSenderId)
    {
        var sorted = partyHealerSenderIds
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();
        return sorted.IndexOf(selfSenderId);
    }

    public static float BackoffSeconds(int rank) => rank <= 0 ? 0f : rank * BackoffStepSeconds;

    /// <summary>Whether this rank's backoff has elapsed for a request of the given age and no
    /// other healer has claimed the pull.</summary>
    public static bool MayFire(int rank, float requestAgeSeconds, bool claimSeen)
        => rank >= 0 && !claimSeen && requestAgeSeconds >= BackoffSeconds(rank);
}
