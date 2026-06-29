using Daedalus.Rotation.ThanatosCore.Helpers;
using Daedalus.Services.Content;
using Xunit;

namespace Daedalus.Tests.Rotation.ThanatosCore.Helpers;

/// <summary>
/// Tests for the content-aware Enshroud low-HP gate: skip the burst on a dying target in dungeons /
/// open world, but never gate in trials/raids/high-end.
/// </summary>
public sealed class ThanatosEnshroudPolicyTests
{
    [Fact]
    public void SkipsInDungeon_WhenHealthiestTargetBelowThreshold()
    {
        Assert.True(ThanatosEnshroudPolicy.ShouldSkipForLowHp(EffectiveDutyProfile.Dungeon, 0.03f, 5f));
    }

    [Fact]
    public void DoesNotSkipInDungeon_WhenAHealthyTargetExists()
    {
        // Healthiest enemy at 80% — a healthy pack must not trip the gate even if some mobs are dying.
        Assert.False(ThanatosEnshroudPolicy.ShouldSkipForLowHp(EffectiveDutyProfile.Dungeon, 0.80f, 5f));
    }

    [Fact]
    public void AppliesInOpenWorld()
    {
        Assert.True(ThanatosEnshroudPolicy.ShouldSkipForLowHp(EffectiveDutyProfile.None, 0.02f, 5f));
    }

    [Theory]
    [InlineData(EffectiveDutyProfile.Trial)]
    [InlineData(EffectiveDutyProfile.Raid)]
    [InlineData(EffectiveDutyProfile.HighEndRaid)]
    public void NeverGatesInBossContent(EffectiveDutyProfile profile)
    {
        // Even at 1% HP, big-boss content always bursts.
        Assert.False(ThanatosEnshroudPolicy.ShouldSkipForLowHp(profile, 0.01f, 5f));
    }

    [Fact]
    public void ThresholdZeroDisablesGate()
    {
        Assert.False(ThanatosEnshroudPolicy.ShouldSkipForLowHp(EffectiveDutyProfile.Dungeon, 0.01f, 0f));
    }
}
