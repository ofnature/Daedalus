using Moq;
using Daedalus.Data;
using Daedalus.Services.Action;
using Daedalus.Tests.Mocks;
using Xunit;

namespace Daedalus.Tests.Services.Action;

public sealed class ActionAvailabilityTests
{
    [Fact]
    public void GetComboFinisher_WhenRoyalAuthorityNotLearned_ReturnsRageOfHalone()
    {
        var actionService = MockBuilders.CreateMockActionService();
        actionService.Setup(x => x.IsActionLearned(PLDActions.RoyalAuthority.ActionId)).Returns(false);
        actionService.Setup(x => x.IsActionLearned(PLDActions.RageOfHalone.ActionId)).Returns(true);

        var finisher = PLDActions.GetComboFinisher(80, actionService.Object);

        Assert.Equal(PLDActions.RageOfHalone.ActionId, finisher.ActionId);
    }

    [Fact]
    public void GetDamageGcdForLevel_WhenStoneIIINotLearned_ReturnsStoneII()
    {
        var actionService = MockBuilders.CreateMockActionService();
        actionService.Setup(x => x.IsActionLearned(WHMActions.StoneIII.ActionId)).Returns(false);
        actionService.Setup(x => x.IsActionLearned(WHMActions.StoneII.ActionId)).Returns(true);

        var spell = WHMActions.GetDamageGcdForLevel(45, actionService.Object);

        Assert.Equal(WHMActions.StoneII.ActionId, spell.ActionId);
    }
}
