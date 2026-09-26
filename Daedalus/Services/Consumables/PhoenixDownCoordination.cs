using System;
using System.Collections.Generic;

namespace Daedalus.Services.Consumables;

/// <summary>What happened to an attempt this frame.</summary>
public enum PhoenixDownAttemptOutcome
{
    /// <summary>Nothing new.</summary>
    None,

    /// <summary>The cast bar appeared — the item really is being used. Claim it now.</summary>
    Started,

    /// <summary>The cast ran to the end; the item was used.</summary>
    Completed,

    /// <summary>The cast bar vanished early — moved, or the target became invalid. Nothing was spent.</summary>
    Cancelled,

    /// <summary>No cast bar ever appeared — the game genuinely refused it.</summary>
    Refused,
}

/// <summary>
/// Follows one Phoenix Down attempt by watching the cast bar, because the game's own answer is wrong.
///
/// <para>
/// Field 2026-09-25, Southern Front: two toons both logged "refused by the game" for their Phoenix Down,
/// and in the same instant the game showed both casts starting ("refused" at 17:53:56.410, "You ready a
/// tuft of phoenix down" at .415). <c>UseAction</c> returns false for an item even when the cast goes
/// through. Believing it, neither toon sent its claim, so neither knew the other was casting — two items
/// on one corpse. The cast bar is the truth, so that is what this reads.
/// </para>
///
/// <para>
/// It also separates a finished cast from a cancelled one. The old code counted any successful call as
/// "used" and locked the item's recast for six minutes — so a cast a dodge cancelled one second from the
/// end (which spends nothing) would have blocked every retry for the rest of the fight.
/// </para>
/// </summary>
public sealed class PhoenixDownCastTracker
{
    /// <summary>How long to wait for the cast bar after asking. Locally it appears within a few ms.</summary>
    public const double ConfirmWindowSeconds = 1.5;

    /// <summary>A cast bar that was this close to full when it vanished is counted as finished.</summary>
    public const float CompletionToleranceSeconds = 0.3f;

    private readonly Func<DateTime> _utcNow;
    private DateTime? _attemptUtc;
    private bool _castSeen;
    private float _lastCurrent;
    private float _lastTotal;

    public PhoenixDownCastTracker(Func<DateTime>? utcNow = null)
        => _utcNow = utcNow ?? (() => DateTime.UtcNow);

    /// <summary>An attempt is between being asked for and being resolved — do not start another.</summary>
    public bool InFlight => _attemptUtc != null;

    /// <summary>Who the attempt in flight is for.</summary>
    public string? TargetName { get; private set; }

    /// <summary>Call immediately before asking the game to use the item.</summary>
    public void BeginAttempt(string targetName)
    {
        _attemptUtc = _utcNow();
        _castSeen = false;
        _lastCurrent = 0f;
        _lastTotal = 0f;
        TargetName = targetName;
    }

    /// <summary>Per frame, with whether a Phoenix Down cast bar is up and its progress.</summary>
    public PhoenixDownAttemptOutcome Observe(bool castingPhoenixDown, float currentCast, float totalCast)
    {
        if (_attemptUtc is not { } started)
            return PhoenixDownAttemptOutcome.None;

        if (castingPhoenixDown)
        {
            _lastCurrent = currentCast;
            _lastTotal = totalCast;
            if (_castSeen)
                return PhoenixDownAttemptOutcome.None;

            _castSeen = true;
            return PhoenixDownAttemptOutcome.Started;
        }

        if (_castSeen)
        {
            var completed = _lastTotal > 0f && _lastCurrent >= _lastTotal - CompletionToleranceSeconds;
            _attemptUtc = null;
            return completed ? PhoenixDownAttemptOutcome.Completed : PhoenixDownAttemptOutcome.Cancelled;
        }

        if ((_utcNow() - started).TotalSeconds > ConfirmWindowSeconds)
        {
            _attemptUtc = null;
            return PhoenixDownAttemptOutcome.Refused;
        }

        return PhoenixDownAttemptOutcome.None;
    }
}

/// <summary>
/// Who goes first when several toons decide to Phoenix Down in the same second.
///
/// <para>
/// Every toon checks once a second, and they all see the last healer die on the same tick, so they
/// decide together; a claim sent after casting arrives too late to stop the others. Instead each toon
/// works out the same fixed order and waits its turn: first in line fires at once, the next only after
/// <see cref="StaggerSeconds"/> with no claim seen, and so on. The claim from whoever went first then
/// reaches everyone behind it with time to spare. If the first in line can't cast — Phoenix Down turned
/// off on that toon, none in the bag — the next one simply goes a moment later.
/// </para>
/// </summary>
public static class PhoenixDownStagger
{
    /// <summary>Spacing between turns. A same-machine claim arrives in milliseconds; this is ample.</summary>
    public const double StaggerSeconds = 1.5;

    /// <summary>
    /// Our place in line: living non-tank party members ordered by entity id. Every toon sees the same
    /// party, so every toon computes the same order. A tank that passed its own policy (a designated
    /// off-tank) goes after all of them — it is not in the others' lists, so it must not jump the queue.
    /// </summary>
    public static int RankOf(uint selfEntityId, bool selfIsTank, IReadOnlyCollection<uint> livingNonTankIds)
    {
        if (selfIsTank)
            return livingNonTankIds.Count;

        var rank = 0;
        foreach (var id in livingNonTankIds)
        {
            if (id < selfEntityId)
                rank++;
        }

        return rank;
    }

    /// <summary>Has our turn come, counting from when the last healer went down?</summary>
    public static bool MayFire(int rank, double secondsSinceAllHealersDown)
        => secondsSinceAllHealersDown >= rank * StaggerSeconds;

    /// <summary>
    /// Spacing between turns to WALK to a corpse. A walk takes seconds, not milliseconds: at the firing spacing
    /// every non-tank would be on its way before the nearest arrived. Whoever gets there casts and claims, and
    /// the claim stops the rest (<see cref="PhoenixDownPolicy.ClaimHoldOffSeconds"/>); the next in line only
    /// sets off if nobody has claimed by then.
    /// </summary>
    public const double ApproachStaggerSeconds = 10.0;

    /// <summary>
    /// Our place in line to walk to the corpse: living non-tanks nearer to it go first -- the nearest has the
    /// shortest walk and the least uptime to give up. Every toon sees the same positions, so every toon computes
    /// the same order. A tank that passed its own policy goes after all of them.
    /// </summary>
    public static int ApproachRankOf(float selfDistance, bool selfIsTank, IReadOnlyCollection<float> otherNonTankDistances)
    {
        if (selfIsTank)
            return otherNonTankDistances.Count;

        var rank = 0;
        foreach (var distance in otherNonTankDistances)
        {
            if (distance < selfDistance)
                rank++;
        }

        return rank;
    }

    /// <summary>Has our turn to walk come, counting from when the last healer went down?</summary>
    public static bool MayApproach(int rank, double secondsSinceAllHealersDown)
        => secondsSinceAllHealersDown >= rank * ApproachStaggerSeconds;
}
