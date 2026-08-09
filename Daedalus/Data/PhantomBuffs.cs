using System.Collections.Generic;

namespace Daedalus.Data;

/// <summary>
/// One phantom self-buff worth collecting: which job casts it, with what, and what it grants.
/// </summary>
/// <param name="Job">The phantom job that must be equipped to cast it.</param>
/// <param name="ActionId">The buff action's real id (we cast by id, not by hotbar slot).</param>
/// <param name="ActionName">Display name, for the UI and skip reasons.</param>
/// <param name="StatusId">The status the buff applies — how we verify it actually landed.</param>
/// <param name="RequiredLevel">Phantom-job level the action unlocks at.</param>
/// <param name="Effect">What it does, for the config UI.</param>
public readonly record struct PhantomBuff(
    PhantomJob Job,
    uint ActionId,
    string ActionName,
    uint StatusId,
    byte RequiredLevel,
    string Effect);

/// <summary>
/// The phantom self-buffs the buff cycle collects.
///
/// <para>
/// These last ~30 minutes and <b>persist after switching away from the job</b>, which is the
/// entire premise: cycle the jobs once, keep the buffs on whatever you actually play. Cast near
/// a Knowledge Crystal they broadcast to the whole party in the zone, so one character can cover
/// a fleet — see docs/occult-buff-cycle.md.
/// </para>
///
/// <para>
/// Action ids come from <see cref="PhantomActions"/>; status ids from BOCCHI's
/// <c>Data/PlayerStatus.cs</c>; effects from the in-game Inquiring Mind tooltip (user screenshot
/// 2026-07-31, so they are ground truth). Required levels match the action unlock levels in
/// <see cref="PhantomActions.All"/> — note Monk needs <b>3</b> where the others need 2.
/// </para>
/// </summary>
public static class PhantomBuffs
{
    public static readonly IReadOnlyList<PhantomBuff> All =
    [
        new(PhantomJob.Knight, 41589, "Pray", 4233, 2, "−10% damage taken"),
        new(PhantomJob.Bard, 41609, "Romeo's Ballad", 4244, 2, "+10% phantom EXP from battle"),
        new(PhantomJob.Monk, 41597, "Counterstance", 4239, 3, "Increased movement speed"),
        new(PhantomJob.Dancer, 46603, "Quickstep", 4799, 2, "+2% damage dealt"),
    ];

    /// <summary>
    /// A fresh 30-minute application reads at or above this. Verifying the status merely EXISTS
    /// would accept the one already on you from half an hour ago and call a failed cast a
    /// success — the point of the check is proving the new cast landed.
    /// </summary>
    public const float FreshApplicationSeconds = 1780f;

    public static PhantomBuff? ForJob(PhantomJob job)
    {
        foreach (var buff in All)
        {
            if (buff.Job == job)
                return buff;
        }

        return null;
    }
}
