using System;
using System.Collections.Generic;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.ClientState.Party;
using Dalamud.Plugin.Services;
using Daedalus.Data;
using Daedalus.Rotation.AsclepiusCore.Helpers;
using Daedalus.Rotation.Common.Helpers;

namespace Daedalus.Services.Sage;

/// <summary>
/// Manages Sage's Kardia system.
/// Kardia places a buff on a target that heals them when the Sage deals damage.
/// Can be swapped between targets with a 5 second cooldown.
/// </summary>
public sealed class KardiaManager : IKardiaManager
{
    private readonly IPartyList _partyList;
    private readonly IObjectTable _objectTable;
    private DateTime _lastSwapTime = DateTime.MinValue;
    private DateTime _sessionResetTime = DateTime.MinValue;
    private ulong _lastKnownKardiaTarget;
    private uint _lastKnownEntityId;
    // Bearer latch: set once Kardion is confirmed on an ally (usually the tank, but any bearer after
    // a smart swap). Survives Trust status-scan flicker; invalidated when the bearer leaves/dies.
    private bool _tankKardionConfirmed;
    private uint _confirmedTankEntityId;
    private DateTime _bearerAbsentSinceUtc = DateTime.MinValue;

    /// <summary>
    /// Cooldown between Kardia target swaps.
    /// </summary>
    public const float SwapCooldown = 5f;

    /// <summary>
    /// Grace period after a territory change before placing Kardia. Gives the new zone's
    /// party/tank time to spawn so the buff lands on the real tank instead of being wasted
    /// on self (or a stale target) during the zone-in window.
    /// </summary>
    public const float PostZoneWarmupSeconds = 5f;

    /// <summary>
    /// How long the confirmed bearer must be continuously unresolvable (object table + party list)
    /// before the latch is dropped. Absorbs single-frame object-table flicker during transitions
    /// while still catching a bearer that actually left the zone. A DEAD bearer invalidates
    /// immediately — Kardion strips on death.
    /// </summary>
    public const float BearerAbsenceGraceSeconds = 3f;

    /// <summary>
    /// Kardia heal potency per damage action.
    /// </summary>
    public const int KardiaHealPotency = 170;

    /// <summary>
    /// Soteria healing boost percentage per stack.
    /// </summary>
    public const float SoteriaBoostPerStack = 0.70f;

    public KardiaManager(IPartyList partyList, IObjectTable objectTable)
    {
        _partyList = partyList;
        _objectTable = objectTable;
    }

    /// <summary>
    /// Gets the object ID of the current Kardia target.
    /// Returns 0 if no Kardia is placed.
    /// </summary>
    public ulong CurrentKardiaTarget => _lastKnownKardiaTarget;

    /// <summary>
    /// Returns true if Kardion (2605) is on a tracked ally.
    /// </summary>
    public bool HasKardia => _hasKardionPlaced;

    private bool _hasKardionPlaced;

    /// <summary>
    /// Returns true if Kardia swap is off cooldown.
    /// </summary>
    public bool CanSwapKardia => (DateTime.Now - _lastSwapTime).TotalSeconds >= SwapCooldown;

    /// <summary>
    /// Gets the time remaining until Kardia can be swapped.
    /// </summary>
    public float SwapCooldownRemaining
    {
        get
        {
            var elapsed = (float)(DateTime.Now - _lastSwapTime).TotalSeconds;
            return Math.Max(0f, SwapCooldown - elapsed);
        }
    }

