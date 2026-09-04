using System;
using System.Collections.Generic;
using Daedalus.Data;

namespace Daedalus.Services.Occult;

/// <summary>Why a buff is not going to be collected this cycle.</summary>
public enum BuffSkipReason
{
    /// <summary>Not skipped — this one will be cast.</summary>
    None = 0,

    /// <summary>Turned off in settings. The user asked; say nothing.</summary>
    Disabled,

    /// <summary>The phantom job has never been unlocked.</summary>
    JobLocked,

    /// <summary>Job unlocked but below the level the buff action needs.</summary>
    JobUnderLevelled,

    /// <summary>Already up with plenty of time left — nothing to do.</summary>
    AlreadyFresh,
}

/// <summary>One buff's disposition for a cycle, with a reason the UI can print.</summary>
/// <param name="Buff">The buff.</param>
/// <param name="Skip">Why it is being skipped, or <see cref="BuffSkipReason.None"/>.</param>
/// <param name="CurrentLevel">The character's level in that job (0 = locked).</param>
public readonly record struct BuffPlanEntry(PhantomBuff Buff, BuffSkipReason Skip, byte CurrentLevel)
{
    public bool WillCast => Skip == BuffSkipReason.None;

    /// <summary>
    /// Human reason for the skip, or empty when it is being cast. Deliberately names the number
    /// the user needs — "Monk Lv1 (needs 3)" is actionable where "unavailable" is not.
    /// </summary>
    public string Describe() => Skip switch
    {
        BuffSkipReason.Disabled => $"{Buff.ActionName}: off",
        BuffSkipReason.JobLocked => $"{Buff.Job} not unlocked",
        BuffSkipReason.JobUnderLevelled => $"{Buff.Job} Lv{CurrentLevel} (needs {Buff.RequiredLevel})",
        BuffSkipReason.AlreadyFresh => $"{Buff.ActionName}: still up",
        _ => string.Empty,
    };
}

/// <summary>
/// Pure decisions for the phantom buff cycle — which buffs are worth collecting and when the set
/// needs refreshing. No game access, so all of it is directly testable.
/// </summary>
public static class PhantomBuffPolicy
{
    /// <summary>
    /// Works out what this cycle will actually do.
    ///
    /// <para>
    /// A character routinely will not have all four — jobs unlock separately and level
    /// separately — so a partial set is the normal case, not an error. Every skip carries a
    /// reason so the button can say which and why rather than silently doing less than asked.
    /// </para>
    /// </summary>
    /// <param name="jobLevels">Per-job phantom levels; missing or 0 means locked.</param>
    /// <param name="isEnabled">Per-buff user toggle.</param>
    /// <param name="remainingSeconds">
    /// Seconds left on each buff's status right now (0 = absent). Used only when
    /// <paramref name="skipFresh"/> is set.
    /// </param>
    /// <param name="skipFresh">
    /// Leave buffs alone that still have <paramref name="freshThresholdSeconds"/> or more left.
    /// A manual press should generally re-apply everything (false); an automatic refresh should
    /// only top up what is running out (true).
    /// </param>
    /// <param name="freshThresholdSeconds">What counts as "still up" for the above.</param>
    public static IReadOnlyList<BuffPlanEntry> Plan(
        IReadOnlyDictionary<PhantomJob, byte>? jobLevels,
        Func<PhantomBuff, bool> isEnabled,
        Func<PhantomBuff, float>? remainingSeconds = null,
        bool skipFresh = false,
        float freshThresholdSeconds = 600f)
    {
        var plan = new List<BuffPlanEntry>(PhantomBuffs.All.Count);

        foreach (var buff in PhantomBuffs.All)
        {
            byte level = 0;
            jobLevels?.TryGetValue(buff.Job, out level);

            // Order matters: the user's own toggle wins over everything, then hard availability,
            // then freshness. Reporting "Dancer not unlocked" for a buff the user turned off
            // would be noise about a thing they do not care about.
            var skip = !isEnabled(buff) ? BuffSkipReason.Disabled
                : level == 0 ? BuffSkipReason.JobLocked
                : level < buff.RequiredLevel ? BuffSkipReason.JobUnderLevelled
                : skipFresh && (remainingSeconds?.Invoke(buff) ?? 0f) >= freshThresholdSeconds
                    ? BuffSkipReason.AlreadyFresh
                    : BuffSkipReason.None;

            plan.Add(new BuffPlanEntry(buff, skip, level));
        }

        return plan;
    }

