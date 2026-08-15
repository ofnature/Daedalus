using System.Collections.Generic;
using Daedalus.Config;

namespace Daedalus.Rotation.Phantom;

/// <summary>
/// Pure decision predicates for the Phase 3 phantom bands (survival / mitigation /
/// interrupt / MP / party buffs). Kept free of game services so every rule is
/// unit-testable; <see cref="PhantomActionLayer"/> feeds them live values.
/// </summary>
/// <summary>Which corpse the phantom raise should take, if any.</summary>
public enum PhantomRaiseDecision
{
    None,
    RaiseHealer,
    RaiseOther,
}

public static class PhantomBandRules
{
    /// <summary>Self HP fraction below which the self-mits (Phantom Guard, Defend) fire.</summary>
    public const float SelfMitHpPct = 0.45f;

    /// <summary>Self HP fraction below which Pray fires (when configured as a heal).</summary>
    public const float PrayHpPct = 0.85f;

    /// <summary>
    /// Who the phantom raise should pick up. Same policy as the Variant layer, for the same
    /// reason: a dead healer is always raised because nobody else can restart the party, while
    /// a dead DPS is left to a living healer — the healer's raise is stronger and the phantom
    /// caster is usually mid-rotation. With no healer alive, anyone is worth raising.
    /// </summary>
    /// <summary>
    /// Whether the phantom layer must give the GCD back to the job.
    /// <para>
    /// The layer pre-empts the GCD before the job's own modules run, which is right for a filler
    /// and wrong for a raise: Raise is a GCD, so a phantom heal or nuke holding the window stops
    /// a healer ever casting it. Field 2026-08-02 — Sage raises worked everywhere EXCEPT the
    /// Horns, the only place this layer runs. A phantom cast is worth a fraction of getting a
    /// player back up.
    /// </para>
    /// </summary>
    public static bool ShouldYieldGcdForRaise(bool jobCanRaise, bool raisableCorpseInRange)
        => jobCanRaise && raisableCorpseInRange;

    /// <summary>
    /// How long a corpse may lie there with a living healer present before the phantom stops
    /// deferring and raises it anyway.
    /// <para>
    /// "Leave it to the healer" assumes the healer will act. Field evidence says that assumption
    /// fails often enough to matter — a healer can be out of range, out of MP, or blocked by
    /// something none of us has pinned down yet — and the deferral then means nobody raises at
    /// all. Long enough that a Swiftcast raise or an 8s hardcast lands first when things work.
    /// </para>
    /// </summary>
    public const float LivingHealerGraceSeconds = 10f;

    public static PhantomRaiseDecision DecideRaise(
        PhantomConfig cfg, bool deadHealerPresent, bool deadOtherPresent, bool livingHealerPresent)
    {
        if (!cfg.UsePhantomRaise)
            return PhantomRaiseDecision.None;
        if (deadHealerPresent)
            return PhantomRaiseDecision.RaiseHealer;
        if (deadOtherPresent && !livingHealerPresent)
            return PhantomRaiseDecision.RaiseOther;
        return PhantomRaiseDecision.None;
    }

    public static bool ShouldUsePotion(PhantomConfig cfg, float selfHpPct, uint potionCount, bool inCombat)
        => inCombat && potionCount > 0 && selfHpPct < cfg.ChemistPotionHpPct;

    public static bool ShouldUseElixir(PhantomConfig cfg, float selfHpPct, uint elixirCount, bool inCombat)
        => inCombat && elixirCount > 0 && selfHpPct < cfg.ChemistElixirPartyHpPct;

    public static bool ShouldUseChakraForHp(PhantomConfig cfg, float selfHpPct, bool inCombat)
        => inCombat && selfHpPct < cfg.MonkChakraHpPct;

    public static bool ShouldUseChakraForMp(PhantomConfig cfg, uint currentMp, uint maxMp, bool inCombat)
        => inCombat && maxMp > 0 && currentMp < cfg.MonkChakraMpThreshold;

    public static bool ShouldUseEther(PhantomConfig cfg, uint currentMp, uint maxMp, uint potionCount, bool inCombat)
        => inCombat && maxMp > 0 && potionCount > 0 && currentMp < cfg.ChemistEtherMpThreshold;

