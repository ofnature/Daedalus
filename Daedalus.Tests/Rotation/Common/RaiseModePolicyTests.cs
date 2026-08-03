using Daedalus.Config;
using Daedalus.Rotation.Common.Modules;
using Xunit;

namespace Daedalus.Tests.Rotation.Common;

/// <summary>
/// The Raise Priority dropdown shipped wired to nothing — all three modes behaved identically.
/// The lever they now control is the hardcast decision, priced in time-to-raise: a Swiftcast
/// raise lands when Swiftcast is ready, a hardcast lands in 8 seconds.
/// </summary>
public sealed class RaiseModePolicyTests
{
    [Fact]
    public void RaiseFirst_WaitsOnlyWhenSwiftcastIsTheFasterPath()
    {
        Assert.Equal(RaiseModePolicy.HardcastSeconds,
            RaiseModePolicy.SwiftcastWaitThresholdSeconds(RaiseExecutionMode.RaiseFirst));
    }

    /// <summary>Balanced accepts up to 2s of extra body-time to stay free to heal — today's default, unchanged.</summary>
    [Fact]
    public void Balanced_KeepsTheTenSecondWindow()
    {
        Assert.Equal(10f, RaiseModePolicy.SwiftcastWaitThresholdSeconds(RaiseExecutionMode.Balanced));
    }

    /// <summary>HealFirst never commits to an 8s cast in combat — Swiftcast raises only.</summary>
    [Fact]
    public void HealFirst_NeverHardcastsInCombat()
    {
        Assert.Equal(float.PositiveInfinity,
            RaiseModePolicy.SwiftcastWaitThresholdSeconds(RaiseExecutionMode.HealFirst));
    }

    /// <summary>
    /// The ordering IS the feature: more raise priority means a smaller wait window. If these
    /// ever collapse together the dropdown is decorative again.
    /// </summary>
    [Fact]
    public void TheModesActuallyDiffer()
    {
        var raiseFirst = RaiseModePolicy.SwiftcastWaitThresholdSeconds(RaiseExecutionMode.RaiseFirst);
        var balanced = RaiseModePolicy.SwiftcastWaitThresholdSeconds(RaiseExecutionMode.Balanced);
        var healFirst = RaiseModePolicy.SwiftcastWaitThresholdSeconds(RaiseExecutionMode.HealFirst);

        Assert.True(raiseFirst < balanced);
        Assert.True(balanced < healFirst);
    }
}
