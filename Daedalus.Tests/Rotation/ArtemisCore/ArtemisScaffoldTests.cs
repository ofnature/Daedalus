using System;
using System.Linq;
using Daedalus.Data;
using Daedalus.Rotation.ArtemisCore.Helpers;
using Daedalus.Rotation.Common.Helpers;
using Xunit;

namespace Daedalus.Tests.Rotation.ArtemisCore;

/// <summary>
/// Beastmaster. The catalog was harvested from this client's own sheets on 2026-09-08 via
/// <c>/dae dumpjob 43</c> — XIVAPI still has nothing — so these cover the real action set, the
/// instinct cycle as the tooltips state it, the once-per-summon budget, and the limited-job
/// suppressions.
/// <para>
/// Several pin decisions rather than behaviour: the two potencies the sheet leaves blank, the
/// action count (so a second sweep for the Lv41-50 gap shows up), and BST's deliberate absence
/// from <c>IsMeleeDps</c>.
/// </para>
/// </summary>
public sealed class ArtemisScaffoldTests
{
    // ── the catalog, harvested from the client's own sheets 2026-09-08 ───────────────────

    /// <summary>
    /// 20 actions, every one read off ClassJob 43 in this client's sheets. The count is pinned so
    /// a second harvest sweep (for the Lv41-50 actions this dump did not reach) shows up here.
    /// </summary>
    [Fact]
    public void TheCatalogMatchesWhatTheClientReported()
    {
        Assert.Equal(20, BSTActions.All.Count);
        Assert.All(BSTActions.All, a => Assert.NotEqual(0u, a.ActionId));
        Assert.All(BSTActions.All, a => Assert.False(string.IsNullOrWhiteSpace(a.Name)));
    }

    /// <summary>
    /// Both potencies that rendered blank in the sheet dump — Smash Axe ("a potency of .") and
    /// Shield Charge ("a potency of ") — have since been read off the in-game tooltips. No potency
    /// in the catalog is a guess any more, and this pins that so a future harvest that loses them
    /// again cannot silently reintroduce a zero.
    /// </summary>
    [Fact]
    public void NoPotencyInTheCatalogIsAGuess()
    {
        Assert.Equal(100, BSTActions.ById(44879)!.Value.Potency);  // Smash Axe
        Assert.Equal(220, BSTActions.ById(44893)!.Value.Potency);  // Shield Charge

        // Shield Charge is a 20y forward rush with a 6y burst — a DASH, so it must be classified
        // as one or it bypasses the leap safety every other gap closer goes through.
        var charge = BSTActions.ById(44893)!.Value;
        Assert.Equal(BstArchetype.GapCloser, charge.Archetype);
        Assert.Equal(20f, charge.RangeYalms);
        Assert.Equal(6f, charge.EffectRadiusYalms);
    }

    /// <summary>
    /// Borrow maps each of the eight familiar classifications onto its Kinship one-to-one, and the
    /// Kinship is what determines the action Beast Mode becomes. Every classification must resolve
    /// to a real effect, or the rotation cannot say what pressing Beast Mode would do.
    /// </summary>
    [Fact]
    public void EveryClassificationResolvesToAKinshipEffect()
    {
        foreach (var c in Enum.GetValues<BeastClassification>())
        {
            var effect = BSTActions.KinshipEffectFor(c);
            if (c == BeastClassification.Unknown)
                Assert.Equal("unknown", effect);
            else
                Assert.NotEqual("unknown", effect);
        }
    }

    /// <summary>
    /// The 1-2-3 chain is the job's TP generator, and TP is what gates every instinctual skill, so
    /// the per-finisher gains are rotation-critical. Read off the in-game tooltips 2026-09-08:
    /// Axeblade Bite grants 13 and Shieldsplitter 15 — they are NOT the same number, which an
    /// earlier note in the catalog got wrong by attributing 15 to both.
    /// </summary>
    [Fact]
    public void TheComboChainGrantsTheTpThatGatesInstinctualSkills()
    {
        Assert.Equal(0, BSTActions.ById(44879)!.Value.ComboTpGain);    // Smash Axe — the starter
        Assert.Equal(13, BSTActions.ById(44883)!.Value.ComboTpGain);   // Axeblade Bite
        Assert.Equal(15, BSTActions.ById(44885)!.Value.ComboTpGain);   // Shieldsplitter

        // Nothing outside the combo chain generates TP this way.
        Assert.All(
            BSTActions.All.Where(a => a.Archetype != BstArchetype.ComboWeaponskill),
            a => Assert.Equal(0, a.ComboTpGain));
    }