    public static bool ShouldSelfMit(float selfHpPct, bool inCombat)
        => inCombat && selfHpPct < SelfMitHpPct;

    public static bool ShouldInterrupt(bool targetIsCasting, bool castInterruptible, float distanceYalms, float maxRangeYalms)
        => targetIsCasting && castInterruptible && distanceYalms <= maxRangeYalms;

    public static bool ShouldResuscitate(PhantomConfig cfg, float selfHpPct)
        => selfHpPct < cfg.FreelancerResuscitationHpPct;

    /// <summary>
    /// Occult Heal (Knight): instant oGCD, 5s recast, 30y, self or ally. It costs a weave slot
    /// rather than a GCD, so the threshold is generous — sitting on a free heal while chipped is
    /// the mistake, not spending it.
    /// </summary>
    public static bool ShouldOccultHeal(PhantomConfig cfg, float selfHpPct, bool inCombat)
        => inCombat && selfHpPct < cfg.KnightHealHpPct;

    /// <summary>
    /// Pledge (Knight): a real INVULNERABILITY — "renders target impervious to most attacks" for
    /// 10s on a 120s recast — so it is a death-saver, not a top-up. Gated far lower than the
    /// heal, and behind its own switch.
    /// </summary>
    public static bool ShouldPledge(PhantomConfig cfg, float selfHpPct, bool inCombat)
        => inCombat && cfg.KnightPledgeSelf && selfHpPct < cfg.KnightPledgeHpPct;

    public static bool ShouldPray(PhantomConfig cfg, float selfHpPct)
        => cfg.KnightPrayAsHeal && selfHpPct < PrayHpPct;

    /// <summary>Thief Steal is an execute — fire on low-HP targets regardless of burst.</summary>
    public const float StealTargetHpPct = 0.25f;

    public static bool ShouldSteal(float targetHpPct) => targetHpPct < StealTargetHpPct;

    /// <summary>
    /// Damage-band burst hold. Only holds when burst data actually EXISTS
    /// (a burst window has been observed: <paramref name="secondsSinceLastBurstStart"/> ≥ 0) —
    /// solo field farming with no raid buffs must never starve the damage band.
    /// </summary>
    public static bool ShouldHoldDamage(bool saveForBurst, bool inBurstWindow, float secondsSinceLastBurstStart)
        => saveForBurst && !inBurstWindow && secondsSinceLastBurstStart >= 0f;

    /// <summary>Phantom Kick dashes to the target — cap the dash distance (config).</summary>
    public static bool ShouldPhantomKick(float distanceYalms, float maxRangeYalms)
        => distanceYalms <= maxRangeYalms;

    /// <summary>
    /// Phantom Red Mage's Dualcast trait level.
    /// <para>
    /// SIX, not five. The kit is Fire II (1), Cure II (2), Libra (3), Blizzard II (4),
    /// Thunder II (5) — the trait sits above the last action, and the old "Lv.5 trait" note was
    /// the mirror image of the same transcription slip that had Thunder II at 6 and cost thirteen
    /// silent minutes in the field.
    /// </para>
    /// </summary>
    public const byte DualcastTraitLevel = 6;

    /// <summary>Occult Cure II's MP cost.</summary>
    public const int OccultCureIIMpCost = 1500;

    /// <summary>
    /// Default MP floor for priming. Cure II is 1,500 and getting somebody off the floor is about
    /// 2,400 — spending down to nothing for a nuke trades damage for a body staying dead.
    /// <para>
    /// Configurable, because this number decides how a fight FEELS: it is the reason priming
    /// works beautifully early and then quietly stops once the bar has drained, which reads as
    /// the feature being flaky rather than as a budget being enforced (field 2026-08-11).
    /// </para>
    /// </summary>
    public const int DualcastPrimeMpFloor = 5000;

    /// <summary>What Phantom Red Mage should do with its GCD this window.</summary>
    public enum RedMagePlan
    {
        /// <summary>Nothing special — hard-cast a nuke the ordinary way.</summary>
        HardcastNuke,

        /// <summary>Dualcast is up: the nuke is INSTANT, so fire it now before a weaponskill eats it.</summary>
        SpendDualcast,

        /// <summary>Cast Occult Cure II to earn Dualcast, so the matched nuke lands instantly next GCD.</summary>
        PrimeWithCure,
    }

