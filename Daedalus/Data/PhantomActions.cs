using System.Collections.Generic;

namespace Daedalus.Data;

/// <summary>One phantom duty action: ID, display name, owning job, phantom-level unlock.</summary>
public readonly record struct PhantomActionDef(uint ActionId, string Name, PhantomJob Job, byte RequiredLevel)
{
    /// <summary>Oracle cards / Dancer steps: usable at job level 1 but only while the
    /// matching proc status is active (Predict / Dance opens them).</summary>
    public bool RequiresProc { get; init; }
}

/// <summary>
/// The full South Horn phantom action catalog (Phase 2 of docs/occult-phantom-plan.md).
/// Action IDs extracted from the RSR generated action table and spot-verified in the
/// field (Phantom Fire 41626 / Holy Cannon 41627 / Dark Cannon 41628 on the live duty
/// bar). Unlock levels per burn-reference/occult-crescent-phantom-jobs.md.
/// Original 13 jobs occupy the contiguous 41588–41651 block; the post-7.25 trio
/// (Mystic Knight / Gladiator / Dancer) occupies 46590–46605.
/// </summary>
public static class PhantomActions
{
    /// <summary>
    /// Party buffs mapped to the status they grant. These have recasts far shorter than their
    /// durations (Offensive Aria: 5s recast, 70s buff), so the layer paces them on the status
    /// being down rather than on the cooldown — otherwise they re-fire every few seconds.
    /// Status ids are the same-named rows in the phantom block, XIVAPI-verified 2026-07-31.
    /// </summary>
    public static readonly IReadOnlyDictionary<uint, uint> PartyBuffStatusByAction =
        new Dictionary<uint, uint>
        {
            [41608] = 4247, // Offensive Aria
            [41607] = 4246, // Mighty March
            [41610] = 4249, // Hero's Rime
            [41611] = 4251, // Battle Bell
            [41619] = 4257, // Ringing Respite
            [41599] = 4240, // Phantom Aim
            [46590] = 4788, // Magic Shell
        };

