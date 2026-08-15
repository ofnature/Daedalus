using System.Linq;
using Daedalus.Data;
using Daedalus.Rotation.Phantom;
using Daedalus.Services.Occult;
using Xunit;

namespace Daedalus.Tests.Rotation.Phantom;

/// <summary>
/// Unlock levels verified 2026-08-14 against the game's OWN table — <c>MKDSupportJob</c>, whose
/// <c>Action[]</c> and <c>LevelUnlock[]</c> arrays pair each phantom job's slots with the level
/// that opens them. Everything here was previously transcribed by hand from tooltips, which is
/// how Occult Thunder II sat at Lv.6 for thirteen silent minutes in the field.
/// </summary>
public sealed class PhantomLevelTableTests
{
    private static byte LevelOf(uint actionId) =>
        PhantomActions.All.First(a => a.ActionId == actionId).RequiredLevel;

    /// <summary>
    /// Necromancer's trio is spread across three levels, not all at Lv.2 as the catalog claimed.
    /// A Lv.2 Necromancer that picked Chaos Drive fired nothing at all.
    /// </summary>
    [Theory]
    [InlineData(49097u, 1)]  // Drain Touch
    [InlineData(49098u, 2)]  // Deep Freeze  — ice
    [InlineData(49099u, 3)]  // Hell Wind    — wind
    [InlineData(49100u, 4)]  // Chaos Drive  — lightning
    [InlineData(49101u, 5)]  // Doomsday
    public void Necromancer_MatchesTheGameTable(uint actionId, byte expected)
        => Assert.Equal(expected, LevelOf(actionId));

    /// <summary>Oracle's Invulnerability is Lv.5; gating it at 6 meant a Lv.5 Oracle never got it.</summary>
    [Fact]
    public void OracleInvulnerability_IsLevelFive()
        => Assert.Equal(5, LevelOf(41644));

    /// <summary>
    /// The Red Mage fix, now confirmed against the game table rather than a single field sighting.
    /// </summary>
    [Theory]
    [InlineData(49092u, 1)]  // Occult Fire II
    [InlineData(49093u, 2)]  // Occult Cure II
    [InlineData(49094u, 3)]  // Occult Libra
    [InlineData(49095u, 4)]  // Occult Blizzard II
    [InlineData(49096u, 5)]  // Occult Thunder II
    public void RedMage_MatchesTheGameTable(uint actionId, byte expected)
        => Assert.Equal(expected, LevelOf(actionId));

    /// <summary>
    /// Ninja and Cannoneer genuinely DO skip Lv.5 — their fifth slot is Lv.6 with a trait at 5.
    /// Recorded because that shape looks exactly like the Red Mage transcription slip and was
    /// suspected of being one; the game table says it is correct, so leave it alone.
    /// </summary>
    [Theory]
    [InlineData(49066u, 6)]  // Ninja — Image
    [InlineData(41630u, 6)]  // Cannoneer — Silver Cannon
    [InlineData(41591u, 6)]  // Knight — Pledge
    [InlineData(41602u, 6)]  // Ranger — Occult Unicorn
    public void FifthSlotAtSix_IsCorrectNotASlip(uint actionId, byte expected)
        => Assert.Equal(expected, LevelOf(actionId));

    /// <summary>
    /// Every shared-recast trio must offer ALL THREE, best match first. Pushing one pick meant a
    /// single refusal produced no damage — the Red Mage failure, which Necromancer and Summoner
    /// still had until 2026-08-14.
    /// </summary>
    [Fact]
    public void NecromancerNukeOrder_LeadsWithTheMatch_AndOffersTheRest()
    {
        var ice = PhantomBandRules.NecromancerNukeOrder(OccultElement.Ice);
        var wind = PhantomBandRules.NecromancerNukeOrder(OccultElement.Wind);
        var lightning = PhantomBandRules.NecromancerNukeOrder(OccultElement.Lightning);

        Assert.Equal(PhantomBandRules.DeepFreezeId, ice[0]);
        Assert.Equal(PhantomBandRules.HellWindId, wind[0]);
        Assert.Equal(PhantomBandRules.ChaosDriveId, lightning[0]);

        foreach (var order in new[] { ice, wind, lightning, PhantomBandRules.NecromancerNukeOrder(null) })
        {
            Assert.Equal(3, order.Length);
            Assert.Equal(3, order.Distinct().Count());
        }

        // Unknown weakness leads with the earliest unlock, so it is the likeliest to be usable.
        Assert.Equal(PhantomBandRules.DeepFreezeId, PhantomBandRules.NecromancerNukeOrder(null)[0]);
    }

    [Fact]
    public void SummonerNukeOrder_LeadsWithTheMatch_AndOffersTheRest()
    {
        var fire = PhantomBandRules.SummonerNukeOrder(OccultElement.Fire);
        var lightning = PhantomBandRules.SummonerNukeOrder(OccultElement.Lightning);
        var wind = PhantomBandRules.SummonerNukeOrder(OccultElement.Wind);

        Assert.Equal(PhantomBandRules.HellfireId, fire[0]);
        Assert.Equal(PhantomBandRules.JudgmentBoltId, lightning[0]);
        Assert.Equal(PhantomBandRules.ThunderstormId, wind[0]);

        foreach (var order in new[] { fire, lightning, wind, PhantomBandRules.SummonerNukeOrder(null) })
        {
            Assert.Equal(3, order.Length);
            Assert.Equal(3, order.Distinct().Count());
        }

        // The case that fired nothing: wind-weak target, Lv.3 Summoner, Thunderstorm is Lv.4.
        // Hellfire (Lv.1) must still be offered behind it.
        Assert.Contains(PhantomBandRules.HellfireId, wind);
    }

    /// <summary>The single-pick helpers must stay consistent with the orders they now delegate to.</summary>
    [Fact]
    public void SinglePickHelpers_AgreeWithTheOrders()
    {
        foreach (OccultElement? w in new OccultElement?[]
                 { null, OccultElement.Fire, OccultElement.Ice, OccultElement.Wind, OccultElement.Lightning })
        {
            Assert.Equal(PhantomBandRules.NecromancerNukeOrder(w)[0], PhantomBandRules.SelectElementalNuke(w));
            Assert.Equal(PhantomBandRules.SummonerNukeOrder(w)[0], PhantomBandRules.SelectSummonerNuke(w));
        }
    }
}