    /// <summary>
    /// Picks the Red Mage line. Priming is deliberately narrow — it only pays when the follow-up
    /// is the WEAKNESS-MATCHED nuke (390 potency rather than 300), because otherwise it is a whole
    /// GCD and 1,500 MP spent to shave 1.5s off a cast. Against an unidentified target there is no
    /// bonus to protect, so the nuke is simply hard-cast and Libra does the useful work instead.
    /// <para>
    /// What priming buys beyond the cast time: an instant nuke needs no movement pause and cannot
    /// be interrupted, which on a melee main job is the difference between firing and not.
    /// </para>
    /// </summary>
    public static RedMagePlan PlanRedMage(
        bool hasDualcast, byte phantomLevel, bool weaknessKnown, bool nukeReady, bool cureReady,
        int currentMp, bool primeEnabled, int mpFloor)
    {
        if (hasDualcast)
            return RedMagePlan.SpendDualcast;

        if (primeEnabled
            && phantomLevel >= DualcastTraitLevel
            && weaknessKnown
            && nukeReady
            && cureReady
            && currentMp >= mpFloor)
        {
            return RedMagePlan.PrimeWithCure;
        }

        return RedMagePlan.HardcastNuke;
    }

    /// <summary>
    /// Why priming was skipped, or null when nothing STICKY is blocking it.
    /// <para>
    /// Only the reasons that persist. Cure and nuke readiness are false on nearly every frame
    /// simply because the GCD is rolling, so reporting those would bury the line in noise that
    /// says nothing. What the field actually needs explaining is the other kind: "it primed all
    /// fight and then just stopped", which is MP draining past the floor or the target changing
    /// to something that has never been Libra'd.
    /// </para>
    /// </summary>
    public static string? DescribePrimeBlock(
        byte phantomLevel, bool weaknessKnown, int currentMp, int mpFloor)
    {
        if (phantomLevel < DualcastTraitLevel)
            return $"needs phantom Lv.{DualcastTraitLevel} for Dualcast (you are {phantomLevel})";
        if (!weaknessKnown)
            return "target's element is unknown — Libra it first";
        if (currentMp < mpFloor)
            return $"MP {currentMp} is under the {mpFloor} floor";
        return null;
    }

    /// <summary>
    /// How close the GCD must be before it is worth stopping to hard-cast a phantom GCD.
    /// </summary>
    public const float CastHoldLeadSeconds = 0.8f;

    /// <summary>
    /// Should we keep moving because the GCD this cast needs is still a while off?
    /// <para>
    /// Stopping early is pure loss. The phantom pre-pass runs BEFORE the job's modules, so a
    /// phantom GCD wins any window it is standing still for — but only if it is STILL standing
    /// still when that window arrives. Field 2026-08-11: Occult Slowga fired exactly once in a
    /// two-mob pull, on the frame the last mob died. The hold was sized to the cast (1.5 + 0.6 =
    /// 2.1s) while the thing it had to outwait was a full ~2.5s GCD, so it stopped, lost the
    /// window to the job's filler, expired, moved again, and looped — never stationary and never
    /// casting, the worst of both.
    /// </para>
    /// </summary>
    public static bool ShouldKeepMovingUntilGcd(float gcdRemaining, bool isGcd, float leadSeconds = CastHoldLeadSeconds)
        => isGcd && gcdRemaining > leadSeconds;

    /// <summary>
    /// How long the toon actually has to stand still: the wait for the GCD plus the cast itself.
    /// Both the safety check and the hold are sized off this, so we never promise to stand
    /// somewhere for less time than we mean to.
    /// </summary>
    public static float StillSecondsForCast(float gcdRemaining, bool isGcd, float castSeconds)
        => (isGcd && gcdRemaining > 0f ? gcdRemaining : 0f) + castSeconds;

    /// <summary>
    /// Occult Slowga (Time Mage): a pure debuff, no damage. Fires once and then waits out the
    /// 30s Slow rather than re-spending a GCD every 2.5s, so the gate is "target is not already
    /// slowed" — reapply follows for free when the status drops off.
    /// </summary>
    public static bool ShouldSlowga(PhantomConfig cfg, bool inCombat, bool targetAlreadySlowed)
        => inCombat && cfg.TimeMageUseSlowga && !targetAlreadySlowed;

