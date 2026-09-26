using System;
using System.Collections.Generic;
using System.Linq;
using Daedalus.Config;
using Daedalus.Data;
using Daedalus.Rotation.Phantom;
using Xunit;

namespace Daedalus.Tests.Rotation.Phantom;

/// <summary>The decisions behind the Bozja Lost Action layer.</summary>
public class BozjaActionRulesTests
{
    private static LostActionDef Def(string name) => BozjaActionData.All.Single(d => d.Name == name);

    private static Func<uint, float?> Has(params (uint Id, float Remaining)[] statuses)
    {
        var map = statuses.ToDictionary(s => s.Id, s => s.Remaining);
        return id => map.TryGetValue(id, out var r) ? r : null;
    }

    // ---- buffs ----

    [Fact]
    public void NoBuff_NeedsOne_InOrOutOfCombat()
    {
        Assert.True(BozjaActionRules.NeedsBuff(Def("Lost Protect"), Has(), inCombat: false));
        Assert.True(BozjaActionRules.NeedsBuff(Def("Lost Protect"), Has(), inCombat: true));
    }

    /// <summary>Out of combat a 30-minute barrier is renewed with under 5 minutes left.</summary>
    [Fact]
    public void OutOfCombat_ThirtyMinuteBarrier_RenewedUnderFiveMinutes()
    {
        var def = Def("Lost Shell");
        Assert.True(BozjaActionRules.NeedsBuff(def, Has((BozjaActionData.LostShellStatusId, 299f)), inCombat: false));
        Assert.False(BozjaActionRules.NeedsBuff(def, Has((BozjaActionData.LostShellStatusId, 301f)), inCombat: false));
    }

    /// <summary>A 10-minute buff renews at 2 minutes, not 5 — otherwise it would be recast at half-life.</summary>
    [Fact]
    public void OutOfCombat_TenMinuteBuff_RenewedUnderTwoMinutes()
    {
        var def = Def("Lost Bravery");
        Assert.False(BozjaActionRules.NeedsBuff(def, Has((BozjaActionData.LostBraveryStatusId, 200f)), inCombat: false));
        Assert.True(BozjaActionRules.NeedsBuff(def, Has((BozjaActionData.LostBraveryStatusId, 100f)), inCombat: false));
    }

    /// <summary>In combat only a missing buff is worth a GCD.</summary>
    [Fact]
    public void InCombat_LeavesARunningBuffAlone()
        => Assert.False(BozjaActionRules.NeedsBuff(
            Def("Lost Protect"), Has((BozjaActionData.LostProtectStatusId, 10f)), inCombat: true));

    /// <summary>Tier I never overwrites tier II (15% for 10%).</summary>
    [Fact]
    public void TierOne_StandsDownForTierTwo()
        => Assert.False(BozjaActionRules.NeedsBuff(
            Def("Lost Protect"), Has((BozjaActionData.LostProtectIIStatusId, 1500f)), inCombat: false));

    /// <summary>Tier II upgrades a tier I rather than waiting for it to run out.</summary>
    [Fact]
    public void TierTwo_UpgradesTierOne()
        => Assert.True(BozjaActionRules.NeedsBuff(
            Def("Lost Shell II"), Has((BozjaActionData.LostShellStatusId, 1500f)), inCombat: false));

    [Fact]
    public void Protect_DoesNotCoverShell()
        => Assert.True(BozjaActionRules.NeedsBuff(
            Def("Lost Shell"), Has((BozjaActionData.LostProtectStatusId, 1500f)), inCombat: false));

    /// <summary>Lost Chainspell stands down under any instant-cast buff (RSR's SwiftcastStatus list).</summary>
    [Theory]
    [InlineData(167u)]   // Swiftcast
    [InlineData(1249u)]  // Dualcast
    [InlineData(2560u)]  // Lost Chainspell itself
    public void Chainspell_StandsDownUnderInstantCast(uint statusId)
        => Assert.False(BozjaActionRules.NeedsBuff(Def("Lost Chainspell"), Has((statusId, 5f)), inCombat: true));

