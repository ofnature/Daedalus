using Daedalus.Services.Rescue;
using Xunit;

namespace Daedalus.Tests.Services.Rescue;

/// <summary>
/// Deterministic healer election (docs/rescue-plan.md Phase 0). Every machine must derive the
/// same ranks from the same roster with no negotiation, and a lower rank's failure is covered
/// by the next rank one backoff step later.
/// </summary>
public sealed class RescueElectionTests
{
    private static readonly string[] Healers = ["Beta@World", "Alpha@World"];

    [Fact]
    public void Rank_IsOrdinalSorted_IdenticalOnEveryMachine()
    {
        // Input order must not matter — only the sorted position does.
        Assert.Equal(0, RescueElection.Rank(Healers, "Alpha@World"));
        Assert.Equal(1, RescueElection.Rank(Healers, "Beta@World"));
        Assert.Equal(1, RescueElection.Rank(["Alpha@World", "Beta@World"], "Beta@World"));
    }

    [Fact]
    public void NonHealer_HasNoRank_AndNeverFires()
    {
        Assert.Equal(-1, RescueElection.Rank(Healers, "Tanky@World"));
        Assert.False(RescueElection.MayFire(-1, requestAgeSeconds: 10f, claimSeen: false));
    }

    [Fact]
    public void RankZero_FiresImmediately_RankOneWaitsOneStep()
    {
        Assert.True(RescueElection.MayFire(0, requestAgeSeconds: 0f, claimSeen: false));

        Assert.False(RescueElection.MayFire(1, RescueElection.BackoffStepSeconds - 0.05f, claimSeen: false));
        Assert.True(RescueElection.MayFire(1, RescueElection.BackoffStepSeconds, claimSeen: false));
    }

    [Fact]
    public void ClaimSeen_SuppressesEveryRank()
    {
        Assert.False(RescueElection.MayFire(0, requestAgeSeconds: 5f, claimSeen: true));
        Assert.False(RescueElection.MayFire(1, requestAgeSeconds: 5f, claimSeen: true));
    }

    [Fact]
    public void DuplicateSenderIds_FoldBeforeRanking()
    {
        // A double-registered healer (zone-in heartbeat blip) must not shift everyone's rank.
        Assert.Equal(1, RescueElection.Rank(["Alpha@World", "Alpha@World", "Beta@World"], "Beta@World"));
    }

    [Fact]
    public void BackoffStep_CoversTheSignalToPullBudget()
    {
        // Rank 0's pull takes ~150–400ms end-to-end; rank 1 must not jump in before a healthy
        // rank 0's claim can arrive.
        Assert.True(RescueElection.BackoffStepSeconds >= 0.3f);
        Assert.Equal(0f, RescueElection.BackoffSeconds(0));
        Assert.Equal(RescueElection.BackoffStepSeconds * 2, RescueElection.BackoffSeconds(2), 3);
    }
}