    public static readonly IReadOnlyList<PhantomActionDef> All =
    [
        // ── Freelancer (levels via mastery count, so unlocks run past 6) ──
        new(41650, "Occult Resuscitation", PhantomJob.Freelancer, 5),
        new(41651, "Occult Treasuresight", PhantomJob.Freelancer, 10),

        // ── Knight ──
        new(41588, "Phantom Guard", PhantomJob.Knight, 1),
        new(41589, "Pray", PhantomJob.Knight, 2),
        new(41590, "Occult Heal", PhantomJob.Knight, 3),
        new(41591, "Pledge", PhantomJob.Knight, 6),

        // ── Berserker ──
        new(41592, "Rage", PhantomJob.Berserker, 1),
        new(41594, "Deadly Blow", PhantomJob.Berserker, 2),

        // ── Monk ──
        new(41595, "Phantom Kick", PhantomJob.Monk, 1),
        new(41596, "Occult Counter", PhantomJob.Monk, 2),
        new(41597, "Counterstance", PhantomJob.Monk, 3),
        new(41598, "Occult Chakra", PhantomJob.Monk, 5),

        // ── Ranger ──
        new(41599, "Phantom Aim", PhantomJob.Ranger, 1),
        new(41600, "Occult Featherfoot", PhantomJob.Ranger, 2),
        new(41601, "Occult Falcon", PhantomJob.Ranger, 4),
        new(41602, "Occult Unicorn", PhantomJob.Ranger, 6),

        // ── Samurai ──
        new(41603, "Mineuchi", PhantomJob.Samurai, 1),
        new(41604, "Shirahadori", PhantomJob.Samurai, 2),
        new(41605, "Iainuki", PhantomJob.Samurai, 3),
        new(41606, "Zeninage", PhantomJob.Samurai, 4),

        // ── Bard ──
        new(41608, "Offensive Aria", PhantomJob.Bard, 1),
        new(41609, "Romeo's Ballad", PhantomJob.Bard, 2),
        new(41607, "Mighty March", PhantomJob.Bard, 3),
        new(41610, "Hero's Rime", PhantomJob.Bard, 4),

        // ── Geomancer (the six Lv.2 buffs are weather-gated — the game offers only the
        //    one matching current weather) ──
        new(41611, "Battle Bell", PhantomJob.Geomancer, 1),
        new(41613, "Sunbath", PhantomJob.Geomancer, 2),
        new(41614, "Cloudy Caress", PhantomJob.Geomancer, 2),
        new(41615, "Blessed Rain", PhantomJob.Geomancer, 2),
        new(41616, "Misty Mirage", PhantomJob.Geomancer, 2),
        new(41617, "Hasty Mirage", PhantomJob.Geomancer, 2),
        new(41618, "Aetherial Gain", PhantomJob.Geomancer, 2),
        new(41619, "Ringing Respite", PhantomJob.Geomancer, 3),
        new(41620, "Suspend", PhantomJob.Geomancer, 4),

        // ── Time Mage ──
        new(41621, "Occult Slowga", PhantomJob.TimeMage, 1),
        new(41623, "Occult Comet", PhantomJob.TimeMage, 2),
        new(41624, "Occult Mage Masher", PhantomJob.TimeMage, 3),
        new(41622, "Occult Dispel", PhantomJob.TimeMage, 4),
        new(41625, "Occult Quick", PhantomJob.TimeMage, 5),

        // ── Cannoneer ──
        new(41626, "Phantom Fire", PhantomJob.Cannoneer, 1),
        new(41627, "Holy Cannon", PhantomJob.Cannoneer, 2),
        new(41628, "Dark Cannon", PhantomJob.Cannoneer, 3),
        new(41629, "Shock Cannon", PhantomJob.Cannoneer, 4),
        new(41630, "Silver Cannon", PhantomJob.Cannoneer, 6),

        // ── Chemist (Potion + Ether both consume an Occult Potion item) ──
        new(41631, "Occult Potion", PhantomJob.Chemist, 1),
        new(41633, "Occult Ether", PhantomJob.Chemist, 2),
        new(41634, "Revive", PhantomJob.Chemist, 3),
        new(41635, "Occult Elixir", PhantomJob.Chemist, 4),

        // ── Oracle (cards open off Predict) ──
        new(41636, "Predict", PhantomJob.Oracle, 1),
        new(41637, "Phantom Judgment", PhantomJob.Oracle, 1) { RequiresProc = true },
        new(41638, "Cleansing", PhantomJob.Oracle, 1) { RequiresProc = true },
        new(41639, "Blessing", PhantomJob.Oracle, 1) { RequiresProc = true },
        new(41640, "Starfall", PhantomJob.Oracle, 1) { RequiresProc = true },
        new(41641, "Recuperation", PhantomJob.Oracle, 2),
        new(41642, "Phantom Doom", PhantomJob.Oracle, 3),
        new(41643, "Phantom Rejuvenation", PhantomJob.Oracle, 4),
        new(41644, "Invulnerability", PhantomJob.Oracle, 6),

        // ── Thief ──
        new(41646, "Occult Sprint", PhantomJob.Thief, 1),
        new(41645, "Steal", PhantomJob.Thief, 2),
        new(41647, "Vigilance", PhantomJob.Thief, 3),
        new(41648, "Trap Detection", PhantomJob.Thief, 4),
        new(41649, "Pilfer Weapon", PhantomJob.Thief, 5),

        // ── Mystic Knight ──
        new(46591, "Sundering Spellblade", PhantomJob.MysticKnight, 1),
        new(46590, "Magic Shell", PhantomJob.MysticKnight, 2),
        new(46592, "Holy Spellblade", PhantomJob.MysticKnight, 3),
        new(46593, "Blazing Spellblade", PhantomJob.MysticKnight, 4),

        // ── Gladiator ──
        new(46594, "Finisher", PhantomJob.Gladiator, 1),
        new(46595, "Defend", PhantomJob.Gladiator, 2),
        new(46596, "Long Reach", PhantomJob.Gladiator, 3),
        new(46597, "Bladeblitz", PhantomJob.Gladiator, 4),

        // ── Dancer (steps open off Dance) ──
        new(46598, "Dance", PhantomJob.Dancer, 1),
        new(46599, "Phantom Sword Dance", PhantomJob.Dancer, 1) { RequiresProc = true },
        new(46600, "Tempting Tango", PhantomJob.Dancer, 1) { RequiresProc = true },
        new(46601, "Jitterbug", PhantomJob.Dancer, 1) { RequiresProc = true },
        new(46602, "Mystery Waltz", PhantomJob.Dancer, 1) { RequiresProc = true },
        new(46603, "Quickstep", PhantomJob.Dancer, 2),
        new(46604, "Steadfast Stance", PhantomJob.Dancer, 3),
        new(46605, "Mesmerize", PhantomJob.Dancer, 4),

        // ── Phantom Ninja (North Horn; kit field-confirmed 2026-07-31 from the Phantom Job
        //    panel + tooltips). Everything is an Ability (weave), so none of it costs a GCD.
        //    Lv.5 is the First Strike TRAIT (faster casts/recasts/auto-attacks for 25s on
        //    entering combat) — passive, nothing to press. ──
        new(49062, "Fuma Shuriken", PhantomJob.PhantomNinja, 1),    // 230 potency, 60s, single target
        new(49063, "Smoke", PhantomJob.PhantomNinja, 2),            // +20% evasion, 90s, 5s recast
        new(49064, "Lightning Scroll", PhantomJob.PhantomNinja, 3), // 150 (195 lightning-weak), 5y AoE, 60s
        new(49065, "Flame Scroll", PhantomJob.PhantomNinja, 4),     // 150 (195 fire-weak), 5y AoE, 60s
        new(49066, "Image", PhantomJob.PhantomNinja, 6),            // 3 stacks, nullifies most physical, 30s

        // ── Phantom Red Mage (North Horn; kit field-confirmed 2026-07-31 from the job panel
        //    + tooltips). Fire II / Blizzard II / Thunder II SHARE one 30s recast — the same
        //    one-nuke-three-elements shape as the Necromancer trio, so the target's weakness
        //    picks which one fires (300, or 390 on a match). Lv.5 is the Dualcast TRAIT: any
        //    cast-time spell makes the NEXT spell instant, so the trio effectively casts free
        //    behind Cure II. ──
        // ── Phantom Blue Mage (North Horn; tooltips field-captured 2026-08-02). Sits in a
        //    contiguous 49085-49091 block immediately before Red Mage's. Unlike every other
        //    phantom job its actions are LEARNED FROM ENEMIES, so the levels below are the
        //    TRAIT tiers that make each learnable ("Occult Learning I/II/III") rather than a
        //    guarantee you have it — the duty-bar slot gate is what proves you actually do.
        //    Aero I/II/III are the same button in ascending grades, so only the best learned
        //    one is ever worth firing. ──
        new(49085, "Occult Aero", PhantomJob.PhantomBlueMage, 1),          // wind, 150 (195 vs wind-weak), 30s
        new(49086, "Occult Missile", PhantomJob.PhantomBlueMage, 1),       // 35% chance of 75% CURRENT HP, 30s
        new(49087, "Occult Aqua Breath", PhantomJob.PhantomBlueMage, 1),   // unaspected 300, 5y AoE, 60s
        new(49088, "Occult Mighty Guard", PhantomJob.PhantomBlueMage, 2),  // party 20% mit 15s, 20y, 120s
        new(49089, "Occult Aero II", PhantomJob.PhantomBlueMage, 2),       // wind, upgrade of Aero
        // Lv.3 pair INFERRED from the Learning III trait tier — the tooltip was not captured.
        new(49090, "Occult White Wind", PhantomJob.PhantomBlueMage, 3),    // party heal = own CURRENT HP, 150s
        new(49091, "Occult Aero III", PhantomJob.PhantomBlueMage, 3),      // wind, top grade

        new(49092, "Occult Fire II", PhantomJob.PhantomRedMage, 1),      // fire
        new(49093, "Occult Cure II", PhantomJob.PhantomRedMage, 2),      // 40,000 cure potency, 1500 MP
        new(49094, "Occult Libra", PhantomJob.PhantomRedMage, 3),        // REVEALS elemental affinity, 120s
        new(49095, "Occult Blizzard II", PhantomJob.PhantomRedMage, 4),  // ice
        new(49096, "Occult Thunder II", PhantomJob.PhantomRedMage, 6),   // lightning

        // ── Phantom Dragoon (North Horn; kit field-confirmed 2026-07-31). The smallest kit
        //    on the roster: THREE actions (Lv.1-3) plus a Lv.4 trait, where every other job
        //    has five. Occult Jump is a Weaponskill; the other two are Abilities. ──
        new(49077, "Occult Jump", PhantomJob.PhantomDragoon, 1),  // 400 potency + 60% damage taken down 2s
        new(49078, "Step Forth", PhantomJob.PhantomDragoon, 2),   // 10y directional hop, 10s
        new(49079, "Lance", PhantomJob.PhantomDragoon, 3),        // 300, drains as HP, overheal becomes a 60s barrier
        // Lv.4 trait "Enhanced Occult Jump": Jump 400 -> 500 potency and its damage
        // reduction 60% -> 90%. Passive; nothing to press.

        // ── Phantom Summoner (North Horn; kit field-confirmed 2026-07-31, five actions at
        //    Lv.1-5, no traits). The heaviest hitter of the North Horn kits — 600 potency a
        //    nuke (780 matched) and a 1,000 Megaflare — but the casts are FOUR seconds and
        //    "cannot be affected by status effects or gear attributes", so no haste or
        //    Dualcast shortens them. Hellfire / Judgment Bolt / Thunderstorm SHARE one 60s
        //    recast. NOTE: Thunderstorm deals WIND damage despite the name, and is this
        //    roster's only wind coverage. ──
        new(49080, "Hellfire", PhantomJob.PhantomSummoner, 1),      // fire 600/780, 12y
        new(49081, "Judgment Bolt", PhantomJob.PhantomSummoner, 2), // lightning 600/780, 12y
        new(49082, "Earthen Wall", PhantomJob.PhantomSummoner, 3),  // 40,000-potency party barrier, 20y, 120s
        new(49083, "Thunderstorm", PhantomJob.PhantomSummoner, 4),  // WIND 600/780, 30y cone
        new(49084, "Megaflare", PhantomJob.PhantomSummoner, 5),     // unaspected 1,000, 15y, 90s

        // ── Phantom White Mage (North Horn; kit field-confirmed 2026-07-31, five actions at
        //    Lv.1-5, no traits). Note the sheet carries TWO "Occult Cure II" rows — 49067 is
        //    this one, 49093 is Red Mage's, which is why neither was cataloged on id adjacency
        //    alone. ──
        new(49067, "Occult Cure II", PhantomJob.PhantomWhiteMage, 1),   // 40,000 cure, 1,500 MP
        new(49068, "Occult Cure III", PhantomJob.PhantomWhiteMage, 2),  // 30,000 cure, 15y AoE, 3,000 MP
        new(49069, "Occult Blink", PhantomJob.PhantomWhiteMage, 3),     // immune to one magic hit, 30s
        new(49070, "Occult Raise", PhantomJob.PhantomWhiteMage, 4),     // instant, works under Res Restricted
        new(49071, "Occult Holy", PhantomJob.PhantomWhiteMage, 5),      // 500 (750 vs undead), 8y, 60s

        // ── Phantom Black Mage (North Horn; kit field-confirmed 2026-07-31, job panel shows
        //    five actions at Lv.1-5 and NO traits). Unlike Red Mage's II-tier, the III-tier
        //    nukes carry NO "shares a recast" line — they are three INDEPENDENT 40s recasts,
        //    so all three are usable and the weakness only decides the order. 400 potency,
        //    520 on a match. ──
        new(49072, "Occult Fire III", PhantomJob.PhantomBlackMage, 1),
        new(49073, "Occult Blizzard III", PhantomJob.PhantomBlackMage, 2),
        new(49074, "Occult Thunder III", PhantomJob.PhantomBlackMage, 3),
        new(49075, "Occult Toad", PhantomJob.PhantomBlackMage, 4),   // -99% damage dealt, 20s
        new(49076, "Occult Flare", PhantomJob.PhantomBlackMage, 5),  // unaspected 500, 8y, 2.3s cast

        // North Horn ids seen in the sheet but NOT yet attributed (no sighting): 49060 Meteor,
        // 49061 Comet. Note Occult Cure II turned out to be RED MAGE's, not White Mage's as
        // the id neighbourhood suggested — which is exactly why these stay uncataloged until
        // seen on a bar.

        // ── Necromancer (North Horn; block starts 49097 — only field-confirmed actions are
        // cataloged, one bar screenshot per level unlock extends this) ──
        new(49097, "Drain Touch", PhantomJob.Necromancer, 1),
        // The Doom nukes. All are 1.5s cast / 30y line, cost 10% MAX HP and DOOM the caster
        // for 10s (cleared only by a heal to FULL). Off by default — see PhantomConfig.
        //
        // Deep Freeze / Hell Wind / Chaos Drive SHARE ONE 40s RECAST: they are a single nuke
        // in three elements (ice / wind / lightning), so the only question is WHICH element
        // the target is weak to — 300 base, 390 weak, or 400/520 under Drain Touch.
        new(49098, "Deep Freeze", PhantomJob.Necromancer, 2),   // ice
        new(49099, "Hell Wind", PhantomJob.Necromancer, 2),     // wind  (+10% Petrify w/ Drain Touch)
        new(49100, "Chaos Drive", PhantomJob.Necromancer, 2),   // lightning (+Paralysis w/ Drain Touch)
        // Doomsday: its own 120s recast, unaspected (350, 500 under Drain Touch) so no
        // weakness applies, and it strips one buff from the target under Drain Touch.
        new(49101, "Doomsday", PhantomJob.Necromancer, 2),
    ];

