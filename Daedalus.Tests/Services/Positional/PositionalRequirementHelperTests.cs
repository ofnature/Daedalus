using Daedalus.Services.Positional;
using Xunit;

namespace Daedalus.Tests.Services.Positional;

public class PositionalRequirementHelperTests
{
    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(5, false)]
    public void ShouldApply_OnlyWhenSingleTarget(int enemyCount, bool expected)
    {
        Assert.Equal(expected, PositionalRequirementHelper.ShouldApply(enemyCount));
    }
}
