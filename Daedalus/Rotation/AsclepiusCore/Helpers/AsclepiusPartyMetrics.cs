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
    /// AoE EMERGENCY for Sage handlers — see <see cref="AoEEmergencyHelper.IsAoEEmergency"/>
    /// (the shared implementation all four healers consult).
    /// </summary>
    public static bool IsAoEEmergency(IPartyHelper partyHelper, IPlayerCharacter player, HealingConfig healing) =>
        AoEEmergencyHelper.IsAoEEmergency(partyHelper, player, healing);
}
