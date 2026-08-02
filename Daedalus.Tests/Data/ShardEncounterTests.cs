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
    public void ShardCriticalEncounters_CoverTheDropOnlyJobs()
    {
        var jobs = new List<PhantomJob>();
        foreach (var (_, job) in PhantomJobData.ShardCriticalEncounters)
            jobs.Add(job);

        Assert.Contains(PhantomJob.Oracle, jobs);
        Assert.Contains(PhantomJob.Ranger, jobs);
        Assert.Contains(PhantomJob.Berserker, jobs);
        Assert.Contains(PhantomJob.Necromancer, jobs);
        Assert.Contains(PhantomJob.PhantomBlueMage, jobs);
    }

    /// <summary>
    /// Blue Mage was missed on the first pass — its unlock hint doesn't share the
    /// "Critical Encounter:" wording, so a text search for the others skipped it and the banner
    /// never fired when Appalling Behavior popped.
    /// <para>
    /// Derive the requirement from the unlock data instead of trusting a hand-written list:
    /// every job unlocked by a CE must name the encounter that drops its shard.
    /// </para>
    /// </summary>
    [Fact]
    public void EveryCriticalEncounterUnlockedJob_NamesItsEncounter()
    {
        var named = new HashSet<PhantomJob>();
        foreach (var (_, job) in PhantomJobData.ShardCriticalEncounters)
            named.Add(job);

        var missing = new List<PhantomJob>();
        foreach (PhantomJob job in System.Enum.GetValues<PhantomJob>())
        {
            if (job == PhantomJob.None)
                continue;
            if (PhantomJobData.GetUnlockCost(job).Kind != PhantomJobData.UnlockKind.CriticalEncounter)
                continue;
            if (!named.Contains(job))
                missing.Add(job);
        }

        Assert.Empty(missing);
    }

    [Fact]
    public void UnclaimedShardEncounters_FlagsAppallingBehaviourForBlueMage()
    {
        var result = PhantomJobData.UnclaimedShardEncounters(
            ["Appalling Behavior"], Levels((PhantomJob.PhantomBlueMage, 0)));

        var (_, job) = Assert.Single(result);
        Assert.Equal(PhantomJob.PhantomBlueMage, job);
    }
}