    /// <summary>Necromancer elemental nuke ids — one shared 40s recast, three elements.</summary>
    public const uint DeepFreezeId = 49098;   // ice
    public const uint HellWindId = 49099;     // wind
    public const uint ChaosDriveId = 49100;   // lightning

    /// <summary>
    /// Picks which of the three shared-recast nukes to fire. They are the same button in
    /// different elements, so the target's revealed weakness decides: 520 potency instead of
    /// 400 under Drain Touch (+30%). An unknown weakness falls back to ice — Deep Freeze is
    /// the one the player is likeliest to have slotted, and the duty-bar gate covers the rest.
    /// Fire weakness has no matching nuke in this kit, so it also falls through.
    /// </summary>
    public static uint SelectElementalNuke(Daedalus.Services.Occult.OccultElement? knownWeakness)
        => NecromancerNukeOrder(knownWeakness)[0];

    /// <summary>
    /// The whole Necromancer trio, best match first — the same fix Red Mage and Summoner needed.
    /// <para>
    /// The unlocks are spread the same way and the game's own table proves it: Deep Freeze Lv.2,
    /// Hell Wind Lv.3, Chaos Drive Lv.4 (this catalog had all three at Lv.2 until 2026-08-14).
    /// So a Lv.2 Necromancer facing a lightning-weak enemy picked Chaos Drive, which it does not
    /// own, and fired nothing. They share one recast, so the extra pushes cost nothing.
    /// </para>
    /// </summary>
    public static uint[] NecromancerNukeOrder(Daedalus.Services.Occult.OccultElement? knownWeakness)
    {
        if (knownWeakness is { } w)
        {
            if ((w & Daedalus.Services.Occult.OccultElement.Ice) != 0)
                return [DeepFreezeId, HellWindId, ChaosDriveId];
            if ((w & Daedalus.Services.Occult.OccultElement.Wind) != 0)
                return [HellWindId, DeepFreezeId, ChaosDriveId];
            if ((w & Daedalus.Services.Occult.OccultElement.Lightning) != 0)
                return [ChaosDriveId, DeepFreezeId, HellWindId];
        }

        // Fire has no nuke in this kit, and an unknown weakness has nothing to match — either way
        // Deep Freeze leads as the earliest unlock.
        return [DeepFreezeId, HellWindId, ChaosDriveId];
    }

    /// <summary>
    /// Occult White Wind heals the PARTY for the caster's CURRENT HP, so its value rises the
    /// healthier the caster is — a full-HP caster with a dying party is the ideal case, not a
    /// blocked one. The trigger is party-average HP; the self floor only stops firing a copy
    /// worth almost nothing.
    /// </summary>
    public const float WhiteWindPartyAvgHpPct = 0.80f;
    public const float WhiteWindSelfHpFloorPct = 0.40f;

    public static bool ShouldWhiteWind(float partyAvgHpPct, float selfHpPct, bool inCombat)
        => inCombat && partyAvgHpPct < WhiteWindPartyAvgHpPct && selfHpPct > WhiteWindSelfHpFloorPct;

    /// <summary>Phantom Blue Mage — Aero grades, all the same button.</summary>
    public const uint OccultAeroId = 49085;
    public const uint OccultAeroIIId = 49089;
    public const uint OccultAeroIIIId = 49091;

    /// <summary>
    /// Aero grades best-first. They are one button in ascending grades, but Blue Mage LEARNS
    /// from enemies rather than levels, so the phantom level proves nothing about which grade
    /// is actually known — push all of them at descending priority and let the duty-bar gate
    /// (fail-closed on unlearned actions) pick the one really on the bar.
    /// </summary>
    public static readonly uint[] AeroGradesDescending = [OccultAeroIIIId, OccultAeroIIId, OccultAeroId];

    /// <summary>Occult Cure II (Red Mage): 40,000 potency self-heal, 1,500 MP, 2.5s recast.</summary>
    public static bool ShouldOccultCure(PhantomConfig cfg, float selfHpPct, bool inCombat)
        => inCombat && selfHpPct < cfg.RedMageCureHpPct;

