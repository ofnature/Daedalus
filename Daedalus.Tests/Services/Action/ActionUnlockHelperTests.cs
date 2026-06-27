using Daedalus.Data;
using Daedalus.Services.Action;

namespace Daedalus.Tests.Services.Action;

public sealed class ActionUnlockHelperTests
{
    [Fact]
    public void IsUnlockLinkSatisfied_WhenZero_ReturnsTrue()
    {
        Assert.True(ActionUnlockHelper.IsUnlockLinkSatisfied(0));
    }

    [Fact]
    public void IsActionQuestUnlocked_WhenDataManagerNull_ReturnsTrue()
    {
        Assert.True(ActionUnlockHelper.IsActionQuestUnlocked(null, MNKActions.FourPointFury.ActionId));
    }
}
