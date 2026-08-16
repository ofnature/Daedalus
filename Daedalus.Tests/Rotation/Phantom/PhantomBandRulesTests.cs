using Daedalus.Config;
using System.Linq;
using Daedalus.Services.Occult;
using Daedalus.Data;
using Daedalus.Rotation.Phantom;
using Xunit;

namespace Daedalus.Tests.Rotation.Phantom;

/// <summary>
/// Tests for the Phase 3 phantom band rules (docs/occult-phantom-plan.md) — the pure
/// predicates behind the survival / mitigation / interrupt / MP bands.
/// </summary>
public class PhantomBandRulesTests
{
    private static PhantomConfig DefaultConfig() => new();

    [Fact]
    public void Potion_FiresOnlyInCombat_WithItems_BelowThreshold()
    {
        var cfg = DefaultConfig(); // 0.50

        Assert.True(PhantomBandRules.ShouldUsePotion(cfg, 0.40f, potionCount: 5, inCombat: true));
        Assert.False(PhantomBandRules.ShouldUsePotion(cfg, 0.40f, potionCount: 0, inCombat: true));
        Assert.False(PhantomBandRules.ShouldUsePotion(cfg, 0.40f, potionCount: 5, inCombat: false));
        Assert.False(PhantomBandRules.ShouldUsePotion(cfg, 0.60f, potionCount: 5, inCombat: true));
    }

    [Fact]
    public void Elixir_UsesItsOwnLowerThreshold()
    {
        var cfg = DefaultConfig(); // elixir 0.30 vs potion 0.50

        Assert.False(PhantomBandRules.ShouldUseElixir(cfg, 0.40f, elixirCount: 2, inCombat: true));
        Assert.True(PhantomBandRules.ShouldUseElixir(cfg, 0.25f, elixirCount: 2, inCombat: true));
    }

    [Fact]
    public void Ether_GatesOnPotionInventory_BecauseEtherConsumesPotions()
    {
        var cfg = DefaultConfig(); // 2000 MP

        Assert.True(PhantomBandRules.ShouldUseEther(cfg, currentMp: 1500, maxMp: 10000, potionCount: 3, inCombat: true));
        Assert.False(PhantomBandRules.ShouldUseEther(cfg, currentMp: 1500, maxMp: 10000, potionCount: 0, inCombat: true));
        Assert.False(PhantomBandRules.ShouldUseEther(cfg, currentMp: 5000, maxMp: 10000, potionCount: 3, inCombat: true));
        // Jobs without an MP pool must never trigger ether usage.
        Assert.False(PhantomBandRules.ShouldUseEther(cfg, currentMp: 0, maxMp: 0, potionCount: 3, inCombat: true));
    }

    [Fact]
    public void Chakra_SplitsHpAndMpTriggers()
    {
        var cfg = DefaultConfig(); // HP 0.30, MP 3000

        Assert.True(PhantomBandRules.ShouldUseChakraForHp(cfg, 0.20f, inCombat: true));
        Assert.False(PhantomBandRules.ShouldUseChakraForHp(cfg, 0.50f, inCombat: true));
        Assert.True(PhantomBandRules.ShouldUseChakraForMp(cfg, currentMp: 2000, maxMp: 10000, inCombat: true));
        Assert.False(PhantomBandRules.ShouldUseChakraForMp(cfg, currentMp: 2000, maxMp: 0, inCombat: true));
    }

    /// <summary>
    /// Occult Cure III is a 15y AoE for 3,000 MP — it wants MULTIPLE hurt bodies, and since
    /// "injured" starts at a 95% scratch, it also wants a real dent in the party average.
    /// </summary>
    [Fact]
    public void CureIII_FiresOnTwoInjured_WithHurtPartyAverage()
    {
        Assert.True(PhantomBandRules.ShouldOccultCureIII(partyAvgHpPct: 0.60f, injuredCount: 2, inCombat: true));
        Assert.True(PhantomBandRules.ShouldOccultCureIII(partyAvgHpPct: 0.60f, injuredCount: 4, inCombat: true));
    }

    [Fact]
    public void CureIII_OneInjured_IsCureIIsJob()
    {
        Assert.False(PhantomBandRules.ShouldOccultCureIII(partyAvgHpPct: 0.60f, injuredCount: 1, inCombat: true));
    }

    [Fact]
    public void CureIII_TwoScratchedMembers_DoNotBurnTheMp()
    {
        // Two members at 94% put injuredCount at 2 while the average stays high — no cast.
        Assert.False(PhantomBandRules.ShouldOccultCureIII(partyAvgHpPct: 0.93f, injuredCount: 2, inCombat: true));
    }

    [Fact]
    public void CureIII_OutOfCombat_Holds()
    {
        Assert.False(PhantomBandRules.ShouldOccultCureIII(partyAvgHpPct: 0.60f, injuredCount: 2, inCombat: false));
    }

