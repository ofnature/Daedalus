using Daedalus.Data;
using Daedalus.Rotation.ArtemisCore.Helpers;
using Xunit;

namespace Daedalus.Tests.Data;

/// <summary>
/// The Crucible modes (user, 2026-11-06): Clear and Level loop; Legendary (on the degree picked) and Third Degree
/// (always Third Degree) are one attempt each.
/// </summary>
public class CrucibleModesTests
{
    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    public void ThirdDegreeAlwaysGoesInOnThirdDegree(int picked)
        => Assert.Equal(3, CrucibleLoopRules.EnteredDegree(CrucibleTeamGoal.ThirdDegree, picked));

    [Theory]
    [InlineData(CrucibleTeamGoal.Legendary, 1)]
    [InlineData(CrucibleTeamGoal.Legendary, 0)]
    [InlineData(CrucibleTeamGoal.Clear, 2)]
    [InlineData(CrucibleTeamGoal.Level, -1)]
    public void OtherGoalsGoInOnTheDegreePicked(CrucibleTeamGoal goal, int picked)
        => Assert.Equal(picked, CrucibleLoopRules.EnteredDegree(goal, picked));

    [Fact]
    public void OnlyLegendaryAndThirdDegreeAreOneShot()
    {
        Assert.True(CrucibleLoopRules.IsOneShot(CrucibleTeamGoal.Legendary));
        Assert.True(CrucibleLoopRules.IsOneShot(CrucibleTeamGoal.ThirdDegree));
        Assert.False(CrucibleLoopRules.IsOneShot(CrucibleTeamGoal.Clear));
        Assert.False(CrucibleLoopRules.IsOneShot(CrucibleTeamGoal.Level));
    }

    [Fact]
    public void AOneShotGoalNeverHasMoreThanOneRunLeft()
    {
        Assert.Equal(1, CrucibleLoopRules.RunsLeft(CrucibleTeamGoal.Legendary, 25));
        Assert.Equal(0, CrucibleLoopRules.RunsLeft(CrucibleTeamGoal.ThirdDegree, 0));
        Assert.Equal(25, CrucibleLoopRules.RunsLeft(CrucibleTeamGoal.Level, 25));
    }

    [Fact]
    public void ThirdDegreeUsesTheDegreeTeamsLikeClear()
    {
        var team = CrucibleDegreeTeams.For(3, 15);
        Assert.Equal(team, CrucibleLevelPlan.HornChoice(CrucibleTeamGoal.ThirdDegree, null, [1, 2, 3], degreeTeam: team));
    }

    // --- Legendary tanking: the beastmaster tanks; the familiar takes the Second Master's tankbusters ---

    [Fact]
    public void OnTheSecondMastersBoardTheFamiliarTakesTheTankbuster()
    {
        var lead = ArtemisCooldownRules.TankbusterLeadSeconds;
        Assert.True(ArtemisCooldownRules.LegendaryFamiliarTakesBuster(ArtemisCooldownRules.SecondMastersBoardIndex, lead));
        Assert.False(ArtemisCooldownRules.LegendaryFamiliarTakesBuster(ArtemisCooldownRules.SecondMastersBoardIndex, lead + 1f));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void OnEveryOtherBoardYouTakeTheTankbuster(int board)
        => Assert.False(ArtemisCooldownRules.LegendaryFamiliarTakesBuster(board, 1f));

    [Fact]
    public void OnlyAFamiliarAboveTheFallingLineIsSnarledIntoIt()
    {
        var line = ArtemisCooldownRules.FamiliarFallingHpPct;
        Assert.True(ArtemisCooldownRules.ShouldSnarlForLegendaryBuster(true, true, true, 0.9f, line));
        Assert.False(ArtemisCooldownRules.ShouldSnarlForLegendaryBuster(true, true, true, line - 0.01f, line));
        Assert.False(ArtemisCooldownRules.ShouldSnarlForLegendaryBuster(true, true, hasFamiliar: false, 0.9f, line));
        Assert.False(ArtemisCooldownRules.ShouldSnarlForLegendaryBuster(true, ready: false, true, 0.9f, line));
    }

    // --- Start run and "until all rank 25" (field 2026-11-06, Saar) ---

    [Fact]
    public void StartRunOnLevelUntilRank25LoopsOn()
        => Assert.Equal(CrucibleLoopRules.UntilMaxRankRuns, CrucibleLoopRules.RunsForStart(CrucibleTeamGoal.Level, untilMaxRank: true));

    [Theory]
    [InlineData(CrucibleTeamGoal.Level, false)]
    [InlineData(CrucibleTeamGoal.Clear, true)]
    [InlineData(CrucibleTeamGoal.Legendary, true)]
    [InlineData(CrucibleTeamGoal.ThirdDegree, false)]
    public void StartRunOtherwiseGoesInOnce(CrucibleTeamGoal goal, bool untilMaxRank)
        => Assert.Equal(1, CrucibleLoopRules.RunsForStart(goal, untilMaxRank));

    // --- Legendary's last-resort potion: only when it can save the run (user, 2026-11-06) ---

    [Fact]
    public void WithAFamiliarLeftThePotionIsDrunk()
        => Assert.True(CrucibleAutoPilotRules.PotionCanSaveRun(1, 0.2f, 0.3f, 0.05f, 0.9f, 0.001f));

    [Fact]
    public void AloneAgainstAnEnemyThatOutlastsYouThePotionIsKept()
        // 50% to live on at 5%/s = 10 s; the enemy needs 90% at 1%/s = 90 s.
        => Assert.False(CrucibleAutoPilotRules.PotionCanSaveRun(0, 0.2f, 0.3f, 0.05f, 0.9f, 0.01f));

    [Fact]
    public void AloneAgainstANearlyDeadEnemyThePotionIsDrunk()
        // 10 s to live with it; the enemy dies in 5 s.
        => Assert.True(CrucibleAutoPilotRules.PotionCanSaveRun(0, 0.2f, 0.3f, 0.05f, 0.05f, 0.01f));

    [Fact]
    public void AnUnknownRateDrinks()
    {
        Assert.True(CrucibleAutoPilotRules.PotionCanSaveRun(0, 0.2f, 0.3f, null, 0.9f, 0.01f));
        Assert.True(CrucibleAutoPilotRules.PotionCanSaveRun(0, 0.2f, 0.3f, 0.05f, null, null));
        Assert.True(CrucibleAutoPilotRules.PotionCanSaveRun(0, 0.2f, 0.3f, 0.05f, 0.9f, 0f)); // the enemy isn't losing HP: can't tell
    }

    [Fact]
    public void HpLossIsMeasuredOverTheWindow()
    {
        Assert.Null(CrucibleAutoPilotRules.HpLossPerSecond([(0, 1f), (3, 0.9f)])); // under 5 s
        Assert.Equal(0.02f, CrucibleAutoPilotRules.HpLossPerSecond([(0, 1f), (5, 0.95f), (10, 0.8f)])!.Value, 3);
        Assert.Equal(0f, CrucibleAutoPilotRules.HpLossPerSecond([(0, 0.5f), (10, 0.8f)])); // healed: no loss
    }
}
