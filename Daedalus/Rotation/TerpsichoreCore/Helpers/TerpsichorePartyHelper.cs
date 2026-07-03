using System.Collections.Generic;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.ClientState.Party;
using Dalamud.Plugin.Services;
using Daedalus.Config.DPS;
using Daedalus.Data;
using Daedalus.Rotation.Common.Helpers;

namespace Daedalus.Rotation.TerpsichoreCore.Helpers;

/// <summary>
/// Helper for party member queries and dance partner selection in Dancer rotation.
/// </summary>
public sealed class TerpsichorePartyHelper
{
    private readonly IObjectTable _objectTable;
    private readonly IPartyList _partyList;

    // Dance partner priority: highest personal-DPS gain jobs first (they benefit most from the
    // Standard/Technical Finish damage buff and feed back the most Esprit). Refreshed for current
    // patch (Dawntrail 7.x): Pictomancer is now a top-tier partner and sits at the front, ahead of
    // the melee. This is a static job-rank heuristic, not a live parse — adjust the order here when
    // the meta shifts. Order: PCT > SAM > VPR > MNK > DRG > RPR > NIN > BLM > SMN > RDM > MCH > BRD > DNC > Tank > Healer.
    private static readonly uint[] PartnerPriority = new[]
    {
        JobRegistry.Pictomancer, // 1st priority - PCT (top partner in 7.x)
        JobRegistry.Samurai,     // 2nd - SAM
        JobRegistry.Viper,       // 3rd - VPR
        JobRegistry.Monk,        // 4th - MNK
        JobRegistry.Dragoon,     // 5th - DRG
        JobRegistry.Reaper,      // 6th - RPR
        JobRegistry.Ninja,       // 7th - NIN
        JobRegistry.BlackMage,   // 8th - BLM
        JobRegistry.Summoner,    // 9th - SMN
        JobRegistry.RedMage,     // 10th - RDM
        JobRegistry.Machinist,   // 11th - MCH
        JobRegistry.Bard,        // 12th - BRD
        JobRegistry.Dancer,      // 13th - DNC
        // Tanks
        JobRegistry.Paladin,
        JobRegistry.Gladiator,
        JobRegistry.Warrior,
        JobRegistry.Marauder,
        JobRegistry.DarkKnight,
        JobRegistry.Gunbreaker,
        // Healers (lowest priority)
        JobRegistry.WhiteMage,
        JobRegistry.Conjurer,
        JobRegistry.Scholar,
        JobRegistry.Arcanist,
        JobRegistry.Astrologian,
        JobRegistry.Sage
    };

    public TerpsichorePartyHelper(IObjectTable objectTable, IPartyList partyList)
    {
        _objectTable = objectTable;
        _partyList = partyList;
    }

    /// <summary>
    /// Gets all party members: party list when in a real party, otherwise the player plus any
    /// Trust/Duty Support allies from the object table (the party list is EMPTY in Trust content —
    /// same pattern as <see cref="BasePartyHelper"/>).
    /// </summary>
    public IEnumerable<IBattleChara> GetAllPartyMembers(IPlayerCharacter player)
    {
        if (_partyList.Length > 0)
        {
            foreach (var member in _partyList)
            {
                if (member.GameObject is IBattleChara battleChara)
                {
                    yield return battleChara;
                }
            }
            yield break;
        }

        yield return player;

        foreach (var obj in _objectTable)
        {
            if (BasePartyHelper.IsValidTrustNpc(obj, out var npc))
                yield return npc!;
        }
    }

    /// <summary>
    /// Gets the HP percentage for a party member.
    /// </summary>
    public float GetHpPercent(IBattleChara member)
    {
        if (member.MaxHp == 0)
            return 1f;

        return (float)member.CurrentHp / member.MaxHp;
    }

    /// <summary>
    /// Counts party members below a certain HP threshold.
    /// </summary>
    public int CountMembersBelow(IPlayerCharacter player, float threshold)
    {
        var count = 0;
        foreach (var member in GetAllPartyMembers(player))
        {
            if (GetHpPercent(member) < threshold)
                count++;
        }
        return count;
    }

    /// <summary>
    /// Gets the number of party members in range of a given radius.
    /// </summary>
    public int CountMembersInRange(IPlayerCharacter player, float radius)
    {
        var count = 0;
        var radiusSq = radius * radius;

        foreach (var member in GetAllPartyMembers(player))
        {
            if (member.EntityId == player.EntityId)
                continue;

            var dx = player.Position.X - member.Position.X;
            var dz = player.Position.Z - member.Position.Z;
            var distSq = dx * dx + dz * dz;

            if (distSq <= radiusSq)
                count++;
        }

        return count + 1; // Include self
    }

    /// <summary>
    /// Selects the best dance partner based on job priority and the configured selection mode.
    /// Works in real parties AND Trust/Duty Support content — candidates come from
    /// <see cref="GetAllPartyMembers"/>, so trust allies are valid partners (Closed Position
    /// buffs the dancer's own damage regardless of who holds it).
    /// </summary>
    public IBattleChara? SelectDancePartner(IPlayerCharacter player, PartnerSelection mode = PartnerSelection.HighestDps)
    {
        return mode switch
        {
            PartnerSelection.MeleePriority => SelectPartnerWithRolePriority(player, preferMelee: true),
            PartnerSelection.RangedPriority => SelectPartnerWithRolePriority(player, preferMelee: false),
            _ => SelectPartnerByRank(player),
        };
    }

