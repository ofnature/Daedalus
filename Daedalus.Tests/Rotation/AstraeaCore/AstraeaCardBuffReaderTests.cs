using System.Linq;
using Daedalus.Data;
using Daedalus.Rotation.AstraeaCore.Helpers;
using Xunit;

namespace Daedalus.Tests.Rotation.AstraeaCore;

/// <summary>The Debug window's "Card buffs" column: every card, who has it from us, how long is left.</summary>
public class AstraeaCardBuffReaderTests
{
    private const uint Us = 0x1001;
    private const uint OtherAst = 0x2002;

    [Fact]
    public void EveryCardHasARow_InOrder()
    {
        var rows = AstraeaCardBuffReader.Build(Us, []);
        Assert.Equal(["The Balance", "The Spear", "The Arrow", "The Bole", "The Ewer", "The Spire"], rows.Select(r => r.Card));
        Assert.All(rows, r => Assert.Empty(r.Holders));
    }

    [Fact]
    public void ShowsWhoHasItAndForHowLong()
    {
        var rows = AstraeaCardBuffReader.Build(Us,
        [
            ("Thancred's Avatar", ASTActions.TheBoleStatusId, Us, 12.4f),
            ("Saar Ishere", ASTActions.TheBalanceStatusId, Us, 8f),
        ]);

        var bole = Assert.Single(rows.Single(r => r.Card == "The Bole").Holders);
        Assert.Equal("Thancred's Avatar", bole.Target);
        Assert.Equal(12.4f, bole.SecondsLeft);
        Assert.Equal("Saar Ishere", Assert.Single(rows.Single(r => r.Card == "The Balance").Holders).Target);
    }

    /// <summary>Another Astrologian's card is not ours.</summary>
    [Fact]
    public void OnlyOurBuffsCount()
        => Assert.Empty(AstraeaCardBuffReader.Build(Us, [("Tank", ASTActions.TheSpearStatusId, OtherAst, 10f)])
            .Single(r => r.Card == "The Spear").Holders);

    [Fact]
    public void TwoHoldersOfOneCardAreBothListed()
        => Assert.Equal(2, AstraeaCardBuffReader.Build(Us,
        [
            ("A", ASTActions.TheEwerStatusId, Us, 5f),
            ("B", ASTActions.TheEwerStatusId, Us, 14f),
        ]).Single(r => r.Card == "The Ewer").Holders.Count);

    [Fact]
    public void OtherStatusesAreIgnored()
        => Assert.All(AstraeaCardBuffReader.Build(Us, [("A", 1878u, Us, 10f)]), r => Assert.Empty(r.Holders));
}
