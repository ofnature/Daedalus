using System.Collections.Generic;
using System.Numerics;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.ClientState.Party;
using Dalamud.Plugin.Services;
using Daedalus.Config;
using Daedalus.Data;
using Daedalus.Rotation.ApolloCore.Helpers;
using Daedalus.Rotation.Common.Helpers;
using Daedalus.Services.Action;
using Daedalus.Services.Party;
using Daedalus.Services.Prediction;
using Daedalus.Services.Targeting;

namespace Daedalus.Rotation.AsclepiusCore.Helpers;

/// <summary>
/// Sage party helper with trust-aware tank resolution and AoE heal counting.
/// </summary>
public class AsclepiusPartyHelper : HealerPartyHelper, IPartyHelper
{
    private readonly AsclepiusStatusHelper _statusHelper;

    public AsclepiusPartyHelper(
        IObjectTable objectTable,
        IPartyList partyList,
        HpPredictionService hpPredictionService,
        Configuration configuration,
        AsclepiusStatusHelper statusHelper)
        : base(objectTable, partyList, hpPredictionService, configuration)
    {
        _statusHelper = statusHelper;
    }

    #region IPartyHelper-style Methods

    public IBattleChara? FindLowestHpPartyMember(IPlayerCharacter player, int healAmount = 0) =>
        FindLowestHpPartyMember(player, SGEActions.Diagnosis.RangeSquared, healAmount);

    public IBattleChara? FindDeadPartyMemberNeedingRaise(IPlayerCharacter player, bool includeAlliance = false) =>
        FindDeadPartyMemberNeedingRaise(player, RoleActions.Ascend.RangeSquared, includeAlliance);

    public (int count, List<uint> targetIds) CountPartyMembersNeedingAoEHeal(IPlayerCharacter player, int healAmount) =>
        CountPartyMembersNeedingAoEHeal(player, SGEActions.Prognosis.RadiusSquared, healAmount);

    (int count, bool anyHaveRegen, List<(uint entityId, string name)> allTargets, int averageMissingHp)
        IPartyHelper.CountPartyMembersNeedingAoEHeal(IPlayerCharacter player, int healAmount)
    {
        var (count, targetIds) = CountPartyMembersNeedingAoEHeal(player, healAmount);
        var allTargets = new List<(uint entityId, string name)>(targetIds.Count);
        foreach (var id in targetIds)
            allTargets.Add((id, string.Empty));
        return (count, false, allTargets, 0);
    }

    (IBattleChara? target, int count, List<uint> targetIds) IPartyHelper.FindBestCureIIITarget(
        IPlayerCharacter player, int healAmount) =>
        (null, 0, []);

    IBattleChara? IPartyHelper.FindRegenTarget(
        IPlayerCharacter player, float tankHpThreshold, float nonTankHpThreshold, float regenRefreshThreshold) =>
        null;

    bool IPartyHelper.NeedsRegen(IBattleChara target, float hpThreshold, float refreshThreshold) => false;

    IBattleChara? IPartyHelper.FindMostEndangeredPartyMember(
        IPlayerCharacter player,
        IDamageIntakeService damageIntakeService,
        int healAmount,
        IDamageTrendService? damageTrendService,
        IShieldTrackingService? shieldTrackingService) =>
        FindMostEndangeredPartyMember(player, damageIntakeService, healAmount, damageTrendService, shieldTrackingService);

    int IPartyHelper.CountInjuredInAoERange(IPlayerCharacter player, float radius, float hpThreshold)
    {
        var count = 0;
        var radiusSquared = radius * radius;
        var playerPos = player.Position;

        foreach (var member in GetAllPartyMembers(player))
        {
            if (member.IsDead)
                continue;
            if (Vector3.DistanceSquared(playerPos, member.Position) > radiusSquared)
                continue;

            if (GetHpPercent(member) < hpThreshold)
                count++;
        }

        return count;
    }

    #endregion

    /// <inheritdoc />
    public override IBattleChara? FindTankInParty(IPlayerCharacter player) =>
        TrustPartyRoleHelper.FindTankInParty(
            player,
            GetAllPartyMembers(player),
            ObjectTable,
            PartyList,
            _statusHelper.HasTankStance);

    /// <summary>
    /// Party health metrics for AoE heal decisions. Injured count respects <see cref="SageConfig.AoEHealCountMode"/>.
    /// </summary>
    public (float avgHpPercent, float lowestHpPercent, int injuredCount) GetAoEHealMetrics(IPlayerCharacter player)
    {
        var (avgHp, lowestHp, _) = CalculatePartyHealthMetrics(player);

        if (Configuration.Sage.AoEHealCountMode != SageAoEHealCountMode.TankCentered)
        {
            var (_, _, injured) = CalculatePartyHealthMetrics(player);
            return (avgHp, lowestHp, injured);
        }

        var anchor = FindTankInParty(player) ?? player;
        var anchorPos = anchor.Position;
        var radiusSquared = SGEActions.Prognosis.RadiusSquared;
        var injuredNearAnchor = 0;

        foreach (var member in GetAllPartyMembers(player))
        {
            if (member.IsDead)
                continue;
            if (Vector3.DistanceSquared(anchorPos, member.Position) > radiusSquared)
                continue;

            if (GetHpPercent(member) < FFXIVConstants.InjuredHpThreshold)
                injuredNearAnchor++;
        }

        return (avgHp, lowestHp, injuredNearAnchor);
    }

    /// <summary>
    /// Enemy count for Dyskrasia — ALWAYS player-centered. Dyskrasia is a point-blank self AoE
    /// (5y around the PLAYER), so the target-centered count mode was a correctness bug, not a
    /// preference: with the pack clustered around a target 20y away the gate passed while the
    /// player hit nothing (2026-07-01 validation log: four "Dyskrasia II ×0" zero-damage casts,
    /// caught by the AoE commit counter). <see cref="SageConfig.AoEDamageCountMode"/> is retained
    /// for config-file compat but no longer consulted.
    /// </summary>
    public int CountEnemiesForAoEDamage(
        IPlayerCharacter player,
        float radius,
        ITargetingService targetingService,
        IActionService? actionService = null)
    {
        return targetingService.CountEnemiesInRange(radius, player);
    }
}
