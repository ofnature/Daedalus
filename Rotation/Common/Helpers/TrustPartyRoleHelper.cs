using System;
using System.Collections.Generic;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.ClientState.Party;
using Dalamud.Plugin.Services;
using Olympus.Data;

namespace Olympus.Rotation.Common.Helpers;

/// <summary>
/// Resolves party member roles for trust allies and player buff targeting.
/// Trust NPCs are <see cref="IBattleNpc"/> — never gate role checks on <see cref="IPlayerCharacter"/> alone.
/// </summary>
public static class TrustPartyRoleHelper
{
    /// <summary>
    /// Resolves ClassJob from the battle character, then party list entry when the object has no job set.
    /// </summary>
    public static uint ResolveJobId(IBattleChara chara, IPartyList partyList)
    {
        var jobId = chara.ClassJob.RowId;
        if (jobId != 0)
            return jobId;

        if (partyList.Length == 0)
            return 0;

        foreach (var member in partyList)
        {
            if (member.EntityId == chara.EntityId)
                return member.ClassJob.RowId;
        }

        return 0;
    }

    public static bool IsTank(IBattleChara chara, IPartyList partyList, Func<IBattleChara, bool>? hasTankStance = null)
    {
        var jobId = ResolveJobId(chara, partyList);
        if (jobId != 0 && JobRegistry.IsTank(jobId))
            return true;

        return hasTankStance?.Invoke(chara) == true;
    }

    public static bool IsHealer(IBattleChara chara, IPartyList partyList)
    {
        var jobId = ResolveJobId(chara, partyList);
        return jobId != 0 && JobRegistry.IsHealer(jobId);
    }

    public static bool IsMeleeDps(IBattleChara chara, IPartyList partyList)
    {
        var jobId = ResolveJobId(chara, partyList);
        return jobId != 0 && JobRegistry.IsMeleeDps(jobId);
    }

    public static bool IsRangedPhysicalDps(IBattleChara chara, IPartyList partyList)
    {
        var jobId = ResolveJobId(chara, partyList);
        return jobId != 0 && JobRegistry.IsRangedPhysicalDps(jobId);
    }

    public static bool IsCasterDps(IBattleChara chara, IPartyList partyList)
    {
        var jobId = ResolveJobId(chara, partyList);
        return jobId != 0 && JobRegistry.IsCasterDps(jobId);
    }

    public static bool IsRangedOrCasterDps(IBattleChara chara, IPartyList partyList) =>
        IsRangedPhysicalDps(chara, partyList) || IsCasterDps(chara, partyList);

    public static bool IsDps(IBattleChara chara, IPartyList partyList) =>
        IsMeleeDps(chara, partyList) || IsRangedOrCasterDps(chara, partyList);

    /// <summary>
    /// Finds the party tank: ClassJob/stance first, then trust fallback via enemy aggro.
    /// </summary>
    public static IBattleChara? FindTankInParty(
        IPlayerCharacter player,
        IEnumerable<IBattleChara> members,
        IObjectTable objectTable,
        IPartyList partyList,
        Func<IBattleChara, bool>? hasTankStance = null)
    {
        foreach (var member in members)
        {
            if (member.EntityId == player.EntityId || member.IsDead)
                continue;
            if (IsTank(member, partyList, hasTankStance))
                return member;
        }

        IBattleChara? effectiveTank = null;
        if (objectTable is not null)
        {
            foreach (var obj in objectTable)
            {
                if (obj is not IBattleNpc enemy)
                    continue;
                if (enemy.TargetObjectId is 0 or 0xE0000000)
                    continue;

                foreach (var member in members)
                {
                    if (member.EntityId == player.EntityId || member.IsDead)
                        continue;
                    if (member.GameObjectId != enemy.TargetObjectId)
                        continue;

                    effectiveTank = member;
                    break;
                }

                if (effectiveTank != null)
                    break;
            }
        }

        return effectiveTank;
    }
}