    /// <summary>
    /// Occult Cure III (White Mage): 30,000 cure in a 15y AoE for 3,000 MP. An AoE heal wants
    /// multiple hurt bodies — one injured member is Cure II's job — and "injured" starts at a
    /// 95% scratch, so the party-average bar keeps 3,000 MP from going to chip damage.
    /// </summary>
    public const int CureIIIMinInjured = 2;
    public const float CureIIIPartyAvgHpPct = 0.80f;

    public static bool ShouldOccultCureIII(float partyAvgHpPct, int injuredCount, bool inCombat)
        => inCombat && injuredCount >= CureIIIMinInjured && partyAvgHpPct < CureIIIPartyAvgHpPct;

    /// <summary>Phantom Red Mage elemental trio — one shared 30s recast, like the Necromancer's.</summary>
    public const uint OccultFireIIId = 49092;
    public const uint OccultBlizzardIIId = 49095;
    public const uint OccultThunderIIId = 49096;

    /// <summary>
    /// Red Mage's shared-recast trio: 300 potency, 390 on a matched weakness. Fire is the
    /// fallback for an unknown or unmatched weakness (wind has no nuke in this kit).
    /// </summary>
    public static uint SelectRedMageNuke(Daedalus.Services.Occult.OccultElement? knownWeakness)
        => RedMageNukeOrder(knownWeakness)[0];

    /// <summary>
    /// The whole trio, best match first. Pushing only the single best pick meant one refusal —
    /// unlearned, not slotted, out of range — produced NO damage at all rather than the
    /// second-best nuke, and because a level gate refuses silently it read as "nothing
    /// eligible". They share a recast, so the extra pushes cost nothing: the first one the
    /// gates accept is the one that fires.
    /// </summary>
    public static uint[] RedMageNukeOrder(Daedalus.Services.Occult.OccultElement? knownWeakness)
    {
        if (knownWeakness is { } w)
        {
            if ((w & Daedalus.Services.Occult.OccultElement.Fire) != 0)
                return [OccultFireIIId, OccultBlizzardIIId, OccultThunderIIId];
            if ((w & Daedalus.Services.Occult.OccultElement.Ice) != 0)
                return [OccultBlizzardIIId, OccultFireIIId, OccultThunderIIId];
            if ((w & Daedalus.Services.Occult.OccultElement.Lightning) != 0)
                return [OccultThunderIIId, OccultFireIIId, OccultBlizzardIIId];
        }

        // Wind has no nuke in this kit, and an unknown weakness has nothing to match — either
        // way Fire leads because it is the earliest unlock and so the likeliest to be usable.
        return [OccultFireIIId, OccultBlizzardIIId, OccultThunderIIId];
    }

    /// <summary>Phantom Summoner nukes — one shared 60s recast (Thunderstorm is WIND).</summary>
    public const uint HellfireId = 49080;
    public const uint JudgmentBoltId = 49081;
    public const uint ThunderstormId = 49083;

    /// <summary>
    /// Summoner's shared-recast trio: 600 potency, 780 on a matched weakness. Covers fire,
    /// lightning and WIND (Thunderstorm, despite the name) — no ice, so an ice-weak target
    /// falls back to Hellfire.
    /// </summary>
    public static uint SelectSummonerNuke(Daedalus.Services.Occult.OccultElement? knownWeakness)
        => SummonerNukeOrder(knownWeakness)[0];

    /// <summary>
    /// The whole Summoner trio, best match first — the same fix Red Mage needed.
    /// <para>
    /// Pushing only the single best pick means one refusal produces NO damage at all instead of
    /// the second-best nuke, and a level gate refuses in a way that reads as "nothing eligible".
    /// It bites hardest here because the unlocks are spread: Hellfire is Lv.1 but Thunderstorm —
    /// this kit's ONLY wind coverage — is Lv.4, so a Lv.3 Summoner facing a wind-weak enemy
    /// picked an action it does not own and fired nothing. They share one 60s recast, so the
    /// extra pushes cost nothing; the first the gates accept is the one that fires.
    /// </para>
    /// </summary>
    public static uint[] SummonerNukeOrder(Daedalus.Services.Occult.OccultElement? knownWeakness)
    {
        if (knownWeakness is { } w)
        {
            if ((w & Daedalus.Services.Occult.OccultElement.Fire) != 0)
                return [HellfireId, JudgmentBoltId, ThunderstormId];
            if ((w & Daedalus.Services.Occult.OccultElement.Lightning) != 0)
                return [JudgmentBoltId, HellfireId, ThunderstormId];
            if ((w & Daedalus.Services.Occult.OccultElement.Wind) != 0)
                return [ThunderstormId, HellfireId, JudgmentBoltId];
        }

        // Unknown weakness: Hellfire leads as the earliest unlock, so it is the likeliest to be
        // usable at all — the same tie-break Red Mage uses.
        return [HellfireId, JudgmentBoltId, ThunderstormId];
    }

