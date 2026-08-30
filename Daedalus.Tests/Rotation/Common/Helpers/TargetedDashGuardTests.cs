using System.Numerics;
using Moq;
using Daedalus.Rotation.Common.Helpers;
using Daedalus.Services.Positional.Navigation;
using Xunit;

namespace Daedalus.Tests.Rotation.Common.Helpers;

/// <summary>
/// Phantom Kick leaps at its target. Two things can go wrong that standing still never does:
/// the target is over a pit, and the flight path crosses a telegraph. Neither source is
/// mandatory — a missing plugin means no opinion, not a blocked ability.
/// </summary>
public class TargetedDashGuardTests
{
    private delegate bool TryGetFloorDelegate(Vector3 position, out Vector3 floor);

    private readonly Mock<IVNavService> _vNav = new();
    private readonly Mock<IBossModSafetyService> _bossMod = new();

    private static readonly Vector3 Player = new(0f, 10f, 0f);
    private static readonly Vector3 Target = new(0f, 10f, 5f);

    public TargetedDashGuardTests()
    {
        _vNav.Setup(x => x.IsAvailable).Returns(true);
        _vNav.Setup(x => x.IsNavReady).Returns(true);
        SetFloor((Vector3 p, out Vector3 floor) => { floor = p; return true; });
        _bossMod.Setup(x => x.IsSegmentSafe(It.IsAny<Vector3>(), It.IsAny<Vector3>())).Returns(true);
        _bossMod.Setup(x => x.QueryPositionSafety(It.IsAny<Vector3>(), It.IsAny<float>()))
            .Returns(PositionSafety.Safe);
    }

    private void SetFloor(TryGetFloorDelegate floorQuery)
        => _vNav.Setup(x => x.TryGetFloorPoint(It.IsAny<Vector3>(), out It.Ref<Vector3>.IsAny)).Returns(floorQuery);

    private bool Check(Vector3? target = null, IVNavService? vNav = null, IBossModSafetyService? bossMod = null)
        => TargetedDashGuard.IsTargetedDashSafe(
            Player, target ?? Target, TargetedDashGuard.PhantomKickDashYalms,
            vNav ?? _vNav.Object, bossMod ?? _bossMod.Object);

    [Fact]
    public void FlatFloor_NoTelegraph_Dashes()
    {
        Assert.True(Check());
    }

    /// <summary>No navmesh floor under the target at all — a hole, a void, or off the mesh.</summary>
    [Fact]
    public void NoFloorUnderTarget_Blocks()
    {
        SetFloor((Vector3 p, out Vector3 floor) => { floor = p; return false; });
        Assert.False(Check());
    }

    /// <summary>The floor is there, it is just a long way down. That is the pit.</summary>
    [Fact]
    public void FloorFarBelow_Blocks()
    {
        SetFloor((Vector3 p, out Vector3 floor)
            => { floor = new Vector3(p.X, Player.Y - TargetedDashGuard.MaxLandingDropYalms - 1f, p.Z); return true; });
        Assert.False(Check());
    }

    /// <summary>A step down is not a pit — arenas are not perfectly flat.</summary>
    [Fact]
    public void SmallStepDown_StillDashes()
    {
        SetFloor((Vector3 p, out Vector3 floor)
            => { floor = new Vector3(p.X, Player.Y - 1f, p.Z); return true; });
        Assert.True(Check());
    }

    /// <summary>Higher ground is not a pit either — the drop check is one-directional.</summary>
    [Fact]
    public void TargetOnHigherGround_StillDashes()
    {
        SetFloor((Vector3 p, out Vector3 floor)
            => { floor = new Vector3(p.X, Player.Y + 8f, p.Z); return true; });
        Assert.True(Check());
    }

    [Fact]
    public void TelegraphOnThePath_Blocks()
    {
        _bossMod.Setup(x => x.IsSegmentSafe(It.IsAny<Vector3>(), It.IsAny<Vector3>())).Returns(false);
        Assert.False(Check());
    }

    [Theory]
    [InlineData(PositionSafety.Unsafe)]
    [InlineData(PositionSafety.Imminent)]
    public void HazardWhereWeLand_Blocks(PositionSafety safety)
    {
        _bossMod.Setup(x => x.QueryPositionSafety(It.IsAny<Vector3>(), It.IsAny<float>())).Returns(safety);
        Assert.False(Check(), $"{safety} landing should not be dashed into");
    }

    /// <summary>
    /// BMR models this dash as a fixed 15y along the target direction, so the hazard sweep has to
    /// ask about the full length rather than stopping at a target 5y away.
    /// </summary>
    [Fact]
    public void SegmentSweptToFullDashLength_NotJustToTheTarget()
    {
        Check();
        _bossMod.Verify(x => x.IsSegmentSafe(
            Player,
            It.Is<Vector3>(v => Vector3.Distance(v, Player) > TargetedDashGuard.PhantomKickDashYalms - 0.01f)),
            Times.Once);
    }

    /// <summary>
    /// No vnavmesh means nothing is known about the floor. Refusing the Monk's damage button
    /// because a movement plugin is not installed is the worse failure.
    /// </summary>
    [Fact]
    public void WithoutNavmesh_FloorCheckIsSkipped()
    {
        _vNav.Setup(x => x.IsAvailable).Returns(false);
        SetFloor((Vector3 p, out Vector3 floor) => { floor = p; return false; });
        Assert.True(Check());
    }

    [Fact]
    public void WithoutNavmeshLoaded_FloorCheckIsSkipped()
    {
        _vNav.Setup(x => x.IsNavReady).Returns(false);
        SetFloor((Vector3 p, out Vector3 floor) => { floor = p; return false; });
        Assert.True(Check());
    }

    [Fact]
    public void WithoutEitherSource_Dashes()
    {
        Assert.True(TargetedDashGuard.IsTargetedDashSafe(
            Player, Target, TargetedDashGuard.PhantomKickDashYalms, null, null));
    }

    /// <summary>Standing inside the target: no travel, nothing to check, no divide by zero.</summary>
    [Fact]
    public void TargetOnTopOfUs_Dashes()
    {
        SetFloor((Vector3 p, out Vector3 floor) => { floor = p; return false; });
        Assert.True(Check(Player));
    }
}
