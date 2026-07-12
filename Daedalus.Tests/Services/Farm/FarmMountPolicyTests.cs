using Daedalus.Config;
using Daedalus.Services.Farm;
using Xunit;

namespace Daedalus.Tests.Services.Farm;

/// <summary>
/// Farm v4 mounted-travel decisions (docs/farm-mode.md §v4): mount/walk thresholds, dismount
/// choreography, combat interrupts, and the specific-mount unlock fallback.
/// </summary>
public sealed class FarmMountPolicyTests
{
    private static FarmTravelAction Decide(
        bool mounted = false,
        bool inFlight = false,
        bool inCombat = false,
        bool castPending = false,
        bool suppressed = false,
        float distance = 100f,
        float mountThreshold = 40f,
        float dismountRange = 15f,
        bool destinationIsMob = true)
        => FarmMountPolicy.Decide(
            mounted, inFlight, inCombat, castPending, suppressed,
            distance, mountThreshold, dismountRange, destinationIsMob);

    // ---- mount / walk threshold ----

    [Fact]
    public void UnmountedFarLeg_CastsMount()
        => Assert.Equal(FarmTravelAction.CastMount, Decide(distance: 80f, mountThreshold: 40f));

    [Fact]
    public void UnmountedShortHop_Walks()
        => Assert.Equal(FarmTravelAction.Walk, Decide(distance: 25f, mountThreshold: 40f));

    [Fact]
    public void ThresholdIsExclusive_AtExactlyThresholdWalks()
        => Assert.Equal(FarmTravelAction.Walk, Decide(distance: 40f, mountThreshold: 40f));

    [Fact]
    public void SuppressedMount_WalksEvenWhenFar()
        => Assert.Equal(FarmTravelAction.Walk, Decide(distance: 90f, suppressed: true));

    [Fact]
    public void CastPending_AwaitsMount()
        => Assert.Equal(FarmTravelAction.AwaitMount, Decide(castPending: true, distance: 80f));

    // ---- riding / dismount ----

    [Fact]
    public void MountedFarFromMob_Rides()
        => Assert.Equal(FarmTravelAction.Ride, Decide(mounted: true, distance: 60f));

    [Fact]
    public void MountedAtDismountRange_Dismounts()
        => Assert.Equal(FarmTravelAction.Dismount, Decide(mounted: true, distance: 14f, dismountRange: 15f));

    [Fact]
    public void MountedAirborneAtMob_LandsBeforeDismounting()
        => Assert.Equal(
            FarmTravelAction.LandThenDismount,
            Decide(mounted: true, inFlight: true, distance: 10f, dismountRange: 15f));

    [Fact]
    public void SpotDestination_NeverDismountsAtRange_StaysRiding()
        => Assert.Equal(
            FarmTravelAction.Ride,
            Decide(mounted: true, distance: 10f, dismountRange: 15f, destinationIsMob: false));

    [Fact]
    public void MountedArrivedAtSpot_HandsBackToGroundLogic()
        => Assert.Equal(
            FarmTravelAction.Walk,
            Decide(mounted: true, distance: 3f, destinationIsMob: false));

    // ---- combat interrupts ----

    [Fact]
    public void CombatWhileMounted_DismountsImmediately()
        => Assert.Equal(FarmTravelAction.Dismount, Decide(mounted: true, inCombat: true, distance: 90f));

    [Fact]
    public void CombatWhileUnmounted_NeverMounts()
        => Assert.Equal(FarmTravelAction.Walk, Decide(inCombat: true, distance: 90f));

    [Fact]
    public void CombatBeatsPendingCast()
        => Assert.Equal(FarmTravelAction.Walk, Decide(inCombat: true, castPending: true, distance: 90f));

    // ---- specific-mount fallback ----

    [Fact]
    public void SpecificUnlocked_UsesSpecificMount()
        => Assert.True(FarmMountPolicy.UseSpecificMount(FarmMountMode.Specific, 71, specificUnlocked: true));

    [Fact]
    public void SpecificNotUnlocked_FallsBackToRoulette()
        => Assert.False(FarmMountPolicy.UseSpecificMount(FarmMountMode.Specific, 71, specificUnlocked: false));

    [Fact]
    public void SpecificWithNoMountPicked_FallsBackToRoulette()
        => Assert.False(FarmMountPolicy.UseSpecificMount(FarmMountMode.Specific, 0, specificUnlocked: true));

    [Fact]
    public void RouletteMode_NeverUsesSpecific()
        => Assert.False(FarmMountPolicy.UseSpecificMount(FarmMountMode.Roulette, 71, specificUnlocked: true));

    // ---- no-match fallthrough (documented contract with FarmRoamPolicy) ----

    [Fact]
    public void WalkResult_LeavesRoamPolicyInCharge()
    {
        // The travel layer returning Walk hands the tick to FarmRoamPolicy unchanged — the
        // acquisition widening must not alter the existing no-match spot-roam behavior.
        Assert.Equal(FarmTravelAction.Walk, Decide(distance: 20f, mountThreshold: 40f, destinationIsMob: false));
        Assert.Equal(
            FarmRoamAction.AdvanceSpot,
            FarmRoamPolicy.Decide(navBusy: false, distanceToSpotYalms: 2f, secondsIdleAtSpot: 20, respawnWaitSeconds: 12, spotCount: 3));
    }
}
