using System.Collections.Generic;
using Daedalus.Data;

namespace Daedalus.Ipc;

/// <summary>
/// The eight standard party slot codes — MT, OT, H1, H2, M1, M2, R1, R2 — derived from the LAN
/// roster.
/// <para>
/// Needed because Daedalus's own two role fields are neither of those. <c>Role</c> is the coarse
/// "Tank"/"Healer"/"DPS", and <c>AssignedSlot</c> is "Tank 1"/"Healer 2"/"DPS 3" — useful in the
/// coordination window, and not what a consumer keyed on the standard eight can read. Publishing
/// either verbatim would leave every entry unassigned, which is exactly the dead-weight state
/// exposing the fields is meant to end.
/// </para>
/// <para>
/// The DPS split is the part that cannot come from <c>AssignedSlot</c> at all: it numbers DPS
/// 1..4 without distinguishing melee from ranged, so the job is what separates M from R.
/// </para>
/// </summary>
public static class PartySlotCode
{
    /// <summary>
    /// Codes per sender id. Input must already be in the roster's canonical order — the same
    /// sender-id ordering the slot assignment itself uses, so <c>role</c> and <c>slot</c> agree
    /// with each other and every box derives the same answer without negotiating.
    /// </summary>
    public static Dictionary<string, string> Assign(IEnumerable<(string SenderId, string Role, uint JobId)> ordered)
    {
        var result = new Dictionary<string, string>(System.StringComparer.Ordinal);
        int tank = 0, healer = 0, melee = 0, ranged = 0;

        foreach (var (senderId, role, jobId) in ordered)
        {
            if (string.IsNullOrEmpty(senderId))
                continue;

            result[senderId] = role switch
            {
                "Tank" => ++tank switch { 1 => "MT", 2 => "OT", _ => "" },
                "Healer" => ++healer switch { 1 => "H1", 2 => "H2", _ => "" },
                _ => DpsCode(jobId, ref melee, ref ranged),
            };
        }

        return result;
    }

    /// <summary>
    /// Melee gets M, physical ranged and casters get R. An unrecognised job returns EMPTY rather
    /// than defaulting to one side: a wrong slot is worse than no slot, because the consumer acts
    /// on it — the doc's own tower example is that nobody soaking is recoverable and two people
    /// soaking the same tower usually is not.
    /// </summary>
    private static string DpsCode(uint jobId, ref int melee, ref int ranged)
    {
        if (JobRegistry.IsMeleeDps(jobId))
            return ++melee switch { 1 => "M1", 2 => "M2", _ => "" };

        if (JobRegistry.IsRangedPhysicalDps(jobId) || JobRegistry.IsCasterDps(jobId))
            return ++ranged switch { 1 => "R1", 2 => "R2", _ => "" };

        return "";
    }
}
