using Daedalus.Services.Targeting;
using Xunit;

namespace Daedalus.Tests.Services.Targeting;

public sealed class FightTargetHintsTests
{
    [Fact]
    public void IsForbidden_MatchesOnlyListedIds()
    {
        ulong[] forbidden = [0x4000_1000, 0x4000_1001];
        Assert.True(FightTargetHints.IsForbidden(0x4000_1001, forbidden));
        Assert.False(FightTargetHints.IsForbidden(0x4000_1002, forbidden));
    }

    [Fact]
    public void IsForbidden_NoOpinionOrNoTarget_IsFalse()
    {
        Assert.False(FightTargetHints.IsForbidden(0x4000_1000, null));
        Assert.False(FightTargetHints.IsForbidden(0x4000_1000, []));
        Assert.False(FightTargetHints.IsForbidden(0, [0]));
    }

    [Fact]
    public void FirstPreferred_TakesTheEngineOrder_SkippingWhatCannotBeResolved()
    {
        ulong[] priority = [1, 2, 3];
        var picked = FightTargetHints.FirstPreferred(priority, id => id == 1 ? null : $"enemy{id}");
        Assert.Equal("enemy2", picked);
    }

    [Fact]
    public void FirstPreferred_NoOpinion_IsNull()
    {
        Assert.Null(FightTargetHints.FirstPreferred<string>(null, id => $"enemy{id}"));
        Assert.Null(FightTargetHints.FirstPreferred<string>([], id => $"enemy{id}"));
    }
}
