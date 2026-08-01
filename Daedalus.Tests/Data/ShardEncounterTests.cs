using System.Collections.Generic;
using Daedalus.Data;
using Xunit;

namespace Daedalus.Tests.Data;

/// <summary>
/// Oracle, Ranger, Berserker and Necromancer can only be unlocked from a Critical Encounter
/// shard drop, and the encounters run on their own timers — so the window calls one out while
/// it is live, but only while the job is still locked.
/// </summary>
public sealed class ShardEncounterTests
{
    private static IReadOnlyDictionary<PhantomJob, byte> Levels(params (PhantomJob Job, byte Level)[] entries)
    {
        var levels = new Dictionary<PhantomJob, byte>();
        foreach (var (job, level) in entries)
            levels[job] = level;
        return levels;
    }

    [Fact]
    public void UnclaimedShardEncounters_FlagsLiveEncounterForALockedJob()
    {
        var result = PhantomJobData.UnclaimedShardEncounters(
            ["On the Hunt"], Levels((PhantomJob.Oracle, 0)));

        var (encounter, job) = Assert.Single(result);
        Assert.Equal("On the Hunt", encounter);
        Assert.Equal(PhantomJob.Oracle, job);
    }

    /// <summary>The whole point of the filter — once you have the job the shard is worthless.</summary>
    [Fact]
    public void UnclaimedShardEncounters_SuppressesEncounterForAnUnlockedJob()
    {
        var result = PhantomJobData.UnclaimedShardEncounters(
            ["On the Hunt"], Levels((PhantomJob.Oracle, 1)));

        Assert.Empty(result);
    }

    [Fact]
    public void UnclaimedShardEncounters_IgnoresEncountersThatDropNoShard()
    {
        var result = PhantomJobData.UnclaimedShardEncounters(
            ["Trial by Claw", "Scourge of the Mind"], Levels((PhantomJob.Oracle, 0)));

        Assert.Empty(result);
    }

    /// <summary>A job absent from the level array has not been unlocked.</summary>
    [Fact]
    public void UnclaimedShardEncounters_TreatsMissingJobAsLocked()
    {
        var result = PhantomJobData.UnclaimedShardEncounters(
            ["The Black Regiment"], new Dictionary<PhantomJob, byte>());

        Assert.Single(result);
    }

    [Fact]
    public void UnclaimedShardEncounters_MatchesCaseInsensitively()
    {
        var result = PhantomJobData.UnclaimedShardEncounters(
            ["the unbridled"], Levels((PhantomJob.Berserker, 0)));

        var (_, job) = Assert.Single(result);
        Assert.Equal(PhantomJob.Berserker, job);
    }

    [Fact]
    public void UnclaimedShardEncounters_HandlesSeveralLiveEncounters()
    {
        var result = PhantomJobData.UnclaimedShardEncounters(
            ["On the Hunt", "The Unbridled", "Some Other CE"],
            Levels((PhantomJob.Oracle, 0), (PhantomJob.Berserker, 2)));

        var (_, job) = Assert.Single(result);
        Assert.Equal(PhantomJob.Oracle, job);
    }

    [Fact]
    public void UnclaimedShardEncounters_IsSafeOnEmptyAndBlankInput()
    {
        Assert.Empty(PhantomJobData.UnclaimedShardEncounters([], Levels()));
        Assert.Empty(PhantomJobData.UnclaimedShardEncounters(["", "   "], Levels()));
    }

    [Fact]
    public void ShardCriticalEncounters_CoverTheFourDropOnlyJobs()
    {
        var jobs = new List<PhantomJob>();
        foreach (var (_, job) in PhantomJobData.ShardCriticalEncounters)
            jobs.Add(job);

        Assert.Contains(PhantomJob.Oracle, jobs);
        Assert.Contains(PhantomJob.Ranger, jobs);
        Assert.Contains(PhantomJob.Berserker, jobs);
        Assert.Contains(PhantomJob.Necromancer, jobs);
    }
}