    /// <summary>Stoneskin and Stoneskin II share one status: either covers both.</summary>
    [Fact]
    public void Stoneskin_TiersShareTheStatus()
        => Assert.False(BozjaActionRules.NeedsBuff(
            Def("Lost Stoneskin II"), Has((BozjaActionData.StoneskinStatusId, 20f)), inCombat: true));

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(false, false, false)]
    public void Party_OnlyWhenBuffPartyIsOn(bool buffParty, bool isSelf, bool allowed)
        => Assert.Equal(allowed, BozjaActionRules.TargetAllowed(new BozjaConfig { BuffParty = buffParty }, isSelf));

    // ---- heals ----

    [Fact]
    public void Heal_BelowThresholdOnly()
    {
        var cfg = new BozjaConfig { HealHpPct = 0.6f };
        Assert.True(BozjaActionRules.NeedsHeal(cfg, 0.59f));
        Assert.False(BozjaActionRules.NeedsHeal(cfg, 0.6f));
    }

    /// <summary>Four-man parties exist in Bozja too: an area heal needs 2 hurt, never 3.</summary>
    [Fact]
    public void AreaHeal_NeedsTwo()
        => Assert.Equal(2, BozjaActionRules.AreaHealMinTargets);

    // ---- forges ----

    /// <summary>
    /// The aversion is on the ENEMY. RSR checks it on the friendly it buffs, which never has it, so RSR's
    /// forge never fires; this is the intended rule.
    /// </summary>
    [Theory]
    // spellforge, member deals magic, enemy magic-averse, enemy physical-averse, expected
    [InlineData(true, false, true, false, true)]    // physical attacker vs magic-averse: Spellforge
    [InlineData(true, true, true, false, false)]    // a caster already deals magic
    [InlineData(true, false, false, true, false)]   // wrong aversion
    [InlineData(false, true, false, true, true)]    // caster vs physical-averse: Steelsting
    [InlineData(false, false, false, true, false)]  // a physical attacker already deals physical
    [InlineData(false, true, true, false, false)]   // wrong aversion
    public void Forge_GoesToWhoeverIsDealingTheWrongDamage(bool spellforge, bool magic, bool magAverse, bool physAverse, bool wants)
        => Assert.Equal(wants, BozjaActionRules.WantsForge(spellforge, magic, magAverse, physAverse));

    // ---- Burst / Rampage ----

    [Theory]
    [InlineData(true, 2, false, false, true)]   // RSR default: AoE damage on 2+ enemies, aversion ignored
    [InlineData(true, 1, true, false, false)]   // ...but not on one
    [InlineData(false, 5, true, false, true)]   // strict: an averse target without the debuff
    [InlineData(false, 5, true, true, false)]   // strict: already debuffed
    [InlineData(false, 5, false, false, false)] // strict: not averse
    public void AversionAoe_TwoModes(bool spam, int enemies, bool averse, bool debuffed, bool fire)
        => Assert.Equal(fire, BozjaActionRules.ShouldAversionAoe(spam, enemies, averse, debuffed));

    // ---- Seraph Strike ----

    [Theory]
    [InlineData(false, false, true, true)]
    [InlineData(true, false, true, false)]   // healer: Cleric Stance cuts healing by 60%
    [InlineData(false, true, true, false)]   // already in Cleric Stance
    [InlineData(false, false, false, false)] // unsafe landing
    public void SeraphStrike_Gate(bool healer, bool stance, bool dashSafe, bool allowed)
        => Assert.Equal(allowed, BozjaActionRules.SeraphAllowed(healer, stance, dashSafe));

    // ---- GCD pre-empt ----

    [Theory]
    [InlineData(false, false, false, false, true)] // out of combat, anyone
    [InlineData(true, false, false, false, true)]  // DPS/tank in combat (RSR: duty GCDs before the job's)
    [InlineData(true, true, false, false, false)]  // healer in combat: free GCDs only
    [InlineData(false, true, true, false, false)]  // a body the job can raise comes first
    [InlineData(true, true, true, true, true)]     // ...unless the raise is our own Lost Arise
    public void WhenTheLayerMayTakeTheGcd(bool inCombat, bool healer, bool raisePending, bool raiseQueued, bool may)
        => Assert.Equal(may, BozjaActionRules.MayPreemptGcd(inCombat, healer, raisePending, raiseQueued));
}

/// <summary>The Lost Action table against the game data and RSR's rotation it was read from.</summary>
public class BozjaActionDataTests
{
    /// <summary>Exactly the 29 actions RSR's "Bozja Reborn" rotation uses (BozjaDefault.cs).</summary>
    [Fact]
    public void CoversRsrsBozjaRotation()
    {
        string[] rsr =
        [
            "Banner of Honed Acuity", "Banner of Honored Sacrifice", "Lost Font of Power", "Lost Font of Magic",
            "Lost Focus", "Lost Chainspell", "Lost Cure II", "Lost Full Cure", "Lost Stoneskin", "Lost Stoneskin II",
            "Lost Cure", "Lost Cure III", "Lost Cure IV", "Lost Arise", "Lost Sacrifice", "Lost Seraph Strike",
            "Lost Slash", "Lost Spellforge", "Lost Steelsting", "Lost Burst", "Lost Rampage", "Lost Bravery",
            "Lost Bubble", "Lost Shell II", "Lost Shell", "Lost Protect II", "Lost Protect", "Lost Flare Star",
            "Banner of Solemn Clarity",
        ];
        Assert.Equal(rsr.OrderBy(n => n), BozjaActionData.All.Select(d => d.Name).OrderBy(n => n));
    }

