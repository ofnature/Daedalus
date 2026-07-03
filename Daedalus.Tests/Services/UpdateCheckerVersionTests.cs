using Daedalus.Services;
using Xunit;

namespace Daedalus.Tests.Services;

/// <summary>
/// repo.json carries 4-part versions ("0.1.1.0"), the plugin carries 3-part ("0.1.1").
/// Without normalization System.Version calls the 4-part form newer at the SAME version
/// and the update checker nags forever.
/// </summary>
public sealed class UpdateCheckerVersionTests
{
    [Theory]
    [InlineData("0.1.1.0", "0.1.1", false)] // same version, different component counts
    [InlineData("0.1.1", "0.1.1", false)]
    [InlineData("0.1.2.0", "0.1.1", true)]
    [InlineData("0.2.0.0", "0.1.9", true)]
    [InlineData("1.0.0.0", "0.9.9", true)]
    [InlineData("0.1.0.0", "0.1.1", false)] // remote older than local dev build
    [InlineData("garbage", "0.1.1", false)]
    public void IsNewer_NormalizesComponentCounts(string latest, string current, bool expected)
    {
        Assert.Equal(expected, UpdateCheckerService.IsNewer(latest, current));
    }
}
