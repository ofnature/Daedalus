using Daedalus.Data;

namespace Daedalus.Rotation.Common.Helpers;

/// <summary>
/// Where a limited job's content restrictions have to suppress automation that every other job
/// takes for granted.
///
/// <para>
/// Beastmaster is barred from Duty Roulettes, Eureka/Bozja/Occult Crescent, Variant/Criterion,
/// Ultimates, Deep Dungeons and PvP. In practice it runs solo in the open world, in
/// unrestricted or preformed parties, and in its own solo duty (Crucible of the Unbroken).
/// Blue Mage carries the same shape of restriction.
/// </para>
///
/// <para>
/// The point of stating this in code rather than in a comment is that Daedalus has a pile of
/// machinery that assumes it may be in a roulette, a duty with a Trust party, or a
/// content-specific override list. On a limited job those paths are not merely unused — asking
/// them anything is a category error, and several of them will happily answer with a plan for
/// content the character cannot enter.
/// </para>
///
/// <para>
/// Pure and static so it is testable and so any caller can consult it without taking a
/// dependency. Fail-open is deliberately NOT the rule here: an unknown job is treated as
/// unrestricted, because the restrictions belong to two specific jobs and guessing outward
/// would silently disable ordinary jobs.
/// </para>
/// </summary>
public static class LimitedJobContentPolicy
{
    /// <summary>
    /// Whether this job is barred from the instanced content the automation bridges target.
    /// </summary>
    public static bool IsContentRestricted(uint jobId) => JobRegistry.IsLimitedJob(jobId);

    /// <summary>
    /// Should duty/roulette automation be offered at all for this job?
    /// <para>
    /// False for limited jobs. AutoDuty, the roulette bridges and the Trust-party helpers all
    /// resolve content this job cannot queue for, so the honest answer is not "try and fail" but
    /// "do not offer".
    /// </para>
    /// </summary>
    public static bool AllowsDutyAutomation(uint jobId) => !IsContentRestricted(jobId);

    /// <summary>
    /// Should per-content (per-territory) rotation overrides be consulted for this job?
    /// <para>
    /// False for limited jobs. A territory-keyed override table only ever holds entries for
    /// content a limited job cannot enter, so a lookup is guaranteed noise — and a stray global
    /// (territory 0) entry would apply somewhere it was never authored for.
    /// </para>
    /// </summary>
    public static bool AllowsContentOverrides(uint jobId) => !IsContentRestricted(jobId);

    /// <summary>
    /// Should role-action logic run for this job?
    /// <para>
    /// False for limited jobs: they have <b>no role actions at all</b>. Not "none learned" —
    /// none exist. Anything reaching for Swiftcast, Surecast, Second Wind, Leg Sweep, True North
    /// or an interrupt on a limited job is reaching for something the game does not grant it,
    /// and a "not learned" reading would misreport that as a levelling gap in the Missing window.
    /// </para>
    /// </summary>
    public static bool AllowsRoleActions(uint jobId) => !IsContentRestricted(jobId);

    /// <summary>
    /// Should party-coordination features (burst sync, tank swaps, co-healer arbitration, the LAN
    /// roster's role slots) run for this job?
    /// <para>
    /// False for limited jobs. They cannot enter the content those features coordinate, and a
    /// limited job in an unrestricted party has no role slot to occupy.
    /// </para>
    /// </summary>
    public static bool AllowsPartyCoordination(uint jobId) => !IsContentRestricted(jobId);

    /// <summary>
    /// One-line explanation for a UI or debug readout, or empty when nothing is suppressed.
    /// Named so the reason reaches the user rather than a feature silently doing nothing —
    /// the failure mode this codebase keeps re-learning.
    /// </summary>
    public static string DescribeSuppression(uint jobId) => IsContentRestricted(jobId)
        ? $"{JobRegistry.GetJobName(jobId)} is a limited job — duty automation, content overrides, "
          + "role actions and party coordination do not apply."
        : string.Empty;
}
