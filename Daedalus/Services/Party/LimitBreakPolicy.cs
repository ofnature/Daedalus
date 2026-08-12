using Daedalus.Data;
using Daedalus.Services.Network;

namespace Daedalus.Services.Party;

/// <summary>
/// The pure half of the fleet limit-break call: which role a job answers for, and how long a
/// call stays live. No game state — see <see cref="LimitBreakService"/> for the casting half.
/// </summary>
public static class LimitBreakPolicy
{
    /// <summary>
    /// How long a call stays armed on the receiving box.
    /// <para>
    /// Not instant-or-nothing on purpose. The datagram lands on whatever frame it lands on, and
    /// the LB can legitimately be un-castable right then — mid-cast, mid-animation-lock, or the
    /// bar filling as the operator presses. A few seconds of retry is the difference between
    /// "works" and "works most of the time".
    /// </para>
    /// </summary>
    public const float ArmWindowSeconds = 6f;

    /// <summary>Don't hammer UseAction every frame while the game is refusing it.</summary>
    public const float RetryIntervalSeconds = 0.25f;

    /// <summary>
    /// How long a box that is NOT the right role waits for someone to confirm before reporting
    /// that nobody answered. Longer than the arm window, so the acting toon has used up all its
    /// retries and its confirmation has had time to cross the network before we call it a miss.
    /// </summary>
    public const float AnswerWaitSeconds = ArmWindowSeconds + 2f;

    /// <summary>
    /// Which limit break this job would fire, or null for anything that has none (crafters,
    /// gatherers, an unset job). Note this is the job's OWN category — the operator picks a role
    /// and only matching toons act, so a party with no melee simply ignores a melee call.
    /// </summary>
    public static LimitBreakRole? RoleFor(uint jobId)
    {
        if (JobRegistry.IsTank(jobId)) return LimitBreakRole.Tank;
        if (JobRegistry.IsHealer(jobId)) return LimitBreakRole.Healer;
        if (JobRegistry.IsMeleeDps(jobId)) return LimitBreakRole.Melee;
        if (JobRegistry.IsRangedPhysicalDps(jobId)) return LimitBreakRole.RangedPhysical;
        if (JobRegistry.IsCasterDps(jobId)) return LimitBreakRole.Caster;
        return null;
    }

    /// <summary>Should the toon on <paramref name="jobId"/> answer a call for this role?</summary>
    public static bool Answers(LimitBreakRole call, uint jobId) => RoleFor(jobId) == call;

    /// <summary>Button/label text for a role.</summary>
    public static string Label(LimitBreakRole role) => role switch
    {
        LimitBreakRole.Tank => "Tank",
        LimitBreakRole.Healer => "Healer",
        LimitBreakRole.Melee => "Melee",
        LimitBreakRole.RangedPhysical => "Ranged",
        LimitBreakRole.Caster => "Caster",
        _ => role.ToString(),
    };
}
