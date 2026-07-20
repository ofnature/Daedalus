using System;
using Daedalus.Config;
using Daedalus.Rotation.ApolloCore.Helpers;
using Daedalus.Rotation.AsclepiusCore.Helpers;
using Daedalus.Rotation.Common.Helpers;
using Dalamud.Game.ClientState.Objects.SubKinds;

namespace Daedalus.Rotation.AsclepiusCore.Helpers;

/// <summary>
/// AoE heal metric helpers for Sage handlers.
/// </summary>
public static class AsclepiusPartyMetrics
{
    /// <summary>
    /// Scheduler priority AoE heal candidates use during an AoE emergency — above single-target
    /// triage (10), below the emergency Swiftcast (4), so group recovery owns the weave slots.
    /// </summary>
    public const int AoEEmergencyPriority = 5;

    public static (float avgHpPercent, float lowestHpPercent, int injuredCount) GetAoEHealMetrics(
        IPartyHelper partyHelper,
        IPlayerCharacter player)
    {
        if (partyHelper is AsclepiusPartyHelper sageParty)
            return sageParty.GetAoEHealMetrics(player);

        return partyHelper.CalculatePartyHealthMetrics(player);
    }

    /// <summary>
    /// AoE EMERGENCY: enough living party members are critically low at once (≤ the GCD-emergency
    /// threshold) that group heals must flow from EVERY healer simultaneously — the recovery after
    /// "reduce everyone to 1 HP" mechanics. While true, AoE handlers bypass their min-target /
    /// avg-HP gates and the cross-healer AoE reservation, and outrank single-target triage.
    /// Deliberately reads RAW HP (no prediction): a co-healer's in-flight heal must not talk this
    /// healer out of the emergency — that deferral is exactly the failure being fixed.
    /// </summary>
    public static bool IsAoEEmergency(IPartyHelper partyHelper, IPlayerCharacter player, HealingConfig healing)
    {
        if (!healing.ForceGroupHealsInEmergency)
            return false;

        var threshold = healing.GcdEmergencyThreshold;
        var critical = 0;
        foreach (var member in partyHelper.GetAllPartyMembers(player))
        {
            if (member.CurrentHp == 0 || member.MaxHp == 0)
                continue;
            if ((float)member.CurrentHp / member.MaxHp <= threshold)
                critical++;
        }

        var required = Math.Max(2, AoEHealTargetHelper.GetEffectiveMinTargets(
            healing, partyHelper.GetPartySize(player)));
        return critical >= required;
    }
}
