using System;
using System.Collections.Generic;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.ClientState.Party;
using Dalamud.Plugin.Services;
using Daedalus.Data;

namespace Daedalus.Rotation.Common.Helpers;

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
    /// Ambient source of the coordination layer's designated off-tank character name (from the LAN
    /// window's off-tank picker / tank role settings). Set once by Plugin; null / empty = no
    /// designation. Static-backed so party helpers can consult it without service plumbing.
    /// </summary>
    public static Func<string?>? DesignatedOffTankNameSource;

    /// <summary>
    /// Finds the party tank: ClassJob/stance first, then trust fallback via enemy aggro.
    /// With TWO tanks the main tank is preferred — the one the biggest engaged enemy (boss) is
    /// targeting, falling back to the LAN off-tank designation (skip the designated OT), then party
    /// order. Buff/heal anchors (Kardia, tank oGCD heals) must sit on the MT, not whichever tank
    /// happens to be listed first.
    /// </summary>
    public static IBattleChara? FindTankInParty(
        IPlayerCharacter player,
        IEnumerable<IBattleChara> members,
        IObjectTable objectTable,
        IPartyList partyList,
        Func<IBattleChara, bool>? hasTankStance = null)
    {
        IBattleChara? firstTank = null;
        List<IBattleChara>? tanks = null;
        foreach (var member in members)
        {
            if (member.EntityId == player.EntityId || member.IsDead)
                continue;
            if (!IsTank(member, partyList, hasTankStance))
                continue;

            if (firstTank == null)
            {
                firstTank = member;
            }
            else
            {
                tanks ??= new List<IBattleChara> { firstTank };
                tanks.Add(member);
            }
        }

        if (firstTank != null && tanks == null)
            return firstTank;

        if (tanks != null)
            return PickMainTank(tanks, objectTable);

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

    /// <summary>
    /// Two-plus-tank disambiguation. Aggro reality wins: the tank targeted by the largest-MaxHp
    /// engaged enemy (the boss picks the MT; a trash mob on the OT must not out-vote it). This
    /// also tracks coordinated tank swaps automatically. Out of combat (no enemy targeting any
    /// tank) the LAN off-tank designation breaks the tie; last resort is party order.
    /// </summary>
    private static IBattleChara PickMainTank(List<IBattleChara> tanks, IObjectTable objectTable)
    {
        IBattleChara? aggroTank = null;
        uint aggroEnemyMaxHp = 0;
        if (objectTable is not null)
        {
            foreach (var obj in objectTable)
            {
                if (obj is not IBattleNpc enemy)
                    continue;
                if (enemy.TargetObjectId is 0 or 0xE0000000)
                    continue;

                foreach (var tank in tanks)
                {
                    if (tank.GameObjectId != enemy.TargetObjectId)
                        continue;

                    if (aggroTank == null || enemy.MaxHp > aggroEnemyMaxHp)
                    {
                        aggroTank = tank;
                        aggroEnemyMaxHp = enemy.MaxHp;
                    }

                    break;
                }
            }
        }

        if (aggroTank != null)
            return aggroTank;

        var offTankName = DesignatedOffTankNameSource?.Invoke();
        if (!string.IsNullOrEmpty(offTankName))
        {
            foreach (var tank in tanks)
            {
                var name = tank.Name?.TextValue;
                if (!string.IsNullOrEmpty(name)
                    && !string.Equals(name, offTankName, StringComparison.OrdinalIgnoreCase))
                {
                    return tank;
                }
            }
        }

        return tanks[0];
    }
}
