using System;
using Daedalus.Data;
using Daedalus.Rotation.ArtemisCore.Helpers;
using Daedalus.Rotation.Common.Helpers;
using Xunit;

namespace Daedalus.Tests.Rotation.ArtemisCore;

/// <summary>
/// Beastmaster scaffold. The action data is unpublished (verified 2026-09-08: ClassJob row 43
/// exists, no BST actions in the sheets), so these cover the parts that are knowable without it —
/// the instinct cycle, the once-per-summon budget, and the limited-job suppressions — plus the
/// safety property that keeps the whole thing inert until the ids land.
/// </summary>
public sealed class ArtemisScaffoldTests
{
    // ── the safety property ──────────────────────────────────────────────────────────────

    /// <summary>
    /// The scaffold must ship with NO action ids. An empty table is what guarantees nothing can
    /// be pressed; a plausible-looking guess reaching a hotbar is the one outcome to prevent.
    /// </summary>
    [Fact]
    public void NoActionIdsAreInvented()
    {
        Assert.Empty(BSTActions.All);
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

    [Theory]
    [InlineData(InstinctColor.Yellow, InstinctColor.Green)]
    [InlineData(InstinctColor.Green, InstinctColor.Blue)]
    [InlineData(InstinctColor.Blue, InstinctColor.Red)]
    [InlineData(InstinctColor.Red, InstinctColor.Yellow)]
    public void TheCycleIsClockwiseAndWraps(InstinctColor from, InstinctColor to)
        => Assert.Equal(to, BSTActions.NextClockwise(from));

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
        Assert.Equal(InstinctColor.None, t.PreferredNext);
        // Opening a chain is not itself an Intentional Combo — there is nothing to chain from.
        Assert.False(t.WouldBeIntentional(InstinctColor.Yellow));
    }

    [Fact]
    public void ClockwiseScoresTheIntentionalCombo()
    {
        var t = Tracker(out var advance);
        t.Record(InstinctColor.Yellow);
        advance(1.0);

        Assert.True(t.IsWindowOpen);
        Assert.Equal(InstinctColor.Green, t.PreferredNext);
        Assert.True(t.WouldBeIntentional(InstinctColor.Green));
    }

    /// <summary>Off-order still combos — it just scores less. Both facts matter to the picker.</summary>
    [Fact]
    public void OffOrderStillCombos()
    {
        var t = Tracker(out var advance);
        t.Record(InstinctColor.Yellow);
        advance(1.0);

        Assert.False(t.WouldBeIntentional(InstinctColor.Red));
        Assert.True(t.WouldCombo(InstinctColor.Red));
    }

    [Fact]
    public void TheWindowLapses()
    {
        var t = Tracker(out var advance);
        t.Record(InstinctColor.Yellow);
        advance(ArtemisInstinctTracker.WindowSeconds + 0.1);

        Assert.False(t.IsWindowOpen);
        Assert.Equal(InstinctColor.None, t.LastColor);
        Assert.False(t.WouldCombo(InstinctColor.Green));
    }

    /// <summary>
    /// The window length is assumed, not measured. Pinned so nobody reads 7s as verified — when
    /// it is confirmed in game, this test is what tells you to flip the flag.
    /// </summary>
    [Fact]
    public void TheWindowLengthIsStillAGuess()
        => Assert.False(ArtemisInstinctTracker.WindowSecondsIsConfirmed);

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