    [Fact]
    public void SelfMit_RequiresCombatAndLowHp()
    {
        Assert.True(PhantomBandRules.ShouldSelfMit(0.40f, inCombat: true));
        Assert.False(PhantomBandRules.ShouldSelfMit(0.40f, inCombat: false));
        Assert.False(PhantomBandRules.ShouldSelfMit(0.60f, inCombat: true));
    }

    [Fact]
    public void Interrupt_RequiresInterruptibleCastInRange()
    {
        Assert.True(PhantomBandRules.ShouldInterrupt(targetIsCasting: true, castInterruptible: true, distanceYalms: 3f, maxRangeYalms: 5f));
        Assert.False(PhantomBandRules.ShouldInterrupt(targetIsCasting: false, castInterruptible: true, distanceYalms: 3f, maxRangeYalms: 5f));
        Assert.False(PhantomBandRules.ShouldInterrupt(targetIsCasting: true, castInterruptible: false, distanceYalms: 3f, maxRangeYalms: 5f));
        Assert.False(PhantomBandRules.ShouldInterrupt(targetIsCasting: true, castInterruptible: true, distanceYalms: 8f, maxRangeYalms: 5f));
    }

    [Fact]
    public void Resuscitation_AndPray_UseConfigThresholds()
    {
        var cfg = DefaultConfig(); // resus 0.70; Pray off by default

        Assert.True(PhantomBandRules.ShouldResuscitate(cfg, 0.60f));
        Assert.False(PhantomBandRules.ShouldResuscitate(cfg, 0.80f));

        Assert.False(PhantomBandRules.ShouldPray(cfg, 0.50f)); // disabled by default
        cfg.KnightPrayAsHeal = true;
        Assert.True(PhantomBandRules.ShouldPray(cfg, 0.50f));
        Assert.False(PhantomBandRules.ShouldPray(cfg, 0.95f));
    }

    // ── Phase 4: damage band ──

    [Fact]
    public void DamageHold_OnlyHoldsWhenBurstDataExists()
    {
        // Solo field farming: no burst window ever observed (-1) → never hold.
        Assert.False(PhantomBandRules.ShouldHoldDamage(saveForBurst: true, inBurstWindow: false, secondsSinceLastBurstStart: -1f));
        // Burst data exists and we're between windows → hold.
        Assert.True(PhantomBandRules.ShouldHoldDamage(saveForBurst: true, inBurstWindow: false, secondsSinceLastBurstStart: 45f));
        // Inside the window → fire.
        Assert.False(PhantomBandRules.ShouldHoldDamage(saveForBurst: true, inBurstWindow: true, secondsSinceLastBurstStart: 0f));
        // Config off → never hold.
        Assert.False(PhantomBandRules.ShouldHoldDamage(saveForBurst: false, inBurstWindow: false, secondsSinceLastBurstStart: 45f));
    }

    [Fact]
    public void Steal_IsAnExecute_Below25Percent()
    {
        Assert.True(PhantomBandRules.ShouldSteal(0.20f));
        Assert.False(PhantomBandRules.ShouldSteal(0.30f));
    }

    [Fact]
    public void PhantomKick_RespectsConfiguredDashCap()
    {
        Assert.True(PhantomBandRules.ShouldPhantomKick(distanceYalms: 4f, maxRangeYalms: 5f));
        Assert.False(PhantomBandRules.ShouldPhantomKick(distanceYalms: 9f, maxRangeYalms: 5f));
    }

    /// <summary>
    /// The RSR parity set is still covered in full — it has just been SPLIT by mechanism rather
    /// than dropped. RSR gates its own GCD chain on these, so lumping them together was a fair
    /// starting point; the difference is that our layer also fires oGCDs, and holding those for
    /// a damage buff costs something and buys nothing.
    /// </summary>
    [Fact]
    public void LockoutStatusList_CoversTheRsrParitySet()
    {
        var all = PhantomActions.LockoutStatusIds.Concat(PhantomActions.GcdHoldStatusIds).ToList();

        Assert.Equal(8, all.Count);
        Assert.Contains(3670u, all); // Reawakened
        Assert.Contains(2688u, all); // Overheated
        Assert.Contains(1177u, all); // Inner Release
        Assert.Contains(2606u, all); // Eukrasia
        Assert.Contains(496u, all);  // Mudra
        Assert.Contains(1186u, all); // Ten Chi Jin
        Assert.Contains(851u, all);  // Reassembled
        Assert.Contains(3866u, all); // Full Metal Field ready
    }

    // ── Necromancer Deep Freeze (North Horn): a SUICIDE gate, not a DPS gate. Costs 10% max
    //    HP and Dooms the caster 10s, cleared only by a heal to FULL (Oracle False Prediction
    //    precedent — an unattended toon that can't clear the timer just dies). ──

    private static PhantomConfig DeepFreezeOptedIn() => new()
    {
        NecromancerUseDeepFreeze = true,
        NecromancerDeepFreezeRequireDrainTouch = true,
        NecromancerDeepFreezeMinHpPercent = 0.95f,
    };