    /// <summary>Inquiring Mind — the Freelancer ability that grants the whole set in one cast.</summary>
    public const uint InquiringMindActionId = 46606;

    /// <summary>
    /// Freelancer level it unlocks at. Freelancer levels by mastery count rather than by phantom
    /// EXP, so its unlocks run 5 / 10 / 15 / 20 where every other job stops at 5 — Inquiring Mind
    /// is the third of those (game's own MKDSupportJob table).
    /// </summary>
    public const byte InquiringMindFreelancerLevel = 15;

    /// <summary>
    /// Can this character collect the whole set with one cast instead of touring four jobs?
    /// <para>
    /// The crystal is not optional here, unlike the individual buffs. Pray, Counterstance,
    /// Romeo's Ballad and Quickstep all buff the caster anywhere and merely BROADCAST at a
    /// crystal; Inquiring Mind does nothing at all away from one — "when executed near a
    /// knowledge crystal" is the whole tooltip. So away from a crystal the four-job cycle is
    /// still the only way, and this returns false rather than trading a working cycle for a
    /// wasted GCD.
    /// </para>
    /// </summary>
    public static bool CanUseInquiringMind(
        IReadOnlyDictionary<PhantomJob, byte>? jobLevels, bool nearKnowledgeCrystal)
    {
        if (!nearKnowledgeCrystal || jobLevels is null)
            return false;

        return jobLevels.TryGetValue(PhantomJob.Freelancer, out var level)
               && level >= InquiringMindFreelancerLevel;
    }

    /// <summary>
    /// The one-cast stand-in for the whole plan. Its status is that of the first buff the
    /// character actually qualifies for: all four come from the same cast, so any one of them
    /// landing proves the cast landed, and one is all the verifier can watch.
    /// </summary>
    public static PhantomBuff? InquiringMindStandIn(IReadOnlyList<BuffPlanEntry> plan)
    {
        foreach (var entry in plan)
        {
            if (entry.WillCast)
            {
                return new PhantomBuff(
                    PhantomJob.Freelancer, InquiringMindActionId, "Inquiring Mind",
                    entry.Buff.StatusId, InquiringMindFreelancerLevel,
                    "the whole set in one cast");
            }
        }

        return null;
    }

    /// <summary>
    /// Lowest remaining time across the buffs this character can actually hold, which is what the
    /// refresh threshold and the UI readout key off.
    ///
    /// <para>
    /// Only <b>collectable</b> buffs count. Including one the character can never have would peg
    /// the minimum at zero forever and make an automatic refresh fire endlessly, re-collecting
    /// three buffs to chase a fourth that will never appear.
    /// </para>
    ///
    /// <para>Returns null when there is nothing collectable at all — a different state from zero.</para>
    /// </summary>
    public static float? LowestRemaining(
        IReadOnlyList<BuffPlanEntry> plan,
        Func<PhantomBuff, float> remainingSeconds)
    {
        float? lowest = null;

        foreach (var entry in plan)
        {
            // AlreadyFresh is collectable — it is skipped for being up, not for being impossible.
            if (entry.Skip is BuffSkipReason.JobLocked or BuffSkipReason.JobUnderLevelled or BuffSkipReason.Disabled)
                continue;

            var remaining = Math.Max(0f, remainingSeconds(entry.Buff));
            if (lowest is null || remaining < lowest)
                lowest = remaining;
        }

        return lowest;
    }

    /// <summary>
    /// Should an automatic refresh run? Only when something is collectable and the weakest buff
    /// has dropped below the threshold. A missing buff reads as 0 and therefore qualifies.
    /// </summary>
    public static bool ShouldRefresh(float? lowestRemaining, float thresholdSeconds)
        => lowestRemaining is { } lowest && lowest < thresholdSeconds;

    /// <summary>
    /// The completion line, e.g.
    /// <c>"Buffed 2 of 4 · Monk Lv1 (needs 3) · Dancer not unlocked"</c>.
    /// Silent skips (user toggles) are omitted — reporting what someone deliberately turned off
    /// is noise.
    /// </summary>
    public static string DescribeOutcome(IReadOnlyList<BuffPlanEntry> plan, int castCount)
    {
        var attempted = 0;
        var reasons = new List<string>();

        foreach (var entry in plan)
        {
            if (entry.WillCast)
                attempted++;
            else if (entry.Skip != BuffSkipReason.Disabled)
                reasons.Add(entry.Describe());
        }

        var summary = $"Buffed {castCount} of {attempted}";
        return reasons.Count == 0 ? summary : summary + " · " + string.Join(" · ", reasons);
    }
}
