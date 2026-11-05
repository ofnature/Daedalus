using Daedalus.Data;
using Xunit;

namespace Daedalus.Tests.Data;

/// <summary>Going back in: Lauda's menu and the warning before a board, with the recorded wording.</summary>
public class CrucibleLoopRulesTests
{
    // --- Loop until every beast is rank 25 (user 2026-11-05) ---

    [Fact]
    public void LevelingIsDoneWhenEveryCapturedBeastIsRank25()
        => Assert.Equal("Loop finished: every beast is rank 25.",
            CrucibleLoopRules.LevelingFinished([3, 7, 12], _ => 25));

    [Fact]
    public void OneBeastBelow25KeepsTheLoopGoing()
    {
        int? Rank(int no) => no == 7 ? 24 : 25;
        Assert.Null(CrucibleLoopRules.LevelingFinished([3, 7, 12], Rank));
        Assert.Equal([7], CrucibleLoopRules.BelowMaxRank([3, 7, 12], Rank));
    }

    [Fact]
    public void ARankNeverReadCountsAsNotDone()
        => Assert.Equal([12], CrucibleLoopRules.BelowMaxRank([3, 12], no => no == 3 ? 25 : null));

    [Fact]
    public void NoCapturedBeastsIsNotDone()
        => Assert.Null(CrucibleLoopRules.LevelingFinished([], _ => 25));

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

    // --- The degree on the board layout (Korha 2026-11-04: callback 5 steps 0 -> 1 -> 2 -> 3) ---

    [Fact]
    public void AsSetInGameNeverTouchesTheDegree()
    {
        Assert.Equal(CrucibleLoopRules.DegreeAction.Ready, CrucibleLoopRules.NextDegreeAction(3, CrucibleLoopRules.DegreeAsSet, 0));
        Assert.Equal(CrucibleLoopRules.DegreeAction.Ready, CrucibleLoopRules.NextDegreeAction(0, CrucibleLoopRules.DegreeAsSet, 0));
    }

    [Fact]
    public void TheRightDegreeIsReady()
        => Assert.Equal(CrucibleLoopRules.DegreeAction.Ready, CrucibleLoopRules.NextDegreeAction(3, 3, 0));

    [Fact]
    public void StandardStepsUpToThirdDegree()
    {
        for (var current = 0; current < 3; current++)
            Assert.Equal(CrucibleLoopRules.DegreeAction.StepUp, CrucibleLoopRules.NextDegreeAction(current, 3, current));
        Assert.Equal(CrucibleLoopRules.DegreeAction.Ready, CrucibleLoopRules.NextDegreeAction(3, 3, 3));
    }

    /// <summary>Korha 2026-11-04 16:16: setting Standard, board on Third — ▶ does not wrap, so it steps down (◀).</summary>
    [Theory]
    [InlineData(3, 0)]
    [InlineData(3, 2)]
    [InlineData(1, 0)]
    public void AHigherDegreeThanWantedStepsDown(int current, int wanted)
        => Assert.Equal(CrucibleLoopRules.DegreeAction.StepDown, CrucibleLoopRules.NextDegreeAction(current, wanted, 0));

    [Fact]
    public void ThirdDownToStandardTakesThreeSteps()
    {
        for (var steps = 0; steps < 3; steps++)
            Assert.Equal(CrucibleLoopRules.DegreeAction.StepDown, CrucibleLoopRules.NextDegreeAction(3 - steps, 0, steps));
        Assert.Equal(CrucibleLoopRules.DegreeAction.Ready, CrucibleLoopRules.NextDegreeAction(0, 0, 3));
    }

    [Fact]
    public void AStepDownThatDoesNothingGivesUp()
        => Assert.Equal(CrucibleLoopRules.DegreeAction.GiveUp, CrucibleLoopRules.NextDegreeAction(3, 0, CrucibleLoopRules.DegreeCount));

    [Fact]
    public void ALockedDegreeGivesUpAfterAFullTurn()
    {
        // The steps do nothing on a character without the degrees: still Standard after four presses.
        Assert.Equal(CrucibleLoopRules.DegreeAction.StepUp, CrucibleLoopRules.NextDegreeAction(0, 3, 3));
        Assert.Equal(CrucibleLoopRules.DegreeAction.GiveUp, CrucibleLoopRules.NextDegreeAction(0, 3, CrucibleLoopRules.DegreeCount));
    }

    [Fact]
    public void AnUnknownSettingIsLeftAlone()
        => Assert.Equal(CrucibleLoopRules.DegreeAction.Ready, CrucibleLoopRules.NextDegreeAction(1, 7, 0));

    [Theory]
    [InlineData(0, "Standard")]
    [InlineData(3, "Third Degree")]
    [InlineData(-1, "as set in game")]
    public void DegreesAreNamed(int degree, string name)
        => Assert.Equal(name, CrucibleLoopRules.DegreeName(degree));
}
