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
    /// Two potencies are BLANK in the sheet itself — Smash Axe reads "a potency of ." and Shield
    /// Charge "a potency of ". They are recorded as 0 rather than guessed, and this pins that so
    /// nobody later mistakes the gap for a transcription slip and invents a number.
    /// </summary>
    [Fact]
    public void ThePotenciesTheSheetDoesNotStateAreZeroNotGuessed()
    {
        Assert.Equal(0, BSTActions.ById(44879)!.Value.Potency);   // Smash Axe
        Assert.Equal(0, BSTActions.ById(44893)!.Value.Potency);   // Shield Charge
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
