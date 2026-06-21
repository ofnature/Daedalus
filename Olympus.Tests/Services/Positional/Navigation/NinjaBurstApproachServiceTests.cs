using System.Numerics;
using Moq;
using Olympus.Services.Action;
using Olympus.Services.Positional;
using Olympus.Services.Positional.Navigation;

namespace Olympus.Tests.Services.Positional.Navigation;

public class NinjaBurstApproachServiceTests
{
    private readonly Mock<IVNavService> _vNav = new();
    private readonly Mock<IBossModSafetyService> _bossMod = new();
    private readonly Mock<IActionService> _action = new();

    public NinjaBurstApproachServiceTests()
    {
        _bossMod.Setup(x => x.ShouldAbortMovement()).Returns(false);
        _bossMod.Setup(x => x.QueryPositionSafety(It.IsAny<Vector3>(), It.IsAny<float>()))
            .Returns(PositionSafety.Safe);
        _bossMod.Setup(x => x.IsSegmentSafe(It.IsAny<Vector3>(), It.IsAny<Vector3>())).Returns(true);

        _action.Setup(x => x.GcdRemaining).Returns(1.5f);
        _action.Setup(x => x.IsCasting).Returns(false);
        _action.Setup(x => x.AnimationLockRemaining).Returns(0f);

        _vNav.Setup(x => x.IsPathRunning).Returns(false);
        _vNav.Setup(x => x.IsPathfindInProgress).Returns(false);
        _vNav.Setup(x => x.SnapToFloor(It.IsAny<Vector3>())).Returns<Vector3>(v => v);
        _vNav.Setup(x => x.PathfindAndMoveCloseTo(It.IsAny<Vector3>(), It.IsAny<float>(), It.IsAny<bool>()))
            .Returns(VNavMoveResult.Queued);
    }

    [Fact]
    public void Update_WhenOutOfMeleeAndBurstReady_QueuesVNavMove()
    {
        var service = CreateService();

        service.Update(CreateRequest(alreadyInMelee: false));

        Assert.Equal(PositionalMovementPhase.Moving, service.State.Phase);
        _vNav.Verify(x => x.PathfindAndMoveCloseTo(It.IsAny<Vector3>(), It.IsAny<float>(), false), Times.Once);
    }

    [Fact]
    public void Update_WhenPositionalPathActiveButOutOfMelee_StillQueuesMove()
    {
        var service = CreateService();

        service.Update(CreateRequest(alreadyInMelee: false, positionalPathActive: true));

        Assert.Equal(PositionalMovementPhase.Moving, service.State.Phase);
        _vNav.Verify(x => x.PathfindAndMoveCloseTo(It.IsAny<Vector3>(), It.IsAny<float>(), It.IsAny<bool>()), Times.Once);
    }

