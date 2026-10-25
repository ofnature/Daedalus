using Daedalus.Data;
using Xunit;

namespace Daedalus.Tests.Data;

/// <summary>Going back in: Lauda's menu and the warning before a board, with the recorded wording.</summary>
public class CrucibleLoopRulesTests
{
    [Fact]
    public void LaudasListPicksTheCrucible()
        => Assert.Equal(1, CrucibleLoopRules.MenuPick(["Sin of Blood", "Crucible of the Unbroken", "Nothing"]));

    [Fact]
    public void ALaudaListWithoutTheCrucibleIsNotOurs()
        => Assert.Equal(-1, CrucibleLoopRules.MenuPick(["Faded Remnants of Resilience", "Achievement Rewards", "Nothing"]));

    [Theory]
    [InlineData("※Your current team is smaller than the maximum allowance. / Proceed to challenge this board?", true)]
    [InlineData("※Your current team is smaller than the maximum allowance. / ※One or more of your familiars has a lower beast rank than is recommended. / Proceed to challenge this board?", true)]
    [InlineData("Discard changes and return to board selection?", false)]
    [InlineData("Proceed to challenge this board?", false)] // no warning mark: not a prompt the loop has seen
    public void OnlyTheBoardWarningIsConfirmed(string prompt, bool confirm)
        => Assert.Equal(confirm, CrucibleLoopRules.IsEntryWarning(prompt));
}