    /// <summary>
    /// THE most consequential line in the whole kit, stated verbatim on every instinctual tooltip:
    /// "Instinctual skills do not share a recast timer with any other actions." They are
    /// Weaponskills with a 5s recast that runs in PARALLEL to the 2.5s combo chain, so the rotation
    /// must dispatch them like oGCDs. Treating them as GCDs would halve the job's throughput, and
    /// that mistake would be invisible in a log — it just looks slow. Pinned so it stays true.
    /// </summary>
    [Fact]
    public void InstinctualSkillsDoNotConsumeTheGcd()
    {
        Assert.All(
            BSTActions.All.Where(a => a.Archetype == BstArchetype.Instinctual),
            a =>
            {
                Assert.True(a.IndependentRecast);
                Assert.Equal(5f, a.RecastSeconds);
            });

        // The combo chain is the opposite: ordinary 2.5s GCDs that DO share the global.
        Assert.All(
            BSTActions.All.Where(a => a.Archetype == BstArchetype.ComboWeaponskill),
            a =>
            {
                Assert.False(a.IndependentRecast);
                Assert.Equal(2.5f, a.RecastSeconds);
            });
    }

    /// <summary>
    /// Two gauges, two gain numbers, and they must not be conflated: the combo chain feeds the
    /// PLAYER's TP (13/15), Parting Blow feeds the FAMILIAR's (24). Trick spends the familiar's.
    /// </summary>
    [Fact]
    public void ThePlayerAndFamiliarTpGainsAreDistinct()
    {
        Assert.Equal(24, BSTActions.PartingBlowFamiliarTpGain);
        Assert.NotEqual(BSTActions.PartingBlowFamiliarTpGain, BSTActions.ById(44885)!.Value.ComboTpGain);
    }

    /// <summary>
    /// The pet orders are not melee. Trick reaches 30y and Parting Blow 25y with an 8y burst, while
    /// the axe chain is 3y — so a range check that assumed "melee job, therefore 3y" would refuse
    /// the two highest-value buttons in the kit.
    /// </summary>
    [Fact]
    public void ThePetOrdersAreRangedNotMelee()
    {
        Assert.Equal(30f, BSTActions.ById(47093)!.Value.RangeYalms);   // Trick
        Assert.Equal(25f, BSTActions.ById(44891)!.Value.RangeYalms);   // Parting Blow
        Assert.Equal(8f, BSTActions.ById(44891)!.Value.EffectRadiusYalms);

        Assert.All(
            BSTActions.All.Where(a => a.Archetype is BstArchetype.ComboWeaponskill or BstArchetype.Instinctual),
            a => Assert.Equal(3f, a.RangeYalms));
    }

    /// <summary>All four instinctual skills exist, one per affinity.</summary>
    [Fact]
    public void EachAffinityHasExactlyOneInstinctualSkill()
    {
        foreach (var affinity in new[]
                 {
                     InstinctAffinity.Volant, InstinctAffinity.Rampant,
                     InstinctAffinity.Durant, InstinctAffinity.Eldritch,
                 })
        {
            var matches = BSTActions.All
                .Where(a => a.Archetype == BstArchetype.Instinctual && a.Affinity == affinity)
                .ToList();
            Assert.True(matches.Count == 1, $"{affinity}: expected 1 skill, found {matches.Count}");
        }
    }

    /// <summary>Three Battlehorns, unlocking at 1 / 10 / 20 — they are separate actions, not one with a slot arg.</summary>
    [Fact]
    public void ThereAreThreeBattlehornActions()
    {
        var horns = BSTActions.All
            .Where(a => a.Archetype == BstArchetype.Battlehorn)
            .OrderBy(a => a.MinLevel)
            .ToList();

        Assert.Equal(3, horns.Count);
        Assert.Equal(new byte[] { 1, 10, 20 }, horns.Select(h => h.MinLevel).ToArray());
    }

    [Fact]
    public void BeastmasterIsRegisteredAsALimitedJob()
    {
        Assert.Equal(43u, JobRegistry.Beastmaster);
        Assert.True(JobRegistry.IsLimitedJob(JobRegistry.Beastmaster));
        Assert.True(JobRegistry.IsLimitedJob(JobRegistry.BlueMage));
        Assert.False(JobRegistry.IsLimitedJob(JobRegistry.Samurai));
    }

