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
    ];

    /// <summary>
    /// Rotation-critical player statuses that block ALL phantom actions while active
    /// (RSR HasLockoutStatus parity) — a phantom weave/GCD must never stomp a burst or
    /// combo window. IDs verified against the RSR StatusID enum.
    /// </summary>
    public static readonly IReadOnlyList<uint> LockoutStatusIds =
    [
        3670, // Reawakened (VPR)
        2688, // Overheated (MCH)
        1177, // Inner Release (WAR)
        2606, // Eukrasia (SGE)
        496,  // Mudra (NIN)
        1186, // Ten Chi Jin (NIN)
        3866, // Full Metal Field ready (MCH)
        851,  // Reassembled (MCH)
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
