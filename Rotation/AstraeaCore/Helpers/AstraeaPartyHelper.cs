using System.Collections.Generic;
using System.Numerics;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.ClientState.Party;
using Dalamud.Plugin.Services;
using Olympus.Config;
using Olympus.Data;
using Olympus.Models.Action;
using Olympus.Rotation.Common.Helpers;
using Olympus.Services.Prediction;

namespace Olympus.Rotation.AstraeaCore.Helpers;

/// <summary>
/// Astrologian party helper with AST-specific targeting logic.
/// Extends HealerPartyHelper with card targeting and AST-specific heals.
/// </summary>
public class AstraeaPartyHelper : HealerPartyHelper
{
    private readonly AstraeaStatusHelper _statusHelper;

    public AstraeaPartyHelper(
        IObjectTable objectTable,
        IPartyList partyList,
        HpPredictionService hpPredictionService,
        Configuration configuration,
        AstraeaStatusHelper statusHelper)
        : base(objectTable, partyList, hpPredictionService, configuration)
    {
        _statusHelper = statusHelper;
    }

    #region IPartyHelper-style Methods

    /// <summary>
    /// Finds the lowest HP party member that needs healing.
    /// </summary>
    public IBattleChara? FindLowestHpPartyMember(IPlayerCharacter player, int healAmount = 0)
    {
        return FindLowestHpPartyMember(player, ASTActions.Benefic.RangeSquared, healAmount);
    }

    /// <summary>
    /// Finds a dead party member that needs resurrection.
    /// </summary>
    public IBattleChara? FindDeadPartyMemberNeedingRaise(IPlayerCharacter player)
    {
        return FindDeadPartyMemberNeedingRaise(player, RoleActions.Ascend.RangeSquared);
    }

    #endregion

    #region AoE Heal Counting

    /// <summary>
    /// Counts party members needing AoE heal.
    /// </summary>
    public (int count, List<uint> targetIds) CountPartyMembersNeedingAoEHeal(IPlayerCharacter player, int healAmount)
    {
        return CountPartyMembersNeedingAoEHeal(player, ASTActions.Helios.RadiusSquared, healAmount);
    }

    #endregion

    #region Card Targeting

    /// <inheritdoc />
    public override IBattleChara? FindTankInParty(IPlayerCharacter player) =>
        TrustPartyRoleHelper.FindTankInParty(
            player,
            GetAllPartyMembers(player),
            ObjectTable,
            PartyList,
            _statusHelper.HasTankStance);

    /// <summary>
    /// Finds the best target for The Balance card (melee DPS buff).
    /// </summary>
    public IBattleChara? FindBalanceTarget(IPlayerCharacter player)
    {
        IBattleChara? bestMelee = null;
        IBattleChara? bestRanged = null;

        foreach (var member in GetAllPartyMembers(player))
        {
            if (!IsEligibleCardTarget(player, member, ASTActions.TheBalance.RangeSquared, ASTActions.TheBalanceStatusId))
                continue;

            if (TrustPartyRoleHelper.IsMeleeDps(member, PartyList))
                bestMelee ??= member;
            else if (TrustPartyRoleHelper.IsRangedOrCasterDps(member, PartyList))
                bestRanged ??= member;
        }

        return bestMelee ?? bestRanged;
    }

    /// <summary>
    /// Finds the best target for The Spear card (ranged DPS buff).
    /// </summary>
    public IBattleChara? FindSpearTarget(IPlayerCharacter player)
    {
        IBattleChara? bestRanged = null;
        IBattleChara? bestMelee = null;

        foreach (var member in GetAllPartyMembers(player))
        {
            if (!IsEligibleCardTarget(player, member, ASTActions.TheSpear.RangeSquared, ASTActions.TheSpearStatusId))
                continue;

            if (TrustPartyRoleHelper.IsRangedOrCasterDps(member, PartyList))
                bestRanged ??= member;
            else if (TrustPartyRoleHelper.IsMeleeDps(member, PartyList))
                bestMelee ??= member;
        }

        return bestRanged ?? bestMelee;
    }