    [Fact]
    public void DeepFreeze_OffByDefault_NeverFires()
    {
        var cfg = new PhantomConfig();
        Assert.False(cfg.NecromancerUseDeepFreeze);
        Assert.False(PhantomBandRules.ShouldFireDoomNuke(cfg, selfHpPct: 1f, hasDoom: false, hasDrainTouchBuff: true));
    }

    [Fact]
    public void DeepFreeze_AllConditionsMet_Fires()
    {
        Assert.True(PhantomBandRules.ShouldFireDoomNuke(
            DeepFreezeOptedIn(), selfHpPct: 1f, hasDoom: false, hasDrainTouchBuff: true));
    }

    [Fact]
    public void DeepFreeze_NeverStacksASecondDeathTimer()
    {
        Assert.False(PhantomBandRules.ShouldFireDoomNuke(
            DeepFreezeOptedIn(), selfHpPct: 1f, hasDoom: true, hasDrainTouchBuff: true));
    }

    [Fact]
    public void DeepFreeze_HeldBelowTheHpFloor()
    {
        // 94% with a 95% floor: the 10% cost would land at ~84% with a 10s clock running.
        Assert.False(PhantomBandRules.ShouldFireDoomNuke(
            DeepFreezeOptedIn(), selfHpPct: 0.94f, hasDoom: false, hasDrainTouchBuff: true));
    }

    [Fact]
    public void DeepFreeze_HeldWithoutDrainTouch_WhenRequired()
    {
        Assert.False(PhantomBandRules.ShouldFireDoomNuke(
            DeepFreezeOptedIn(), selfHpPct: 1f, hasDoom: false, hasDrainTouchBuff: false));
    }

    [Fact]
    public void DeepFreeze_DrainTouchRequirementCanBeWaived_ButOtherGatesHold()
    {
        var cfg = DeepFreezeOptedIn();
        cfg.NecromancerDeepFreezeRequireDrainTouch = false;

        Assert.True(PhantomBandRules.ShouldFireDoomNuke(cfg, 1f, hasDoom: false, hasDrainTouchBuff: false));
        // Doom and the HP floor are NOT waivable — they are the death conditions.
        Assert.False(PhantomBandRules.ShouldFireDoomNuke(cfg, 1f, hasDoom: true, hasDrainTouchBuff: false));
        Assert.False(PhantomBandRules.ShouldFireDoomNuke(cfg, 0.5f, hasDoom: false, hasDrainTouchBuff: false));
    }

    [Fact]
    public void DeepFreeze_HpFloorIsClampedToSaneRange()
    {
        var cfg = new PhantomConfig { NecromancerDeepFreezeMinHpPercent = 0.1f };
        Assert.Equal(0.5f, cfg.NecromancerDeepFreezeMinHpPercent); // never below half HP
        cfg.NecromancerDeepFreezeMinHpPercent = 2f;
        Assert.Equal(1f, cfg.NecromancerDeepFreezeMinHpPercent);
    }

    // ── North Horn element pickers ──

    [Fact]
    public void NecromancerTrio_FiresTheElementTheTargetIsWeakTo()
    {
        // Deep Freeze / Hell Wind / Chaos Drive share ONE 40s recast, so this is an exclusive
        // choice: 520 potency instead of 400 under Drain Touch.
        Assert.Equal(PhantomBandRules.DeepFreezeId,
            PhantomBandRules.SelectElementalNuke(Daedalus.Services.Occult.OccultElement.Ice));
        Assert.Equal(PhantomBandRules.HellWindId,
            PhantomBandRules.SelectElementalNuke(Daedalus.Services.Occult.OccultElement.Wind));
        Assert.Equal(PhantomBandRules.ChaosDriveId,
            PhantomBandRules.SelectElementalNuke(Daedalus.Services.Occult.OccultElement.Lightning));
    }

    [Fact]
    public void NecromancerTrio_UnknownOrUnmatchedWeakness_FallsBackNotBlocks()
    {
        // Unknown must never mean "fire nothing"; fire weakness has no nuke in this kit.
        Assert.Equal(PhantomBandRules.DeepFreezeId, PhantomBandRules.SelectElementalNuke(null));
        Assert.Equal(PhantomBandRules.DeepFreezeId,
            PhantomBandRules.SelectElementalNuke(Daedalus.Services.Occult.OccultElement.Fire));
    }