    /// <summary>Phantom Black Mage III-tier — INDEPENDENT 40s recasts, so all three are usable.</summary>
    public const uint OccultFireIIIId = 49072;
    public const uint OccultBlizzardIIIId = 49073;
    public const uint OccultThunderIIIId = 49074;

    /// <summary>
    /// Black Mage nukes in the order they should be pushed. They do NOT share a recast (unlike
    /// Red Mage's II-tier), so the weakness decides which LEADS, not which is skipped —
    /// 520 potency on a match, 400 otherwise, and the other two still fire.
    /// </summary>
    public static uint[] BlackMageNukeOrder(Daedalus.Services.Occult.OccultElement? knownWeakness)
    {
        var w = knownWeakness ?? Daedalus.Services.Occult.OccultElement.None;

        // An enemy can carry MORE THAN ONE weakness (field 2026-07-31: Crescent Soblyn showed
        // two at once), so this is a partition, not a single "lead" pick — every matched
        // element outranks every unmatched one, and all three fire regardless since they do
        // not share a recast.
        var matched = new List<uint>();
        var rest = new List<uint>();
        void Place(uint id, Daedalus.Services.Occult.OccultElement element)
            => ((w & element) != 0 ? matched : rest).Add(id);

        Place(OccultFireIIIId, Daedalus.Services.Occult.OccultElement.Fire);
        Place(OccultBlizzardIIIId, Daedalus.Services.Occult.OccultElement.Ice);
        Place(OccultThunderIIIId, Daedalus.Services.Occult.OccultElement.Lightning);

        matched.AddRange(rest);
        return matched.ToArray();
    }

    /// <summary>Phantom Ninja scrolls — SEPARATE 60s recasts, so both are usable.</summary>
    public const uint LightningScrollId = 49064;
    public const uint FlameScrollId = 49065;

    /// <summary>
    /// Which Ninja scroll to fire FIRST. Unlike the Necromancer trio these have independent
    /// recasts, so this is ordering rather than an exclusive choice: lead with the element
    /// the target is weak to (195 potency instead of 150) and the other still follows.
    /// </summary>
    public static uint PreferredScroll(Daedalus.Services.Occult.OccultElement? knownWeakness)
        => knownWeakness is { } w && (w & Daedalus.Services.Occult.OccultElement.Fire) != 0
            ? FlameScrollId
            : LightningScrollId;

    /// <summary>
    /// Necromancer Doom nukes — a SUICIDE-RISK gate, not a DPS gate. The action costs 10% of
    /// max HP and applies Doom to the caster for 10s, dispelled ONLY by a heal back to FULL.
    /// The Oracle False Prediction death (2026-07-25) is the precedent: an unattended toon that
    /// cannot clear the timer simply dies. Every condition must hold:
    ///   • the user opted in (off by default),
    ///   • no Doom already ticking — never stack a second death timer,
    ///   • HP at/above the configured floor so the 10% cost lands somewhere recoverable,
    ///   • the Drain Touch self-buff is up when required — "attacks cannot reduce own HP below
    ///     1" is what makes the cost survivable, and it raises the potency too.
    /// </summary>
    public static bool ShouldFireDoomNuke(
        PhantomConfig cfg, float selfHpPct, bool hasDoom, bool hasDrainTouchBuff)
    {
        if (!cfg.NecromancerUseDeepFreeze)
            return false;
        if (hasDoom)
            return false;
        if (selfHpPct < cfg.NecromancerDeepFreezeMinHpPercent)
            return false;
        if (cfg.NecromancerDeepFreezeRequireDrainTouch && !hasDrainTouchBuff)
            return false;
        return true;
    }
}
