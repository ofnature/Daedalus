using System;
using System.Numerics;
using Moq;
using Daedalus.Services.Positional.Navigation;

namespace Daedalus.Tests.Services.Positional.Navigation;

public class MovementArbiterTests
{
    private readonly Mock<IVNavService> _inner = new();
    private readonly Mock<IBossModSafetyService> _bossMod = new();
    private bool _yieldEnabled = true;
    private DateTime _now = new(2026, 7, 5, 12, 0, 0, DateTimeKind.Utc);

    private static readonly Vector3 DestA = new(10f, 0f, 10f);
    private static readonly Vector3 DestB = new(20f, 0f, 20f);

    public MovementArbiterTests()
    {
        // Calm defaults: BMR loaded, no danger, vNav idle, submissions succeed.
        _bossMod.Setup(x => x.IsAvailable).Returns(true);
        _bossMod.Setup(x => x.ForbiddenZonesCount).Returns(0);
        _bossMod.Setup(x => x.IsBmrNavigating).Returns(false);
        _bossMod.Setup(x => x.NextDamageInSeconds).Returns(float.MaxValue);
        _bossMod.Setup(x => x.ForbiddenZoneActivationInSeconds).Returns(float.MaxValue);
        _bossMod.Setup(x => x.BmrNaviTarget).Returns((Vector3?)null);

        _inner.Setup(x => x.IsPathRunning).Returns(false);
        _inner.Setup(x => x.IsPathfindInProgress).Returns(false);
        _inner.Setup(x => x.PathfindAndMoveTo(It.IsAny<Vector3>(), It.IsAny<bool>()))
            .Returns(VNavMoveResult.Queued);
        _inner.Setup(x => x.PathfindAndMoveCloseTo(It.IsAny<Vector3>(), It.IsAny<float>(), It.IsAny<bool>()))
            .Returns(VNavMoveResult.Queued);
    }

    private MovementArbiter CreateArbiter()
        => new(_inner.Object, _bossMod.Object, () => _yieldEnabled, () => _now);

    private void Advance(double seconds) => _now = _now.AddSeconds(seconds);

    [Fact]
    public void Submit_WhenBmrUnavailable_DelegatesQueued()
    {
        _bossMod.Setup(x => x.IsAvailable).Returns(false);
        var arbiter = CreateArbiter();
        arbiter.BeginFrame();

        var result = arbiter.PathfindAndMoveCloseTo(DestA, 0.35f);

        Assert.Equal(VNavMoveResult.Queued, result);
        _inner.Verify(x => x.PathfindAndMoveCloseTo(DestA, 0.35f, false), Times.Once);
    }

    [Fact]
    public void BeginFrame_DangerWithOwnedRunningPath_StopsExactlyOnce()
    {
        var arbiter = CreateArbiter();
        arbiter.BeginFrame();
        Assert.Equal(VNavMoveResult.Queued, arbiter.PathfindAndMoveCloseTo(DestA, 0.35f));
        _inner.Setup(x => x.IsPathRunning).Returns(true);

        _bossMod.Setup(x => x.ForbiddenZoneActivationInSeconds).Returns(1.0f);
        arbiter.BeginFrame();

        _inner.Verify(x => x.Stop(), Times.Once);
        Assert.Equal(VNavMoveResult.Suppressed, arbiter.PathfindAndMoveCloseTo(DestA, 0.35f));
        Assert.Equal(MovementSuppression.BmrDanger, arbiter.Snapshot.Suppression);
    }

    [Fact]
    public void Submit_ZonesFarFromActivation_DoesNotSuppress()
    {
        // Zones appear at cast START (5-7s before activation on trash). Yielding for the whole cast
        // parked melee out of range — only activation-imminent (or BMR steering) may suppress.
        _bossMod.Setup(x => x.ForbiddenZonesCount).Returns(2);
        _bossMod.Setup(x => x.ForbiddenZoneActivationInSeconds).Returns(5.3f);

        var arbiter = CreateArbiter();
        arbiter.BeginFrame();

        Assert.Equal(VNavMoveResult.Queued, arbiter.PathfindAndMoveCloseTo(DestA, 0.35f));
    }

