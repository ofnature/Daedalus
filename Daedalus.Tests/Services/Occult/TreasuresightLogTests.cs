using System;
using Daedalus.Services.Occult;
using Xunit;

namespace Daedalus.Tests.Services.Occult;

/// <summary>
/// Occult Treasuresight answers in chat. The first line below is the game's own, copied from a field
/// screenshot (2026-10-11).
/// </summary>
public class TreasuresightLogTests
{
    private const string FieldLine =
        "You sense the presence of 4 silver coffers and 27 bronze coffers in the area!";

    private static TreasuresightLog Log() => new(chatGui: null, clientState: null);

    [Fact]
    public void ReadsTheGamesLine()
    {
        Assert.True(TreasuresightLog.TryParse(FieldLine, out var counts));
        Assert.Equal([new CofferCount(4, "silver"), new CofferCount(27, "bronze")], counts);
    }

    [Fact]
    public void ReadsASingleKindAndTheSingular()
    {
        Assert.True(TreasuresightLog.TryParse("You sense the presence of 1 bronze coffer in the area!", out var counts));
        Assert.Equal([new CofferCount(1, "bronze")], counts);
    }

    /// <summary>A kind this has never seen is shown as the game names it, not dropped.</summary>
    [Fact]
    public void KeepsAKindItHasNeverSeen()
    {
        Assert.True(TreasuresightLog.TryParse(
            "You sense the presence of 2 gold coffers, 4 silver coffers and 27 bronze coffers in the area!", out var counts));
        Assert.Equal(3, counts.Count);
        Assert.Equal(new CofferCount(2, "gold"), counts[0]);
    }

    [Theory]
    [InlineData("You obtain 3 bronze coffers.")]                       // a number and "coffers", but not the answer
    [InlineData("You sense something far to the north.")]              // the pot hunt's line
    [InlineData("You sense the presence of treasure in the area!")]    // no count to show
    [InlineData("")]
    [InlineData(null)]
    public void IgnoresOtherChat(string? text)
        => Assert.False(TreasuresightLog.TryParse(text, out _));

    [Fact]
    public void RemembersTheLastAnswerForItsZone()
    {
        var log = Log();
        var now = new DateTime(2026, 10, 11, 12, 0, 0, DateTimeKind.Utc);

        Assert.True(log.Observe(FieldLine, territoryId: 1252, now));

        Assert.Equal("4 silver · 27 bronze", log.Describe(1252));
        Assert.Equal(now, log.TakenUtc);
    }

    /// <summary>A count taken in one Horn says nothing about the other.</summary>
    [Fact]
    public void ShowsNothingInAnotherZone()
    {
        var log = Log();
        log.Observe(FieldLine, 1252, DateTime.UtcNow);
        Assert.Equal(string.Empty, log.Describe(1278));
    }

    [Fact]
    public void OtherChatDoesNotDisturbTheAnswer()
    {
        var log = Log();
        log.Observe(FieldLine, 1252, DateTime.UtcNow);
        Assert.False(log.Observe("You obtain 3 bronze coffers.", 1252, DateTime.UtcNow));
        Assert.Equal("4 silver · 27 bronze", log.Describe(1252));
    }

    [Fact]
    public void ShowsNothingBeforeAnAnswer() => Assert.Equal(string.Empty, Log().Describe(1252));

    [Theory]
    [InlineData(5, "just now")]
    [InlineData(59, "just now")]
    [InlineData(180, "3m ago")]
    [InlineData(3900, "1h 5m ago")]
    public void SaysHowOldItIs(int seconds, string expected)
        => Assert.Equal(expected, TreasuresightLog.DescribeAge(TimeSpan.FromSeconds(seconds)));
}
