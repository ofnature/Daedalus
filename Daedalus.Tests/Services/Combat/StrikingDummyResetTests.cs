using Daedalus.Services.Combat;
using Xunit;

namespace Daedalus.Tests.Services.Combat;

/// <summary>Which dummies "End dummy fight" resets.</summary>
public class StrikingDummyResetTests
{
    private static StrikingDummyReset.Candidate C(uint id, uint nameId, float distance) => new(id, nameId, distance);

    [Fact]
    public void BothKindsOfDummyAreReset()
        => Assert.Equal([10u, 11u], StrikingDummyReset.Select([C(10, 541, 5), C(11, 13078, 6)]));

    /// <summary>Real enemies are never touched: the command is for dummies only.</summary>
    [Fact]
    public void RealEnemiesAreLeftAlone()
        => Assert.Equal([10u], StrikingDummyReset.Select([C(10, 541, 5), C(20, 4130, 3)]));

    [Fact]
    public void DummiesOutOfRangeAreSkipped()
        => Assert.Empty(StrikingDummyReset.Select([C(10, 541, StrikingDummyReset.Range + 1)]));

    [Fact]
    public void NearestComesFirst()
        => Assert.Equal([12u, 10u], StrikingDummyReset.Select([C(10, 541, 30), C(12, 541, 4)]));

    [Fact]
    public void AnObjectWithNoIdIsSkipped()
        => Assert.Empty(StrikingDummyReset.Select([C(0, 541, 2)]));

    private static StrikingDummyReset.HoldStep Step(double sinceStart, double? quietFor, double sinceSent = 0, bool petOut = false, bool awaySent = false)
        => StrikingDummyReset.Decide(System.TimeSpan.FromSeconds(sinceStart), quietFor is { } q ? System.TimeSpan.FromSeconds(q) : null,
            System.TimeSpan.FromSeconds(sinceSent), petOut, awaySent);

    /// <summary>
    /// The hold ends only after a real stretch out of combat. Field, 2026-10-22: it let go after a one-frame drop of the
    /// combat flag while the familiar was still swinging, and the dummy was pulled straight back.
    /// </summary>
    [Fact]
    public void ABlipOutOfCombatDoesNotEndTheHold() => Assert.False(Step(1.0, quietFor: 0.1).Release);

    [Fact]
    public void TwoQuietSecondsEndTheHold() => Assert.True(Step(3.0, quietFor: 2.0).Release);

    /// <summary>DoT ticks and stray hits re-aggro the dummy, so the reset goes out again while combat lasts.</summary>
    [Fact]
    public void TheResetIsSentAgainWhileStillInCombat() => Assert.True(Step(3.0, quietFor: null, sinceSent: 1.6).Resend);

    [Fact]
    public void NotSentAgainTooSoon() => Assert.False(Step(3.0, quietFor: null, sinceSent: 0.5).Resend);

    [Fact]
    public void NothingIsResentWhileQuiet() => Assert.False(Step(3.0, quietFor: 1.0, sinceSent: 5).Resend);

    /// <summary>A familiar kept swinging through Heel for ten seconds: a pet still fighting is sent Away, once.</summary>
    [Fact]
    public void APetStillFightingIsSentAway() => Assert.True(Step(4.5, quietFor: null, petOut: true).SendAway);

    [Fact]
    public void AwayIsSentOnlyOnce() => Assert.False(Step(8, quietFor: null, petOut: true, awaySent: true).SendAway);

    [Fact]
    public void AwayWaitsForHeelToHaveAChance() => Assert.False(Step(2, quietFor: null, petOut: true).SendAway);

    /// <summary>A fight that never ends cannot stall the rotation for good.</summary>
    [Fact]
    public void TheHoldNeverOutlastsTheMaximum() => Assert.True(Step(StrikingDummyReset.MaxHold.TotalSeconds, quietFor: null).Release);

    private const uint Me = 100;
    private const byte PetKind = (byte)Dalamud.Game.ClientState.Objects.Enums.BattleNpcSubKind.Pet;
    private const byte ChocoboKind = (byte)Dalamud.Game.ClientState.Objects.Enums.BattleNpcSubKind.Buddy;

    /// <summary>A pet or chocobo still swinging at the dummy pulls you straight back in (field, 2026-10-22).</summary>
    [Fact]
    public void YourOwnPetAndChocoboAreFound()
        => Assert.Equal((true, true), StrikingDummyReset.Companions([new(Me, PetKind), new(Me, ChocoboKind)], Me));

    /// <summary>Another player's pet or chocobo is not yours to command.</summary>
    [Fact]
    public void SomeoneElsesCompanionsAreIgnored()
        => Assert.Equal((false, false), StrikingDummyReset.Companions([new(200, PetKind), new(200, ChocoboKind)], Me));

    [Fact]
    public void NoCompanionsOutMeansNoCommands()
        => Assert.Equal((false, false), StrikingDummyReset.Companions([], Me));

    /// <summary>Heel first (keeps the pet out), Away as the fallback; Withdraw keeps the chocobo summoned.</summary>
    [Fact]
    public void TheCommandsAreHeelAwayAndWithdraw()
    {
        Assert.Equal(2u, StrikingDummyReset.PetHeel);
        Assert.Equal(1u, StrikingDummyReset.PetAway);
        Assert.Equal(2u, StrikingDummyReset.ChocoboWithdraw);
    }

    /// <summary>
    /// Odysseus targets a dummy on purpose for job quests; Daedalus dropped it every time (field, 2026-10-23).
    /// Every other automation still must not fight one.
    /// </summary>
    [Theory]
    [InlineData("Odysseus", true)]
    [InlineData("Questionable", false)]
    [InlineData("Henchman", false)]
    [InlineData("AutoDuty", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void OnlyOdysseusMayFightADummy(string? source, bool allowed)
        => Assert.Equal(allowed, TrainingDummyPolicy.AutomationMayFight(source));

    /// <summary>The command number the subcommand menu sends (OmenTools / Daily Routines: ResetStrikingDummy).</summary>
    [Fact]
    public void TheCommandIsTheGamesResetStrikingDummy() => Assert.Equal(319, StrikingDummyReset.ResetStrikingDummyCommand);
}
