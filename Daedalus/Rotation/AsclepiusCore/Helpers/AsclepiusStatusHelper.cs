using System.Collections.Generic;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using Daedalus.Data;
using Daedalus.Rotation.Common.Helpers;
using Daedalus.Services.Action;

namespace Daedalus.Rotation.AsclepiusCore.Helpers;

/// <summary>
/// Helper class for checking Sage-specific status effects on characters.
/// </summary>
public sealed class AsclepiusStatusHelper : BaseStatusHelper
{
    private const ushort IronWillStatusId = 79;
    private const ushort DefianceStatusId = 91;
    private const ushort GritStatusId = 743;
    private const ushort RoyalGuardStatusId = 1833;

    // Role action status IDs (shared with all healers)
    public static class RoleStatusIds
    {
        public const uint Raise = 148;
    }

    // Core status methods (HasStatus, GetStatusStacks, HasStatusFromSource) inherited from BaseStatusHelper

    #region Role Actions

    /// <summary>
    /// Checks if player has Surecast buff active.
    /// </summary>
    public static bool HasSurecast(IPlayerCharacter player) =>
        HasStatus(player, SharedStatusIds.Surecast);

    #endregion

    #region Eukrasia System

    /// <summary>
    /// Checks if Eukrasia buff is active on the player.
    /// </summary>
    public static bool HasEukrasia(IPlayerCharacter player) =>
        HasStatus(player, SGEActions.EukrasiaStatusId);

    /// <summary>
    /// Checks if Zoe buff is active on the player (+50% next GCD heal).
    /// </summary>
    public static bool HasZoe(IPlayerCharacter player) =>
        HasStatus(player, SGEActions.ZoeStatusId);

    /// <summary>
    /// Gets the remaining duration of Zoe buff.
    /// </summary>
    public static float GetZoeRemaining(IPlayerCharacter player)
    {
        if (HasStatus(player, SGEActions.ZoeStatusId, out float remaining))
            return remaining;
        return 0f;
    }

    #endregion

    #region Kardia / Kardion System

    /// <summary>
    /// Checks if the player has the Kardia self-buff (secondary signal; prefer Kardion on target).
    /// </summary>
    public static bool HasKardia(IPlayerCharacter player) =>
        TryReadStatusFromList(player, SGEActions.KardiaStatusId);

    /// <summary>
    /// Checks if a target has Kardion (2605) on any status scan candidate.
    /// </summary>
    public static bool HasKardion(
        IBattleChara target,
        IObjectTable? objectTable = null,
        IPartyList? partyList = null) =>
        TryScanKardion(target, sage: null, objectTable, partyList, requireFromSage: false, out _);

    /// <summary>
    /// Checks if a target has Kardion from this Sage.
    /// </summary>
    public static bool HasKardionFrom(
        IBattleChara target,
        IPlayerCharacter sage,
        IObjectTable? objectTable = null,
        IPartyList? partyList = null) =>
        TryScanKardion(target, sage, objectTable, partyList, requireFromSage: true, out _);

    /// <summary>
    /// Checks if a target has Kardion (receiver buff from a Sage entity id).
    /// </summary>
    public static bool HasKardionFrom(IBattleChara target, uint sageEntityId) =>
        HasStatusFromSource(target, SGEActions.KardionStatusId, sageEntityId);

    /// <summary>
    /// True when the tank currently bears Kardion (2605). Primary pre-pull / placement signal.
    /// </summary>
    public static bool TankHasKardion(
        IPlayerCharacter player,
        IBattleChara? tank,
        IObjectTable objectTable,
        IPartyList partyList,
        ulong knownBearerId = 0)
    {
        if (tank == null || tank.EntityId == player.EntityId)
            return false;

        if (TryDetectKardionOn(tank, player, objectTable, partyList, out _))
            return true;

        if (knownBearerId != 0 && knownBearerId == tank.GameObjectId)
            return true;

        return InferKardionOnTank(player, tank, objectTable, partyList);
    }

