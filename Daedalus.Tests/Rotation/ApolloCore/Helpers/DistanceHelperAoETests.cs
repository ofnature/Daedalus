using System.Numerics;
using Daedalus.Rotation.ApolloCore.Helpers;
using Xunit;

namespace Daedalus.Tests.Rotation.ApolloCore.Helpers;

/// <summary>
/// Target-circle AoE hit test: the circle extends the action radius from the anchor's CENTER,
/// and only the candidate's hitbox widens the ring. Regression for the Circe AoE-mode latch
/// (Vanguard log 2026-07-02): including the anchor's hitbox made spread mobs count as a pack,
/// so the rotation kept casting AoE that hit one target.
/// </summary>
public class DistanceHelperAoETests
{
    [Fact]
    public void IsWithinTargetCircleAoE_CandidateInsideRadiusPlusOwnHitbox_ReturnsTrue()
    {
        // 5y radius + 4y candidate hitbox = 9y ring; candidate at 8.5y is hit.
        Assert.True(DistanceHelper.IsWithinTargetCircleAoE(
            Vector3.Zero, new Vector3(8.5f, 0, 0), radius: 5f, candidateHitboxRadius: 4f));
    }

    [Fact]
    public void IsWithinTargetCircleAoE_AnchorHitboxDoesNotExtendCircle_ReturnsFalse()
    {
        // Two large mobs 10y apart (both 4y hitboxes): the old math allowed 5 + 4 + 4 = 13y and
        // counted this as a hit; the game only reaches 5 + 4 = 9y.
        Assert.False(DistanceHelper.IsWithinTargetCircleAoE(
            Vector3.Zero, new Vector3(10f, 0, 0), radius: 5f, candidateHitboxRadius: 4f));
    }

    [Fact]
    public void IsWithinTargetCircleAoE_ExactBoundary_ReturnsTrue()
    {
        Assert.True(DistanceHelper.IsWithinTargetCircleAoE(
            Vector3.Zero, new Vector3(9f, 0, 0), radius: 5f, candidateHitboxRadius: 4f));
    }

    [Fact]
    public void IsWithinTargetCircleAoE_ZeroHitbox_UsesPureRadius()
    {
        Assert.True(DistanceHelper.IsWithinTargetCircleAoE(
            Vector3.Zero, new Vector3(5f, 0, 0), radius: 5f, candidateHitboxRadius: 0f));
        Assert.False(DistanceHelper.IsWithinTargetCircleAoE(
            Vector3.Zero, new Vector3(5.1f, 0, 0), radius: 5f, candidateHitboxRadius: 0f));
    }
}