    /// <summary>
    /// Updates the known Kardia target from status effects.
    /// Call this each frame to keep track of current Kardia placement.
    /// </summary>
    /// <param name="player">The Sage player character.</param>
    public void UpdateKardiaTarget(IPlayerCharacter player)
    {
        if (player == null)
        {
            _lastKnownKardiaTarget = 0;
            _lastKnownEntityId = 0;
            _hasKardionPlaced = false;
            return;
        }

        // The latch asserts "Kardion is on the confirmed bearer" — verify that bearer still exists
        // BEFORE trusting it. Without this, a tank leaving the zone left a phantom latch that kept
        // reporting placement while dispatch churned over fallback targets (chain-cast bug).
        InvalidateLatchIfBearerGone(player);

        var tank = FindTankAlly(player);
        var allies = EnumerateAllies(player);

        if (_tankKardionConfirmed)
        {
            // Latched bearer placement survives Trust status scan failures and Kardia recast windows.
            _hasKardionPlaced = true;
            if (_lastKnownKardiaTarget == 0 && tank != null)
                RememberBearer(tank.GameObjectId, tank.EntityId);
            return;
        }

        if (AsclepiusStatusHelper.TryFindKardionBearer(
                player,
                _objectTable,
                _partyList,
                allies,
                tank,
                out var detectedId))
        {
            RememberBearer(detectedId);
            TryConfirmTankFromBearer(tank, detectedId);
            return;
        }

        if (AsclepiusStatusHelper.HasKardia(player))
        {
            _hasKardionPlaced = true;
            if (_lastKnownKardiaTarget == 0 && tank != null)
                RememberBearer(tank.GameObjectId, tank.EntityId);
            return;
        }

        if (HasRecentPlacementMemory())
        {
            _hasKardionPlaced = true;
            return;
        }

        if (_lastKnownKardiaTarget != 0)
        {
            if (_objectTable.SearchById(_lastKnownKardiaTarget) is IBattleChara lastTarget
                && AsclepiusStatusHelper.HasKardion(lastTarget, _objectTable, _partyList))
            {
                _hasKardionPlaced = true;
                return;
            }

            if (tank != null
                && MatchesTrackedTarget(tank)
                && AsclepiusStatusHelper.InferKardionOnTank(player, tank, _objectTable, _partyList))
            {
                _hasKardionPlaced = true;
                return;
            }
        }

        if (tank != null
            && AsclepiusStatusHelper.InferKardionOnTank(player, tank, _objectTable, _partyList))
        {
            RememberBearer(tank.GameObjectId, tank.EntityId);
            ConfirmTankKardion(tank);
            return;
        }

        if (!_tankKardionConfirmed)
        {
            _hasKardionPlaced = false;
            _lastKnownKardiaTarget = 0;
            _lastKnownEntityId = 0;
        }
    }

    /// <summary>Clears latched tank state (duty exit, disconnect, etc.).</summary>
    public void ResetSession()
    {
        ClearBearerState();
        _sessionResetTime = DateTime.Now;
    }

    private void ClearBearerState()
    {
        _tankKardionConfirmed = false;
        _confirmedTankEntityId = 0;
        _hasKardionPlaced = false;
        _lastKnownKardiaTarget = 0;
        _lastKnownEntityId = 0;
        _bearerAbsentSinceUtc = DateTime.MinValue;
    }

    /// <summary>
    /// Mid-zone latch invalidation: drops the bearer latch (and the placement memory built on it)
    /// when the confirmed bearer is dead (Kardion strips on death — immediate) or has been
    /// unresolvable for <see cref="BearerAbsenceGraceSeconds"/> (left the zone / despawned; the
    /// grace absorbs object-table flicker). Without this the latch was only cleared on territory
    /// change, so a tank leaving mid-zone left the manager asserting a phantom placement.
    /// </summary>
    private void InvalidateLatchIfBearerGone(IPlayerCharacter player)
    {
        if (!_tankKardionConfirmed || _confirmedTankEntityId == 0)
        {
            _bearerAbsentSinceUtc = DateTime.MinValue;
            return;
        }

        var bearer = ResolveConfirmedBearer(player);
        if (bearer != null)
        {
            if (bearer.CurrentHp > 0)
            {
                _bearerAbsentSinceUtc = DateTime.MinValue;
                return;
            }

            // Dead bearer: the buff is gone with certainty — no grace needed.
            ClearBearerState();
            return;
        }

        if (_bearerAbsentSinceUtc == DateTime.MinValue)
        {
            _bearerAbsentSinceUtc = DateTime.Now;
            return;
        }

        if ((DateTime.Now - _bearerAbsentSinceUtc).TotalSeconds >= BearerAbsenceGraceSeconds)
            ClearBearerState();
    }