    /// <summary>
    /// Rotation-critical player statuses that block ALL phantom actions while active
    /// (RSR HasLockoutStatus parity) — a phantom weave/GCD must never stomp a burst or
    /// combo window. IDs verified against the RSR StatusID enum.
    /// </summary>
    /// <summary>
    /// TRUE locks: the job has taken over the hotbar or is mid-chain, so a phantom action is
    /// either impossible or would break the sequence. These hold the whole layer.
    /// </summary>
    public static readonly IReadOnlyList<uint> LockoutStatusIds =
    [
        3670, // Reawakened (VPR)  — hotbar replaced by the Reawaken combo
        2688, // Overheated (MCH)  — hotbar replaced by Heat Blast/Auto Crossbow
        2606, // Eukrasia (SGE)    — transforms the next spell
        496,  // Mudra (NIN)       — mid mudra sequence
        1186, // Ten Chi Jin (NIN) — hotbar replaced
    ];

    /// <summary>
    /// NOT locks — buffs whose value would be wasted by spending a GCD elsewhere. These suppress
    /// phantom GCDs only; oGCDs and utility still fire.
    /// <para>
    /// Field 2026-08-11: Inner Release was in the hard-lock list above, so a Warrior running
    /// Phantom Red Mage had the ENTIRE layer held for 15 seconds of every minute — including
    /// Occult Libra, which is an oGCD (ActionCategory 4, 5s recast) and costs no GCD at all.
    /// Since Libra is the only thing that reveals elemental weaknesses, that quietly stopped the
    /// weakness table improving on the very job that gathers it.
    /// </para>
    /// </summary>
    public static readonly IReadOnlyList<uint> GcdHoldStatusIds =
    [
        1177, // Inner Release (WAR)          — free Fell Cleaves, don't spend the GCD elsewhere
        3866, // Full Metal Field ready (MCH) — proc waiting to be spent
        851,  // Reassembled (MCH)            — buffs the next weaponskill
    ];