    [Fact]
    public void ActionIdsAreUnique()
        => Assert.Equal(BozjaActionData.All.Count, BozjaActionData.All.Select(d => d.ActionId).Distinct().Count());

    /// <summary>
    /// GCD follows the recast group, not the category: Lost Focus is an "Ability" that shares the GCD, and
    /// Lost Slash is a "Weaponskill" on its own 90s timer.
    /// </summary>
    [Theory]
    [InlineData("Lost Focus", true)]
    [InlineData("Lost Slash", false)]
    [InlineData("Lost Cure IV", false)]
    [InlineData("Lost Burst", true)]
    public void GcdFollowsTheRecastGroup(string name, bool isGcd)
        => Assert.Equal(isGcd, BozjaActionData.All.Single(d => d.Name == name).IsGcd);

    /// <summary>Tier II listed (so prioritised) ahead of tier I.</summary>
    [Fact]
    public void TierTwoComesFirst()
    {
        var names = BozjaActionData.All.Select(d => d.Name).ToList();
        Assert.True(names.IndexOf("Lost Protect II") < names.IndexOf("Lost Protect"));
        Assert.True(names.IndexOf("Lost Shell II") < names.IndexOf("Lost Shell"));
    }

    /// <summary>A raise outranks everything; weave heals outrank GCD heals.</summary>
    [Fact]
    public void RaisesThenWeaveHealsLead()
    {
        var names = BozjaActionData.All.Select(d => d.Name).ToList();
        Assert.Equal("Lost Arise", names[0]);
        Assert.True(names.IndexOf("Lost Cure II") < names.IndexOf("Lost Cure"));
        Assert.True(names.IndexOf("Lost Cure IV") < names.IndexOf("Lost Cure III"));
    }

    /// <summary>Only the two with a real downside start switched off.</summary>
    [Fact]
    public void OffByDefault_OnlySacrificeAndSolemnClarity()
        => Assert.Equal(
            new[] { "Banner of Solemn Clarity", "Lost Sacrifice" },
            BozjaActionData.All.Where(d => !d.DefaultEnabled).Select(d => d.Name).OrderBy(n => n));

    [Fact]
    public void EveryPartyBuffHasARefreshWindow()
        => Assert.All(BozjaActionData.All.Where(d => d.Role == LostRole.PartyBuff),
            d => Assert.True(d.RefreshOutOfCombatSeconds > 0, d.Name));

    [Fact]
    public void Find_UnknownIsNull()
        => Assert.Null(BozjaActionData.Find(120));
}

public class BozjaConfigTests
{
    private static LostActionDef Def(string name) => BozjaActionData.All.Single(d => d.Name == name);

    [Fact]
    public void Default_FollowsTheTable()
    {
        var cfg = new BozjaConfig();
        Assert.True(cfg.IsEnabled(Def("Lost Arise")));
        Assert.False(cfg.IsEnabled(Def("Lost Sacrifice")));
    }

    /// <summary>Only changes are stored, both ways.</summary>
    [Fact]
    public void Override_WinsBothWays()
    {
        var cfg = new BozjaConfig();
        cfg.ActionEnabled[Def("Lost Arise").ActionId] = false;
        cfg.ActionEnabled[Def("Lost Sacrifice").ActionId] = true;
        Assert.False(cfg.IsEnabled(Def("Lost Arise")));
        Assert.True(cfg.IsEnabled(Def("Lost Sacrifice")));
    }

    /// <summary>
    /// A saved config round-trips without a pre-filled default being merged back in (why this is a
    /// dictionary of overrides and not a pre-populated set).
    /// </summary>
    [Fact]
    public void SavedOverridesSurviveALoad()
    {
        var cfg = new BozjaConfig();
        cfg.ActionEnabled[Def("Lost Arise").ActionId] = false;
        var json = Newtonsoft.Json.JsonConvert.SerializeObject(cfg);
        var loaded = Newtonsoft.Json.JsonConvert.DeserializeObject<BozjaConfig>(json)!;
        Assert.False(loaded.IsEnabled(Def("Lost Arise")));
        Assert.Single(loaded.ActionEnabled);
    }
}
