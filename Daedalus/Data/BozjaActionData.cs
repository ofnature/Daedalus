using System.Collections.Generic;

namespace Daedalus.Data;

/// <summary>What a Lost Action is for, which decides how the Bozja layer uses it.</summary>
public enum LostRole : byte
{
    /// <summary>Raise a dead party member.</summary>
    Raise,
    /// <summary>Heal one party member.</summary>
    SingleHeal,
    /// <summary>Heal everyone near a target.</summary>
    AreaHeal,
    /// <summary>Heal everyone near us (Lost Full Cure).</summary>
    SelfAreaHeal,
    /// <summary>Barrier on the party member an enemy is casting at.</summary>
    Barrier,
    /// <summary>Barrier on everyone near us, ahead of an AoE.</summary>
    AreaBarrier,
    /// <summary>Convert a party member's damage to the type the enemy is averse to.</summary>
    Forge,
    /// <summary>Lost Burst / Lost Rampage: AoE around us.</summary>
    AversionAoe,
    /// <summary>A long buff kept on ourselves and the party.</summary>
    PartyBuff,
    /// <summary>Lost Flare Star: AoE around us that leaves a DoT.</summary>
    AoeDot,
    /// <summary>Lost Seraph Strike: leap onto the target.</summary>
    GapCloser,
    /// <summary>Lost Slash: cone in front of us.</summary>
    Cone,
    /// <summary>A combat buff on ourselves, used on cooldown.</summary>
    SelfBuff,
    /// <summary>Lost Focus: a self buff on the GCD, used only when the job leaves the GCD free.</summary>
    FreeGcdSelfBuff,
}

/// <summary>
/// One Lost Action. <see cref="Provides"/> are the statuses that make a use pointless (RSR's
/// <c>StatusProvide</c> / <c>TargetStatusProvide</c>): on ourselves for self buffs, on the target for
/// party buffs and forges, on the enemy for Lost Flare Star and non-spam Burst/Rampage.
/// </summary>
public sealed record LostActionDef(
    string Name,
    uint ActionId,
    LostRole Role,
    bool IsGcd,
    float CastTime,
    float RecastTime,
    float Range,
    float Radius,
    uint[] Provides,
    float RefreshOutOfCombatSeconds = 0f,
    bool DefaultEnabled = true);

/// <summary>
/// Bozja Lost Actions (Southern Front, Zadnor and their duties) — the ones RSR's "Bozja Reborn" rotation
/// uses. Action ids come from the MYCTemporaryItem sheet, everything else from the Action sheet
/// (XIVAPI, 2026-09-25); status ids from the Status sheet, matching RSR's.
/// <para>
/// <see cref="LostActionDef.IsGcd"/> follows the recast group, NOT the action category: Lost Focus is
/// an "Ability" that shares the GCD (group 58), and Lost Slash is a "Weaponskill" on its own 90s timer.
/// </para>
/// <para>The list is in priority order — lower index wins when two compete for the same slot.</para>
/// </summary>
public static class BozjaActionData
{
    // Party barriers
    public const uint LostProtectStatusId = 2333;
    public const uint LostShellStatusId = 2334;
    public const uint LostProtectIIStatusId = 2561;
    public const uint LostShellIIStatusId = 2562;
    public const uint LostBraveryStatusId = 2341;
    public const uint LostBubbleStatusId = 2563;
    public const uint StoneskinStatusId = 151;

    // Forges and the enemy aversions they answer ("Vulnerable to physical/magic attacks" — on ENEMIES)
    public const uint LostSpellforgeStatusId = 2338;
    public const uint LostSteelstingStatusId = 2339;
    public const uint PhysicalAversionStatusId = 2369;
    public const uint MagicalAversionStatusId = 2370;

    // Enemy debuffs
    public const uint LostBurstStatusId = 2558;
    public const uint LostRampageStatusId = 2559;
    public const uint LostFlareStarStatusId = 2440;

    // Self buffs
    public const uint BoostStatusId = 1656;
    public const uint LostFontOfPowerStatusId = 2346;
    public const uint LostFontOfMagicStatusId = 2332;
    public const uint BannerOfHonoredSacrificeStatusId = 2327;
    public const uint BannerOfHonedAcuityStatusId = 2331;
    public const uint BannerOfSolemnClarityStatusId = 2330;
    public const uint ClericStanceStatusId = 2484;
    public const uint LostChainspellStatusId = 2560;

    /// <summary>RSR's SwiftcastStatus list: Lost Chainspell stands down while any of these is up.</summary>
    public static readonly uint[] InstantCastStatusIds = [167, 1211, 1249, 5438, 4260, LostChainspellStatusId];

    private const float ThirtyMinutesRefresh = 300f; // renew with under 5 min left out of combat
    private const float TenMinutesRefresh = 120f;

