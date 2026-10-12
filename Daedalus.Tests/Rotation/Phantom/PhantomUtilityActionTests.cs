using System.Linq;
using Daedalus.Data;
using Daedalus.Rotation.Phantom;
using Xunit;

namespace Daedalus.Tests.Rotation.Phantom;

/// <summary>
/// Six phantom actions the layer never pressed, plus the Treasuresight setting nothing read. Numbers are the
/// game's own (Action / ActionTransient / Status sheets, read 2026-10-11).
/// </summary>
public class PhantomUtilityActionTests
{
    // ── Occult Unicorn (Ranger): 40,000 party barrier ────────────────────────────────────

    [Theory]
    [InlineData(true, false, 0.40f, 1.00f, true)]   // the Ranger is in trouble
    [InlineData(true, false, 1.00f, 0.60f, true)]   // the party is, the Ranger is not
    [InlineData(true, false, 0.90f, 0.90f, false)]  // nobody is
    [InlineData(true, true, 0.40f, 0.60f, false)]   // already up
    [InlineData(false, false, 0.40f, 0.60f, false)] // out of combat
    public void Unicorn(bool inCombat, bool barrierUp, float selfHp, float partyAvg, bool expected)
        => Assert.Equal(expected, PhantomBandRules.ShouldUnicorn(inCombat, barrierUp, selfHp, partyAvg));

    // ── Phantom Rejuvenation (Oracle): the heal lands 20s later, so it starts early ──────

    [Theory]
    [InlineData(true, false, 1.00f, 0.85f, true)]   // someone is chipped
    [InlineData(true, false, 0.85f, 1.00f, true)]   // the Oracle is
    [InlineData(true, false, 1.00f, 1.00f, false)]  // nothing to heal
    [InlineData(true, true, 0.50f, 0.50f, false)]   // one is already ticking down
    [InlineData(false, false, 0.50f, 0.50f, false)] // out of combat
    public void Rejuvenation(bool inCombat, bool alreadyUp, float selfHp, float lowest, bool expected)
        => Assert.Equal(expected, PhantomBandRules.ShouldRejuvenate(inCombat, alreadyUp, selfHp, lowest));

    // ── Recuperation (Oracle): cleanses 20s after the cast ───────────────────────────────

    [Fact]
    public void Recuperation_ForADebuffThatOutlastsTheDelay()
        => Assert.True(PhantomBandRules.ShouldRecuperate(alreadyUp: false, debuffSecondsLeft: 45f));

    /// <summary>A 15s debuff is gone five seconds before the cleanse arrives.</summary>
    [Fact]
    public void Recuperation_NotForADebuffThatEndsFirst()
        => Assert.False(PhantomBandRules.ShouldRecuperate(false, PhantomBandRules.RecuperationDelaySeconds - 5f));

    [Fact]
    public void Recuperation_ForADebuffWithNoTimer()
        => Assert.True(PhantomBandRules.ShouldRecuperate(false, 0f));

    [Fact]
    public void Recuperation_NotWithoutADebuff()
        => Assert.False(PhantomBandRules.ShouldRecuperate(false, null));

    [Fact]
    public void Recuperation_NotWhileOneIsRunning()
        => Assert.False(PhantomBandRules.ShouldRecuperate(alreadyUp: true, 45f));

    // ── Phantom Doom (Oracle): a chance to kill ──────────────────────────────────────────

    [Fact]
    public void Doom_OnAnOrdinaryEnemy()
        => Assert.True(PhantomBandRules.ShouldPhantomDoom(true, false, false, false, 0, 60f));

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Doom_NotOnCriticalEncounterOrFateEnemies(bool ce, bool fate)
        => Assert.False(PhantomBandRules.ShouldPhantomDoom(true, false, ce, fate, 0, 60f));

    /// <summary>It only has a chance to land, so a miss is retried — but not for ever on something immune.</summary>
    [Fact]
    public void Doom_RetriesAFewTimesThenStops()
    {
        Assert.True(PhantomBandRules.ShouldPhantomDoom(true, false, false, false,
            PhantomBandRules.PhantomDoomMaxAttempts - 1, 60f));
        Assert.False(PhantomBandRules.ShouldPhantomDoom(true, false, false, false,
            PhantomBandRules.PhantomDoomMaxAttempts, 60f));
    }

