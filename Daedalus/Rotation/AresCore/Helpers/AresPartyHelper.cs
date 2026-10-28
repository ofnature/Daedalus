using System;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.ClientState.Party;
using Dalamud.Plugin.Services;
using Daedalus.Data;
using Daedalus.Rotation.Common.Helpers;

namespace Daedalus.Rotation.AresCore.Helpers;

/// <summary>
/// Warrior party helper with WAR-specific targeting logic.
/// Extends BasePartyHelper with Nascent Flash and co-tank targeting.
/// </summary>
public sealed class AresPartyHelper : BasePartyHelper
{
    public AresPartyHelper(IObjectTable objectTable, IPartyList partyList)
        : base(objectTable, partyList)
    {
    }

    /// <summary>
    /// Finds the co-tank in the party (another tank that isn't the player).
    /// Returns null if solo or no co-tank.
    /// </summary>
    public IBattleChara? FindCoTank(IPlayerCharacter player)
    {
        foreach (var member in PartyList)
        {
            if (member.EntityId == player.GameObjectId)
                continue;

            var partyMember = ObjectTable.SearchById(member.EntityId) as IBattleChara;
            if (partyMember != null && JobRegistry.IsTank(partyMember.ClassJob.RowId))
                return partyMember;
        }
        return null;
    }

    /// <summary>
    /// Gets the HP percentage of a character.
    /// </summary>
    public float GetHpPercent(IBattleChara character)
    {
        return GetRawHpPercent(character);
    }

    /// <summary>
    /// HP Recovery Down. RSR's NoNeedHealingStatus counts it as a heal block; the shared
    /// <see cref="HealerPartyHelper.IsNoHealStatusId"/> list doesn't have it.
    /// </summary>
    private const uint HpRecoveryDownStatusId = 2852;

    /// <summary>
    /// Finds a party member that could benefit from Nascent Flash.
    /// Returns null if no one needs protection.
    /// </summary>
    /// <param name="player">The player character.</param>
    /// <param name="hpThreshold">HP threshold below which to consider using Nascent Flash.</param>
    /// <param name="gcdDuration">Current GCD length; an invuln or heal block ending within two of these doesn't skip the member.</param>
    /// <param name="noHealStatusRemaining">Test seam: seconds left on the member's longest invuln / heal block (0 if none).</param>
    public IBattleChara? FindNascentFlashTarget(
        IPlayerCharacter player,
        float hpThreshold = 0.60f,
        float gcdDuration = 2.5f,
        Func<IBattleChara, float>? noHealStatusRemaining = null)
    {
        IBattleChara? lowestMember = null;
        float lowestHp = 1f;
        noHealStatusRemaining ??= GetNoHealStatusRemaining;

        foreach (var member in GetAllPartyMembers(player))
        {
            if (member.GameObjectId == player.GameObjectId)
                continue;

            // RSR (StatusHelper.CanBeHealed): skip a member in a tank invuln or under a heal block that lasts
            // beyond two GCDs. Their low HP isn't the danger it looks like (Superbolide sits at 1 HP), so the
            // lowest-HP pick would waste Nascent Flash on them. Walking Dead is not skipped: that DRK needs healing.
            if (noHealStatusRemaining(member) > 2f * gcdDuration)
                continue;

            var hp = GetHpPercent(member);
            if (hp < hpThreshold && hp < lowestHp)
            {
                lowestHp = hp;
                lowestMember = member;
            }
        }

        return lowestMember;
    }

    /// <summary>
    /// True for the statuses that keep Nascent Flash off a member: the shared no-heal list (tank invulns,
    /// delayed heals already covering them) plus HP Recovery Down.
    /// </summary>
    internal static bool IsNascentFlashBlockingStatus(uint statusId)
        => HealerPartyHelper.IsNoHealStatusId(statusId) || statusId == HpRecoveryDownStatusId;

    private static float GetNoHealStatusRemaining(IBattleChara member)
    {
        if (member.StatusList == null)
            return 0f;

        var longest = 0f;
        foreach (var status in member.StatusList)
        {
            if (IsNascentFlashBlockingStatus(status.StatusId) && status.RemainingTime > longest)
                longest = status.RemainingTime;
        }
        return longest;
    }

    /// <summary>
    /// Counts how many party members are injured (below threshold).
    /// </summary>
    /// <param name="player">The player character.</param>
    /// <param name="threshold">HP threshold to consider injured.</param>
    public int CountInjuredMembers(IPlayerCharacter player, float threshold = 0.80f)
    {
        var count = 0;
        foreach (var member in GetAllPartyMembers(player))
        {
            if (GetHpPercent(member) < threshold)
                count++;
        }
        return count;
    }
}