    private IBattleChara? SelectPartnerByRank(IPlayerCharacter player)
    {
        IBattleChara? bestPartner = null;
        var bestPriority = int.MaxValue;

        foreach (var member in GetAllPartyMembers(player))
        {
            if (member.EntityId == player.EntityId)
                continue;
            if (member.CurrentHp == 0)
                continue;

            var priority = GetJobPriority(TrustPartyRoleHelper.ResolveJobId(member, _partyList));
            if (priority < bestPriority)
            {
                bestPriority = priority;
                bestPartner = member;
            }
        }

        return bestPartner;
    }

    private IBattleChara? SelectPartnerWithRolePriority(IPlayerCharacter player, bool preferMelee)
    {
        // First pass: look for a partner in the preferred role group
        IBattleChara? preferred = null;
        var preferredPriority = int.MaxValue;

        foreach (var member in GetAllPartyMembers(player))
        {
            if (member.EntityId == player.EntityId)
                continue;
            if (member.CurrentHp == 0)
                continue;

            var jobId = TrustPartyRoleHelper.ResolveJobId(member, _partyList);
            bool inPreferredRole = preferMelee
                ? JobRegistry.IsMeleeDps(jobId)
                : (JobRegistry.IsRangedPhysicalDps(jobId) || JobRegistry.IsCasterDps(jobId));

            if (!inPreferredRole)
                continue;

            var priority = GetJobPriority(jobId);
            if (priority < preferredPriority)
            {
                preferredPriority = priority;
                preferred = member;
            }
        }

        if (preferred != null)
            return preferred;

        // Fallback: no one in the preferred role — use the default rank
        return SelectPartnerByRank(player);
    }

    /// <summary>
    /// Gets the entity ID of our current dance partner.
    /// </summary>
    public uint GetDancePartnerId(IPlayerCharacter player, TerpsichoreStatusHelper statusHelper)
    {
        if (!statusHelper.HasClosedPosition(player))
            return 0;

        // Find who has the Dance Partner buff from us
        foreach (var member in GetAllPartyMembers(player))
        {
            if (member.EntityId == player.EntityId)
                continue;

            if (statusHelper.HasDancePartnerFrom(member, player.EntityId))
                return member.EntityId;
        }

        return 0;
    }

    /// <summary>
    /// Gets the name of our current dance partner.
    /// </summary>
    public string? GetDancePartnerName(IPlayerCharacter player, TerpsichoreStatusHelper statusHelper)
    {
        var partnerId = GetDancePartnerId(player, statusHelper);
        if (partnerId == 0)
            return null;

        foreach (var member in GetAllPartyMembers(player))
        {
            if (member.EntityId == partnerId)
                return member.Name?.TextValue;
        }

        return null;
    }

    /// <summary>
    /// Checks if we need to update our dance partner: we have none, the current one is dead/gone, or
    /// the configured selection would now pick a strictly higher-priority partner (e.g. a better job
    /// revived or joined). Upgrades are strict-better only, so we never thrash between equal-priority
    /// members.
    /// </summary>
    public bool ShouldUpdatePartner(
        IPlayerCharacter player,
        TerpsichoreStatusHelper statusHelper,
        PartnerSelection mode = PartnerSelection.HighestDps)
    {
        var currentPartnerId = GetDancePartnerId(player, statusHelper);

        // No partner, we should get one
        if (currentPartnerId == 0)
            return true;

        // Resolve the current partner: re-partner if dead or no longer present in the party.
        var currentPriority = int.MaxValue;
        var foundCurrent = false;
        foreach (var member in GetAllPartyMembers(player))
        {
            if (member.EntityId != currentPartnerId)
                continue;
            if (member.CurrentHp == 0)
                return true; // partner died
            // Resolve via the party-list entry: a Trust/Duty Support NPC's GameObject can report
            // ClassJob 0, which would wrongly rank as lowest priority.
            currentPriority = GetJobPriority(TrustPartyRoleHelper.ResolveJobId(member, _partyList));
            foundCurrent = true;
            break;
        }
        if (!foundCurrent)
            return true; // partner left the party / no longer in the object table

        // Upgrade: switch only when the configured selection yields a strictly better partner.
        var best = SelectDancePartner(player, mode);
        if (best == null || best.EntityId == currentPartnerId)
            return false;

        var bestPriority = GetJobPriority(TrustPartyRoleHelper.ResolveJobId(best, _partyList));
        return ShouldUpgradePartner(currentPriority, bestPriority);
    }

    /// <summary>
    /// Pure decision: should we re-partner to a candidate at <paramref name="candidatePriority"/> when
    /// our current partner sits at <paramref name="currentPriority"/>? True only for a strict upgrade
    /// (lower index = higher priority), so equal-priority candidates never trigger a swap.
    /// </summary>
    public static bool ShouldUpgradePartner(int currentPriority, int candidatePriority)
        => candidatePriority < currentPriority;

    /// <summary>
    /// Rank of a job in the dance-partner priority list; lower is better. Unknown jobs return
    /// <see cref="int.MaxValue"/> (lowest priority).
    /// </summary>
    public static int GetJobPriority(uint jobId)
    {
        for (int i = 0; i < PartnerPriority.Length; i++)
        {
            if (PartnerPriority[i] == jobId)
                return i;
        }

        // Unknown job: lowest priority but still SELECTABLE — int.MaxValue can never win the
        // `< bestPriority` comparison, which left the DNC partnerless instead of taking a
        // last-resort partner (Standard Finish buffs the dancer regardless of who holds it).
        return PartnerPriority.Length;
    }
}