    [Fact]
    public void BlackMageOrder_PutsEveryMatchedElementFirst()
    {
        var E = typeof(Daedalus.Services.Occult.OccultElement);
        // Dual weakness (field: Crescent Soblyn showed two at once) — BOTH matched nukes must
        // outrank the unmatched one, not just whichever the picker happened to check first.
        var order = PhantomBandRules.BlackMageNukeOrder(
            Daedalus.Services.Occult.OccultElement.Ice | Daedalus.Services.Occult.OccultElement.Lightning);

        Assert.Equal(3, order.Length);
        Assert.Equal(PhantomBandRules.OccultFireIIIId, order[2]); // the only unmatched one, last
        Assert.Contains(PhantomBandRules.OccultBlizzardIIIId, order[..2].ToArray());
        Assert.Contains(PhantomBandRules.OccultThunderIIIId, order[..2].ToArray());
    }

    [Fact]
    public void BlackMageOrder_AlwaysFiresAllThree()
    {
        // Independent 40s recasts — the weakness reorders, it never skips.
        foreach (var w in new Daedalus.Services.Occult.OccultElement?[]
                 { null, Daedalus.Services.Occult.OccultElement.Fire, Daedalus.Services.Occult.OccultElement.Wind })
        {
            Assert.Equal(3, PhantomBandRules.BlackMageNukeOrder(w).Distinct().Count());
        }
    }

    [Fact]
    public void NinjaScrolls_LeadWithTheMatchingElement_ButBothStayUsable()
    {
        // Independent 60s recasts, so this is ordering, not exclusion (195 vs 150).
        Assert.Equal(PhantomBandRules.FlameScrollId,
            PhantomBandRules.PreferredScroll(Daedalus.Services.Occult.OccultElement.Fire));
        Assert.Equal(PhantomBandRules.LightningScrollId,
            PhantomBandRules.PreferredScroll(Daedalus.Services.Occult.OccultElement.Lightning));
        Assert.Equal(PhantomBandRules.LightningScrollId, PhantomBandRules.PreferredScroll(null));
    }

    [Fact]
    public void PhantomNinja_KitIsCataloged()
    {
        var nin = PhantomActions.ForJob(PhantomJob.PhantomNinja);
        Assert.Contains(nin, a => a.ActionId == 49062 && a.RequiredLevel == 1); // Fuma Shuriken
        Assert.Contains(nin, a => a.ActionId == 49063 && a.RequiredLevel == 2); // Smoke
        Assert.Contains(nin, a => a.ActionId == 49064 && a.RequiredLevel == 3); // Lightning Scroll
        Assert.Contains(nin, a => a.ActionId == 49065 && a.RequiredLevel == 4); // Flame Scroll
        Assert.Contains(nin, a => a.ActionId == 49066 && a.RequiredLevel == 6); // Image
        // Lv.5 is the First Strike TRAIT — passive, never an action.
        Assert.DoesNotContain(nin, a => a.RequiredLevel == 5);
    }

    [Fact]
    public void Necromancer_StatusIds_MatchTheSheets()
    {
        // XIVAPI 2026-07-31: 5326 Drain Touch (self HP-floor buff), 1769 the Doom variant whose
        // description is "dissipates once fully healed", 5323 Ice Weakness (Deep Freeze bonus).
        Assert.Equal(5326u, PhantomActions.StatusIds.DrainTouch);
        Assert.Equal(1769u, PhantomActions.StatusIds.DoomDispelledByFullHeal);
        Assert.Equal(5323u, PhantomActions.StatusIds.IceWeakness);
    }

    /// <summary>
    /// Field 2026-08-11: a Warrior running Phantom Red Mage showed "held — lockout status 1177"
    /// with nothing ever fired. 1177 is Inner Release, a WAR damage buff — it replaces no
    /// hotbar and blocks no action, so treating it as a hard lock stopped the ENTIRE layer for
    /// 15s of every minute. That included Occult Libra, an oGCD costing no GCD at all, which is
    /// the only thing that reveals elemental weaknesses — so it quietly starved the weakness
    /// table on the very job that gathers it.
    /// </summary>
    [Fact]
    public void InnerRelease_IsAGcdHold_NotAHardLockout()
    {
        Assert.DoesNotContain(1177u, PhantomActions.LockoutStatusIds);
        Assert.Contains(1177u, PhantomActions.GcdHoldStatusIds);
    }

    /// <summary>Hard locks are hotbar/chain takeovers; nothing may be in both lists.</summary>
    [Fact]
    public void LockoutAndGcdHoldLists_AreDisjoint()
    {
        Assert.Empty(PhantomActions.LockoutStatusIds.Intersect(PhantomActions.GcdHoldStatusIds));
    }

    [Theory]
    [InlineData(3670u)] // Reawakened (VPR)
    [InlineData(2688u)] // Overheated (MCH)
    [InlineData(2606u)] // Eukrasia (SGE)
    [InlineData(496u)]  // Mudra (NIN)
    [InlineData(1186u)] // Ten Chi Jin (NIN)
    public void RealHotbarTakeovers_StayHardLockouts(uint statusId)
    {
        Assert.Contains(statusId, PhantomActions.LockoutStatusIds);
    }

