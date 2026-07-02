using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using Daedalus.Config.DPS;
using Daedalus.Data;
using Daedalus.Rotation.Common.Helpers;

namespace Daedalus.Rotation.ProteusCore.Helpers;

/// <summary>
/// Finds an ally of the desired archetype for Aetheric Mimicry (Mimicry Helper parity): the role
/// dropdown decides which archetype to copy. Scans the PARTY first (players AND Trust NPCs —
/// mimicry works on trusts; zeroed trust jobs resolve via <see cref="TrustPartyRoleHelper"/>),
/// then the surrounding AREA for any player of the desired role. The area scan is the critical
/// half: in an all-BLU party everyone reads as DPS, so a Tank/Healer-role BLU can only source its
/// mimicry from a REAL tank/healer standing nearby (grab it in town — the buff survives zoning).
/// Fail-open by design: no match → null → the rotation plays on without mimicry.
/// </summary>
public static class MimicryScanHelper
{
    public static IBattleChara? FindArchetypeAlly(
        CasterPartyHelper partyHelper,
        IPartyList partyList,
        IObjectTable objectTable,
        IPlayerCharacter player,
        BluRole role,
        System.Collections.Generic.ISet<ulong>? excludedTargets = null)
    {
        IBattleChara? best = null;
        var bestDistSq = BLUActions.AethericMimicry.Range * BLUActions.AethericMimicry.Range;

        // Party first (includes Trust/support NPCs).
        foreach (var member in partyHelper.GetAllPartyMembers(player))
        {
            if (member.EntityId == player.EntityId) continue;
            if (member.CurrentHp == 0) continue;
            if (excludedTargets?.Contains(member.GameObjectId) == true) continue;

            var jobId = TrustPartyRoleHelper.ResolveJobId(member, partyList);
            if (!MatchesRole(jobId, role)) continue;

            var distSq = System.Numerics.Vector3.DistanceSquared(player.Position, member.Position);
            if (distSq <= bestDistSq)
            {
                best = member;
                bestDistSq = distSq;
            }
        }

        if (best != null)
            return best;

        // Area scan: any nearby PLAYER of the desired role (Mimicry Helper behavior).
        foreach (var obj in objectTable)
        {
            if (obj is not IPlayerCharacter pc) continue;
            if (pc.EntityId == player.EntityId) continue;
            if (pc.CurrentHp == 0) continue;
            if (excludedTargets?.Contains(pc.GameObjectId) == true) continue;
            if (!MatchesRole(pc.ClassJob.RowId, role)) continue;

            var distSq = System.Numerics.Vector3.DistanceSquared(player.Position, pc.Position);
            if (distSq <= bestDistSq)
            {
                best = pc;
                bestDistSq = distSq;
            }
        }

        return best;
    }

    internal static bool MatchesRole(uint jobId, BluRole role) => role switch
    {
        BluRole.Tank => JobRegistry.IsTank(jobId),
        BluRole.Healer => JobRegistry.IsHealer(jobId),
        // DPS = anything that's a combat job and neither tank nor healer (BLU mimics all DPS kinds).
        _ => jobId != 0 && !JobRegistry.IsTank(jobId) && !JobRegistry.IsHealer(jobId),
    };
}