    /// <summary>
    /// Beastmaster is a melee job but deliberately stays out of IsMeleeDps: that predicate gates
    /// positional and boundary-camp machinery built for content this job cannot enter, and its
    /// positional requirements are unknown. Pinned so the omission reads as a decision.
    /// </summary>
    [Fact]
    public void BeastmasterIsNotTreatedAsAnOrdinaryMeleeDps()
    {
        Assert.False(JobRegistry.IsMeleeDps(JobRegistry.Beastmaster));
        Assert.False(JobRegistry.IsDps(JobRegistry.Beastmaster));
    }

    // ── limited-job suppressions ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData(JobRegistry.Beastmaster)]
    [InlineData(JobRegistry.BlueMage)]
    public void LimitedJobs_SuppressContentAutomation(uint jobId)
    {
        Assert.True(LimitedJobContentPolicy.IsContentRestricted(jobId));
        Assert.False(LimitedJobContentPolicy.AllowsDutyAutomation(jobId));
        Assert.False(LimitedJobContentPolicy.AllowsContentOverrides(jobId));
        Assert.False(LimitedJobContentPolicy.AllowsRoleActions(jobId));
        Assert.False(LimitedJobContentPolicy.AllowsPartyCoordination(jobId));
        Assert.NotEqual(string.Empty, LimitedJobContentPolicy.DescribeSuppression(jobId));
    }

    /// <summary>
    /// The restrictions belong to two specific jobs. Guessing outward would silently disable
    /// ordinary jobs, so an unrecognised id is unrestricted.
    /// </summary>
    [Theory]
    [InlineData(JobRegistry.Samurai)]
    [InlineData(JobRegistry.WhiteMage)]
    [InlineData(999u)]
    public void EverythingElse_IsUnrestricted(uint jobId)
    {
        Assert.False(LimitedJobContentPolicy.IsContentRestricted(jobId));
        Assert.True(LimitedJobContentPolicy.AllowsDutyAutomation(jobId));
        Assert.Equal(string.Empty, LimitedJobContentPolicy.DescribeSuppression(jobId));
    }

    // ── the instinct cycle ───────────────────────────────────────────────────────────────

    /// <summary>
    /// The cycle, read off the tooltips: Volant → Rampant → Durant → Eldritch → Volant. The
    /// scaffold originally modelled this as yellow/green/blue/red, which was wrong — the
    /// affinities are named, and the shape only happened to be a four-cycle.
    /// </summary>
    [Theory]
    [InlineData(InstinctAffinity.Volant, InstinctAffinity.Rampant)]
    [InlineData(InstinctAffinity.Rampant, InstinctAffinity.Durant)]
    [InlineData(InstinctAffinity.Durant, InstinctAffinity.Eldritch)]
    [InlineData(InstinctAffinity.Eldritch, InstinctAffinity.Volant)]
    public void TheCycleFollowsTheTooltipsAndWraps(InstinctAffinity from, InstinctAffinity to)
        => Assert.Equal(to, BSTActions.NextInCycle(from));

    /// <summary>
    /// The two intentional combos alternate around the cycle. Straight from the tooltips:
    /// Rampant-after-Volant and Eldritch-after-Durant are Sunstrider; Durant-after-Rampant and
    /// Volant-after-Eldritch are Moonstalker.
    /// </summary>
    [Theory]
    [InlineData(InstinctAffinity.Volant, InstinctAffinity.Rampant, IntentionalCombo.Sunstrider)]
    [InlineData(InstinctAffinity.Durant, InstinctAffinity.Eldritch, IntentionalCombo.Sunstrider)]
    [InlineData(InstinctAffinity.Rampant, InstinctAffinity.Durant, IntentionalCombo.Moonstalker)]
    [InlineData(InstinctAffinity.Eldritch, InstinctAffinity.Volant, IntentionalCombo.Moonstalker)]
    public void TheTwoCombosAlternate(InstinctAffinity from, InstinctAffinity to, IntentionalCombo expected)
        => Assert.Equal(expected, BSTActions.ComboFor(from, to));

    /// <summary>An off-cycle transition scores no intentional combo.</summary>
    [Fact]
    public void OffCycleScoresNoIntentionalCombo()
        => Assert.Equal(IntentionalCombo.None,
            BSTActions.ComboFor(InstinctAffinity.Volant, InstinctAffinity.Eldritch));

    private static ArtemisInstinctTracker Tracker(out Action<double> advance)
    {
        var now = new DateTime(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc);
        var t = new ArtemisInstinctTracker { UtcNow = () => now };
        advance = seconds => now = now.AddSeconds(seconds);
        return t;
    }

