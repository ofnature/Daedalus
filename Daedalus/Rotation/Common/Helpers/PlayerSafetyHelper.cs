using Dalamud.Game.ClientState.Objects.Types;
using Daedalus.Data;

namespace Daedalus.Rotation.Common.Helpers;

/// <summary>
/// Helpers for player-status-driven rotation safety gates.
/// Owns the forced-movement detection path that lets damage modules suppress
/// cast-time GCD execution while the player is under Forward/Backward/Left/Right
/// March or similar involuntary-movement debuffs.
/// </summary>
public static class PlayerSafetyHelper
{
    /// <summary>
    /// Pure predicate over the forced-movement status ID list.
    /// Exposed for unit testing — the full <see cref="IsForcedMovementActive"/>
    /// path cannot be tested because Dalamud's StatusList is a native struct.
    /// </summary>
    public static bool IsForcedMovementStatusId(uint statusId) =>
        FFXIVConstants.ForcedMovementStatusIds.Contains(statusId);

    /// <summary>
    /// True while bound by an instanced duty (dungeon/trial/raid) — the scope for wall-to-wall
    /// ranged-pull tagging so it never fires in the open world. Returns true when the Condition
    /// service is unavailable (e.g. unit tests) so the gated logic remains testable.
    /// </summary>
    public static bool IsInInstancedDuty() =>
        Daedalus.Rotation.Base.RotationServices.Condition?[
            Dalamud.Game.ClientState.Conditions.ConditionFlag.BoundByDuty] ?? true;

    /// <summary>
    /// True when a nearby enemy is casting a look-away/gaze action (<see cref="FFXIVConstants.GazeCastActionIds"/>).
    /// Used to suppress auto-face during the gaze so the bot's casts don't turn the character into it.
    /// No-op (returns false) until the gaze list is populated, so there's zero cost otherwise.
    /// </summary>
    public static bool IsLookAwayMechanicActive(Dalamud.Plugin.Services.IObjectTable? objectTable)
    {
        if (objectTable == null || FFXIVConstants.GazeCastActionIds.Count == 0)
            return false;

        foreach (var obj in objectTable)
        {
            if (obj is not Dalamud.Game.ClientState.Objects.Types.IBattleChara npc)
                continue;
            if (obj.ObjectKind != Dalamud.Game.ClientState.Objects.Enums.ObjectKind.BattleNpc)
                continue;
            if (npc.IsCasting && FFXIVConstants.GazeCastActionIds.Contains(npc.CastActionId))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Returns true if the player has any forced-movement debuff active.
    /// Guards against null player and null StatusList — IBattleChara.StatusList
    /// can be null mid-frame for despawning actors and in unit-test mocks.
    /// </summary>
    public static bool IsForcedMovementActive(IBattleChara? player)
    {
        if (player?.StatusList == null)
            return false;

        foreach (var status in player.StatusList)
        {
            if (status == null)
                continue;
            if (FFXIVConstants.ForcedMovementStatusIds.Contains(status.StatusId))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Pure predicate over the stand-still punisher status ID list (Pyretic family).
    /// </summary>
    public static bool IsStandStillPunisherStatusId(uint statusId) =>
        FFXIVConstants.StandStillPunisherStatusIds.Contains(statusId);

    /// <summary>
    /// Returns true if the player has a Pyretic-style "any action kills you" debuff active.
    /// Used to halt all rotation/healing module execution until the debuff resolves.
    /// </summary>
    public static bool IsStandStillPunisherActive(IBattleChara? player)
    {
        if (player?.StatusList == null)
            return false;

        foreach (var status in player.StatusList)
        {
            if (status == null)
                continue;
            if (FFXIVConstants.StandStillPunisherStatusIds.Contains(status.StatusId))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Pure predicate over the player-intent channel status ID list.
    /// </summary>
    public static bool IsPlayerIntentChannelStatusId(uint statusId) =>
        FFXIVConstants.PlayerIntentChannelStatusIds.Contains(statusId);

    /// <summary>
    /// Returns true if the player has an active channel/stance that would be cancelled
    /// by any other action (Passage of Arms, Flamethrower, Meditate, Collective Unconscious,
    /// Improvisation). Used to halt rotation execution until the player releases it.
    /// </summary>
    public static bool IsPlayerIntentChannelActive(IBattleChara? player)
    {
        if (player?.StatusList == null)
            return false;

        foreach (var status in player.StatusList)
        {
            if (status == null)
                continue;
            if (FFXIVConstants.PlayerIntentChannelStatusIds.Contains(status.StatusId))
                return true;
        }

        return false;
    }
}