    private IBattleChara? ResolveConfirmedBearer(IPlayerCharacter player)
    {
        if (player.EntityId == _confirmedTankEntityId)
            return player;

        if (_partyList.Length > 0)
        {
            foreach (var member in _partyList)
            {
                if (member?.EntityId == _confirmedTankEntityId && member.GameObject is IBattleChara fromParty)
                    return fromParty;
            }
        }

        return _objectTable.SearchByEntityId(_confirmedTankEntityId) as IBattleChara;
    }

    /// <summary>
    /// True during the post-territory-change grace period, when Kardia placement should
    /// wait for the new zone's party/tank to finish loading.
    /// </summary>
    public bool IsPostZoneWarmupActive =>
        (DateTime.Now - _sessionResetTime).TotalSeconds < PostZoneWarmupSeconds;

    private bool HasRecentPlacementMemory() =>
        (_lastKnownKardiaTarget != 0 || _lastKnownEntityId != 0) && SwapCooldownRemaining > 0;

    private void RememberBearer(ulong gameObjectId, uint entityId = 0)
    {
        _lastKnownKardiaTarget = gameObjectId;
        _lastKnownEntityId = entityId != 0 ? entityId : ResolveEntityId(gameObjectId);
        _hasKardionPlaced = true;
    }

    private uint ResolveEntityId(ulong gameObjectId)
    {
        if (gameObjectId == 0)
            return 0;

        if (_objectTable.SearchById(gameObjectId) is IBattleChara fromTable)
            return fromTable.EntityId;

        if (_partyList.Length > 0)
        {
            foreach (var member in _partyList)
            {
                if (member?.GameObject is IBattleChara fromParty
                    && fromParty.GameObjectId == gameObjectId)
                {
                    return fromParty.EntityId;
                }
            }
        }

        return 0;
    }

    private bool MatchesTrackedTarget(IBattleChara target) =>
        (_lastKnownKardiaTarget != 0 && _lastKnownKardiaTarget == target.GameObjectId)
        || (_lastKnownEntityId != 0 && _lastKnownEntityId == target.EntityId);

    /// <summary>
    /// Aligns manager memory with a live Kardion bearer detected outside RecordSwap
    /// (status scan / inference). Does not affect swap cooldown.
    /// </summary>
    public void SyncDetectedBearer(ulong bearerGameObjectId)
    {
        if (bearerGameObjectId == 0)
            return;

        RememberBearer(bearerGameObjectId);
    }

    /// <summary>
    /// Latches recast suppression once Kardion is confirmed on a bearer (the tank in the normal
    /// case, but any ally after a smart swap — the name is historical). Survives brief status-scan
    /// flicker on trust allies between frames; invalidated when the bearer dies or leaves the zone
    /// (see <see cref="InvalidateLatchIfBearerGone"/>).
    /// </summary>
    public void ConfirmTankKardion(IBattleChara tank)
    {
        if (tank == null)
            return;

        _tankKardionConfirmed = true;
        _confirmedTankEntityId = tank.EntityId;
        _bearerAbsentSinceUtc = DateTime.MinValue;
        RememberBearer(tank.GameObjectId, tank.EntityId);
    }

    /// <summary>
    /// Returns true when tank Kardion was confirmed and should suppress recasts on that entity.
    /// </summary>
    public bool IsTankKardionLatched(uint targetEntityId) =>
        _tankKardionConfirmed
        && _confirmedTankEntityId != 0
        && targetEntityId == _confirmedTankEntityId;

