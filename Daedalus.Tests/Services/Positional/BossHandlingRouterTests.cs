using System.Numerics;
using Daedalus.Config;
using Daedalus.Services.Positional.Navigation;
using Xunit;

namespace Daedalus.Tests.Services.Positional;

/// <summary>
/// The router is the whole point of the boss-handling setting: eight consumers keep asking one
/// interface, and exactly one engine answers. What is worth testing is that the selection is
/// honoured on every member — a member that forgot to delegate would silently keep talking to
/// BossMod while the user believes Minerva is driving.
/// </summary>
public sealed class BossHandlingRouterTests
{
    private sealed class Stub : IBossModSafetyService
    {
        private readonly bool _available;
        public Stub(bool available) => _available = available;

        public int SnapshotCalls { get; private set; }

        public bool IsAvailable => _available;
        public void BeginUpdateSnapshot() => SnapshotCalls++;
        public bool ShouldAbortMovement() => _available;
        public PositionSafety QueryPositionSafety(Vector3 destination, float window = 3f)
            => _available ? PositionSafety.Unsafe : PositionSafety.Safe;
        public bool IsSegmentSafe(Vector3 from, Vector3 to) => !_available;
        public float NextDamageInSeconds => _available ? 1f : 99f;
        public float ForbiddenZoneActivationInSeconds => _available ? 2f : 98f;
        public int ForbiddenZonesCount => _available ? 5 : 0;
        public bool IsBmrNavigating => _available;
        public Vector3? BmrNaviTarget => _available ? Vector3.One : null;
    }

    private static (BossHandlingRouter router, Stub bmr, Stub min, BossHandling[] sel) Build()
    {
        var bmr = new Stub(true);
        var min = new Stub(false);
        var sel = new[] { BossHandling.BossMod };
        return (new BossHandlingRouter(bmr, min, () => sel[0]), bmr, min, sel);
    }

    [Fact]
    public void DefaultSelection_RoutesToBossMod()
    {
        var (router, _, _, _) = Build();
        Assert.Equal(BossHandling.BossMod, router.Selected);
        Assert.True(router.IsAvailable);
        Assert.Equal(1f, router.NextDamageInSeconds);
    }

    /// <summary>Every member must follow the switch, not just the ones someone remembered.</summary>
    [Fact]
    public void SelectingMinerva_MovesEveryMember()
    {
        var (router, bmr, min, sel) = Build();
        sel[0] = BossHandling.Minerva;

        Assert.Equal(BossHandling.Minerva, router.Selected);
        Assert.False(router.IsAvailable);
        Assert.False(router.ShouldAbortMovement());
        Assert.Equal(PositionSafety.Safe, router.QueryPositionSafety(Vector3.Zero));
        Assert.True(router.IsSegmentSafe(Vector3.Zero, Vector3.One));
        Assert.Equal(99f, router.NextDamageInSeconds);
        Assert.Equal(98f, router.ForbiddenZoneActivationInSeconds);
        Assert.Equal(0, router.ForbiddenZonesCount);
        Assert.False(router.IsBmrNavigating);
        Assert.Null(router.BmrNaviTarget);

        router.BeginUpdateSnapshot();
        Assert.Equal(1, min.SnapshotCalls);
        Assert.Equal(0, bmr.SnapshotCalls);
    }

    /// <summary>The user can switch mid-session, so the selection is read per call.</summary>
    [Fact]
    public void SwitchingBack_TakesEffectImmediately()
    {
        var (router, _, _, sel) = Build();
        sel[0] = BossHandling.Minerva;
        Assert.False(router.IsAvailable);
        sel[0] = BossHandling.BossMod;
        Assert.True(router.IsAvailable);
    }

    /// <summary>
    /// Picking an engine that is not installed must NOT silently fall back to the other one. The
    /// setting would then be lying about who is driving, and the user would debug the wrong
    /// plugin. Unavailable is the honest answer, and every consumer fails open on it.
    /// </summary>
    [Fact]
    public void AnUninstalledSelection_DoesNotFallBack()
    {
        var bmr = new Stub(true);
        var minervaMissing = new Stub(false);
        var router = new BossHandlingRouter(bmr, minervaMissing, () => BossHandling.Minerva);

        Assert.False(router.IsAvailable);
        Assert.Equal(PositionSafety.Safe, router.QueryPositionSafety(Vector3.Zero));
    }

    /// <summary>
    /// The BMR-management router: BossMod when selected, a switched-off engine otherwise. This is
    /// what stops Daedalus creating and applying a BossMod autorotation preset while Minerva
    /// dodges.
    /// </summary>
    [Fact]
    public void ManagementRouter_GoesInertUnderMinerva()
    {
        var sel = new[] { BossHandling.BossMod };
        var router = new BossHandlingRouter(new Stub(true), InactiveSafetyService.Instance, () => sel[0]);

        Assert.True(router.IsAvailable);
        sel[0] = BossHandling.Minerva;
        Assert.False(router.IsAvailable);
        Assert.Equal(0, router.ForbiddenZonesCount);
        Assert.Equal(float.MaxValue, router.NextDamageInSeconds);
    }
}