    [Fact]
    public void Steering_StickyAcrossNavigatingFlicker_KeepsSuppressing()
    {
        var arbiter = CreateArbiter();

        // BMR AI has a nav target for one frame, then the raw signal drops (per-frame flicker).
        _bossMod.Setup(x => x.IsBmrNavigating).Returns(true);
        arbiter.BeginFrame();
        _bossMod.Setup(x => x.IsBmrNavigating).Returns(false);

        // 2s later, still inside the 3s sticky window → BMR owns movement.
        Advance(2.0);
        arbiter.BeginFrame();
        Assert.Equal(VNavMoveResult.Suppressed, arbiter.PathfindAndMoveCloseTo(DestA, 0.35f));
        Assert.Equal(MovementSuppression.BmrNavigating, arbiter.Snapshot.Suppression);

        // Cast-hold stays instantaneous: no nav target this frame → casters may hard-cast.
        Assert.False(arbiter.IsExternalMovementActive);

        // Sticky expired (3.5s) and the regrab cooldown (0.75s from the last sticky-danger frame) has
        // passed → movement is ours again.
        Advance(1.5);
        arbiter.BeginFrame();
        Assert.Equal(VNavMoveResult.Queued, arbiter.PathfindAndMoveCloseTo(DestA, 0.35f));
    }

