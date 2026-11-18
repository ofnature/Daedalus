using System.Linq;
using Daedalus.Data;
using Daedalus.Rotation.ArtemisCore.Helpers;
using Xunit;

namespace Daedalus.Tests.Data;

/// <summary>Third Degree: the documented clears' teams for the First Board, and the earlier retreat on a degree.</summary>
public class CrucibleDegreeTeamsTests
{
    private const int OpoOpo = 5, Megalocrab = 9, Wespe = 10, Mandragora = 12, Geshunpest = 13, Crab = 15, Mantis = 16,
        Colibri = 32, Coeurl = 33, Damselfly = 44, IceGolem = 47, Behemoth = 50;

    [Fact]
    public void StandardHasNoDegreeTeam()
    {
        Assert.Null(CrucibleDegreeTeams.For(0, 1));
        Assert.Empty(CrucibleDegreeTeams.Roster(0, 1, _ => true));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void EveryDegreeAboveStandardUsesTheDocumentedTeams(int degree)
        => Assert.Equal([Wespe, Mandragora, OpoOpo], CrucibleDegreeTeams.For(degree, 1));

    [Fact]
    public void TheWespeLeadsEveryFightOfTheRoute()
    {
        // The Bone Knight fight too (user, 2026-11-15): held back to tank him it ended at 30% and missed the rest.
        foreach (var battle in new[] { 1, 2, 4, 5, 6 })
            Assert.Equal(Wespe, CrucibleDegreeTeams.For(3, battle)![0]);
    }

    [Fact]
    public void PasDeSeulGetsBehemothAndColibri()
        => Assert.Equal([Wespe, Behemoth, Colibri], CrucibleDegreeTeams.For(3, 6));

    [Fact]
    public void ABattleWithNoDocumentedTeamHasNone()
        => Assert.Null(CrucibleDegreeTeams.For(3, 14)); // the Third Board's Cavalier Piece, not written yet

    [Fact]
    public void TheRosterIsTheRoutesTenThenTheOtherFork()
    {
        var roster = CrucibleDegreeTeams.Roster(3, 1, _ => true);
        Assert.Equal([Wespe, Mandragora, OpoOpo, Geshunpest, Coeurl, IceGolem, Behemoth, Damselfly, Megalocrab, Colibri],
            roster.Take(10));
        Assert.Equal([Crab, Mantis], roster.Skip(10));
    }

    [Fact]
    public void AnUncapturedBeastIsLeftOffTheRoster()
    {
        var roster = CrucibleDegreeTeams.Roster(3, 1, no => no != Mandragora);
        Assert.DoesNotContain(Mandragora, roster);
        Assert.Equal(11, roster.Count);
    }

    [Fact]
    public void OnlyTheFirstBoardIsWrittenSoFar()
        => Assert.Empty(CrucibleDegreeTeams.Roster(3, 3, _ => true));

    // --- Horn choice ---

    [Fact]
    public void ThePlayersOwnPicksBeatTheDegreeTeam()
        => Assert.Equal([Coeurl, 0, 0], CrucibleLevelPlan.HornChoice(CrucibleTeamGoal.Clear, [Coeurl, 0, 0], [Damselfly],
            degreeTeam: CrucibleDegreeTeams.For(3, 1)));

    [Fact]
    public void WithNoPicksTheDegreeTeamIsUsed()
        => Assert.Equal([Wespe, Mandragora, OpoOpo], CrucibleLevelPlan.HornChoice(CrucibleTeamGoal.Clear, null, [Damselfly],
            degreeTeam: CrucibleDegreeTeams.For(3, 1)));

    [Fact]
    public void TheDegreeTeamComesBeforeTheLevellingCore()
        => Assert.Equal([Wespe, Mandragora, OpoOpo], CrucibleLevelPlan.HornChoice(CrucibleTeamGoal.Level, [0, 0, 0], [Damselfly],
            degreeTeam: CrucibleDegreeTeams.For(3, 1)));

    [Fact]
    public void OnStandardClearStaysAuto()
        => Assert.Null(CrucibleLevelPlan.HornChoice(CrucibleTeamGoal.Clear, null, [Damselfly],
            degreeTeam: CrucibleDegreeTeams.For(0, 1)));

    [Fact]
    public void LegendaryKeepsItsOwnThree()
    {
        var horns = CrucibleLevelPlan.HornChoice(CrucibleTeamGoal.Legendary, null, [Damselfly, Mantis, Wespe],
            degreeTeam: CrucibleDegreeTeams.For(3, 1));
        Assert.NotNull(horns);
        Assert.All(horns!, no => Assert.Contains(no, new[] { Damselfly, Mantis, Wespe }));
    }

    // --- The falling line ---

    [Fact]
    public void StandardKeepsTheTwentyPercentLine()
        => Assert.Equal(ArtemisCooldownRules.FamiliarFallingHpPct, ArtemisCooldownRules.FallingFloor(0));

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void ADegreeRetreatsAt35Percent(int degree)
        => Assert.Equal(0.35f, ArtemisCooldownRules.FallingLine(0f, 0f, ArtemisCooldownRules.FallingFloor(degree)));

    [Fact]
    public void ADegreesLineStillRisesForHardHitsAndStaysCapped()
    {
        var floor = ArtemisCooldownRules.FallingFloor(3);
        Assert.Equal(0.45f, ArtemisCooldownRules.FallingLine(0.30f, 0f, floor), 3);
        Assert.Equal(ArtemisCooldownRules.FallingLineMax, ArtemisCooldownRules.FallingLine(0.60f, 0f, floor));
    }
}
