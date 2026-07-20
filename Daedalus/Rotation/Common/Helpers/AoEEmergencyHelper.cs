using System;
using Daedalus.Config;
using Daedalus.Rotation.ApolloCore.Helpers;
using Dalamud.Game.ClientState.Objects.SubKinds;

namespace Daedalus.Rotation.Common.Helpers;

/// <summary>
/// Shared AoE-emergency detection for all healers. EMERGENCY = enough living party members are
/// critically low at once (≤ the GCD-emergency threshold) that group heals must flow from EVERY
/// healer simultaneously — the recovery after "reduce everyone to 1 HP" mechanics. While true,
/// AoE heal handlers bypass their min-target / avg-HP gates, co-healer deferral, and the
/// cross-healer AoE reservation (<c>TryReserveAoEHeal(force: true)</c>), and push at a priority
/// above single-target triage.
/// Deliberately reads RAW HP (no prediction): a co-healer's in-flight heal must not talk this
/// healer out of the emergency — that deferral is exactly the failure being fixed.
/// </summary>
public static class AoEEmergencyHelper
{
    /// <summary>WHM/SGE path — party access via the <see cref="IPartyHelper"/> interface.</summary>
    public static bool IsAoEEmergency(IPartyHelper partyHelper, IPlayerCharacter player, HealingConfig healing) =>
        IsAoEEmergency(partyHelper.GetAllPartyMembers(player), partyHelper.GetPartySize(player), healing);

    /// <summary>AST/SCH path — their contexts expose the concrete <see cref="BasePartyHelper"/>.</summary>
    public static bool IsAoEEmergency(BasePartyHelper partyHelper, IPlayerCharacter player, HealingConfig healing) =>
        IsAoEEmergency(partyHelper.GetAllPartyMembers(player), partyHelper.GetPartySize(player), healing);

    public static bool IsAoEEmergency(
        System.Collections.Generic.IEnumerable<Dalamud.Game.ClientState.Objects.Types.IBattleChara> members,
        int partySize,
        HealingConfig healing)
    {
        if (!healing.ForceGroupHealsInEmergency)
            return false;

        var threshold = healing.GcdEmergencyThreshold;
        var critical = 0;
        foreach (var member in members)
        {
            if (member.CurrentHp == 0 || member.MaxHp == 0)
                continue;
            if ((float)member.CurrentHp / member.MaxHp <= threshold)
                critical++;
        }

        var required = Math.Max(2, AoEHealTargetHelper.GetEffectiveMinTargets(healing, partySize));
        return critical >= required;
    }
}