    [Fact]
    public void Update_WhenPositionalPathActiveAndInMelee_Skips()
    {
        var service = CreateService();

        service.Update(CreateRequest(alreadyInMelee: true, positionalPathActive: true));

        Assert.Equal(PositionalMovementPhase.Skipped, service.State.Phase);
        Assert.Equal("positional path active", service.State.SkipReason);
        _vNav.Verify(x => x.PathfindAndMoveCloseTo(It.IsAny<Vector3>(), It.IsAny<float>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public void Update_DuringAnimationLock_StillQueuesMove()
    {
        var service = CreateService();
        _action.Setup(x => x.AnimationLockRemaining).Returns(0.45f);

        service.Update(CreateRequest(alreadyInMelee: false));

        Assert.Equal(PositionalMovementPhase.Moving, service.State.Phase);
        _vNav.Verify(x => x.PathfindAndMoveCloseTo(It.IsAny<Vector3>(), It.IsAny<float>(), false), Times.Once);
    }

    [Fact]
    public void Update_WhenBurstPrepActiveButKbNotReady_StillQueuesMove()
    {
        var service = CreateService();

        service.Update(CreateRequest(alreadyInMelee: false, burstPrepActive: true));

        Assert.Equal(PositionalMovementPhase.Moving, service.State.Phase);
        _vNav.Verify(x => x.PathfindAndMoveCloseTo(It.IsAny<Vector3>(), It.IsAny<float>(), false), Times.Once);
    }

    [Fact]
    public void Update_WhenNotInBurstPrep_SkipsWithoutQueuing()
    {
        var service = CreateService();

        service.Update(CreateRequest(alreadyInMelee: false, burstPrepActive: false));

        Assert.Equal(PositionalMovementPhase.Skipped, service.State.Phase);
        Assert.Equal("not in burst prep", service.State.SkipReason);
        _vNav.Verify(x => x.PathfindAndMoveCloseTo(It.IsAny<Vector3>(), It.IsAny<float>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public void Update_WhenNotInCombat_DoesNotStopUserVNavPath()
    {
        var service = CreateService();
        _vNav.Setup(x => x.IsPathRunning).Returns(true);

        service.Update(CreateRequest(alreadyInMelee: false, inCombat: false));

        Assert.Equal(PositionalMovementPhase.Skipped, service.State.Phase);
        _vNav.Verify(x => x.Stop(), Times.Never);
    }

    [Fact]
    public void Update_WhenTargetNullButPathRunning_KeepsMoving()
    {
        var service = CreateService();
        _vNav.Setup(x => x.IsPathRunning).Returns(true);

        service.Update(CreateRequest(alreadyInMelee: false, target: null));

        Assert.Equal(PositionalMovementPhase.Moving, service.State.Phase);
        _vNav.Verify(x => x.Stop(), Times.Never);
    }

    [Fact]
    public void Update_WhenVNavBusyButPathRunning_DoesNotStopPath()
    {
        var service = CreateService();
        _vNav.Setup(x => x.IsPathRunning).Returns(true);

        service.Update(CreateRequest(alreadyInMelee: false));

        Assert.Equal(PositionalMovementPhase.Moving, service.State.Phase);
        _vNav.Verify(x => x.Stop(), Times.Never);
    }

    [Fact]
    public void Update_WhenPathfindInProgress_ShowsMovingNotSkipped()
    {
        var service = CreateService();
        _vNav.Setup(x => x.IsPathfindInProgress).Returns(true);

        service.Update(CreateRequest(alreadyInMelee: false));

        Assert.Equal(PositionalMovementPhase.Moving, service.State.Phase);
        Assert.Contains("pathfind in progress", service.State.SkipReason);
        _vNav.Verify(x => x.PathfindAndMoveCloseTo(It.IsAny<Vector3>(), It.IsAny<float>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public void Update_WhenVNavBusyAfterPreemptStop_RetriesOnce()
    {
        var service = CreateService();
        var attempts = 0;
        _vNav.Setup(x => x.PathfindAndMoveCloseTo(It.IsAny<Vector3>(), It.IsAny<float>(), It.IsAny<bool>()))
            .Returns(() =>
            {
                attempts++;
                return attempts >= 2 ? VNavMoveResult.Queued : VNavMoveResult.Busy;
            });

        service.Update(CreateRequest(alreadyInMelee: false));

        Assert.Equal(PositionalMovementPhase.Moving, service.State.Phase);
        Assert.Equal(2, attempts);
        _vNav.Verify(x => x.Stop(), Times.AtLeastOnce);
    }

    private NinjaBurstApproachService CreateService()
        => new(_vNav.Object, _bossMod.Object);

    private NinjaBurstApproachRequest CreateRequest(
        bool alreadyInMelee,
        bool positionalPathActive = false,
        bool burstPrepActive = true,
        PositionalMovementTarget? target = null,
        bool inCombat = true)
        => new(
            Enabled: true,
            InCombat: inCombat,
            BurstPrepActive: burstPrepActive,
            AlreadyInMeleeRange: alreadyInMelee,
            PositionalPathActive: positionalPathActive,
            PlayerPosition: new Vector3(0, 0, 0),
            PlayerHitboxRadius: 0.5f,
            Target: target ?? new PositionalMovementTarget(
                new Vector3(10, 0, 0),
                HitboxRadius: 2f,
                RotationRadians: 0f,
                HasPositionalImmunity: false),
            ActionService: _action.Object);
}
