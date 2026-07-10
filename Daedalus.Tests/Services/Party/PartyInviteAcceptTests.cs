using Daedalus.Services.Party;
using Xunit;

namespace Daedalus.Tests.Services.Party;

/// <summary>
/// Invite auto-accept parsing: the SelectYesno prompt match ("Join {name}'s party?") and the
/// roster whitelist check. The addon interaction itself is native-only and validated live.
/// </summary>
public class PartyInviteAcceptTests
{
    [Theory]
    [InlineData("Join Korha Ishere's party?", "Korha Ishere")]
    [InlineData("Join Xia Discord's party?", "Xia Discord")]
    [InlineData("  Join Saar Ishere's party?  ", "Saar Ishere")]
    public void MatchInviter_ParsesPartyInvitePrompt(string prompt, string expected)
        => Assert.Equal(expected, PartyInviteAcceptService.MatchInviter(prompt));

    [Theory]
    [InlineData("Retire to an inn room?")]
    [InlineData("Leave the party?")]
    [InlineData("")]
    [InlineData(null)]
    public void MatchInviter_IgnoresOtherPrompts(string? prompt)
        => Assert.Null(PartyInviteAcceptService.MatchInviter(prompt));

    [Fact]
    public void MatchInviter_NameWithInternalApostrophe()
        => Assert.Equal("Miso'rry Forthis", PartyInviteAcceptService.MatchInviter("Join Miso'rry Forthis's party?"));

    [Fact]
    public void IsWhitelisted_ExactNameCaseInsensitive()
    {
        var roster = new[] { "Korha Ishere", "Xia Discord" };
        Assert.True(PartyInviteAcceptService.IsWhitelisted("korha ishere", roster));
        Assert.True(PartyInviteAcceptService.IsWhitelisted("Xia Discord", roster));
        Assert.False(PartyInviteAcceptService.IsWhitelisted("Korha", roster));       // partial never matches
        Assert.False(PartyInviteAcceptService.IsWhitelisted("Random Stranger", roster));
        Assert.False(PartyInviteAcceptService.IsWhitelisted("Korha Ishere", System.Array.Empty<string>()));
    }
}
