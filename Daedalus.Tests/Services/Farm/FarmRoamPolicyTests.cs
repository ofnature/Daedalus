using Daedalus.Services.Farm;
using Xunit;

namespace Daedalus.Tests.Services.Farm;

public class FarmRoamPolicyTests
{
    [Fact]
    public void Decide_FarFromSpot_NavIdle_MovesToSpot()
    {
        Assert.Equal(FarmRoamAction.MoveToSpot,
            FarmRoamPolicy.Decide(navBusy: false, distanceToSpotYalms: 30f, secondsIdleAtSpot: 0, respawnWaitSeconds: 12, spotCount: 2));
    }

    [Fact]
    public void Decide_FarFromSpot_NavBusy_Stays()
    {
        // Re-issuing PathfindAndMoveTo while a path runs causes the vNav stutter-step.
        Assert.Equal(FarmRoamAction.Stay,
            FarmRoamPolicy.Decide(navBusy: true, distanceToSpotYalms: 30f, secondsIdleAtSpot: 0, respawnWaitSeconds: 12, spotCount: 2));
    }

    [Fact]
    public void Decide_AtSpot_BeforeRespawnWait_Stays()
    {
        Assert.Equal(FarmRoamAction.Stay,
            FarmRoamPolicy.Decide(navBusy: false, distanceToSpotYalms: 2f, secondsIdleAtSpot: 5, respawnWaitSeconds: 12, spotCount: 3));
    }

    [Fact]
    public void Decide_AtSpot_AfterRespawnWait_AdvancesSpot()
    {
        Assert.Equal(FarmRoamAction.AdvanceSpot,
            FarmRoamPolicy.Decide(navBusy: false, distanceToSpotYalms: 2f, secondsIdleAtSpot: 13, respawnWaitSeconds: 12, spotCount: 3));
    }

    [Fact]
    public void Decide_SingleSpot_NeverAdvances()
    {
        Assert.Equal(FarmRoamAction.Stay,
            FarmRoamPolicy.Decide(navBusy: false, distanceToSpotYalms: 2f, secondsIdleAtSpot: 600, respawnWaitSeconds: 12, spotCount: 1));
    }

    [Fact]
    public void NextSpotIndex_WrapsAround()
    {
        Assert.Equal(1, FarmRoamPolicy.NextSpotIndex(0, 3));
        Assert.Equal(2, FarmRoamPolicy.NextSpotIndex(1, 3));
        Assert.Equal(0, FarmRoamPolicy.NextSpotIndex(2, 3));
        Assert.Equal(0, FarmRoamPolicy.NextSpotIndex(5, 0));
    }

    [Fact]
    public void IsComplete_ThresholdBehavior()
    {
        Assert.False(FarmRoamPolicy.IsComplete(9, 10));
        Assert.True(FarmRoamPolicy.IsComplete(10, 10));
        Assert.True(FarmRoamPolicy.IsComplete(11, 10));
        Assert.False(FarmRoamPolicy.IsComplete(5, 0)); // unset target never completes
    }

    [Fact]
    public void DecideApproach_EngagedMob_HoldsAtAnyDistance()
    {
        // Tagged mob is running to us — stand and shoot it on the way in.
        Assert.Equal(FarmApproachAction.Hold, FarmRoamPolicy.DecideApproach(true, 30f, 0));
        Assert.Equal(FarmApproachAction.Hold, FarmRoamPolicy.DecideApproach(true, 10f, 0));
        Assert.Equal(FarmApproachAction.Hold, FarmRoamPolicy.DecideApproach(true, 1f, 99));
    }

    [Fact]
    public void DecideApproach_FarTarget_MovesToTagRange()
    {
        Assert.Equal(FarmApproachAction.MoveToTagRange, FarmRoamPolicy.DecideApproach(false, 30f, 0));
    }

    [Fact]
    public void DecideApproach_AtTagRange_HoldsThroughTagWindow()
    {
        // Standing at tag range: give the rotation time to land the ranged tag.
        Assert.Equal(FarmApproachAction.Hold, FarmRoamPolicy.DecideApproach(false, 15f, 1.0));
        Assert.Equal(FarmApproachAction.Hold, FarmRoamPolicy.DecideApproach(false, 15f, 3.9));
    }

    [Fact]
    public void DecideApproach_TagWindowExpired_WalksToMelee()
    {
        // Kits with no ranged attack (MNK) never tag — walk in instead.
        Assert.Equal(FarmApproachAction.MoveToMelee, FarmRoamPolicy.DecideApproach(false, 15f, 4.1));
    }

    [Fact]
    public void DecideApproach_AlreadyInMeleeReach_Holds()
    {
        Assert.Equal(FarmApproachAction.Hold, FarmRoamPolicy.DecideApproach(false, 2f, 99));
    }

    [Fact]
    public void FarmProfile_Validity_And_MobList()
    {
        var profile = new FarmProfile();
        Assert.False(profile.IsValid);

        profile.ItemId = 5106; // Copper Ore
        profile.ItemName = "Copper Ore";
        profile.TargetCount = 20;
        profile.AddMob(123, "test slug");
        profile.AddMob(123, "duplicate ignored");
        profile.Spots.Add(new System.Numerics.Vector3(1, 2, 3));
        profile.TerritoryId = 148;

        Assert.True(profile.IsValid);
        Assert.Single(profile.Mobs);
        Assert.Contains(123u, profile.MobNameIds);

        profile.RemoveMobAt(0);
        Assert.Empty(profile.Mobs);
        Assert.False(profile.IsValid);
    }
}