    public static readonly IReadOnlyList<LostActionDef> All =
    [
        // Raises. Lost Sacrifice KOs the caster 10s later, so it is off unless chosen.
        new("Lost Arise", 20730, LostRole.Raise, IsGcd: true, 3.0f, 2.5f, 30f, 0f, []),
        new("Lost Sacrifice", 22345, LostRole.Raise, IsGcd: true, 3.0f, 2.5f, 30f, 0f, [], DefaultEnabled: false),

        // Heals: oGCDs first, so a GCD is only spent when no weave heal is available.
        new("Lost Full Cure", 23920, LostRole.SelfAreaHeal, IsGcd: false, 0f, 180f, 0f, 15f, []),
        new("Lost Cure IV", 20729, LostRole.AreaHeal, IsGcd: false, 0f, 5f, 30f, 15f, []),
        new("Lost Cure II", 20727, LostRole.SingleHeal, IsGcd: false, 0f, 5f, 30f, 0f, []),
        new("Lost Cure III", 20728, LostRole.AreaHeal, IsGcd: true, 2.0f, 2.5f, 30f, 15f, []),
        new("Lost Cure", 20726, LostRole.SingleHeal, IsGcd: true, 2.0f, 2.5f, 30f, 0f, []),

        // Barriers
        new("Lost Stoneskin II", 23908, LostRole.AreaBarrier, IsGcd: true, 3.0f, 2.5f, 0f, 15f, [StoneskinStatusId]),
        new("Lost Stoneskin", 20712, LostRole.Barrier, IsGcd: true, 2.0f, 2.5f, 30f, 0f, [StoneskinStatusId]),

        // RSR's GeneralGCD order from here.
        new("Lost Spellforge", 20706, LostRole.Forge, IsGcd: true, 2.5f, 2.5f, 30f, 0f,
            [LostSpellforgeStatusId, LostSteelstingStatusId]),
        new("Lost Steelsting", 20707, LostRole.Forge, IsGcd: true, 2.5f, 2.5f, 30f, 0f,
            [LostSpellforgeStatusId, LostSteelstingStatusId]),
        new("Lost Burst", 23909, LostRole.AversionAoe, IsGcd: true, 0f, 2.5f, 0f, 10f, [LostBurstStatusId]),
        new("Lost Rampage", 23910, LostRole.AversionAoe, IsGcd: true, 0f, 2.5f, 0f, 10f, [LostRampageStatusId]),
        new("Lost Bravery", 20713, LostRole.PartyBuff, IsGcd: true, 2.5f, 2.5f, 30f, 0f,
            [LostBraveryStatusId], TenMinutesRefresh),
        new("Lost Bubble", 23917, LostRole.PartyBuff, IsGcd: true, 2.5f, 2.5f, 30f, 0f,
            [LostBubbleStatusId], TenMinutesRefresh),
        // Tier II stands down only for itself (so it upgrades a tier I); tier I for either tier.
        new("Lost Shell II", 23916, LostRole.PartyBuff, IsGcd: true, 2.5f, 2.5f, 30f, 0f,
            [LostShellIIStatusId], ThirtyMinutesRefresh),
        new("Lost Shell", 20710, LostRole.PartyBuff, IsGcd: true, 2.5f, 2.5f, 30f, 0f,
            [LostShellStatusId, LostShellIIStatusId], ThirtyMinutesRefresh),
        new("Lost Protect II", 23915, LostRole.PartyBuff, IsGcd: true, 2.5f, 2.5f, 30f, 0f,
            [LostProtectIIStatusId], ThirtyMinutesRefresh),
        new("Lost Protect", 20709, LostRole.PartyBuff, IsGcd: true, 2.5f, 2.5f, 30f, 0f,
            [LostProtectStatusId, LostProtectIIStatusId], ThirtyMinutesRefresh),
        new("Lost Flare Star", 22352, LostRole.AoeDot, IsGcd: true, 0f, 5f, 0f, 10f, [LostFlareStarStatusId]),

        // oGCD attacks
        new("Lost Seraph Strike", 22354, LostRole.GapCloser, IsGcd: false, 0f, 60f, 20f, 10f, [ClericStanceStatusId]),
        new("Lost Slash", 20718, LostRole.Cone, IsGcd: false, 0f, 90f, 8f, 8f, []),

        // RSR's GeneralAbility order: combat self buffs.
        new("Banner of Honed Acuity", 20725, LostRole.SelfBuff, IsGcd: false, 0f, 180f, 0f, 0f, [BannerOfHonedAcuityStatusId]),
        new("Banner of Honored Sacrifice", 20721, LostRole.SelfBuff, IsGcd: false, 0f, 90f, 0f, 0f,
            [BannerOfHonoredSacrificeStatusId]),
        new("Lost Font of Power", 20717, LostRole.SelfBuff, IsGcd: false, 0f, 120f, 0f, 0f, [LostFontOfPowerStatusId]),
        new("Lost Font of Magic", 20715, LostRole.SelfBuff, IsGcd: false, 0f, 120f, 0f, 0f, [LostFontOfMagicStatusId]),
        new("Lost Chainspell", 23913, LostRole.SelfBuff, IsGcd: false, 0f, 90f, 0f, 0f, InstantCastStatusIds),
        // Ends the moment you act or move, so used mid-rotation it is gone at once. Off unless chosen.
        new("Banner of Solemn Clarity", 20724, LostRole.SelfBuff, IsGcd: false, 0f, 180f, 0f, 0f,
            [BannerOfSolemnClarityStatusId], DefaultEnabled: false),
        new("Lost Focus", 20714, LostRole.FreeGcdSelfBuff, IsGcd: true, 0f, 2.5f, 0f, 0f, [BoostStatusId]),
    ];

    /// <summary>The definition for an action id, or null for anything that is not one of these.</summary>
    public static LostActionDef? Find(uint actionId)
    {
        foreach (var def in All)
        {
            if (def.ActionId == actionId)
                return def;
        }

        return null;
    }
}
