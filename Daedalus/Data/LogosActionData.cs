using System.Collections.Generic;

namespace Daedalus.Data;

/// <summary>
/// Eureka Logos Actions (Pyros, Hydatos, the Baldesion Arsenal) that the duty-action layer uses, in priority order.
/// Ids, casts, recasts and ranges from the Action / EurekaMagiaAction sheets, statuses from the Status sheet
/// (docs/eureka-research-data.md, 2026-11-01). Like the Lost Actions, they sit in the two duty action slots, so
/// only what is slotted is ever used.
/// </summary>
public static class LogosActionData
{
    public const uint ProtectLStatusId = 1642;
    public const uint ShellLStatusId = 1643;
    public const uint BraveryLStatusId = 1646;
    public const uint BloodbathLStatusId = 1677;
    public const uint SpiritOfTheRememberedStatusId = 1641;

    /// <summary>Evasion Down: what Feint L puts on the enemy (BMR's Eureka module checks the same).</summary>
    public const uint EvasionDownStatusId = 32;

    /// <summary>
    /// Every Wisdom's status (1631-1640 from 12958-12967, 1739-1742 from 14477-14480). Only one Wisdom can be up
    /// at a time, so each stands down for any of them: with two slotted they would otherwise take turns
    /// replacing each other and burn their three uses.
    /// </summary>
    public static readonly uint[] WisdomStatusIds =
        [1631, 1632, 1633, 1634, 1635, 1636, 1637, 1638, 1639, 1640, 1739, 1740, 1741, 1742];

    private const float ThirtyMinutesRefresh = 300f; // renew with under 5 min left out of combat
    private const float FiveMinutesRefresh = 60f;

    private static LostActionDef Wisdom(string name, uint actionId)
        => new(name, actionId, LostRole.Stance, IsGcd: false, 0f, 10f, 0f, 0f, WisdomStatusIds);

    public static readonly IReadOnlyList<LostActionDef> All =
    [
        // Raises. Sacrifice L has only ten uses; off unless chosen.
        new("Raise L", 12996, LostRole.Raise, IsGcd: true, 3.0f, 2.5f, 30f, 0f, []),
        new("Sacrifice L", 14481, LostRole.Raise, IsGcd: true, 3.0f, 2.5f, 30f, 0f, [], DefaultEnabled: false),

        // Heals: the weave first, so a GCD is only spent when it is not ready.
        new("Cure L II", 12990, LostRole.SingleHeal, IsGcd: false, 0f, 5f, 30f, 0f, []),
        new("Cure L III", 12992, LostRole.AreaHeal, IsGcd: true, 2.0f, 2.5f, 30f, 15f, []),
        new("Cure L", 12989, LostRole.SingleHeal, IsGcd: true, 2.0f, 2.5f, 30f, 0f, []),

        // Stances: kept up whenever missing. Spirit of the Remembered has one use, the Wisdoms three.
        new("Spirit of the Remembered", 12968, LostRole.Stance, IsGcd: false, 0f, 10f, 0f, 0f, [SpiritOfTheRememberedStatusId]),
        Wisdom("Wisdom of the Aetherweaver", 12958),
        Wisdom("Wisdom of the Martialist", 12959),
        Wisdom("Wisdom of the Platebearer", 12960),
        Wisdom("Wisdom of the Guardian", 12961),
        Wisdom("Wisdom of the Ordained", 12962),
        Wisdom("Wisdom of the Skirmisher", 12963),
        Wisdom("Wisdom of the Watcher", 12964),
        Wisdom("Wisdom of the Templar", 12965),
        Wisdom("Wisdom of the Irregular", 12966),
        Wisdom("Wisdom of the Breathtaker", 12967),
        Wisdom("Wisdom of the Elder", 14477),
        Wisdom("Wisdom of the Duelist", 14478),
        Wisdom("Wisdom of the Fiendhunter", 14479),
        Wisdom("Wisdom of the Indomitable", 14480),

        // Party buffs (30 min, 30 min, 5 min).
        new("Protect L", 12969, LostRole.PartyBuff, IsGcd: true, 2.5f, 2.5f, 30f, 0f, [ProtectLStatusId], ThirtyMinutesRefresh),
        new("Shell L", 12970, LostRole.PartyBuff, IsGcd: true, 2.5f, 2.5f, 30f, 0f, [ShellLStatusId], ThirtyMinutesRefresh),
        new("Bravery L", 12997, LostRole.PartyBuff, IsGcd: true, 2.5f, 2.5f, 30f, 0f, [BraveryLStatusId], FiveMinutesRefresh),

        // Combat.
        new("Feint L", 12980, LostRole.EvasionDown, IsGcd: true, 0f, 2.5f, 3f, 0f, [EvasionDownStatusId]),
        new("Bloodbath L", 12985, LostRole.LowHpSelf, IsGcd: false, 0f, 60f, 0f, 0f, [BloodbathLStatusId]),
    ];
}