    /// <summary>
    /// Finds target for The Bole: main tank, or injured ally below tank-support threshold.
    /// </summary>
    public IBattleChara? FindBoleTarget(IPlayerCharacter player, float hpThreshold)
    {
        var tank = FindTankInParty(player);
        if (tank != null
            && !tank.IsDead
            && Vector3.DistanceSquared(player.Position, tank.Position) <= ASTActions.TheBole.RangeSquared
            && !HasCardBuff(tank, ASTActions.TheBoleStatusId))
        {
            return tank;
        }

        IBattleChara? bestInjured = null;
        var lowestHp = 1f;

        foreach (var member in GetAllPartyMembers(player))
        {
            if (member.IsDead) continue;
            if (Vector3.DistanceSquared(player.Position, member.Position) > ASTActions.TheBole.RangeSquared) continue;
            if (HasCardBuff(member, ASTActions.TheBoleStatusId)) continue;

            var hp = GetHpPercent(member);
            if (hp <= hpThreshold && hp < lowestHp)
            {
                lowestHp = hp;
                bestInjured = member;
            }
        }

        return bestInjured ?? tank ?? player;
    }

    /// <summary>
    /// Finds lowest-HP ally below threshold for healing-support cards (Arrow, Ewer).
    /// </summary>
    public IBattleChara? FindHealingCardTarget(IPlayerCharacter player, float hpThreshold, ushort statusId)
    {
        IBattleChara? best = null;
        var lowestHp = 1f;

        foreach (var member in GetAllPartyMembers(player))
        {
            if (member.IsDead) continue;
            if (Vector3.DistanceSquared(player.Position, member.Position) > ASTActions.TheArrow.RangeSquared) continue;
            if (HasCardBuff(member, statusId)) continue;

            var hp = GetHpPercent(member);
            if (hp <= hpThreshold && hp < lowestHp)
            {
                lowestHp = hp;
                best = member;
            }
        }

        return best;
    }

    /// <summary>
    /// Finds target for The Spire: lowest MP ally below threshold, else lowest HP below threshold.
    /// </summary>
    public IBattleChara? FindSpireTarget(IPlayerCharacter player, float hpThreshold)
    {
        IBattleChara? bestMp = null;
        var lowestMp = 1f;

        foreach (var member in GetAllPartyMembers(player))
        {
            if (member.IsDead) continue;
            if (Vector3.DistanceSquared(player.Position, member.Position) > ASTActions.TheSpire.RangeSquared) continue;
            if (HasCardBuff(member, ASTActions.TheSpireStatusId)) continue;

            if (member is IPlayerCharacter pc && pc.MaxMp > 0)
            {
                var mpRatio = (float)pc.CurrentMp / pc.MaxMp;
                if (mpRatio <= hpThreshold && mpRatio < lowestMp)
                {
                    lowestMp = mpRatio;
                    bestMp = member;
                }
            }
        }

        if (bestMp != null) return bestMp;
        return FindHealingCardTarget(player, hpThreshold, ASTActions.TheSpireStatusId) ?? player;
    }

    /// <summary>
    /// Resolves play target for a specific card action.
    /// </summary>
    public IBattleChara? ResolveCardTarget(IPlayerCharacter player, ActionDefinition action, AstrologianConfig config)
    {
        return action.ActionId switch
        {
            var id when id == ASTActions.TheBalance.ActionId => FindBalanceTarget(player),
            var id when id == ASTActions.TheSpear.ActionId => FindSpearTarget(player),
            var id when id == ASTActions.TheBole.ActionId => FindBoleTarget(player, config.CardTankSupportThreshold),
            var id when id == ASTActions.TheArrow.ActionId => FindHealingCardTarget(player, config.CardHealingThreshold, ASTActions.TheArrowStatusId),
            var id when id == ASTActions.TheEwer.ActionId => FindHealingCardTarget(player, config.CardHealingThreshold, ASTActions.TheEwerStatusId),
            var id when id == ASTActions.TheSpire.ActionId => FindSpireTarget(player, config.CardHealingThreshold),
            _ => null,
        };
    }

    /// <summary>
    /// True when a support card has a valid injured target (not dump fallback).
    /// </summary>
    public bool HasValidSupportTarget(IPlayerCharacter player, ActionDefinition action, AstrologianConfig config)
    {
        return action.ActionId switch
        {
            var id when id == ASTActions.TheBole.ActionId =>
                FindBoleTarget(player, config.CardTankSupportThreshold) is { } bole
                && (TrustPartyRoleHelper.IsTank(bole, PartyList, _statusHelper.HasTankStance)
                    || GetHpPercent(bole) <= config.CardTankSupportThreshold),

            var id when id == ASTActions.TheArrow.ActionId =>
                FindHealingCardTarget(player, config.CardHealingThreshold, ASTActions.TheArrowStatusId) != null,

            var id when id == ASTActions.TheEwer.ActionId =>
                FindHealingCardTarget(player, config.CardHealingThreshold, ASTActions.TheEwerStatusId) != null,

            var id when id == ASTActions.TheSpire.ActionId => HasSpireTarget(player, config.CardHealingThreshold),

            _ => true,
        };
    }

