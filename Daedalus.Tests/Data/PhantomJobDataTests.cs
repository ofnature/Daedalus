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
    public void ConsumableItemIds_MatchFieldVerifiedIds()
    {
        // Field-verified 2026-07-25 (Lumina names in Debug tab): 47740 Occult Coffer (Zeninage),
        // 47741 Occult Potion, 47743 Occult Elixir. There is NO ether item — the Occult Ether
        // ACTION consumes an Occult Potion, so exactly three consumables exist.
        Assert.Equal(3, PhantomJobData.ConsumableItemIds.Count);
        Assert.Contains(47740u, PhantomJobData.ConsumableItemIds);
        Assert.Contains(47741u, PhantomJobData.ConsumableItemIds);
        Assert.Contains(47743u, PhantomJobData.ConsumableItemIds);
    }

    [Fact]
    public void OccultTerritoryIds_ContainSouthHorn()
    {
        Assert.Contains((ushort)1252, (IEnumerable<ushort>)PhantomJobData.OccultTerritoryIds);
    }

    // North Horn (added 2026-07-28, XIVAPI TerritoryType 1346; currencies are Obols not Pieces).

    [Fact]
    public void OccultTerritoryIds_ContainNorthHorn()
    {
        Assert.Contains((ushort)1346, (IEnumerable<ushort>)PhantomJobData.OccultTerritoryIds);
    }

    [Fact]
    public void CurrencyItemIds_SouthHorn_ArePieces()
    {
        var (silver, gold) = PhantomJobData.CurrencyItemIds(PhantomJobData.SouthHornTerritoryId);
        Assert.Equal(45043u, silver);
        Assert.Equal(45044u, gold);
    }

    [Fact]
    public void CurrencyItemIds_NorthHorn_AreObols()
    {
        var (silver, gold) = PhantomJobData.CurrencyItemIds(PhantomJobData.NorthHornTerritoryId);
        Assert.Equal(51975u, silver);
        Assert.Equal(51976u, gold);
    }

    [Fact]
    public void CurrencyItemIds_UnknownTerritory_FallsBackToPieces()
    {
        var (silver, gold) = PhantomJobData.CurrencyItemIds(0);
        Assert.Equal(45043u, silver);
        Assert.Equal(45044u, gold);
    }
}
