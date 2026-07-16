using System;
using Daedalus.Config.DPS;
using Daedalus.Data;

namespace Daedalus.Services.Blu;

/// <summary>
/// Additive capability bitfield a BLU toon advertises in its LAN heartbeat (payload field
/// <c>cap</c>): which coordination-relevant spells are learned AND slotted right now, plus the
/// configured role hints elections need. Old clients simply read 0 (no capabilities) — every
/// consumer treats missing bits as "cannot be assigned", so the field is fully back-compatible.
/// Bit positions are WIRE FORMAT — append new bits, never reorder.
/// </summary>
[Flags]
public enum BluCapabilities : uint
{
    None = 0,

    /// <summary>Song of Torment slotted — bleed-family DoT (shared status 1714).</summary>
    SongOfTorment = 1u << 0,

    /// <summary>Mortal Flame slotted — the infinite DoT (one owner casts it once per target).</summary>
    MortalFlame = 1u << 1,

    /// <summary>Breath of Magic slotted — the 60s DoT.</summary>
    BreathOfMagic = 1u << 2,

    /// <summary>Moon Flute slotted — participates in synced burst windows / stagger groups.</summary>
    MoonFlute = 1u << 3,

    /// <summary>Gobskin slotted — shield-coordinator candidate.</summary>
    Gobskin = 1u << 4,

    /// <summary>Cactguard slotted AND not the tank-role toon (a tank never Cactguards itself).</summary>
    Cactguard = 1u << 5,

    /// <summary>Final Sting slotted AND allowed to sting (tank role never advertises — v3.4 seed).</summary>
    FinalSting = 1u << 6,

    /// <summary>Level 5 Death slotted (Coil T5 ×1 / T13 ×2 carriers).</summary>
    Level5Death = 1u << 7,

    /// <summary>Sticky Tongue slotted (Coil T9 golem pullers ×2).</summary>
    StickyTongue = 1u << 8,

    /// <summary>Avail slotted (Coil T13 Earth Shaker redirect).</summary>
    Avail = 1u << 9,

    /// <summary>The Ram's Voice slotted (v3.6 FreezeLead seed).</summary>
    RamsVoice = 1u << 10,

    /// <summary>Ultravibration slotted (v3.6 ShatterOwner seed).</summary>
    Ultravibration = 1u << 11,

    /// <summary>Configured BluRole = Healer — the Gobskin election prefers the healer-mimic
    /// (250p vs 100p; every BLU heartbeat reads "DPS" job-wise, so the role must ride here).</summary>
    HealerRole = 1u << 12,

    /// <summary>Configured BluRole = Tank — elections that must exclude the tank use this
    /// (Cactguard target, never-sting rule).</summary>
    TankRole = 1u << 13,
}

/// <summary>Builds the local toon's capability bitfield from the learned+slotted availability check.</summary>
public static class BluCapabilityMap
{
    /// <summary>
    /// Compute the advertised bitfield. <paramref name="isSpellUsable"/> is the standard BLU
    /// availability check (learned AND slotted, fail-open to learned-only without slot data) —
    /// the same gate the rotation itself uses, so a toon never advertises what it can't cast.
    /// </summary>
    public static BluCapabilities Compute(Func<uint, bool> isSpellUsable, BluRole role)
    {
        var caps = BluCapabilities.None;

        void Add(uint actionId, BluCapabilities flag)
        {
            if (isSpellUsable(actionId)) caps |= flag;
        }

        Add(BLUActions.SongOfTorment.ActionId, BluCapabilities.SongOfTorment);
        Add(BLUActions.MortalFlame.ActionId, BluCapabilities.MortalFlame);
        Add(BLUActions.BreathOfMagic.ActionId, BluCapabilities.BreathOfMagic);
        Add(BLUActions.MoonFlute.ActionId, BluCapabilities.MoonFlute);
        Add(BLUActions.Gobskin.ActionId, BluCapabilities.Gobskin);
        Add(BLUActions.UtilityIds.Level5Death, BluCapabilities.Level5Death);
        Add(BLUActions.UtilityIds.StickyTongue, BluCapabilities.StickyTongue);
        Add(BLUActions.UtilityIds.Avail, BluCapabilities.Avail);
        Add(BLUActions.TheRamsVoice.ActionId, BluCapabilities.RamsVoice);
        Add(BLUActions.Ultravibration.ActionId, BluCapabilities.Ultravibration);

        // Role-conditional bits: the tank never Cactguards (it's the target) and never stings
        // (someone must hold the boss); the healer hint drives the Gobskin preference.
        if (role != BluRole.Tank)
        {
            Add(BLUActions.Cactguard.ActionId, BluCapabilities.Cactguard);
            Add(BLUActions.FinalSting.ActionId, BluCapabilities.FinalSting);
        }

        if (role == BluRole.Healer) caps |= BluCapabilities.HealerRole;
        if (role == BluRole.Tank) caps |= BluCapabilities.TankRole;

        return caps;
    }
}