    private bool HasSpireTarget(IPlayerCharacter player, float threshold)
    {
        foreach (var member in GetAllPartyMembers(player))
        {
            if (member.IsDead) continue;
            if (Vector3.DistanceSquared(player.Position, member.Position) > ASTActions.TheSpire.RangeSquared) continue;
            if (HasCardBuff(member, ASTActions.TheSpireStatusId)) continue;

            if (member is IPlayerCharacter pc && pc.MaxMp > 0)
            {
                if ((float)pc.CurrentMp / pc.MaxMp <= threshold) return true;
            }

            if (GetHpPercent(member) <= threshold) return true;
        }

        return false;
    }

    private static bool HasCardBuff(IBattleChara member, ushort statusId)
    {
        if (member.StatusList == null) return false;
        foreach (var status in member.StatusList)
        {
            if (status.StatusId == statusId) return true;
        }
        return false;
    }

    private bool IsEligibleCardTarget(
        IPlayerCharacter player,
        IBattleChara member,
        float rangeSquared,
        ushort buffStatusId)
    {
        if (member.IsDead) return false;
        if (member.EntityId == player.EntityId) return false;
        if (Vector3.DistanceSquared(player.Position, member.Position) > rangeSquared) return false;
        if (HasCardBuff(member, buffStatusId)) return false;
        if (TrustPartyRoleHelper.IsHealer(member, PartyList)) return false;
        if (TrustPartyRoleHelper.IsTank(member, PartyList, _statusHelper.HasTankStance)) return false;
        return true;
    }

    #endregion

    #region Essential Dignity Target

    /// <summary>
    /// Finds the best target for Essential Dignity.
    /// Essential Dignity scales with missing HP (400-1100 potency), so lower HP is better.
    /// </summary>
    public IBattleChara? FindEssentialDignityTarget(IPlayerCharacter player, float hpThreshold = 0.4f)
    {
        IBattleChara? bestTarget = null;
        float lowestHp = 1f;

        foreach (var member in GetAllPartyMembers(player))
        {
            if (member.IsDead)
                continue;
            if (Vector3.DistanceSquared(player.Position, member.Position) > ASTActions.EssentialDignity.RangeSquared)
                continue;

            var hpPercent = GetHpPercent(member);

            if (hpPercent < hpThreshold && hpPercent < lowestHp)
            {
                lowestHp = hpPercent;
                bestTarget = member;
            }
        }

        return bestTarget;
    }

    #endregion

    #region Synastry Target

    /// <summary>
    /// Finds the best target for Synastry (usually tank taking sustained damage).
    /// </summary>
    public IBattleChara? FindSynastryTarget(IPlayerCharacter player)
    {
        // Priority 1: Tank
        var tank = FindTankInParty(player);
        if (tank != null && !_statusHelper.HasSynastryLink(tank))
        {
            var hpPercent = GetHpPercent(tank);
            if (hpPercent < 0.8f) // Only if tank has taken some damage
                return tank;
        }

        // Priority 2: Lowest HP party member
        return FindLowestHpPartyMember(player);
    }

    #endregion

    #region Exaltation Target

    /// <summary>
    /// Finds the best target for Exaltation (damage reduction + delayed heal).
    /// </summary>
    public IBattleChara? FindExaltationTarget(IPlayerCharacter player)
    {
        // Priority 1: Tank without Exaltation taking damage
        var tank = FindTankInParty(player);
        if (tank != null && !_statusHelper.HasExaltation(tank))
        {
            var hpPercent = GetHpPercent(tank);
            if (hpPercent < 0.8f)
                return tank;
        }

        // Priority 2: Any party member below threshold without Exaltation
        IBattleChara? lowestMember = null;
        float lowestHp = 1f;

        foreach (var member in GetAllPartyMembers(player))
        {
            if (member.IsDead)
                continue;
            if (Vector3.DistanceSquared(player.Position, member.Position) > ASTActions.Exaltation.RangeSquared)
                continue;
            if (_statusHelper.HasExaltation(member))
                continue;

            var hpPercent = GetHpPercent(member);
            if (hpPercent < lowestHp && hpPercent < 0.7f)
            {
                lowestHp = hpPercent;
                lowestMember = member;
            }
        }

        return lowestMember ?? tank;
    }

    #endregion
}