    [Fact]
    public void Doom_NotOnATargetAlreadyDoomed()
        => Assert.False(PhantomBandRules.ShouldPhantomDoom(true, targetDoomed: true, false, false, 0, 60f));

    [Fact]
    public void Doom_NotOnSomethingAboutToDieAnyway()
        => Assert.False(PhantomBandRules.ShouldPhantomDoom(true, false, false, false, 0,
            PhantomBandRules.PhantomDoomMinTtkSeconds - 1f));

    [Fact]
    public void Doom_UnknownTimeToKillCountsAsLong()
        => Assert.True(PhantomBandRules.ShouldPhantomDoom(true, false, false, false, 0, float.MaxValue));

    [Fact]
    public void Doom_NotOutOfCombat()
        => Assert.False(PhantomBandRules.ShouldPhantomDoom(false, false, false, false, 0, 60f));

    // ── Vigilance (Thief): out of combat only, just before the pull ──────────────────────

    [Theory]
    [InlineData(false, false, false, true, 20f, true)]   // an enemy targeted, close
    [InlineData(false, false, false, true, 45f, false)]  // too far to be a pull
    [InlineData(false, false, false, false, 0f, false)]  // nothing targeted: no recasting in camp
    [InlineData(false, true, false, true, 20f, false)]   // already up
    [InlineData(false, false, true, true, 20f, false)]   // already turned into the crit buff
    [InlineData(true, false, false, true, 20f, false)]   // the game refuses it in combat
    public void Vigilance(bool inCombat, bool hasVigilance, bool hasForeseen, bool hasEnemy, float distance, bool expected)
        => Assert.Equal(expected,
            PhantomBandRules.ShouldVigilance(inCombat, hasVigilance, hasForeseen, hasEnemy, distance));

    // ── Occult Treasuresight (Freelancer): the setting now does something ────────────────

    [Theory]
    [InlineData(true, false, 3600d, true)]
    [InlineData(true, false, 30d, false)]   // paced to once a minute, not the 10s recast
    [InlineData(true, true, 3600d, false)]  // not mid-fight
    [InlineData(false, false, 3600d, false)] // opt-in
    public void Treasuresight(bool enabled, bool inCombat, double sinceLast, bool expected)
        => Assert.Equal(expected, PhantomBandRules.ShouldTreasuresight(enabled, inCombat, sinceLast));

    // ── the data ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheStatusIdsAreTheGames()
    {
        Assert.Equal(4243u, PhantomActions.StatusIds.OccultUnicorn);
        Assert.Equal(4271u, PhantomActions.StatusIds.Recuperation);
        Assert.Equal(4272u, PhantomActions.StatusIds.FortifiedRecuperation);
        Assert.Equal(4273u, PhantomActions.StatusIds.PhantomDoom);
        Assert.Equal(4274u, PhantomActions.StatusIds.PhantomRejuvenation);
        Assert.Equal(4277u, PhantomActions.StatusIds.Vigilance);
        Assert.Equal(4278u, PhantomActions.StatusIds.ForeseenOffense);
        Assert.Equal(5316u, PhantomActions.StatusIds.OccultBlink);
    }

    [Theory]
    [InlineData(41602u, PhantomJob.Ranger, 6)]
    [InlineData(41641u, PhantomJob.Oracle, 2)]
    [InlineData(41642u, PhantomJob.Oracle, 3)]
    [InlineData(41643u, PhantomJob.Oracle, 4)]
    [InlineData(49069u, PhantomJob.PhantomWhiteMage, 3)]
    [InlineData(41647u, PhantomJob.Thief, 3)]
    [InlineData(41651u, PhantomJob.Freelancer, 10)]
    public void EveryActionIsInTheCatalogAtItsLevel(uint actionId, PhantomJob job, int level)
    {
        var def = PhantomActions.All.Single(a => a.ActionId == actionId);
        Assert.Equal((job, level), (def.Job, (int)def.RequiredLevel));
    }
}