    /// <summary>Red Mage's trio share one recast, so the target's weakness picks which fires.</summary>
    [Theory]
    [InlineData(OccultElement.Lightning, PhantomBandRules.OccultThunderIIId)]
    [InlineData(OccultElement.Ice, PhantomBandRules.OccultBlizzardIIId)]
    [InlineData(OccultElement.Fire, PhantomBandRules.OccultFireIIId)]
    [InlineData(OccultElement.Wind, PhantomBandRules.OccultFireIIId)]  // no wind nuke in the kit
    [InlineData(null, PhantomBandRules.OccultFireIIId)]                // unknown -> fallback
    public void RedMageNuke_FollowsTheRevealedWeakness(OccultElement? weakness, uint expected)
    {
        Assert.Equal(expected, PhantomBandRules.SelectRedMageNuke(weakness));
    }

    /// <summary>
    /// The movement pause must default ON — it exists because an entirely hard-cast phantom kit
    /// is otherwise silent on a moving job (field 2026-08-11: four minutes, zero phantom nukes).
    /// </summary>
    [Fact]
    public void PhantomCastPause_DefaultsOn()
    {
        Assert.True(DefaultConfig().PauseMovementForPhantomCasts);
    }

    /// <summary>
    /// The shared hold is expiry-driven so it can never stick, and requests only ever EXTEND it.
    /// That is what lets the Plugin-side watcher release it on danger and have the release hold.
    /// </summary>
    [Fact]
    public void CastHold_IsExpiryDriven_AndExtendsRatherThanShortens()
    {
        Daedalus.Services.Positional.RaiseCastHold.Clear();
        Assert.False(Daedalus.Services.Positional.RaiseCastHold.Active);

        Daedalus.Services.Positional.RaiseCastHold.Request(30f);
        Assert.True(Daedalus.Services.Positional.RaiseCastHold.Active);

        // A shorter request must not cut an existing longer hold short.
        Daedalus.Services.Positional.RaiseCastHold.Request(0.001f);
        Assert.True(Daedalus.Services.Positional.RaiseCastHold.Active);

        // ...and an explicit release always wins, which is the danger bail.
        Daedalus.Services.Positional.RaiseCastHold.Clear();
        Assert.False(Daedalus.Services.Positional.RaiseCastHold.Active);
    }