    [Theory]
    [InlineData("navigating")]
    [InlineData("zoneActivation")]
    [InlineData("nextDamage")]
    public void Submit_EachDangerSignal_IndependentlySuppresses(string signal)
    {
        switch (signal)
        {
            case "navigating":
                _bossMod.Setup(x => x.IsBmrNavigating).Returns(true);
                break;
            case "zoneActivation":
                _bossMod.Setup(x => x.ForbiddenZoneActivationInSeconds).Returns(1.4f);
                break;
            case "nextDamage":
                _bossMod.Setup(x => x.NextDamageInSeconds).Returns(1.4f);
                break;
        }

        var arbiter = CreateArbiter();
        arbiter.BeginFrame();

        Assert.Equal(VNavMoveResult.Suppressed, arbiter.PathfindAndMoveCloseTo(DestA, 0.35f));
        _inner.Verify(x => x.PathfindAndMoveCloseTo(It.IsAny<Vector3>(), It.IsAny<float>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public void BeginFrame_DangerWithForeignPath_NeverStops()
    {
        // A path is running that the arbiter did not grant (AutoDuty / manual /vnav).
        _inner.Setup(x => x.IsPathRunning).Returns(true);
        _bossMod.Setup(x => x.ForbiddenZoneActivationInSeconds).Returns(1.0f);

        var arbiter = CreateArbiter();
        arbiter.BeginFrame();

        _inner.Verify(x => x.Stop(), Times.Never);
    }

    [Fact]
    public void Submit_AfterDangerClears_WaitsOutRegrabCooldown()
    {
        var arbiter = CreateArbiter();
        _bossMod.Setup(x => x.ForbiddenZoneActivationInSeconds).Returns(1.0f);
        arbiter.BeginFrame();

        _bossMod.Setup(x => x.ForbiddenZoneActivationInSeconds).Returns(float.MaxValue);
        Advance(0.4); // inside the 0.75s cooldown
        arbiter.BeginFrame();
        Assert.Equal(VNavMoveResult.Suppressed, arbiter.PathfindAndMoveCloseTo(DestA, 0.35f));
        Assert.Equal(MovementSuppression.RegrabCooldown, arbiter.Snapshot.Suppression);

        Advance(0.5); // now 0.9s past danger
        arbiter.BeginFrame();
        Assert.Equal(VNavMoveResult.Queued, arbiter.PathfindAndMoveCloseTo(DestA, 0.35f));
    }

    [Fact]
    public void Submit_WithinRepathInterval_SuppressedThenAllowed()
    {
        var arbiter = CreateArbiter();
        arbiter.BeginFrame();
        Assert.Equal(VNavMoveResult.Queued, arbiter.PathfindAndMoveCloseTo(DestA, 0.35f));

        // Path finished instantly (vNav idle) so only the interval rule applies.
        Advance(0.2);
        arbiter.BeginFrame();
        Assert.Equal(VNavMoveResult.Suppressed, arbiter.PathfindAndMoveCloseTo(DestB, 0.35f));

        Advance(0.15); // 0.35s since grant
        arbiter.BeginFrame();
        Assert.Equal(VNavMoveResult.Queued, arbiter.PathfindAndMoveCloseTo(DestB, 0.35f));
    }

    [Fact]
    public void Submit_OwnedPathWithinCommitmentWindow_CannotBeReplaced()
    {
        var arbiter = CreateArbiter();
        arbiter.BeginFrame();
        Assert.Equal(VNavMoveResult.Queued, arbiter.PathfindAndMoveCloseTo(DestA, 0.35f));
        _inner.Setup(x => x.IsPathRunning).Returns(true);

        Advance(0.4); // past repath interval (0.3) but inside commitment (0.5)
        arbiter.BeginFrame();

        Assert.Equal(VNavMoveResult.Suppressed, arbiter.PathfindAndMoveCloseTo(DestB, 0.35f));
        Assert.Equal(MovementSuppression.PathCommitment, arbiter.Snapshot.Suppression);
    }

    [Fact]
    public void Submit_OwnedPathNearDuplicateDestination_Suppressed()
    {
        var arbiter = CreateArbiter();
        arbiter.BeginFrame();
        Assert.Equal(VNavMoveResult.Queued, arbiter.PathfindAndMoveCloseTo(DestA, 0.35f));
        _inner.Setup(x => x.IsPathRunning).Returns(true);

        Advance(0.6); // past both interval and commitment
        arbiter.BeginFrame();

        // 0.5y away from the running goal — below the 0.75y delta → jitter, keep the current path.
        var nearDuplicate = DestA + new Vector3(0.5f, 0f, 0f);
        Assert.Equal(VNavMoveResult.Suppressed, arbiter.PathfindAndMoveCloseTo(nearDuplicate, 0.35f));
        Assert.Equal(MovementSuppression.DestinationDelta, arbiter.Snapshot.Suppression);

        // A genuinely different destination replaces it.
        Assert.Equal(VNavMoveResult.Queued, arbiter.PathfindAndMoveCloseTo(DestB, 0.35f));
    }

    [Fact]
    public void Stop_AlwaysDelegatesAndClearsOwnership()
    {
        var arbiter = CreateArbiter();
        _bossMod.Setup(x => x.ForbiddenZonesCount).Returns(1);
        arbiter.BeginFrame();

        // Even under danger, Stop passes through (telegraph aborts must never be blocked).
        arbiter.Stop();
        _inner.Verify(x => x.Stop(), Times.Once);

        // Ownership cleared: a later foreign path is not stopped on danger.
        _inner.Invocations.Clear();
        _inner.Setup(x => x.IsPathRunning).Returns(true);
        arbiter.BeginFrame();
        _inner.Verify(x => x.Stop(), Times.Never);
    }

    [Fact]
    public void Submit_YieldDisabled_IgnoresDangerButKeepsRateLimits()
    {
        _yieldEnabled = false;
        _bossMod.Setup(x => x.ForbiddenZonesCount).Returns(5);
        _bossMod.Setup(x => x.IsBmrNavigating).Returns(true);

        var arbiter = CreateArbiter();
        arbiter.BeginFrame();

        Assert.Equal(VNavMoveResult.Queued, arbiter.PathfindAndMoveCloseTo(DestA, 0.35f));

        // Churn protection still applies without BMR yield.
        Advance(0.1);
        arbiter.BeginFrame();
        Assert.Equal(VNavMoveResult.Suppressed, arbiter.PathfindAndMoveCloseTo(DestB, 0.35f));
        Assert.Equal(MovementSuppression.RepathInterval, arbiter.Snapshot.Suppression);
    }

    [Fact]
    public void IsExternalMovementActive_OnlyWhenBmrActuallySteering()
    {
        var arbiter = CreateArbiter();

        // Danger without a nav target (e.g. unavoidable raidwide) must NOT flag external movement —
        // that signal holds caster hard-casts, and blocking every cast before a raidwide is a DPS loss.
        _bossMod.Setup(x => x.NextDamageInSeconds).Returns(1.0f);
        arbiter.BeginFrame();
        Assert.False(arbiter.IsExternalMovementActive);

        _bossMod.Setup(x => x.IsBmrNavigating).Returns(true);
        arbiter.BeginFrame();
        Assert.True(arbiter.IsExternalMovementActive);
    }

    [Fact]
    public void Regrab_GrantInterruptedByDanger_DoublesCooldown()
    {
        var arbiter = CreateArbiter();
        arbiter.BeginFrame();
        Assert.Equal(VNavMoveResult.Queued, arbiter.PathfindAndMoveCloseTo(DestA, 0.35f));

        // Danger onset 1s after the grant (inside the 2s backoff trigger) → cooldown escalates to 1.5s.
        Advance(1.0);
        _bossMod.Setup(x => x.ForbiddenZoneActivationInSeconds).Returns(1.0f);
        arbiter.BeginFrame();

        // Danger clears; at base cooldown (0.75s) we'd re-grab — the escalated cooldown must still hold.
        _bossMod.Setup(x => x.ForbiddenZoneActivationInSeconds).Returns(float.MaxValue);
        Advance(1.0); // 1.0s of calm: past 0.75 base, inside escalated 1.5
        arbiter.BeginFrame();
        Assert.Equal(VNavMoveResult.Suppressed, arbiter.PathfindAndMoveCloseTo(DestB, 0.35f));
        Assert.Equal(MovementSuppression.RegrabCooldown, arbiter.Snapshot.Suppression);

        Advance(0.6); // 1.6s of calm: past the escalated 1.5s
        arbiter.BeginFrame();
        Assert.Equal(VNavMoveResult.Queued, arbiter.PathfindAndMoveCloseTo(DestB, 0.35f));
    }

    [Fact]
    public void Regrab_BackoffResetsAfterSustainedCalm()
    {
        var arbiter = CreateArbiter();
        arbiter.BeginFrame();
        Assert.Equal(VNavMoveResult.Queued, arbiter.PathfindAndMoveCloseTo(DestA, 0.35f));

        // Escalate once (grant interrupted).
        Advance(1.0);
        _bossMod.Setup(x => x.ForbiddenZoneActivationInSeconds).Returns(1.0f);
        arbiter.BeginFrame();
        _bossMod.Setup(x => x.ForbiddenZoneActivationInSeconds).Returns(float.MaxValue);

        // 5s+ of continuous calm resets the backoff to base.
        Advance(5.5);
        arbiter.BeginFrame();

        // New danger episode, then base-cooldown calm — a grab at 0.9s proves the cooldown is 0.75 again.
        _bossMod.Setup(x => x.ForbiddenZoneActivationInSeconds).Returns(1.0f);
        arbiter.BeginFrame();
        _bossMod.Setup(x => x.ForbiddenZoneActivationInSeconds).Returns(float.MaxValue);
        Advance(0.9);
        arbiter.BeginFrame();
        Assert.Equal(VNavMoveResult.Queued, arbiter.PathfindAndMoveCloseTo(DestB, 0.35f));
    }

    // ── Positional-arc intent (boundary camping carve-out) ─────────────────────────────────────────────

    [Fact]
    public void ArcIntent_DuringStickySteeeringOnly_Granted()
    {
        _bossMod.Setup(x => x.IsBmrNavigating).Returns(true);
        var arbiter = CreateArbiter();
        arbiter.BeginFrame();

        // BMR is steering (no real danger): arcs run — BMR keeps range, Daedalus owns the angle.
        Assert.Equal(VNavMoveResult.Queued,
            arbiter.PathfindAndMoveCloseTo(DestA, 0.35f, MovementIntent.PositionalArc));

        // Range-keeping still yields fully.
        Advance(0.4); // clear the repath interval from the arc grant
        arbiter.BeginFrame();
        Assert.Equal(VNavMoveResult.Suppressed, arbiter.PathfindAndMoveCloseTo(DestB, 0.35f));
        Assert.Equal(MovementSuppression.BmrNavigating, arbiter.Snapshot.Suppression);
    }

    [Fact]
    public void ArcIntent_RealDangerImminent_Denied()
    {
        _bossMod.Setup(x => x.ForbiddenZoneActivationInSeconds).Returns(1.0f);
        var arbiter = CreateArbiter();
        arbiter.BeginFrame();

        Assert.Equal(VNavMoveResult.Suppressed,
            arbiter.PathfindAndMoveCloseTo(DestA, 0.35f, MovementIntent.PositionalArc));
        Assert.Equal(MovementSuppression.BmrDanger, arbiter.Snapshot.Suppression);
    }

    [Fact]
    public void ArcIntent_RealDangerRegrabCooldown_Denied()
    {
        var arbiter = CreateArbiter();
        _bossMod.Setup(x => x.ForbiddenZoneActivationInSeconds).Returns(1.0f);
        arbiter.BeginFrame();
        _bossMod.Setup(x => x.ForbiddenZoneActivationInSeconds).Returns(float.MaxValue);

        Advance(0.4); // inside the 0.75s regrab after REAL danger
        arbiter.BeginFrame();
        Assert.Equal(VNavMoveResult.Suppressed,
            arbiter.PathfindAndMoveCloseTo(DestA, 0.35f, MovementIntent.PositionalArc));
        Assert.Equal(MovementSuppression.RegrabCooldown, arbiter.Snapshot.Suppression);

        Advance(0.5);
        arbiter.BeginFrame();
        Assert.Equal(VNavMoveResult.Queued,
            arbiter.PathfindAndMoveCloseTo(DestA, 0.35f, MovementIntent.PositionalArc));
    }

    [Fact]
    public void ArcIntent_SteeringDrivenRegrab_NotDenied()
    {
        // Steering refreshes the general danger clock every frame — the arc regrab must key off the
        // REAL-danger clock or arcs would be cooldown-blocked forever while BMR AI is active.
        _bossMod.Setup(x => x.IsBmrNavigating).Returns(true);
        var arbiter = CreateArbiter();
        arbiter.BeginFrame();
        Advance(1.0);
        arbiter.BeginFrame(); // still steering → _lastDangerUtc = now

        Assert.Equal(VNavMoveResult.Queued,
            arbiter.PathfindAndMoveCloseTo(DestA, 0.35f, MovementIntent.PositionalArc));
    }

    [Fact]
    public void ArcIntent_ChurnRulesStillApply()
    {
        var arbiter = CreateArbiter();
        arbiter.BeginFrame();
        Assert.Equal(VNavMoveResult.Queued,
            arbiter.PathfindAndMoveCloseTo(DestA, 0.35f, MovementIntent.PositionalArc));

        Advance(0.2); // inside the 0.3s repath interval
        arbiter.BeginFrame();
        Assert.Equal(VNavMoveResult.Suppressed,
            arbiter.PathfindAndMoveCloseTo(DestB, 0.35f, MovementIntent.PositionalArc));
        Assert.Equal(MovementSuppression.RepathInterval, arbiter.Snapshot.Suppression);
    }

    [Fact]
    public void BeginFrame_SteeringOnly_DoesNotStopArcIntentPath()
    {
        var arbiter = CreateArbiter();
        arbiter.BeginFrame();
        Assert.Equal(VNavMoveResult.Queued,
            arbiter.PathfindAndMoveCloseTo(DestA, 0.35f, MovementIntent.PositionalArc));
        _inner.Setup(x => x.IsPathRunning).Returns(true);

        _bossMod.Setup(x => x.IsBmrNavigating).Returns(true);
        arbiter.BeginFrame();

        // Mid-hop arc survives steering-only danger (~0.4s hop; BMR isn't dodging anything).
        _inner.Verify(x => x.Stop(), Times.Never);
    }

    [Fact]
    public void BeginFrame_RealDanger_StopsArcIntentPath()
    {
        var arbiter = CreateArbiter();
        arbiter.BeginFrame();
        Assert.Equal(VNavMoveResult.Queued,
            arbiter.PathfindAndMoveCloseTo(DestA, 0.35f, MovementIntent.PositionalArc));
        _inner.Setup(x => x.IsPathRunning).Returns(true);

        _bossMod.Setup(x => x.ForbiddenZoneActivationInSeconds).Returns(1.0f);
        arbiter.BeginFrame();

        _inner.Verify(x => x.Stop(), Times.Once);
    }

    // ── Destination memory (chase machine-gun protection) ──────────────────────────────────────────────

    [Fact]
    public void DestinationMemory_NearDuplicateAfterCompletion_Denied()
    {
        var arbiter = CreateArbiter();
        arbiter.BeginFrame();
        Assert.Equal(VNavMoveResult.Queued, arbiter.PathfindAndMoveCloseTo(DestA, 0.35f));

        // Path completed instantly (inner idle) — ownership cleared, but the grant is remembered.
        Advance(0.35); // past the repath interval, inside the 1s memory
        arbiter.BeginFrame();
        var nearDuplicate = DestA + new Vector3(0.2f, 0f, 0f); // the field-logged 0.1-0.2y creep
        Assert.Equal(VNavMoveResult.Suppressed, arbiter.PathfindAndMoveCloseTo(nearDuplicate, 0.35f));
        Assert.Equal(MovementSuppression.DestinationDelta, arbiter.Snapshot.Suppression);

        // A real correction (mob actually moved) passes immediately.
        Assert.Equal(VNavMoveResult.Queued, arbiter.PathfindAndMoveCloseTo(DestB, 0.35f));
    }

    [Fact]
    public void DestinationMemory_ExpiresAfterWindow()
    {
        var arbiter = CreateArbiter();
        arbiter.BeginFrame();
        Assert.Equal(VNavMoveResult.Queued, arbiter.PathfindAndMoveCloseTo(DestA, 0.35f));

        Advance(1.1); // memory window (1.0s) expired
        arbiter.BeginFrame();
        var nearDuplicate = DestA + new Vector3(0.2f, 0f, 0f);
        Assert.Equal(VNavMoveResult.Queued, arbiter.PathfindAndMoveCloseTo(nearDuplicate, 0.35f));
    }

    [Fact]
    public void PassThroughReads_DelegateToInner()
    {
        _inner.Setup(x => x.IsAvailable).Returns(true);
        _inner.Setup(x => x.IsNavReady).Returns(true);
        _inner.Setup(x => x.IsPathRunning).Returns(true);
        _inner.Setup(x => x.IsPathfindInProgress).Returns(true);
        _inner.Setup(x => x.SnapToFloor(DestA)).Returns(DestB);

        var arbiter = CreateArbiter();

        Assert.True(arbiter.IsAvailable);
        Assert.True(arbiter.IsNavReady);
        Assert.True(arbiter.IsPathRunning);
        Assert.True(arbiter.IsPathfindInProgress);
        Assert.Equal(DestB, arbiter.SnapToFloor(DestA));
    }
}
