using System;
using System.Numerics;
using Moq;
using Daedalus.Rotation.Common.Helpers;
using Daedalus.Services.Positional.Navigation;
using Xunit;

namespace Daedalus.Tests.Rotation.Common.Helpers;

/// <summary>
/// PCT Smudge dash guard (field report 2026-07-20: naive weave-while-moving dashed off arena
/// ledges and past BMR micro-steps). A forward dash needs: a running vNav travel leg, movement
/// aligned with facing, navmesh floor at the dash midpoint and landing near the current height,
/// and no BMR hazard there. Fail closed on every missing piece.
/// </summary>
public class ForwardDashGuardTests : IDisposable
{
    private delegate bool TryGetFloorDelegate(Vector3 position, out Vector3 floor);

    private readonly Mock<IVNavService> _vNav = new();
    private readonly Mock<IBossModSafetyService> _bossMod = new();
    private DateTime _now = new(2026, 7, 20, 12, 0, 0, DateTimeKind.Utc);

    public ForwardDashGuardTests()
    {
        ForwardDashGuard.ResetForTest();
        ForwardDashGuard.UtcNow = () => _now;

        _vNav.Setup(x => x.IsAvailable).Returns(true);
        _vNav.Setup(x => x.IsNavReady).Returns(true);
        _vNav.Setup(x => x.IsPathRunning).Returns(true);
        // Flat floor everywhere by default.
        SetFloor((Vector3 p, out Vector3 floor) => { floor = p; return true; });
        _bossMod.Setup(x => x.QueryPositionSafety(It.IsAny<Vector3>(), It.IsAny<float>()))
            .Returns(PositionSafety.Safe);
    }

    private void SetFloor(TryGetFloorDelegate floorQuery)
    {
        _vNav.Setup(x => x.TryGetFloorPoint(It.IsAny<Vector3>(), out It.Ref<Vector3>.IsAny))
            .Returns(floorQuery);
    }

    public void Dispose() => ForwardDashGuard.ResetForTest();

    /// <summary>Two frames of running straight +Z while facing +Z, then evaluate.</summary>
    private bool CheckAfterForwardRun(
        IVNavService? vNav = null, IBossModSafetyService? bossMod = null, float facing = 0f)
    {
        ForwardDashGuard.IsForwardDashSafe(new Vector3(0f, 0f, 0f), facing, ForwardDashGuard.SmudgeDashYalms, vNav ?? _vNav.Object, bossMod ?? _bossMod.Object);
        _now = _now.AddSeconds(0.05);
        return ForwardDashGuard.IsForwardDashSafe(new Vector3(0f, 0f, 0.4f), facing, ForwardDashGuard.SmudgeDashYalms, vNav ?? _vNav.Object, bossMod ?? _bossMod.Object);
    }

    [Fact]
    public void DashAllowed_OnTravelLeg_FlatFloor()
    {
        Assert.True(CheckAfterForwardRun());
    }

    [Fact]
    public void DashBlocked_WhenNoVNavPathRunning()
    {
        // BMR input-injection steps and manual strafing never run vnavmesh paths — never dash.
        _vNav.Setup(x => x.IsPathRunning).Returns(false);
        Assert.False(CheckAfterForwardRun());
    }

    [Fact]
    public void DashBlocked_WhenStrafing_FacingOffMovement()
    {
        // Running +Z but facing +X (auto-face at a boss to the side): dashing forward would launch
        // the caster at the boss, not down the path.
        Assert.False(CheckAfterForwardRun(facing: MathF.PI / 2f));
    }

    [Fact]
    public void DashBlocked_WhenLandingIsOffMesh()
    {
        // No navmesh floor past z=10 — the dash landing (z=15) is an abyss.
        SetFloor((Vector3 p, out Vector3 floor) => { floor = p; return p.Z <= 10f; });
        Assert.False(CheckAfterForwardRun());
    }

    [Fact]
    public void DashBlocked_WhenMidpointIsAGap()
    {
        // Floor at the landing but a hole midway (z ≈ 7.5) — the dash falls in.
        SetFloor((Vector3 p, out Vector3 floor) => { floor = p; return p.Z < 6f || p.Z > 9f; });
        Assert.False(CheckAfterForwardRun());
    }

    [Fact]
    public void DashBlocked_WhenFloorDropsALedge()
    {
        // Navmesh continues but 5y lower — a ledge drop, not a walkable slope.
        SetFloor((Vector3 p, out Vector3 floor) => { floor = new Vector3(p.X, -5f, p.Z); return true; });
        Assert.False(CheckAfterForwardRun());
    }

    [Fact]
    public void DashBlocked_WhenBmrFlagsLandingHazardous()
    {
        _bossMod.Setup(x => x.QueryPositionSafety(It.Is<Vector3>(v => v.Z > 10f), It.IsAny<float>()))
            .Returns(PositionSafety.Unsafe);
        Assert.False(CheckAfterForwardRun());
    }

    [Fact]
    public void DashBlocked_WithoutVNav_FailClosed()
    {
        Assert.False(CheckAfterForwardRun(vNav: new Mock<IVNavService>().Object));
    }

    [Fact]
    public void DashBlocked_WhenNotActuallyTranslating()
    {
        ForwardDashGuard.IsForwardDashSafe(Vector3.Zero, 0f, ForwardDashGuard.SmudgeDashYalms, _vNav.Object, _bossMod.Object);
        _now = _now.AddSeconds(0.05);
        // Same position on the second frame: not moving — no dash, whatever isMoving's grace says.
        Assert.False(ForwardDashGuard.IsForwardDashSafe(Vector3.Zero, 0f, ForwardDashGuard.SmudgeDashYalms, _vNav.Object, _bossMod.Object));
    }

    [Fact]
    public void DashBlocked_OnFirstOrStaleSample()
    {
        // First-ever call has no velocity history.
        Assert.False(ForwardDashGuard.IsForwardDashSafe(Vector3.Zero, 0f, ForwardDashGuard.SmudgeDashYalms, _vNav.Object, _bossMod.Object));

        // A sample older than the freshness window (lag spike) is discarded too.
        _now = _now.AddSeconds(2.0);
        Assert.False(ForwardDashGuard.IsForwardDashSafe(new Vector3(0f, 0f, 0.4f), 0f, ForwardDashGuard.SmudgeDashYalms, _vNav.Object, _bossMod.Object));
    }
}