    /// <summary>
    /// Field 2026-08-11: the catalog had Occult Thunder II at phantom Lv.6 while a Lv.5 Red Mage
    /// had it SLOTTED on the duty bar — you cannot slot what you have not unlocked. The level
    /// gate refuses silently, so on every Lightning-weak target the picker chose Thunder, the
    /// push vanished, and the Duty tab read "idle — nothing eligible" for 13 minutes.
    /// </summary>
    [Fact]
    public void RedMageKit_UnlocksOneThroughFive()
    {
        var rdm = PhantomActions.All
            .Where(a => a.Job == PhantomJob.PhantomRedMage)
            .Select(a => a.RequiredLevel)
            .OrderBy(l => l)
            .ToList();

        Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, rdm);
    }

    /// <summary>
    /// One refusal must not mean zero damage. The trio share a recast, so all three are pushed
    /// best-match-first and whichever the gates accept is the one that fires.
    /// </summary>
    [Fact]
    public void RedMageNukeOrder_LeadsWithTheMatchThenFallsBack()
    {
        var lightning = PhantomBandRules.RedMageNukeOrder(OccultElement.Lightning);
        Assert.Equal(PhantomBandRules.OccultThunderIIId, lightning[0]);
        Assert.Equal(3, lightning.Length);
        Assert.Contains(PhantomBandRules.OccultFireIIId, lightning);
        Assert.Contains(PhantomBandRules.OccultBlizzardIIId, lightning);

        Assert.Equal(PhantomBandRules.OccultBlizzardIIId, PhantomBandRules.RedMageNukeOrder(OccultElement.Ice)[0]);
        Assert.Equal(PhantomBandRules.OccultFireIIId, PhantomBandRules.RedMageNukeOrder(OccultElement.Fire)[0]);

        // Wind has no nuke here, and unknown has nothing to match — Fire leads either way
        // because it is the earliest unlock and so the likeliest to actually be usable.
        Assert.Equal(PhantomBandRules.OccultFireIIId, PhantomBandRules.RedMageNukeOrder(OccultElement.Wind)[0]);
        Assert.Equal(PhantomBandRules.OccultFireIIId, PhantomBandRules.RedMageNukeOrder(null)[0]);

        // Every order must still offer all three, or the fallback is not a fallback.
        foreach (var w in new OccultElement?[] { OccultElement.Fire, OccultElement.Ice, OccultElement.Lightning, OccultElement.Wind, null })
            Assert.Equal(3, PhantomBandRules.RedMageNukeOrder(w).Distinct().Count());
    }

    /// <summary>
    /// Field 2026-08-11: a Lv.4 Phantom Knight with Occult Heal slotted never healed once,
    /// because the action was wired to no band at all. It is an INSTANT oGCD on a 5s recast, so
    /// the threshold is generous — the mistake is sitting on a free heal while chipped.
    /// </summary>
    [Fact]
    public void KnightOccultHeal_FiresGenerously_ButOnlyInCombat()
    {
        var cfg = DefaultConfig();

        Assert.Equal(0.85f, cfg.KnightHealHpPct);
        Assert.True(PhantomBandRules.ShouldOccultHeal(cfg, 0.80f, inCombat: true));
        Assert.False(PhantomBandRules.ShouldOccultHeal(cfg, 0.90f, inCombat: true));
        Assert.False(PhantomBandRules.ShouldOccultHeal(cfg, 0.10f, inCombat: false));
    }

    /// <summary>
    /// Occult Heal needs no toggle because it costs a weave slot; Pray does because it is a
    /// weaponskill and costs a GCD. That asymmetry is the whole reason they are gated differently.
    /// </summary>
    [Fact]
    public void KnightHeal_IsAlwaysOn_WhilePrayStaysOptIn()
    {
        var cfg = DefaultConfig();

        Assert.True(PhantomBandRules.ShouldOccultHeal(cfg, 0.50f, inCombat: true));
        Assert.False(PhantomBandRules.ShouldPray(cfg, 0.50f));
    }

    /// <summary>
    /// Pledge is a real INVULNERABILITY ("impervious to most attacks", 10s, 120s recast), so it
    /// must gate far lower than the heal — spending a two-minute death-saver on chip damage
    /// wastes it. It was also a DEAD toggle until 2026-08-11: nothing pushed Pledge at all, so
    /// neither setting did anything.
    /// </summary>
    [Fact]
    public void KnightPledge_IsALastResort_AndOnByDefault()
    {
        var cfg = DefaultConfig();

        Assert.True(cfg.KnightPledgeSelf, "a dead invuln helps nobody");
        Assert.Equal(0.30f, cfg.KnightPledgeHpPct);

        Assert.True(PhantomBandRules.ShouldPledge(cfg, 0.20f, inCombat: true));
        Assert.False(PhantomBandRules.ShouldPledge(cfg, 0.50f, inCombat: true));
        Assert.False(PhantomBandRules.ShouldPledge(cfg, 0.20f, inCombat: false));

        cfg.KnightPledgeSelf = false;
        Assert.False(PhantomBandRules.ShouldPledge(cfg, 0.20f, inCombat: true));
    }

    /// <summary>
    /// Slowga is the whole of a Lv.1 Time Mage — Comet needs Lv.2 — so it must fire by default,
    /// and it must stop once the Slow is actually up or a 2.5s GCD spell that deals no damage
    /// would be pressed every single GCD.
    /// </summary>
    [Fact]
    public void TimeMageSlowga_FiresByDefault_AndYieldsToAnAlreadySlowedTarget()
    {
        var cfg = DefaultConfig();

        Assert.True(cfg.TimeMageUseSlowga);

        Assert.True(PhantomBandRules.ShouldSlowga(cfg, true, targetAlreadySlowed: false, targetIsCriticalEncounterMob: false));
        Assert.False(PhantomBandRules.ShouldSlowga(cfg, true, targetAlreadySlowed: true, targetIsCriticalEncounterMob: false));
        Assert.False(PhantomBandRules.ShouldSlowga(cfg, false, targetAlreadySlowed: false, targetIsCriticalEncounterMob: false));

        cfg.TimeMageUseSlowga = false;
        Assert.False(PhantomBandRules.ShouldSlowga(cfg, true, targetAlreadySlowed: false, targetIsCriticalEncounterMob: false));
    }

    /// <summary>
    /// The bug this gate exists for. Slowga is paced on the target NOT already being slowed, so
    /// against something that CANNOT be slowed the pacing never engages and a zero-damage 2.5s
    /// GCD spell is re-cast for the entire encounter. Critical-encounter enemies are exactly
    /// that, which is why RSR excludes them from Slowga's targets outright.
    /// </summary>
    [Fact]
    public void TimeMageSlowga_NeverTargetsACriticalEncounterEnemy()
    {
        var cfg = DefaultConfig();

        Assert.False(PhantomBandRules.ShouldSlowga(
            cfg, inCombat: true, targetAlreadySlowed: false, targetIsCriticalEncounterMob: true));

        // ...and it stays refused no matter how many frames go by, because the status that would
        // normally stop the re-cast can never appear.
        for (var frame = 0; frame < 50; frame++)
        {
            Assert.False(PhantomBandRules.ShouldSlowga(
                cfg, inCombat: true, targetAlreadySlowed: false, targetIsCriticalEncounterMob: true));
        }
    }

    /// <summary>
    /// Occult Missile's tooltip says "with some exceptions" and names none. RSR's answer is that
    /// critical-encounter and FATE enemies shrug it off, so the GCD is wasted there.
    /// </summary>
    [Fact]
    public void OccultMissile_SkipsCriticalEncounterAndFateEnemies()
    {
        Assert.True(PhantomBandRules.ShouldMissile(false, false));
        Assert.False(PhantomBandRules.ShouldMissile(targetIsCriticalEncounterMob: true, targetIsFateMob: false));
        Assert.False(PhantomBandRules.ShouldMissile(targetIsCriticalEncounterMob: false, targetIsFateMob: true));
        Assert.False(PhantomBandRules.ShouldMissile(true, true));
    }

    /// <summary>
    /// Dualcast beats everything: it is on a 15s clock the main job's next weaponskill cuts
    /// short, so a free instant nuke has to go out now or not at all.
    /// </summary>
    [Fact]
    public void RedMagePlan_SpendsDualcastAheadOfEverythingElse()
    {
        // Even with no weakness, no MP and priming off, an active Dualcast is spent.
        Assert.Equal(
            PhantomBandRules.RedMagePlan.SpendDualcast,
            PhantomBandRules.PlanRedMage(
                hasDualcast: true, phantomLevel: 6, weaknessKnown: false, nukeReady: false,
                cureReady: false, currentMp: 0, primeEnabled: false,
                mpFloor: PhantomBandRules.DualcastPrimeMpFloor));
    }

    /// <summary>
    /// The primer is narrow on purpose — a whole GCD and 1,500 MP only pays when the follow-up is
    /// the weakness-matched nuke. Every one of these conditions dropping the plan back to a plain
    /// hard cast is the point.
    /// </summary>
    [Theory]
    // level, weaknessKnown, nukeReady, cureReady, mp, primeEnabled, expectPrime
    [InlineData(6, true, true, true, 9000, true, true)]
    [InlineData(5, true, true, true, 9000, true, false)]   // trait not learned yet
    [InlineData(6, false, true, true, 9000, true, false)]  // unidentified target — no bonus to protect
    [InlineData(6, true, false, true, 9000, true, false)]  // nuke on its 30s recast — Dualcast would lapse
    [InlineData(6, true, true, false, 9000, true, false)]  // no Cure to prime with
    [InlineData(6, true, true, true, 4999, true, false)]   // MP floor — a raise costs ~2,400
    [InlineData(6, true, true, true, 9000, false, false)]  // switched off
    public void RedMagePlan_PrimesOnlyWhenItPays(
        byte level, bool weaknessKnown, bool nukeReady, bool cureReady, int mp, bool primeEnabled, bool expectPrime)
    {
        var plan = PhantomBandRules.PlanRedMage(
            hasDualcast: false, phantomLevel: level, weaknessKnown: weaknessKnown,
            nukeReady: nukeReady, cureReady: cureReady, currentMp: mp, primeEnabled: primeEnabled,
            mpFloor: PhantomBandRules.DualcastPrimeMpFloor);

        Assert.Equal(
            expectPrime ? PhantomBandRules.RedMagePlan.PrimeWithCure : PhantomBandRules.RedMagePlan.HardcastNuke,
            plan);
    }

    /// <summary>
    /// "Worked like a charm, then sometimes straight-cast instead" is what a silently-enforced
    /// budget looks like from outside, so the STICKY blocks must be nameable. The transient ones
    /// (cure/nuke off the GCD) deliberately are not — they are false on nearly every frame and
    /// would bury the line in noise.
    /// </summary>
    [Fact]
    public void DescribePrimeBlock_NamesOnlyTheReasonsThatPersist()
    {
        const int floor = PhantomBandRules.DualcastPrimeMpFloor;

        Assert.Null(PhantomBandRules.DescribePrimeBlock(6, weaknessKnown: true, currentMp: floor, mpFloor: floor));

        Assert.Contains("Lv.6", PhantomBandRules.DescribePrimeBlock(5, true, floor, floor));
        Assert.Contains("Libra", PhantomBandRules.DescribePrimeBlock(6, false, floor, floor));
        Assert.Contains("MP", PhantomBandRules.DescribePrimeBlock(6, true, floor - 1, floor));

        // A configured floor of zero must never block, or the slider's bottom end lies.
        Assert.Null(PhantomBandRules.DescribePrimeBlock(6, weaknessKnown: true, currentMp: 0, mpFloor: 0));
        Assert.Equal(
            PhantomBandRules.RedMagePlan.PrimeWithCure,
            PhantomBandRules.PlanRedMage(false, 6, true, true, true, currentMp: 0, primeEnabled: true, mpFloor: 0));
    }

    /// <summary>
    /// SIX. The kit's five actions run Lv.1-5 with Thunder II last, so the trait sits above them —
    /// the old Lv.5 note was the mirror of the slip that had Thunder II at 6 and cost thirteen
    /// silent minutes. Gating the primer one level early would prime for a buff that never comes.
    /// </summary>
    [Fact]
    public void DualcastTrait_IsAboveTheLastAction()
    {
        Assert.Equal(6, PhantomBandRules.DualcastTraitLevel);

        var thunder = PhantomActions.All.First(a => a.ActionId == 49096);
        Assert.Equal(5, thunder.RequiredLevel);
        Assert.True(PhantomBandRules.DualcastTraitLevel > thunder.RequiredLevel);
    }

    /// <summary>
    /// The status the trait grants is INFERRED by position (nothing in the sheets links a trait to
    /// a status), so the whole Dualcast set is matched. Row 5438 is the current-patch one and must
    /// stay in.
    /// </summary>
    [Fact]
    public void DualcastStatusIds_IncludeTheCurrentPatchRow()
    {
        Assert.Contains(5438u, PhantomActions.DualcastStatusIds);
        Assert.Equal(
            PhantomActions.DualcastStatusIds.Count,
            PhantomActions.DualcastStatusIds.Distinct().Count());
    }

    /// <summary>
    /// The hold has to outlast the wait, not just the cast. Sizing it to the cast alone is what
    /// let Slowga stop, lose the GCD to the job's filler, expire, and move off again — stationary
    /// and silent at the same time.
    /// </summary>
    [Fact]
    public void PhantomCastHold_WaitsForTheGcdBeforeStopping_AndCoversTheWholeStand()
    {
        // GCD still rolling: keep moving, do not throw away the distance.
        Assert.True(PhantomBandRules.ShouldKeepMovingUntilGcd(2.4f, isGcd: true));
        Assert.True(PhantomBandRules.ShouldKeepMovingUntilGcd(0.9f, isGcd: true));

        // Nearly up: stop now, so we are still when the window opens.
        Assert.False(PhantomBandRules.ShouldKeepMovingUntilGcd(0.7f, isGcd: true));
        Assert.False(PhantomBandRules.ShouldKeepMovingUntilGcd(0f, isGcd: true));

        // An oGCD hard cast never waits on the GCD.
        Assert.False(PhantomBandRules.ShouldKeepMovingUntilGcd(2.4f, isGcd: false));

        // The stand covers the wait AND the cast — 2.1s was never going to outlast a 2.5s GCD.
        Assert.Equal(2.3f, PhantomBandRules.StillSecondsForCast(0.8f, isGcd: true, castSeconds: 1.5f), 3);
        Assert.Equal(1.5f, PhantomBandRules.StillSecondsForCast(0f, isGcd: true, castSeconds: 1.5f), 3);
        Assert.Equal(1.5f, PhantomBandRules.StillSecondsForCast(2.4f, isGcd: false, castSeconds: 1.5f), 3);
    }

    /// <summary>
    /// The Slow set is checked as a set precisely because Occult Slowga has no status of its own,
    /// so it must stay non-empty and must include the newest generic row — pinning it to one
    /// guessed id is the failure mode this guards against.
    /// </summary>
    [Fact]
    public void SlowStatusIds_CoverTheGenericRows()
    {
        Assert.NotEmpty(PhantomActions.SlowStatusIds);
        Assert.Contains(3493u, PhantomActions.SlowStatusIds);
        Assert.Contains(9u, PhantomActions.SlowStatusIds);
        Assert.Equal(PhantomActions.SlowStatusIds.Count, PhantomActions.SlowStatusIds.Distinct().Count());
    }

    /// <summary>The invuln must sit well below the heal, or it fires first and is wasted.</summary>
    [Fact]
    public void KnightPledge_GatesFarBelowTheHeal()
    {
        var cfg = DefaultConfig();
        Assert.True(cfg.KnightPledgeHpPct < cfg.KnightHealHpPct);
    }

    /// <summary>
    /// The roster is what lets the Duty tab say what is MISSING — the weakness log only knows
    /// what it has seen, so on its own it just shows a shorter list and looks complete.
    /// Both zones have 15, from DynamicEvent rows 33-47 and 49-63.
    /// </summary>
    [Fact]
    public void CriticalEncounterRoster_HasFifteenPerZone_AndNoDuplicates()
    {
        var south = PhantomActions.All is not null
            ? Daedalus.Data.OccultEncounters.SouthHornCriticalEncounters : [];
        var north = Daedalus.Data.OccultEncounters.NorthHornCriticalEncounters;

        Assert.Equal(15, south.Count);
        Assert.Equal(15, north.Count);
        Assert.Equal(south.Count, south.Distinct().Count());
        Assert.Equal(north.Count, north.Distinct().Count());
        Assert.Empty(south.Intersect(north));
    }

    [Fact]
    public void CriticalEncounterRoster_ResolvesByTerritory()
    {
        Assert.Equal(15, Daedalus.Data.OccultEncounters.CriticalEncountersFor(1252).Count);
        Assert.Equal(15, Daedalus.Data.OccultEncounters.CriticalEncountersFor(1346).Count);
        Assert.Empty(Daedalus.Data.OccultEncounters.CriticalEncountersFor(129)); // Limsa
    }
}
