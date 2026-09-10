// The parser under test is DEBUG-only by requirement (Part 1 must not exist in Release), so its
// tests are gated the same way. A Release test run simply has fewer tests; a Release PLUGIN build
// has no scan-collection code at all, which is the point.
#if DEBUG
using Daedalus.Services.Beastmaster;
using Xunit;

namespace Daedalus.Tests.Rotation.ArtemisCore;

/// <summary>
/// The Gauge scan parser.
///
/// <para>
/// These tests deliberately do NOT assert that any particular phrase means any particular tier —
/// the real wording is unconfirmed and every phrase in the table is a guess. What they pin is the
/// behaviour that has to hold no matter what the wording turns out to be: unmatched text stays
/// Unknown rather than defaulting to a tier, the longest matching phrase wins, and the caller's
/// resolved target name beats anything scraped out of prose.
/// </para>
/// </summary>
public sealed class GaugeScanParserTests
{
    /// <summary>
    /// The single most important behaviour here. An unrecognised scan must not resolve to a tier,
    /// because the shipped auto-capture rule refuses to act on Unknown — that refusal is what
    /// stops a wrong guess about wording from making the plugin press Capture on the wrong beast.
    /// </summary>
    [Theory]
    [InlineData("Some wording nobody has seen before.")]
    [InlineData("")]
    [InlineData(null)]
    public void UnrecognisedTextStaysUnknownAndAssertsNothing(string? text)
    {
        var result = GaugeScanParser.Parse(text, "Beast");

        Assert.Equal(BeastCaptureDifficulty.Unknown, result.Difficulty);
        Assert.Null(result.Capturable);
    }

    /// <summary>
    /// A specific phrase must beat a substring of itself, or "very difficult" gets swallowed by
    /// "difficult" and every hard beast is mis-tiered one step down.
    /// </summary>
    [Fact]
    public void TheLongestMatchingPhraseWins()
    {
        var vague = GaugeScanParser.Parse("this is a difficult target to catch", "B");
        var specific = GaugeScanParser.Parse("this is a very difficult target to catch", "B");

        Assert.Equal(BeastCaptureDifficulty.Hard, vague.Difficulty);
        Assert.Equal(BeastCaptureDifficulty.Extreme, specific.Difficulty);
    }

    /// <summary>Explicitly-impossible text must report not-capturable, not merely hard.</summary>
    [Fact]
    public void ImpossibleTextIsNotCapturable()
    {
        var result = GaugeScanParser.Parse("this beast cannot be captured", "B");

        Assert.Equal(BeastCaptureDifficulty.Impossible, result.Difficulty);
        Assert.False(result.Capturable);
    }

    /// <summary>Matching is case-insensitive — the message may be sentence-cased.</summary>
    [Fact]
    public void MatchingIgnoresCase()
        => Assert.Equal(
            BeastCaptureDifficulty.Easy,
            GaugeScanParser.Parse("An Easy Target To Catch", "B").Difficulty);

    /// <summary>Levels appear in several shapes; losing the level to a formatting change is avoidable.</summary>
    [Theory]
    [InlineData("The beast is level 42.", 42)]
    [InlineData("Lv. 7 critter", 7)]
    [InlineData("Lv90 monster", 90)]
    [InlineData("no level here", 0)]
    public void LevelIsReadInEveryCommonShape(string text, int expected)
        => Assert.Equal(expected, GaugeScanParser.Parse(text, "B").Level);

    /// <summary>
    /// The name comes from the actual target, never from the prose — a scraped name would be a
    /// second guess layered on the first, and it keys the whole table.
    /// </summary>
    [Fact]
    public void TheResolvedTargetNameIsUsed()
    {
        var result = GaugeScanParser.Parse("an easy target to catch", "Wild Coeurl");

        Assert.Equal("Wild Coeurl", result.Name);
    }

    /// <summary>With no resolved name there is nothing to key a row on, and it says so.</summary>
    [Fact]
    public void NoResolvedNameMeansNoName()
        => Assert.Null(GaugeScanParser.Parse("an easy target to catch", knownName: null).Name);

    /// <summary>A result with nothing in it is distinguishable from one that learned something.</summary>
    [Fact]
    public void HasAnythingDistinguishesAnEmptyParse()
    {
        Assert.False(GaugeScanParser.Parse("nothing useful", knownName: null).HasAnything);
        Assert.True(GaugeScanParser.Parse("an easy target to catch", knownName: null).HasAnything);
    }
}
#endif
