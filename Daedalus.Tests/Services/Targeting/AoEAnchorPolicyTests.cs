using System.Numerics;
using Daedalus.Services.Targeting;
using Xunit;

namespace Daedalus.Tests.Services.Targeting;

/// <summary>
/// Where a targeted circle AoE is aimed.
/// <para>
/// Field 2026-11-17, Eureka Pagos: an Astrologian and a Pictomancer kept pulling random mobs. The aim scan
/// took every attackable hostile in range, so the densest cluster won whether or not anyone was fighting
/// it, and mobs nobody had touched were counted toward the "enough enemies for an AoE" line.
/// </para>
/// </summary>
public sealed class AoEAnchorPolicyTests
{
    private const float Radius = 5f;

    private static AoEAnchorPolicy.Candidate Ours(ulong id, float x, float z = 0f)
        => new(id, new Vector3(x, 0f, z), InOurFight: true);

    private static AoEAnchorPolicy.Candidate Unpulled(ulong id, float x, float z = 0f)
        => new(id, new Vector3(x, 0f, z), InOurFight: false);

    /// <summary>THE regression: one mob in our fight, a pack of three untouched ones 20y away.</summary>
    [Fact]
    public void AnUnpulledCluster_IsNeverTheAim()
    {
        var (id, hits) = AoEAnchorPolicy.BestCircle(
            [Ours(1, 0f), Unpulled(2, 20f), Unpulled(3, 21f), Unpulled(4, 22f)], Radius);

        Assert.Equal(1UL, id);
        Assert.Equal(1, hits);
    }

    /// <summary>Untouched mobs standing beside our target do not make it a pack worth an AoE.</summary>
    [Fact]
    public void UnpulledMobsBesideOurTarget_AreNotCounted()
    {
        var (id, hits) = AoEAnchorPolicy.BestCircle(
            [Ours(1, 0f), Unpulled(2, 1f), Unpulled(3, 2f)], Radius);

        Assert.Equal(1UL, id);
        Assert.Equal(1, hits);
    }

    [Fact]
    public void NothingInOurFight_NoAim()
    {
        var (id, hits) = AoEAnchorPolicy.BestCircle([Unpulled(2, 0f), Unpulled(3, 1f)], Radius);

        Assert.Equal(0UL, id);
        Assert.Equal(0, hits);
    }

    [Fact]
    public void NoEnemies_NoAim()
    {
        Assert.Equal((0UL, 0), AoEAnchorPolicy.BestCircle([], Radius));
    }

    /// <summary>Among our own, the middle of the pack wins: it reaches both ends.</summary>
    [Fact]
    public void TheDensestPartOfOurPack_IsTheAim()
    {
        var (id, hits) = AoEAnchorPolicy.BestCircle(
            [Ours(1, 0f), Ours(2, 4f), Ours(3, 8f), Ours(4, 30f)], Radius);

        Assert.Equal(2UL, id);
        Assert.Equal(3, hits);
    }

    /// <summary>A smaller pack of ours beats a bigger one nobody is fighting.</summary>
    [Fact]
    public void OurPairBeatsABiggerUnpulledPack()
    {
        var (id, hits) = AoEAnchorPolicy.BestCircle(
            [Unpulled(10, 20f), Unpulled(11, 21f), Unpulled(12, 22f), Unpulled(13, 23f), Ours(1, 0f), Ours(2, 3f)],
            Radius);

        Assert.Equal(1UL, id);
        Assert.Equal(2, hits);
    }

    [Fact]
    public void TheRadiusIsInclusive()
    {
        var (_, hits) = AoEAnchorPolicy.BestCircle([Ours(1, 0f), Ours(2, Radius)], Radius);

        Assert.Equal(2, hits);
    }
}
