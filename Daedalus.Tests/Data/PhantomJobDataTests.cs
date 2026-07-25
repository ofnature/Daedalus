using System.Collections.Generic;
using System.Linq;
using Daedalus.Data;
using Xunit;

namespace Daedalus.Tests.Data;

/// <summary>
/// Tests for Occult Crescent phantom-job detection data (Phase 1,
/// docs/occult-phantom-plan.md). The active phantom job and its level are carried
/// by a player status whose stack count is the level; 255 stacks = no level (RSR rule).
/// </summary>
public class PhantomJobDataTests
{
    [Fact]
    public void ResolveActiveJob_NoStatuses_ReturnsNone()
    {
        var (job, level) = PhantomJobData.ResolveActiveJob([]);

        Assert.Equal(PhantomJob.None, job);
        Assert.Equal(0, level);
    }

    [Fact]
    public void ResolveActiveJob_UnrelatedStatusesOnly_ReturnsNone()
    {
        // Regen, Soteria-style stacked status, Medicated — none are phantom levels.
        var statuses = new (uint, byte)[] { (158, 0), (1218, 3), (49, 1) };

        var (job, _) = PhantomJobData.ResolveActiveJob(statuses);

        Assert.Equal(PhantomJob.None, job);
    }

    [Fact]
    public void ResolveActiveJob_OracleStatusWithStacks_ReturnsOracleAtThatLevel()
    {
        var statuses = new (uint, byte)[] { (158, 0), (4368, 4) };

        var (job, level) = PhantomJobData.ResolveActiveJob(statuses);

        Assert.Equal(PhantomJob.Oracle, job);
        Assert.Equal(4, level);
    }

    [Fact]
    public void ResolveActiveJob_255Stacks_TreatedAsNoLevel()
    {
        // RSR rule: byte.MaxValue stacks means the status is present without a level.
        var statuses = new (uint, byte)[] { (4358, byte.MaxValue) };

        var (job, level) = PhantomJobData.ResolveActiveJob(statuses);

        Assert.Equal(PhantomJob.None, job);
        Assert.Equal(0, level);
    }

    [Fact]
    public void ResolveActiveJob_ZeroStacks_TreatedAsNoLevel()
    {
        var statuses = new (uint, byte)[] { (4360, 0) };

        var (job, _) = PhantomJobData.ResolveActiveJob(statuses);

        Assert.Equal(PhantomJob.None, job);
    }

    [Theory]
    [InlineData(PhantomJob.Freelancer, 4242u)]
    [InlineData(PhantomJob.Knight, 4358u)]
    [InlineData(PhantomJob.Thief, 4369u)]
    [InlineData(PhantomJob.MysticKnight, 4803u)]
    [InlineData(PhantomJob.Dancer, 4805u)]
    public void GetLevelStatusId_MapsJobToVerifiedStatusId(PhantomJob job, uint expectedStatusId)
    {
        Assert.Equal(expectedStatusId, PhantomJobData.GetLevelStatusId(job));
    }

    [Fact]
    public void LevelStatuses_CoverAllSixteenJobs_WithDistinctStatusIds()
    {
        var jobs = PhantomJobData.LevelStatuses.Select(e => e.Key).ToList();
        var statusIds = PhantomJobData.LevelStatuses.Select(e => e.Value).ToList();

        Assert.Equal(16, jobs.Count);
        Assert.Equal(jobs.Count, jobs.Distinct().Count());
        Assert.Equal(statusIds.Count, statusIds.Distinct().Count());
        Assert.DoesNotContain(PhantomJob.None, jobs);
        Assert.Equal(0u, PhantomJobData.GetLevelStatusId(PhantomJob.None));
    }

    [Fact]
    public void ConsumableItemIds_MatchRsrVerifiedIds()
    {
        Assert.Contains(47740u, PhantomJobData.ConsumableItemIds); // Zeninage gil pouch
        Assert.Contains(47741u, PhantomJobData.ConsumableItemIds); // Occult Potion
        Assert.Contains(47743u, PhantomJobData.ConsumableItemIds); // Occult Elixir
    }

    [Fact]
    public void OccultTerritoryIds_ContainSouthHorn()
    {
        Assert.Contains((ushort)1252, (IEnumerable<ushort>)PhantomJobData.OccultTerritoryIds);
    }
}
