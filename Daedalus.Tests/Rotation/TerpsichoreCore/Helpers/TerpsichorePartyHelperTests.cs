using Daedalus.Data;
using Daedalus.Rotation.TerpsichoreCore.Helpers;
using Xunit;

namespace Daedalus.Tests.Rotation.TerpsichoreCore.Helpers;

/// <summary>
/// Pure-policy tests for the dance-partner priority table and the strict-upgrade decision.
/// (Live party/status resolution in <see cref="TerpsichorePartyHelper.ShouldUpdatePartner"/> needs
/// the game's object table / status lists, so it is in-game-validation-only; the decision core is here.)
/// </summary>
public class TerpsichorePartyHelperTests
{
    // --- Priority table (refreshed for Dawntrail 7.x) ---

    [Fact]
    public void Pictomancer_OutranksAllMelee()
    {
        var pct = TerpsichorePartyHelper.GetJobPriority(JobRegistry.Pictomancer);
        Assert.True(pct < TerpsichorePartyHelper.GetJobPriority(JobRegistry.Samurai));
        Assert.True(pct < TerpsichorePartyHelper.GetJobPriority(JobRegistry.Viper));
        Assert.True(pct < TerpsichorePartyHelper.GetJobPriority(JobRegistry.Dragoon));
        Assert.True(pct < TerpsichorePartyHelper.GetJobPriority(JobRegistry.Monk));
        Assert.True(pct < TerpsichorePartyHelper.GetJobPriority(JobRegistry.Reaper));
        Assert.True(pct < TerpsichorePartyHelper.GetJobPriority(JobRegistry.Ninja));
    }

    [Fact]
    public void Melee_OutranksCastersRangedAndSupport()
    {
        var dragoon = TerpsichorePartyHelper.GetJobPriority(JobRegistry.Dragoon);
        Assert.True(dragoon < TerpsichorePartyHelper.GetJobPriority(JobRegistry.BlackMage));
        Assert.True(dragoon < TerpsichorePartyHelper.GetJobPriority(JobRegistry.Machinist));
        Assert.True(dragoon < TerpsichorePartyHelper.GetJobPriority(JobRegistry.Bard));
    }

    [Fact]
    public void Dps_OutranksTanks_AndTanks_OutrankHealers()
    {
        var bard = TerpsichorePartyHelper.GetJobPriority(JobRegistry.Bard);
        var paladin = TerpsichorePartyHelper.GetJobPriority(JobRegistry.Paladin);
        var whiteMage = TerpsichorePartyHelper.GetJobPriority(JobRegistry.WhiteMage);

        Assert.True(bard < paladin);     // any DPS over a tank
        Assert.True(paladin < whiteMage); // tank over a healer
    }

    [Fact]
    public void UnknownJob_IsLowestPriority_ButSelectable()
    {
        // Lowest rank of all — but NOT int.MaxValue: that could never win the `< bestPriority`
        // scan, leaving the dancer partnerless instead of taking a last-resort partner.
        var unknown = TerpsichorePartyHelper.GetJobPriority(9999);
        Assert.True(unknown > TerpsichorePartyHelper.GetJobPriority(JobRegistry.Sage));
        Assert.True(unknown < int.MaxValue);
    }

    // --- Strict-upgrade decision ---

    [Fact]
    public void ShouldUpgrade_WhenCandidateStrictlyBetter()
    {
        // Pictomancer (idx 0) available while partnered to Dragoon (idx 4).
        var current = TerpsichorePartyHelper.GetJobPriority(JobRegistry.Dragoon);
        var candidate = TerpsichorePartyHelper.GetJobPriority(JobRegistry.Pictomancer);
        Assert.True(TerpsichorePartyHelper.ShouldUpgradePartner(current, candidate));
    }

    [Fact]
    public void ShouldNotUpgrade_WhenCandidateEqualPriority()
    {
        // Two Samurai — no thrash.
        var sam = TerpsichorePartyHelper.GetJobPriority(JobRegistry.Samurai);
        Assert.False(TerpsichorePartyHelper.ShouldUpgradePartner(sam, sam));
    }

    [Fact]
    public void ShouldNotUpgrade_WhenCandidateWorse()
    {
        var current = TerpsichorePartyHelper.GetJobPriority(JobRegistry.Pictomancer);
        var candidate = TerpsichorePartyHelper.GetJobPriority(JobRegistry.WhiteMage);
        Assert.False(TerpsichorePartyHelper.ShouldUpgradePartner(current, candidate));
    }

    [Fact]
    public void ShouldUpgrade_FromUnknownToKnownJob()
    {
        // Currently partnered to an unrecognized job (MaxValue) — any known job is an upgrade.
        var current = TerpsichorePartyHelper.GetJobPriority(9999);
        var candidate = TerpsichorePartyHelper.GetJobPriority(JobRegistry.Bard);
        Assert.True(TerpsichorePartyHelper.ShouldUpgradePartner(current, candidate));
    }
}
