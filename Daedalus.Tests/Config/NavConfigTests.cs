using Daedalus.Config;

namespace Daedalus.Tests.Config;

public class NavConfigTests
{
    [Fact]
    public void PositionalBoundaryBiasDegrees_DefaultsToTen()
        => Assert.Equal(10f, new NavConfig().PositionalBoundaryBiasDegrees);

    [Theory]
    [InlineData(-5f, 0f)]
    [InlineData(0f, 0f)]
    [InlineData(17f, 17f)]
    [InlineData(45f, 30f)]
    public void PositionalBoundaryBiasDegrees_ClampsToRange(float set, float expected)
    {
        var config = new NavConfig { PositionalBoundaryBiasDegrees = set };
        Assert.Equal(expected, config.PositionalBoundaryBiasDegrees);
    }

    [Fact]
    public void YieldToBmrMovement_DefaultsOn()
        => Assert.True(new NavConfig().YieldToBmrMovement);
}
