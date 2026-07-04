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

    [Theory]
    [InlineData(19u, 2.5f)]  // PLD (tank)
    [InlineData(34u, 2.5f)]  // SAM (melee)
    [InlineData(23u, 18f)]   // BRD (ranged)
    [InlineData(28u, 18f)]   // SCH (healer)
    [InlineData(25u, 18f)]   // BLM (caster)
    public void ApproachStopYalms_ByRole(uint jobId, float expected)
    {
        Assert.Equal(expected, FarmRoamPolicy.ApproachStopYalms(jobId));
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
