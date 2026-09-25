using System;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.ClientState.Party;
using Dalamud.Plugin.Services;
using Daedalus.Rotation.Common.Helpers;

namespace Daedalus.Services.Targeting;

/// <summary>
/// Decides which hostiles are part of OUR fight, as opposed to merely being in a fight.
///
/// <para>
/// In a dungeon the two are the same thing — every mob nearby is fighting your party or nobody. In
/// open-world content they are not: Bozja's Southern Front (reported 2026-09-25) is full of mobs other
/// players are fighting, and "it has a target" or "it is wounded" is true of all of them. A PCT set to
/// Lowest HP then picked whichever stranger's mob was nearly dead, or pulled an untouched one, instead
/// of helping its own party. So the test is not "is anybody fighting it" but "is it fighting US" —
/// our party, our alliance when opted in, a Trust ally, or a pet one of them owns.
/// </para>
/// </summary>
internal static class EnemyEngagementPolicy
{
    /// <summary>The object id the game uses for "no target".</summary>
    private const ulong NoTarget = 0xE0000000;

    internal static bool IsPlayerEffectivelyInCombat(
        IPlayerCharacter player,
        Configuration configuration,
        IPartyList partyList,
        IObjectTable objectTable)
    {
        if ((player.StatusFlags & StatusFlags.InCombat) != 0)
            return true;

        if (configuration.EnableOnPartyInCombat
            && PartyCombatHelper.IsAnyGroupMemberInCombat(player, partyList, objectTable))
            return true;

        return false;
    }

    /// <summary>
    /// Whether mobs fighting the rest of the ALLIANCE count as ours, not just mobs fighting our own
    /// party. This is what "include hostiles without a combat flag" was added for: in alliance raids
    /// another party often tags a mob first, and it carries no combat flag for our toons until one of
    /// them hits it.
    /// <para>
    /// It used to be a blanket "every hostile counts", which is exactly what made Bozja go wrong — a
    /// stranger is not in your alliance, but the blanket admitted their mobs anyway. Scoped to the
    /// alliance it covers the raid case and nothing else.
    /// </para>
    /// </summary>
    internal static bool IncludesAlliance(Configuration configuration)
        => configuration.Targeting.IncludeHostilesWithoutPersonalCombatFlag;

    /// <summary>
    /// Is this object on our side? Us, a party member, an alliance member (only when
    /// <paramref name="includeAlliance"/>), a Trust / duty-support ally, or a pet or companion owned by
    /// one of those.
    /// <para>
    /// Players are judged by their party/alliance status flags. Trust allies are NOT — memory of
    /// 2026-07-03: status flags lie on Trust avatars, and the party list is empty in Trust content —
    /// so they go through <see cref="BasePartyHelper.IsValidTrustNpc"/>, the codebase's one reliable
    /// test. Pets are judged by their owner, one hop only.
    /// </para>
    /// </summary>
    internal static bool IsOnOurSide(
        IGameObject? obj,
        ulong localPlayerId,
        bool includeAlliance,
        Func<uint, IGameObject?> byEntityId,
        bool isOwnerLookup = false)
    {
        if (obj is null)
            return false;

        if (localPlayerId != 0 && obj.GameObjectId == localPlayerId)
            return true;

        if (obj is IPlayerCharacter pc)
        {
            var ours = includeAlliance
                ? StatusFlags.PartyMember | StatusFlags.AllianceMember
                : StatusFlags.PartyMember;
            return (pc.StatusFlags & ours) != 0;
        }

        if (obj is IBattleNpc npc)
        {
            if (BasePartyHelper.IsValidTrustNpc(npc, out _))
                return true;

            // A mob chewing on a carbuncle, fairy or chocobo is fighting whoever owns it.
            if (!isOwnerLookup && npc.OwnerId is not (0 or (uint)NoTarget)
                && byEntityId(npc.OwnerId) is { } owner)
                return IsOnOurSide(owner, localPlayerId, includeAlliance, byEntityId, isOwnerLookup: true);
        }

        return false;
    }

    /// <summary>
    /// The "is this id on our side?" question as one function, for production and tests alike. Our own id
    /// is recognised before any lookup: whether we are on our own side must never depend on the object
    /// table having us in it on this particular frame.
    /// </summary>
    internal static Func<ulong, bool> OurSideResolver(
        ulong localPlayerId,
        bool includeAlliance,
        Func<ulong, IGameObject?> byId,
        Func<uint, IGameObject?> byEntityId)
        => id => (localPlayerId != 0 && id == localPlayerId)
                 || IsOnOurSide(byId(id), localPlayerId, includeAlliance, byEntityId);

    /// <summary>The enemy's current target is on our side — it is fighting us, not someone else.</summary>
    internal static bool IsFightingOurSide(IBattleNpc enemy, Func<ulong, bool> isOurSide)
        => enemy.TargetObjectId is not (0 or NoTarget) && isOurSide(enemy.TargetObjectId);

    /// <summary>
    /// Is this enemy part of the fight we are already in? Drives AoE counts, pack time-to-kill, and
    /// the nearest-enemy pick, so a "yes" here for a mob that isn't ours both inflates the AoE
    /// thresholds and offers that mob up as a target — an AoE fired on the strength of it clips the
    /// mob and pulls it.
    /// <para>
    /// "Holding a target" and "wounded" used to count on their own. In open-world content both are
    /// true of every mob a stranger is fighting, so they no longer count unless the target is ours.
    /// </para>
    /// </summary>
    internal static bool IsEnemyInTheFight(IBattleNpc enemy, Func<ulong, bool> isOurSide)
    {
        if ((enemy.StatusFlags & StatusFlags.InCombat) != 0)
            return true;

        return IsFightingOurSide(enemy, isOurSide);
    }

    /// <summary>
    /// True when an enemy should be considered for aggregate strategies, AoE counts, and combat retarget.
    /// <para>
    /// Only while we (or our party) are effectively fighting — the documented contract of the alliance
    /// setting, which a 2026-09-17 rewrite broke by applying it out of combat too.
    /// </para>
    /// </summary>
    internal static bool ShouldIncludeEnemyForTargeting(
        IBattleNpc enemy,
        ulong currentTargetId,
        bool playerEffectivelyInCombat,
        Func<ulong, bool> isOurSide)
    {
        if ((enemy.StatusFlags & StatusFlags.InCombat) != 0)
            return true;

        // The user's own hard target is always deliberate, pulled or not.
        if (currentTargetId != 0 && enemy.GameObjectId == currentTargetId)
            return true;

        if (!playerEffectivelyInCombat)
            return false;

        return IsFightingOurSide(enemy, isOurSide);
    }
}
