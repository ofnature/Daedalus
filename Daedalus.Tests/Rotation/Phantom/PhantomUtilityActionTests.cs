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

    // Timed for Starfall (user, 2026-11-16): pressed 12 s before Predict's recast ends, whoever is hurt or not.
    [Theory]
    [InlineData(12f, 1.00f, 1.00f, true)]   // Predict is 12 s away: now, nobody hurt
    [InlineData(5f, 1.00f, 1.00f, true)]
    [InlineData(40f, 0.50f, 0.50f, false)]  // Predict is far off: kept, even with people hurt
    [InlineData(0f, 0.85f, 1.00f, true)]    // Predict ready and unused (the pull): the plain rule
    [InlineData(0f, 1.00f, 1.00f, false)]
    public void RejuvenationTimedForStarfall(float predictRecast, float selfHp, float lowest, bool expected)
        => Assert.Equal(expected, PhantomBandRules.ShouldRejuvenate(
            true, false, selfHp, lowest, timedForStarfall: true, predictRecastSeconds: predictRecast));

    [Fact]
    public void RejuvenationTimedForStarfallIsNeverDoubledNorPressedOutOfCombat()
    {
        Assert.False(PhantomBandRules.ShouldRejuvenate(true, alreadyUp: true, 1f, 1f, true, 5f));
        Assert.False(PhantomBandRules.ShouldRejuvenate(inCombat: false, false, 1f, 1f, true, 5f));
    }

    [Fact]
    public void WithStarfallOffThePredictRecastIsIgnored()
        => Assert.True(PhantomBandRules.ShouldRejuvenate(true, false, 0.5f, 1f, timedForStarfall: false, predictRecastSeconds: 40f));

    [Fact]
    public void StarfallWaitsForTheHealToBeAboutToLand()
    {
        Assert.True(OracleCardPolicy.HoldsStarfallForRejuvenation(rejuvenationSecondsLeft: 6f, cardSecondsLeft: 5f));
        Assert.False(OracleCardPolicy.HoldsStarfallForRejuvenation(4f, 5f));   // close enough: press
        Assert.False(OracleCardPolicy.HoldsStarfallForRejuvenation(15f, 3f));  // the card is about to rotate away
        Assert.False(OracleCardPolicy.HoldsStarfallForRejuvenation(0f, 5f));   // no Rejuvenation running
    }

    [Fact]
    public void AHeldStarfallIsStillPlayedWhenItMustBe()
    {
        var cfg = new Daedalus.Config.PhantomConfig();
        Assert.Equal(OracleDecision.Wait, OracleCardPolicy.Decide(
            OracleCardPolicy.StarfallCard, cfg, lastCard: false, 1f, 1f, 1f, false, false,
            rejuvenationSecondsLeft: 8f, cardSecondsLeft: 5f));
        Assert.Equal(OracleDecision.PlayCard, OracleCardPolicy.Decide(
            OracleCardPolicy.StarfallCard, cfg, lastCard: true, 1f, 1f, 1f, false, false,
            rejuvenationSecondsLeft: 8f, cardSecondsLeft: 5f));
        Assert.Equal(OracleDecision.PlayCard, OracleCardPolicy.Decide(
            OracleCardPolicy.StarfallCard, cfg, lastCard: false, 1f, 1f, 1f, false, false,
            rejuvenationSecondsLeft: 3f, cardSecondsLeft: 5f));
    }

    // ── The parser's phantom job tag ─────────────────────────────────────────────────────

    [Fact]
    public void ThePhantomTagIsTheJobAndItsLevel()
    {
        Assert.Equal("Oracle 4", Daedalus.Data.PhantomJobData.PhantomTag(
            Daedalus.Data.PhantomJobData.ResolveActiveJob([(4368u, (byte)4)])));
        Assert.Equal("Red Mage 5", Daedalus.Data.PhantomJobData.PhantomTag(
            Daedalus.Data.PhantomJobData.ResolveActiveJob([(5334u, (byte)5)])));
        Assert.Equal("", Daedalus.Data.PhantomJobData.PhantomTag(
            Daedalus.Data.PhantomJobData.ResolveActiveJob([(48u, (byte)0)])));
    }

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

    // ── Phantom Aim is held for the job's own burst (user, 2026-11-17) ───────────────────

    [Theory]
    [InlineData(0f, false)]     // burst ready: Aim now, the job presses the burst next
    [InlineData(3f, true)]      // about to come round: wait for it
    [InlineData(30f, true)]
    [InlineData(31f, false)]    // further off than Aim lasts: not worth the wait
    [InlineData(118f, false)]   // the burst has just gone out: Aim covers it
    public void AimWaitsOnlyForABurstThatIsClose(float burstCooldown, bool held)
        => Assert.Equal(held, PhantomBandRules.HoldsAimForBurst(burstCooldown));

    [Theory]
    [InlineData(Daedalus.Data.JobRegistry.Samurai, 16482u)]      // Ikishoten
    [InlineData(Daedalus.Data.JobRegistry.Warrior, 7389u)]       // Inner Release
    [InlineData(Daedalus.Data.JobRegistry.Ninja, 36958u)]        // Kunai's Bane
    [InlineData(Daedalus.Data.JobRegistry.Bard, 101u)]           // Raging Strikes
    [InlineData(Daedalus.Data.JobRegistry.Pictomancer, 34675u)]  // Starry Muse
    [InlineData(Daedalus.Data.JobRegistry.Summoner, 25801u)]     // Searing Light
    public void AimIsTimedToTheJobsOwnBurstCooldown(uint jobId, uint burstAction)
        => Assert.Equal(burstAction, PhantomBandRules.AimBurstAction(jobId, 100));

    [Fact]
    public void AJobWithNoBurstListedPressesAimWheneverItIsReady()
    {
        Assert.Equal(0u, PhantomBandRules.AimBurstAction(Daedalus.Data.JobRegistry.Sage, 100));
        Assert.False(PhantomBandRules.HoldsAimForBurst(0f));
    }

    [Fact]
    public void ABurstNotYetLearnedIsNotWaitedFor()
        => Assert.Equal(0u, PhantomBandRules.AimBurstAction(Daedalus.Data.JobRegistry.Ninja, 91));
}