    /// <summary>
    /// Action-layer gate: resolves <paramref name="targetGameObjectId"/> and applies recast rules.
    /// </summary>
    public bool ShouldBlockKardiaUse(IPlayerCharacter player, ulong targetGameObjectId)
    {
        if (targetGameObjectId == 0)
            return false;

        if (_objectTable.SearchById(targetGameObjectId) is IBattleChara target)
        {
            var tank = FindTankAlly(player);
            return ShouldBlockKardiaRecast(player, target, _objectTable, _partyList, tank);
        }

        if (_lastKnownKardiaTarget != 0 && _lastKnownKardiaTarget == targetGameObjectId)
            return _tankKardionConfirmed || _hasKardionPlaced || SwapCooldownRemaining > 0;

        return _tankKardionConfirmed;
    }

    /// <summary>
    /// Single gate for redundant Kardia casts — live Kardion, recent swap memory,
    /// Sage self-buff (trust-safe), or a latched tank confirmation from an earlier frame.
    /// </summary>
    public bool ShouldBlockKardiaRecast(
        IPlayerCharacter player,
        IBattleChara target,
        IObjectTable objectTable,
        IPartyList partyList,
        IBattleChara? tank = null)
    {
        if (IsTankKardionLatched(target.EntityId))
            return true;

        if (_tankKardionConfirmed
            && _confirmedTankEntityId != 0
            && target.EntityId == _confirmedTankEntityId)
        {
            return true;
        }

        // Trust allies often omit Kardion (2605) from status lists. The Sage Kardia buff (2604)
        // is always readable — when it is up and we are placing on the tank, do not recast.
        // Inference yields to a tracked bearer: when memory says Kardion sits on someone ELSE
        // (post smart-swap), "must be on the tank" is contradicted and must not block the cast.
        if (tank != null
            && target.EntityId == tank.EntityId
            && (_lastKnownKardiaTarget == 0 || _lastKnownKardiaTarget == target.GameObjectId)
            && AsclepiusStatusHelper.HasKardia(player)
            && AsclepiusStatusHelper.InferKardionOnTank(player, tank, objectTable, partyList))
        {
            ConfirmTankKardion(tank);
            return true;
        }

        if (IsKardionOnTarget(player, target, objectTable, partyList, tank))
        {
            if (tank != null && target.EntityId == tank.EntityId)
                ConfirmTankKardion(tank);

            return true;
        }

        return false;
    }