    /// <summary>
    /// Resolves the ally bearing Kardion (2605). Party-list scan first so it matches the party UI.
    /// </summary>
    public static bool TryFindKardionBearer(
        IPlayerCharacter player,
        IObjectTable objectTable,
        IPartyList partyList,
        IEnumerable<IBattleChara> allies,
        IBattleChara? preferredAlly,
        out ulong bearerGameObjectId)
    {
        bearerGameObjectId = 0;

        if (partyList.Length > 0)
        {
            foreach (var member in partyList)
            {
                if (member?.GameObject is not IBattleChara chara || chara.EntityId == player.EntityId)
                    continue;

                if (TryDetectKardionOn(chara, player, objectTable, partyList, out _))
                {
                    bearerGameObjectId = chara.GameObjectId;
                    return true;
                }
            }
        }

        if (preferredAlly != null
            && preferredAlly.EntityId != player.EntityId
            && TryDetectKardionOn(preferredAlly, player, objectTable, partyList, out _))
        {
            bearerGameObjectId = preferredAlly.GameObjectId;
            return true;
        }

        foreach (var ally in allies)
        {
            if (ally.EntityId == player.EntityId)
                continue;

            if (TryDetectKardionOn(ally, player, objectTable, partyList, out _))
            {
                bearerGameObjectId = ally.GameObjectId;
                return true;
            }
        }

        if (preferredAlly != null && InferKardionOnTank(player, preferredAlly, objectTable, partyList))
        {
            bearerGameObjectId = preferredAlly.GameObjectId;
            return true;
        }

        if (HasKardia(player))
        {
            foreach (var ally in allies)
            {
                if (ally.EntityId == player.EntityId)
                    continue;

                if (!JobRegistry.IsTank(TrustPartyRoleHelper.ResolveJobId(ally, partyList)))
                    continue;

                if (!InferKardionOnTank(player, ally, objectTable, partyList))
                    continue;

                bearerGameObjectId = ally.GameObjectId;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// When the Sage has Kardia active but Kardion is not visible on status lists (common on trust allies),
    /// assume Kardion is on the tank if no other ally shows Kardion.
    /// </summary>
    public static bool InferKardionOnTank(
        IPlayerCharacter player,
        IBattleChara tank,
        IObjectTable objectTable,
        IPartyList partyList)
    {
        if (!HasKardia(player))
            return false;

        if (partyList.Length > 0)
        {
            foreach (var member in partyList)
            {
                if (member?.GameObject is not IBattleChara chara || chara.EntityId == player.EntityId)
                    continue;

                if (chara.EntityId == tank.EntityId)
                    continue;

                if (TryDetectKardionOn(chara, player, objectTable, partyList, out _))
                    return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Resolves the ally carrying Kardion (2605).
    /// </summary>
    public static ulong FindKardionTargetId(
        IPlayerCharacter player,
        IObjectTable objectTable,
        IPartyList partyList,
        IEnumerable<IBattleChara> allies,
        IBattleChara? preferredAlly = null) =>
        TryFindKardionBearer(player, objectTable, partyList, allies, preferredAlly, out var id) ? id : 0;

    private static bool TryScanKardion(
        IBattleChara ally,
        IPlayerCharacter? sage,
        IObjectTable? objectTable,
        IPartyList? partyList,
        bool requireFromSage,
        out bool fromSage)
    {
        fromSage = false;

        foreach (var candidate in EnumerateStatusScanTargets(ally, objectTable, partyList))
        {
            if (!TryReadKardionFromStatusList(candidate, sage, requireFromSage, out fromSage))
                continue;

            return true;
        }

        return false;
    }

    private static bool TryDetectKardionOn(
        IBattleChara ally,
        IPlayerCharacter? sage,
        IObjectTable objectTable,
        IPartyList partyList,
        out bool fromSage) =>
        TryScanKardion(ally, sage, objectTable, partyList, requireFromSage: false, out fromSage);

    private static bool TryReadKardionFromStatusList(
        IBattleChara candidate,
        IPlayerCharacter? sage,
        bool requireFromSage,
        out bool fromSage)
    {
        fromSage = false;

        try
        {
            var statusList = candidate.StatusList;
            if (statusList == null)
                return false;

            for (var i = 0; i < statusList.Length; i++)
            {
                var status = statusList[i];
                if (status == null || !IsKardionStatusId(status.StatusId))
                    continue;

                fromSage = sage != null && IsKardionFromSage(status.SourceId, sage);
                if (requireFromSage && !fromSage)
                    continue;

                return true;
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    private static bool IsKardionStatusId(uint statusId) =>
        statusId == SGEActions.KardionStatusId;

    private static IEnumerable<IBattleChara> EnumerateStatusScanTargets(
        IBattleChara ally,
        IObjectTable? objectTable,
        IPartyList? partyList)
    {
        if (partyList != null && partyList.Length > 0)
        {
            foreach (var member in partyList)
            {
                if (member?.GameObject is not IBattleChara fromParty)
                    continue;

                if (fromParty.EntityId == ally.EntityId || fromParty.GameObjectId == ally.GameObjectId)
                    yield return fromParty;
            }
        }

        yield return ally;

        if (objectTable == null)
            yield break;

        if (objectTable.SearchByEntityId(ally.EntityId) is IBattleChara byEntity
            && byEntity.EntityId == ally.EntityId
            && !ReferenceEquals(byEntity, ally))
        {
            yield return byEntity;
        }

        if (objectTable.SearchById(ally.GameObjectId) is IBattleChara byId
            && byId.EntityId == ally.EntityId
            && !ReferenceEquals(byId, ally))
        {
            yield return byId;
        }
    }

    private static bool IsKardionFromSage(uint sourceId, IPlayerCharacter sage)
    {
        var gameObjectId = sage.GameObjectId;
        return sourceId == (uint)gameObjectId
            || sourceId == unchecked((uint)(gameObjectId & 0xFFFFFFFF))
            || sourceId == sage.EntityId;
    }

    private static bool TryReadStatusFromList(IBattleChara character, ushort statusId)
    {
        try
        {
            var statusList = character.StatusList;
            if (statusList == null)
                return false;

            for (var i = 0; i < statusList.Length; i++)
            {
                var status = statusList[i];
                if (status != null && status.StatusId == statusId)
                    return true;
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    /// <summary>
    /// Checks if Soteria buff is active on the player.
    /// </summary>
    public static bool HasSoteria(IPlayerCharacter player) =>
        HasStatus(player, SGEActions.SoteriaStatusId);

    /// <summary>
    /// Gets the number of Soteria stacks remaining.
    /// </summary>
    public static int GetSoteriaStacks(IPlayerCharacter player) =>
        GetStatusStacks(player, SGEActions.SoteriaStatusId);

    /// <summary>
    /// Checks if Philosophia buff is active on the player (party-wide Kardia).
    /// </summary>
    public static bool HasPhilosophia(IPlayerCharacter player) =>
        HasStatus(player, SGEActions.PhilosophiaStatusId);

    #endregion

    #region Shields

    /// <summary>
    /// Checks if target has Eukrasian Diagnosis shield.
    /// </summary>
    public static bool HasEukrasianDiagnosisShield(IBattleChara target) =>
        HasStatus(target, SGEActions.EukrasianDiagnosisStatusId);

    /// <summary>
    /// Checks if target has Eukrasian Prognosis shield.
    /// </summary>
    public static bool HasEukrasianPrognosisShield(IBattleChara target) =>
        HasStatus(target, SGEActions.EukrasianPrognosisStatusId);

    /// <summary>
    /// Checks if target has any Eukrasian shield (Diagnosis or Prognosis).
    /// </summary>
    public static bool HasAnyEukrasianShield(IBattleChara target) =>
        HasEukrasianDiagnosisShield(target) || HasEukrasianPrognosisShield(target);

    /// <summary>
    /// Checks if target has Haima buff active.
    /// </summary>
    public static bool HasHaima(IBattleChara target) =>
        HasStatus(target, SGEActions.HaimaStatusId);

    /// <summary>
    /// Gets remaining Haima stacks on target.
    /// </summary>
    public static int GetHaimaStacks(IBattleChara target) =>
        GetStatusStacks(target, SGEActions.HaimaStatusId);

    /// <summary>
    /// Checks if target has Panhaima buff active.
    /// </summary>
    public static bool HasPanhaima(IBattleChara target) =>
        HasStatus(target, SGEActions.PanhaimaStatusId);

    /// <summary>
    /// Gets remaining Panhaima stacks on target.
    /// </summary>
    public static int GetPanhaimaStacks(IBattleChara target) =>
        GetStatusStacks(target, SGEActions.PanhaimaStatusId);

    #endregion

    #region HoTs and Buffs

    /// <summary>
    /// Checks if target has Physis II HoT active.
    /// </summary>
    public static bool HasPhysisII(IBattleChara target) =>
        HasStatus(target, SGEActions.PhysisIIStatusId);

    /// <summary>
    /// Checks if target has Kerachole mitigation/HoT active.
    /// </summary>
    public static bool HasKerachole(IBattleChara target) =>
        HasStatus(target, SGEActions.KeracholeStatusId);

    /// <summary>
    /// Checks if target has Taurochole mitigation active.
    /// Note: Taurochole and Kerachole share the same mitigation buff.
    /// </summary>
    public static bool HasTaurochole(IBattleChara target) =>
        HasStatus(target, SGEActions.KeracholeStatusId);

    /// <summary>
    /// Checks if target has Holos mitigation active.
    /// </summary>
    public static bool HasHolos(IBattleChara target) =>
        HasStatus(target, SGEActions.HolosStatusId);

    /// <summary>
    /// Checks if target has Krasis buff active (increased healing received).
    /// </summary>
    public static bool HasKrasis(IBattleChara target) =>
        HasStatus(target, SGEActions.KrasisStatusId);

    /// <summary>
    /// Maximum heal-threshold reduction applied when a target is under full HoT coverage.
    /// At full coverage the effective heal threshold drops by this much, so a target that is
    /// already being topped off by a regen is treated as less urgent (RSR HoT-aware Lerp parity).
    /// </summary>
    private const float HotThresholdReduction = 0.15f;

    /// <summary>
    /// Full duration (seconds) used to normalise remaining HoT time into a 0..1 coverage ratio.
    /// </summary>
    private const float HotFullDuration = 15f;

    /// <summary>
    /// Returns 0..1 coverage from the strongest active SGE HoT on the target (Kerachole/Taurochole
    /// regen and Physis II), scaled by remaining time. Used to relax heal thresholds when a regen is
    /// already ticking so we don't over-heal/over-shield a target the HoT will recover on its own.
    /// </summary>
    public static float GetHotCoverageRatio(IBattleChara target)
    {
        var best = 0f;

        if (HasStatus(target, SGEActions.KeracholeStatusId, out var kerachole))
            best = System.Math.Max(best, kerachole);
        if (HasStatus(target, SGEActions.PhysisIIStatusId, out var physis))
            best = System.Math.Max(best, physis);

        if (best <= 0f)
            return 0f;

        return System.Math.Min(1f, best / HotFullDuration);
    }

    /// <summary>
    /// Lowers a heal threshold toward a HoT-adjusted floor based on the target's active HoT coverage.
    /// A target with no HoT keeps the full threshold; a fully-covered target heals at up to
    /// <see cref="HotThresholdReduction"/> lower HP. Mirrors RSR's Lerp(healSingle, healSingleHot, ratio).
    /// </summary>
    public static float ApplyHotAwareness(float threshold, IBattleChara target)
    {
        var ratio = GetHotCoverageRatio(target);
        if (ratio <= 0f)
            return threshold;

        var floor = System.Math.Max(0f, threshold - HotThresholdReduction);
        return threshold + ((floor - threshold) * ratio);
    }

    #endregion

    #region DoT

    /// <summary>
    /// Checks if target has any version of Eukrasian Dosis DoT.
    /// </summary>
    public static bool HasEukrasianDosisDoT(IBattleChara target) =>
        HasEukrasianDosisDoT(target, out _);

    /// <summary>
    /// Returns the longest remaining Eukrasian Dosis DoT on the target (any rank).
    /// </summary>
    public static float GetEukrasianDosisRemaining(IBattleChara target)
    {
        var best = 0f;
        if (target.StatusList == null)
            return best;

        foreach (var status in target.StatusList)
        {
            if ((status.StatusId is SGEActions.EukrasianDosisStatusId
                    or SGEActions.EukrasianDosisIIStatusId
                    or SGEActions.EukrasianDosisIIIStatusId)
                && status.RemainingTime > best)
            {
                best = status.RemainingTime;
            }
        }

        return best;
    }

    /// <summary>
    /// Checks if target has any version of Eukrasian Dosis DoT and returns remaining duration.
    /// </summary>
    public static bool HasEukrasianDosisDoT(IBattleChara target, out float remainingTime)
    {
        remainingTime = GetEukrasianDosisRemaining(target);
        return remainingTime > 0f;
    }

    /// <summary>
    /// Checks if target has Eukrasian Dosis DoT and returns remaining duration.
    /// </summary>
    public static bool HasEukrasianDosis(IBattleChara target, out float remainingTime) =>
        HasEukrasianDosisDoT(target, out remainingTime);

    #endregion

    #region Utility

    /// <summary>
    /// Checks if the target has tank stance active (Trust NPC tank detection).
    /// </summary>
    public bool HasTankStance(IGameObject target)
    {
        if (target is not IBattleChara battleChara)
            return false;

        if (battleChara.StatusList == null)
            return false;

        foreach (var status in battleChara.StatusList)
        {
            if (status.StatusId is IronWillStatusId or
                DefianceStatusId or
                GritStatusId or
                RoyalGuardStatusId)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Gets the appropriate Dosis action for the player's level.
    /// </summary>
    public static uint GetDosisForLevel(byte playerLevel, IActionService? actionService = null) =>
        SGEActions.GetDamageGcdForLevel(playerLevel, actionService).ActionId;

    /// <summary>
    /// Gets the appropriate Eukrasian Dosis action for the player's level.
    /// </summary>
    public static uint GetEukrasianDosisForLevel(byte playerLevel, IActionService? actionService = null) =>
        SGEActions.GetDotForLevel(playerLevel, actionService).ActionId;

    /// <summary>
    /// Gets the appropriate Phlegma action for the player's level.
    /// Returns 0 if below level 26.
    /// </summary>
    public static uint GetPhlegmaForLevel(byte playerLevel, IActionService? actionService = null) =>
        SGEActions.GetPhlegmaForLevel(playerLevel, actionService)?.ActionId ?? 0;

    /// <summary>
    /// Gets the appropriate Toxikon action for the player's level.
    /// Returns 0 if below level 66.
    /// </summary>
    public static uint GetToxikonForLevel(byte playerLevel, IActionService? actionService = null) =>
        SGEActions.GetToxikonForLevel(playerLevel, actionService)?.ActionId ?? 0;

    /// <summary>
    /// Gets the appropriate Dyskrasia action for the player's level.
    /// Returns 0 if below level 46.
    /// </summary>
    public static uint GetDyskrasiaForLevel(byte playerLevel, IActionService? actionService = null) =>
        SGEActions.GetAoEDamageGcdForLevel(playerLevel, actionService)?.ActionId ?? 0;

    #endregion
}