    /// <summary>Phantom-related status IDs (verified against the RSR StatusID enum).</summary>
    public static class StatusIds
    {
        public const uint PredictionOfJudgment = 4265;
        public const uint PredictionOfCleansing = 4266;
        public const uint PredictionOfBlessing = 4267;
        public const uint PredictionOfStarfall = 4268;
        public const uint PoisedToSwordDance = 4794;
        public const uint TemptedToTango = 4795;
        public const uint Jitterbugged = 4796;
        public const uint WillingToWaltz = 4797;
        public const uint PentupRage = 4236;
        public const uint Invulnerability = 4275;

        // ── Necromancer (North Horn) ──
        /// <summary>Self-buff from Drain Touch: "most attacks cannot reduce own HP to less
        /// than 1" — the survival half of the Deep Freeze combo (its 10% HP cost).</summary>
        public const uint DrainTouch = 5326;

        /// <summary>The Doom Deep Freeze puts on YOU (10s). "Dissipates once fully healed" —
        /// anything short of a heal to FULL inside the window is death.</summary>
        public const uint DoomDispelledByFullHeal = 1769;

        /// <summary>Target debuff that boosts ice-aspected phantom damage (Deep Freeze).</summary>
        public const uint IceWeakness = 5323;

        /// <summary>Phantom Ninja Smoke: "Evasion is enhanced" (+20%, 90s).</summary>
        public const uint Smoke = 5327;
    }

    /// <summary>Actions belonging to one phantom job, in unlock order.</summary>
    public static IReadOnlyList<PhantomActionDef> ForJob(PhantomJob job)
    {
        var result = new List<PhantomActionDef>();
        foreach (var def in All)
        {
            if (def.Job == job)
                result.Add(def);
        }

        return result;
    }
}