    /// <summary>
    /// True when Kardion is already on <paramref name="target"/> — live status, recent cast,
    /// or tracked bearer. Used to suppress redundant Kardia presses on the same ally.
    /// </summary>
    public bool IsKardionOnTarget(
        IPlayerCharacter player,
        IBattleChara target,
        IObjectTable objectTable,
        IPartyList partyList,
        IBattleChara? tank = null)
    {
        if (target.EntityId == player.EntityId)
        {
            // Solo (no party, no trust): the only valid Kardia target is the Sage.
            // The Kardia self-buff (2604) is the placement signal, so suppress respam once it is up.
            return IsSolo(player) && AsclepiusStatusHelper.HasKardia(player);
        }

        if (_tankKardionConfirmed
            && _confirmedTankEntityId != 0
            && target.EntityId == _confirmedTankEntityId)
        {
            return true;
        }

        if (MatchesTrackedTarget(target) && (_hasKardionPlaced || SwapCooldownRemaining > 0))
            return true;

        if (AsclepiusStatusHelper.HasKardionFrom(target, player, objectTable, partyList))
            return true;

        if (AsclepiusStatusHelper.HasKardion(target, objectTable, partyList))
            return true;

        if (tank != null
            && target.EntityId == tank.EntityId
            && AsclepiusStatusHelper.TankHasKardion(
                player, tank, objectTable, partyList, _lastKnownKardiaTarget))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Records that a Kardia swap was performed.
    /// </summary>
    /// <param name="newTargetId">The object ID of the new Kardia target.</param>
    public void RecordSwap(ulong newTargetId)
    {
        _lastSwapTime = DateTime.Now;
        RememberBearer(newTargetId);
    }

    /// <summary>
    /// Records that a Kardia swap was performed.
    /// </summary>
    public void RecordSwap(ulong newTargetId, uint entityId)
    {
        _lastSwapTime = DateTime.Now;
        RememberBearer(newTargetId, entityId);
    }

    private void TryConfirmTankFromBearer(IBattleChara? tank, ulong bearerGameObjectId)
    {
        if (tank == null || bearerGameObjectId == 0)
            return;

        if (bearerGameObjectId == tank.GameObjectId
            || (_lastKnownEntityId != 0 && _lastKnownEntityId == tank.EntityId))
        {
            ConfirmTankKardion(tank);
        }
    }

    private void TryClearTankConfirmation(IPlayerCharacter player, IBattleChara? tank)
    {
    }

    /// <summary>
    /// Returns true if we should swap Kardia to a new target.
    /// </summary>
    /// <param name="currentTargetHpPercent">HP percentage of current Kardia target.</param>
    /// <param name="newTargetHpPercent">HP percentage of potential new target.</param>
    /// <param name="swapThreshold">HP threshold below which to consider swapping.</param>
    /// <param name="newTargetIsTank">Whether the new target is a tank (prefer returning Kardia to tank).</param>
    public bool ShouldSwapKardia(float currentTargetHpPercent, float newTargetHpPercent, float swapThreshold, bool newTargetIsTank = false)
    {
        if (!CanSwapKardia)
            return false;

        // Swap TO a non-tank: current target is healthy, new target is urgently low
        if (currentTargetHpPercent > swapThreshold && newTargetHpPercent < swapThreshold)
        {
            // Require a meaningful HP difference to avoid constant swapping
            return currentTargetHpPercent - newTargetHpPercent > 0.15f;
        }

        // Swap BACK to tank: current (non-tank) target is healthy, return Kardia to tank
        if (newTargetIsTank && currentTargetHpPercent > swapThreshold)
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Checks if a party member has Soteria active (from this Sage).
    /// </summary>
    /// <param name="player">The Sage player character.</param>
    public bool HasSoteriaActive(IPlayerCharacter player)
    {
        if (player == null)
            return false;

        foreach (var status in player.StatusList)
        {
            if (status.StatusId == SGEActions.SoteriaStatusId)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Gets the number of Soteria stacks remaining.
    /// </summary>
    /// <param name="player">The Sage player character.</param>
    public int GetSoteriaStacks(IPlayerCharacter player)
    {
        if (player == null)
            return 0;

        foreach (var status in player.StatusList)
        {
            if (status.StatusId == SGEActions.SoteriaStatusId)
                return status.Param;
        }

        return 0;
    }

    /// <summary>
    /// Checks if Philosophia is active (party-wide Kardia effect).
    /// </summary>
    /// <param name="player">The Sage player character.</param>
    public bool HasPhilosophiaActive(IPlayerCharacter player)
    {
        if (player == null)
            return false;

        foreach (var status in player.StatusList)
        {
            if (status.StatusId == SGEActions.PhilosophiaStatusId)
                return true;
        }

        return false;
    }

    /// <summary>
    /// True when the Sage has no party and no trust allies — Kardia should sit on the Sage.
    /// </summary>
    public bool IsSolo(IPlayerCharacter player)
    {
        if (_partyList.Length > 0)
            return false;

        foreach (var _ in EnumerateAllies(player))
            return false;

        return true;
    }

    private IBattleChara? FindTankAlly(IPlayerCharacter player)
    {
        IBattleChara? fallback = null;
        foreach (var ally in EnumerateAllies(player))
        {
            fallback ??= ally;
            if (JobRegistry.IsTank(TrustPartyRoleHelper.ResolveJobId(ally, _partyList)))
                return ally;
        }

        return fallback;
    }

    private IEnumerable<IBattleChara> EnumerateAllies(IPlayerCharacter player)
    {
        if (_partyList.Length > 0)
        {
            foreach (var member in _partyList)
            {
                if (member.EntityId == player.EntityId)
                    continue;

                IBattleChara? fromParty = member.GameObject as IBattleChara;
                if (fromParty != null)
                    yield return fromParty;

                if (_objectTable.SearchByEntityId(member.EntityId) is IBattleChara fromTable
                    && (fromParty == null || fromTable.EntityId != fromParty.EntityId))
                {
                    yield return fromTable;
                }
            }

            yield break;
        }

        foreach (var obj in _objectTable)
        {
            if (BasePartyHelper.IsValidTrustNpc(obj, out var npc))
                yield return npc!;
        }
    }
}

/// <summary>
/// Interface for Kardia management service.
/// </summary>
public interface IKardiaManager
{
    /// <summary>
    /// Gets the object ID of the current Kardia target.
    /// </summary>
    ulong CurrentKardiaTarget { get; }

    /// <summary>
    /// Returns true if Kardia is currently placed on a target.
    /// </summary>
    bool HasKardia { get; }

    /// <summary>
    /// Returns true if Kardia swap is off cooldown.
    /// </summary>
    bool CanSwapKardia { get; }

    /// <summary>
    /// Gets the time remaining until Kardia can be swapped.
    /// </summary>
    float SwapCooldownRemaining { get; }

    /// <summary>
    /// Updates the known Kardia target from status effects.
    /// </summary>
    void UpdateKardiaTarget(IPlayerCharacter player);

    /// <summary>
    /// Records that a Kardia swap was performed.
    /// </summary>
    void RecordSwap(ulong newTargetId);

    /// <summary>
    /// Records that a Kardia swap was performed with a stable entity id.
    /// </summary>
    void RecordSwap(ulong newTargetId, uint entityId);

    /// <summary>
    /// Aligns manager memory with a live Kardion bearer from status scan.
    /// </summary>
    void SyncDetectedBearer(ulong bearerGameObjectId);

    /// <summary>
    /// Latches tank Kardion placement so OOC casts stay suppressed across status flicker.
    /// </summary>
    void ConfirmTankKardion(IBattleChara tank);

    /// <summary>
    /// True when a prior tank Kardion confirmation is latched for this entity.
    /// </summary>
    bool IsTankKardionLatched(uint targetEntityId);

    /// <summary>Clears latched tank state (duty exit, disconnect, etc.).</summary>
    void ResetSession();

    /// <summary>
    /// True during the post-territory-change grace period, when Kardia placement should
    /// wait for the new zone's party/tank to finish loading.
    /// </summary>
    bool IsPostZoneWarmupActive { get; }

    /// <summary>
    /// Action-layer gate for a target game object id.
    /// </summary>
    bool ShouldBlockKardiaUse(IPlayerCharacter player, ulong targetGameObjectId);

    /// <summary>
    /// Returns true when a Kardia cast on <paramref name="target"/> should be suppressed.
    /// </summary>
    bool ShouldBlockKardiaRecast(
        IPlayerCharacter player,
        IBattleChara target,
        IObjectTable objectTable,
        IPartyList partyList,
        IBattleChara? tank = null);

    /// <summary>
    /// True when Kardion is already on the target (status, inference, or recent cast memory).
    /// </summary>
    bool IsKardionOnTarget(
        IPlayerCharacter player,
        IBattleChara target,
        IObjectTable objectTable,
        IPartyList partyList,
        IBattleChara? tank = null);

    /// <summary>
    /// Returns true if we should swap Kardia to a new target.
    /// </summary>
    bool ShouldSwapKardia(float currentTargetHpPercent, float newTargetHpPercent, float swapThreshold, bool newTargetIsTank = false);

    /// <summary>
    /// Checks if Soteria is active.
    /// </summary>
    bool HasSoteriaActive(IPlayerCharacter player);

    /// <summary>
    /// Gets the number of Soteria stacks remaining.
    /// </summary>
    int GetSoteriaStacks(IPlayerCharacter player);

    /// <summary>
    /// Checks if Philosophia is active.
    /// </summary>
    bool HasPhilosophiaActive(IPlayerCharacter player);
}
