using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.ClientState.Party;
using Dalamud.Plugin.Services;
using Daedalus.Rotation.Common.Helpers;

namespace Daedalus.Services.Targeting;

/// <summary>
/// Decides whether a hostile is eligible for auto-targeting when the personal InCombat flag
/// is missing — common in alliance raids where another party tagged the mob first.
/// </summary>
internal static class EnemyEngagementPolicy
{
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
    /// The user has explicitly asked for hostiles that carry no combat flag at all to be fair game.
    /// Opt-in, default off, and blanket: someone who turns this on wants the unpulled ones too.
    /// </summary>
    internal static bool AllowsUnclaimedHostiles(Configuration configuration)
        => configuration.Targeting.IncludeHostilesWithoutPersonalCombatFlag;

    /// <summary>
    /// An ally is fighting, so this character should be fighting too — but that says nothing about
    /// WHICH enemies are fair game, which is why it is kept separate from
    /// <see cref="AllowsUnclaimedHostiles"/>. See <see cref="ShouldIncludeEnemyForTargeting"/>.
    /// </summary>
    internal static bool IsAnyAllyInCombat(
        Configuration configuration,
        IPlayerCharacter player,
        IPartyList partyList,
        IObjectTable objectTable)
        => configuration.EnableOnPartyInCombat
           && PartyCombatHelper.IsAnyGroupMemberInCombat(player, partyList, objectTable);

    /// <summary>
    /// Evidence that this enemy is already IN the fight, for when its own InCombat flag has not
    /// arrived yet. Either something is holding its attention, or something has already hit it.
    /// A mob standing at full health with no target is not in the fight — it is the next pack.
    /// </summary>
    internal static bool HasEngagementEvidence(IBattleNpc enemy)
        => enemy.TargetObjectId != 0 || enemy.CurrentHp < enemy.MaxHp;

    /// <summary>
    /// Is this enemy part of the fight we are already in? Drives AoE counts, pack time-to-kill, and
    /// the nearest-enemy pick, so a "yes" here for an untouched mob both inflates the AoE thresholds
    /// that decide whether to fire an AoE at all and offers that mob up as a target.
    ///
    /// <para>
    /// There used to be a final fallback: our own InCombat flag plus the enemy's Hostile flag. But
    /// Hostile is true of every aggressive mob in the zone whether or not anybody has touched it, so
    /// from the moment the pull began the next pack qualified too (field 2026-09-17). The four tests
    /// below already accept everything genuinely in the fight.
    /// </para>
    /// </summary>
    /// <param name="playerGameObjectId">Used to spot a mob that is coming for us specifically.</param>
    internal static bool IsEnemyInTheFight(IBattleNpc enemy, ulong playerGameObjectId)
    {
        if ((enemy.StatusFlags & StatusFlags.InCombat) != 0)
            return true;

        // Coming for us, even if nothing has landed yet. The id guard matters: an unset
        // TargetObjectId is 0, so without it a mob targeting nobody "matches" a caller that
        // passed no id, and every untouched mob in the zone is suddenly in the fight.
        if (playerGameObjectId != 0 && enemy.TargetObjectId == playerGameObjectId)
            return true;

        var hostile = (enemy.StatusFlags & StatusFlags.Hostile) != 0;

        // Holding someone's attention, or already wounded: somebody is fighting it.
        return hostile && HasEngagementEvidence(enemy);
    }

    /// <summary>
    /// True when an enemy should be considered for aggregate strategies, AoE counts, and combat retarget.
    ///
    /// <para>
    /// Field 2026-09-17: casters pulled random mobs while the tank was fighting. An ally in combat
    /// made this return true for EVERY nearby hostile, pulled or not, because "my party is fighting"
    /// was treated as the same permission as "this mob is fair game". The nearest hostile to a
    /// backline caster is very often a pack nobody has touched, so it got hit, and then it came.
    /// </para>
    ///
    /// <para>
    /// An ally being in combat now only waives the enemy's missing <c>InCombat</c> FLAG, and still
    /// requires <see cref="HasEngagementEvidence"/> — a target or a wound. Only the explicit
    /// <paramref name="allowsUnclaimedHostiles"/> opt-in waives the requirement itself.
    /// </para>
    /// </summary>
    internal static bool ShouldIncludeEnemyForTargeting(
        IBattleNpc enemy,
        ulong currentTargetId,
        bool playerEffectivelyInCombat,
        bool allowsUnclaimedHostiles,
        bool anyAllyInCombat)
    {
        if ((enemy.StatusFlags & StatusFlags.InCombat) != 0)
            return true;

        // The user's own hard target is always deliberate, pulled or not.
        if (currentTargetId != 0 && enemy.GameObjectId == currentTargetId)
            return true;

        if (allowsUnclaimedHostiles)
            return true;

        if (!playerEffectivelyInCombat)
            return false;

        return anyAllyInCombat && HasEngagementEvidence(enemy);
    }
}
