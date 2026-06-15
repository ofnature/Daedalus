using System.Numerics;
using Moq;
using Olympus.Data;
using Olympus.Services.Action;
using Olympus.Services.Positional;
using Olympus.Services.Positional.Navigation;

namespace Olympus.Tests.Services.Positional.Navigation;

public class PositionalMovementServiceTests
{
    private readonly Mock<IVNavService> _vNav = new();
    private readonly Mock<IBossModSafetyService> _bossMod = new();
    private readonly Mock<IActionService> _action = new();
    private readonly TestAnticipationProvider _anticipation = new();

    public PositionalMovementServiceTests()
    {
        _bossMod.Setup(x => x.ShouldAbortMovement()).Returns(false);
        _bossMod.Setup(x => x.QueryPositionSafety(It.IsAny<Vector3>(), It.IsAny<float>()))
            .Returns(PositionSafety.Safe);
        _bossMod.Setup(x => x.IsSegmentSafe(It.IsAny<Vector3>(), It.IsAny<Vector3>())).Returns(true);

        _action.Setup(x => x.GcdRemaining).Returns(2.0f);
        _action.Setup(x => x.IsCasting).Returns(false);
        _action.Setup(x => x.AnimationLockRemaining).Returns(0f);

        _vNav.Setup(x => x.IsPathRunning).Returns(false);
        _vNav.Setup(x => x.SnapToFloor(It.IsAny<Vector3>())).Returns<Vector3>(v => v);
        _vNav.Setup(x => x.PathfindAndMoveCloseTo(It.IsAny<Vector3>(), It.IsAny<float>(), It.IsAny<bool>()))
            .Returns(VNavMoveResult.Queued);
    }

    [Fact]
    public void Update_BeginUpdateSnapshotCalledEachTick()
    {
        var service = CreateService();
        _anticipation.Next = null;

        service.Update(CreateRequest());

        _bossMod.Verify(x => x.BeginUpdateSnapshot(), Times.Once);
    }

    [Fact]
    public void Update_WhenAnticipatedAndSafe_QueuesVNavMove()
    {
        var service = CreateService();
        _anticipation.Next = new PositionalAnticipation(PositionalType.Rear, 7481, PositionalAnticipationReason.ComboSetup);

        service.Update(CreateRequest());

        Assert.Equal(PositionalMovementPhase.Moving, service.State.Phase);
        _vNav.Verify(x => x.PathfindAndMoveCloseTo(It.IsAny<Vector3>(), It.IsAny<float>(), false), Times.Once);
    }

    [Fact]
    public void Update_WhenUnsafe_SkipsWithoutStalling()
    {
        var service = CreateService();
        _anticipation.Next = new PositionalAnticipation(PositionalType.Rear, 7481, PositionalAnticipationReason.ComboSetup);
        _bossMod.Setup(x => x.QueryPositionSafety(It.IsAny<Vector3>(), It.IsAny<float>()))
            .Returns(PositionSafety.Unsafe);

        service.Update(CreateRequest());

        Assert.Equal(PositionalMovementPhase.Skipped, service.State.Phase);
        _vNav.Verify(x => x.PathfindAndMoveCloseTo(It.IsAny<Vector3>(), It.IsAny<float>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public void Update_WhenWouldClipGcd_SkipsMove()
    {
        var service = CreateService();
        _anticipation.Next = new PositionalAnticipation(PositionalType.Rear, 7481, PositionalAnticipationReason.ComboSetup);
        _action.Setup(x => x.GcdRemaining).Returns(0.05f);

        service.Update(CreateRequest());

        Assert.Equal(PositionalMovementPhase.Skipped, service.State.Phase);
        Assert.Equal("would clip GCD", service.State.SkipReason);
    }

    [Fact]
    public void Update_WhenTelegraphAppears_AbortsRunningPath()
    {
        var service = CreateService();
        _vNav.Setup(x => x.IsPathRunning).Returns(true);
        _bossMod.Setup(x => x.ShouldAbortMovement()).Returns(true);

        service.Update(CreateRequest());

        Assert.Equal(PositionalMovementPhase.Aborted, service.State.Phase);
        _vNav.Verify(x => x.Stop(), Times.Once);
    }

    [Fact]
    public void Update_WhenAlreadyAtRear_SkipsMove()
    {
        var service = CreateService();
        _anticipation.Next = new PositionalAnticipation(PositionalType.Rear, 7481, PositionalAnticipationReason.ComboSetup);

        var ctx = BaseAnticipationContext with { IsAtRear = true };
        service.Update(CreateRequest(anticipationContext: ctx));

        Assert.Equal(PositionalMovementPhase.Skipped, service.State.Phase);
        Assert.Equal("already at positional", service.State.SkipReason);
    }

    private sealed class TestAnticipationProvider : IPositionalAnticipationProvider
    {
        public PositionalAnticipation? Next { get; set; }

        public PositionalAnticipation? GetAnticipatedPositional(in PositionalAnticipationContext context) => Next;
    }

    private PositionalMovementService CreateService()
        => new(_vNav.Object, _bossMod.Object);

    private PositionalMovementUpdateRequest CreateRequest(
        PositionalAnticipationContext? anticipationContext = null)
    {
        return new PositionalMovementUpdateRequest(
            AnticipationProvider: _anticipation,
            AnticipationContext: anticipationContext ?? BaseAnticipationContext,
            PlayerPosition: new Vector3(0f, 0f, 5f),
            PlayerHitboxRadius: 0.5f,
            Target: new PositionalMovementTarget(
                Position: Vector3.Zero,
                HitboxRadius: 2f,
                RotationRadians: 0f,
                HasPositionalImmunity: false),
            ActionService: _action.Object,
            InCombat: true);
    }

    private static PositionalAnticipationContext BaseAnticipationContext => new(
        LastComboAction: SAMActions.Jinpu.ActionId,
        PlayerLevel: 100,
        HasTrueNorth: false,
        TargetHasPositionalImmunity: false,
        IsAtRear: false,
        IsAtFlank: false);
}