    [Fact]
    public void AFreshChainWantsNothingInParticular()
    {
        var t = Tracker(out _);
        Assert.False(t.IsWindowOpen);
        Assert.Equal(InstinctAffinity.None, t.PreferredNext);
        // Opening a chain is not itself an Intentional Combo — there is nothing to chain from.
        Assert.False(t.WouldBeIntentional(InstinctAffinity.Volant));
    }

    [Fact]
    public void ClockwiseScoresTheIntentionalCombo()
    {
        var t = Tracker(out var advance);
        t.Record(InstinctAffinity.Volant);
        advance(1.0);

        Assert.True(t.IsWindowOpen);
        Assert.Equal(InstinctAffinity.Rampant, t.PreferredNext);
        Assert.True(t.WouldBeIntentional(InstinctAffinity.Rampant));
    }

    /// <summary>Off-order still combos — it just scores less. Both facts matter to the picker.</summary>
    [Fact]
    public void OffOrderStillCombos()
    {
        var t = Tracker(out var advance);
        t.Record(InstinctAffinity.Volant);
        advance(1.0);

        Assert.False(t.WouldBeIntentional(InstinctAffinity.Durant));
        Assert.True(t.WouldCombo(InstinctAffinity.Durant));
    }

    [Fact]
    public void TheWindowLapses()
    {
        var t = Tracker(out var advance);
        t.Record(InstinctAffinity.Volant);
        advance(ArtemisInstinctTracker.WindowSeconds + 0.1);

        Assert.False(t.IsWindowOpen);
        Assert.Equal(InstinctAffinity.None, t.LastAffinity);
        Assert.False(t.WouldCombo(InstinctAffinity.Rampant));
    }

    /// <summary>
    /// The window is 7s and that is now CONFIRMED — every Heart tooltip states "Duration: 7s".
    /// The scaffold's guess happened to be right, which is not the same as having been right;
    /// the constant now derives from the harvested data.
    /// </summary>
    [Fact]
    public void TheWindowLengthComesFromTheData()
    {
        Assert.True(ArtemisInstinctTracker.WindowSecondsIsConfirmed);
        Assert.Equal(BSTActions.HeartDurationSeconds, ArtemisInstinctTracker.WindowSeconds);
    }

    // ── battlehorns ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// An unreadable roster and an empty one are different states. Until a gauge reader exists,
    /// the state must say "unknown" rather than let a permanently-empty roster read as "no beasts
    /// assigned", which would look like a player problem instead of a missing feature.
    /// </summary>
    [Fact]
    public void AnUnreadRosterSaysUnknownRatherThanEmpty()
    {
        var state = new ArtemisBattlehornState();
        Assert.False(state.IsPopulatedFromGame);
        Assert.Contains("unknown", state.Describe(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SummoningRefreshesTheOncePerSummonBudget()
    {
        var state = new ArtemisBattlehornState();
        state.SetRoster(
            new BattlehornSlot(1001, "Dodo", BeastClassification.Cloudkin),
            BattlehornSlot.Empty,
            BattlehornSlot.Empty);

        state.OnSummoned(0);
        Assert.True(state.TemperedReleaseAvailable);
        Assert.True(state.BorrowAvailable);

        state.MarkTemperedReleaseUsed();
        state.MarkBorrowUsed();
        Assert.False(state.TemperedReleaseAvailable);
        Assert.False(state.BorrowAvailable);

        // A swap is a rotational decision precisely because it refreshes the budget.
        state.OnSummoned(0);
        Assert.True(state.TemperedReleaseAvailable);
        Assert.True(state.BorrowAvailable);
    }

    [Fact]
    public void RetreatingClearsTheActiveBeastAndItsBudget()
    {
        var state = new ArtemisBattlehornState();
        state.SetRoster(
            new BattlehornSlot(1001, "Dodo", BeastClassification.Cloudkin),
            BattlehornSlot.Empty,
            BattlehornSlot.Empty);
        state.OnSummoned(0);
        Assert.NotNull(state.Active);

        state.OnRetreated();

        Assert.Null(state.Active);
        Assert.False(state.TemperedReleaseAvailable);
        Assert.False(state.BorrowAvailable);
    }

    [Fact]
    public void ClassificationTravelsWithTheSlot()
    {
        var state = new ArtemisBattlehornState();
        state.SetRoster(
            new BattlehornSlot(2002, "Coeurl", BeastClassification.Beastkin),
            BattlehornSlot.Empty,
            BattlehornSlot.Empty);
        state.OnSummoned(0);

        // Classification is what will select the Borrow action once the data lands.
        Assert.Equal(BeastClassification.Beastkin, state.Active!.Value.Classification);
    }
}
