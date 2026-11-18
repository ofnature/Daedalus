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

    private const ulong Drake = 0x4000_1234, Barbmole = 0x4000_1235;

    /// <summary>Drake Piece, 2026-10-31: dropped at Blaze Spikes, targeted again once the reflect wears off.</summary>
    [Fact]
    public void ShouldRestore_ADroppedTargetComesBackOnceNoLongerForbidden()
    {
        Assert.False(FightTargetHints.ShouldRestore(0, Drake, [Drake]));
        Assert.True(FightTargetHints.ShouldRestore(0, Drake, []));
        Assert.True(FightTargetHints.ShouldRestore(0, Drake, null));
    }

    [Fact]
    public void ShouldRestore_SomethingTargetedSince_IsLeftAlone()
        => Assert.False(FightTargetHints.ShouldRestore(Barbmole, Drake, []));

    [Fact]
    public void ShouldRestore_NothingDropped_IsNothingToRestore()
        => Assert.False(FightTargetHints.ShouldRestore(0, 0, []));

    /// <summary>
    /// The Bone Bishop dies before the Bone Knight (user, 2026-11-15). The engine names the Knight first whenever the
    /// beastmaster is behind his Forward Guard, and the target went back and forth seven times in two minutes.
    /// </summary>
    [Fact]
    public void KillFirst_PutsTheBishopAheadOfTheEnginesKnight()
    {
        const ulong Bishop = 0x40000010, Knight = 0x40000011;
        Assert.Equal(new[] { Bishop, Knight }, FightTargetHints.KillFirst([Knight], Bishop));
        Assert.Equal(new[] { Bishop, Knight }, FightTargetHints.KillFirst([Knight, Bishop], Bishop));
        Assert.Equal(new[] { Bishop }, FightTargetHints.KillFirst([], Bishop));
    }

    [Fact]
    public void KillFirst_LeavesTheListAloneWhenThereIsNoBishop()
    {
        ulong[] engine = [0x40000011, 0x40000012];
        Assert.Same(engine, FightTargetHints.KillFirst(engine, 0));
        Assert.Same(engine, FightTargetHints.KillFirst(engine, engine[0]));
    }

    [Fact]
    public void TheBishopsNameIdIsTheFirstBoardsBoneBishop() => Assert.Equal(14532u, FightTargetHints.BoneBishopNameId);
}
