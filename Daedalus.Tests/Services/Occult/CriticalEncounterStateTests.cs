using Daedalus.Data;
using Daedalus.Services.Occult;
using Xunit;

namespace Daedalus.Tests.Services.Occult;

/// <summary>
/// "A CE is up" was never actionable on its own — the arena seals partway through, so an
/// encounter you can still run to and one you have already missed read identically. The game
/// tracks the stage itself (DynamicEventState); these pin that we surface it faithfully.
/// </summary>
public class CriticalEncounterStateTests
{
    /// <summary>Stage values are the game's own bytes — renumbering silently mislabels every CE.</summary>
    [Fact]
    public void Stage_values_match_the_games_DynamicEventState()
    {
        Assert.Equal(0, (byte)CriticalEncounterStage.Inactive);
        Assert.Equal(1, (byte)CriticalEncounterStage.Register);
        Assert.Equal(2, (byte)CriticalEncounterStage.Warmup);
        Assert.Equal(3, (byte)CriticalEncounterStage.Battle);
    }

    [Fact]
    public void Only_the_registration_window_can_be_joined()
    {
        Assert.True(Ce(CriticalEncounterStage.Register).CanJoin);
        Assert.False(Ce(CriticalEncounterStage.Warmup).CanJoin);
        Assert.False(Ce(CriticalEncounterStage.Battle).CanJoin);
        Assert.False(Ce(CriticalEncounterStage.Inactive).CanJoin);
    }

    [Fact]
    public void Warmup_and_battle_are_sealed_but_still_live()
    {
        // The distinction that matters for the UI: visible, but the door is shut.
        Assert.False(Ce(CriticalEncounterStage.Register).IsSealed);
        Assert.True(Ce(CriticalEncounterStage.Warmup).IsSealed);
        Assert.True(Ce(CriticalEncounterStage.Battle).IsSealed);
    }

    [Theory]
    [InlineData(0u, "")]
    [InlineData(9u, "0:09")]
    [InlineData(60u, "1:00")]
    [InlineData(95u, "1:35")]
    [InlineData(600u, "10:00")]
    public void Timer_reads_as_minutes_and_seconds(uint secondsLeft, string expected)
    {
        Assert.Equal(expected, (Ce(CriticalEncounterStage.Register) with { SecondsLeft = secondsLeft }).TimeLeftLabel);
    }

    [Fact]
    public void Headcount_survives_a_missing_cap()
    {
        var ce = Ce(CriticalEncounterStage.Register);
        Assert.Equal("12/32", (ce with { Participants = 12, MaxParticipants = 32 }).ParticipantsLabel);
        Assert.Equal("12", (ce with { Participants = 12, MaxParticipants = 0 }).ParticipantsLabel);
        Assert.Equal(string.Empty, (ce with { Participants = 0, MaxParticipants = 0 }).ParticipantsLabel);
    }

    [Fact]
    public void Shard_alert_keeps_the_stage_so_it_can_say_whether_you_can_still_get_in()
    {
        // Oracle is locked (absent from the level table), so its shard CE should surface —
        // and the caller needs the stage to know whether running there is worth anything.
        var active = new[]
        {
            new CriticalEncounterState("On the Hunt", CriticalEncounterStage.Register, 45, 8, 32, 0),
            new CriticalEncounterState("The Black Regiment", CriticalEncounterStage.Battle, 300, 30, 32, 60),
        };
        var levels = new Dictionary<PhantomJob, byte>();

        var unclaimed = PhantomJobData.UnclaimedShardEncountersWithStage(active, levels);

        Assert.Equal(2, unclaimed.Count);
        var hunt = Assert.Single(unclaimed, u => u.Encounter.Name == "On the Hunt");
        Assert.Equal(PhantomJob.Oracle, hunt.Job);
        Assert.True(hunt.Encounter.CanJoin);
        Assert.Equal("0:45", hunt.Encounter.TimeLeftLabel);

        var regiment = Assert.Single(unclaimed, u => u.Encounter.Name == "The Black Regiment");
        Assert.False(regiment.Encounter.CanJoin);
        Assert.True(regiment.Encounter.IsSealed);
    }

    [Fact]
    public void An_unlocked_job_still_produces_no_alert()
    {
        // The stage-aware path must keep the original rule: own the job, no banner.
        var active = new[]
        {
            new CriticalEncounterState("On the Hunt", CriticalEncounterStage.Register, 45, 8, 32, 0),
        };
        var levels = new Dictionary<PhantomJob, byte> { [PhantomJob.Oracle] = 3 };

        Assert.Empty(PhantomJobData.UnclaimedShardEncountersWithStage(active, levels));
    }

    [Fact]
    public void Both_overloads_agree_on_which_encounters_match()
    {
        // They share one matcher on purpose; if they ever diverge, the banner and any
        // name-only caller would disagree about the same CE.
        var levels = new Dictionary<PhantomJob, byte>();
        var names = new[] { "On the Hunt", "Something Irrelevant" };
        var states = new[]
        {
            new CriticalEncounterState("On the Hunt", CriticalEncounterStage.Battle, 10, 1, 32, 5),
            new CriticalEncounterState("Something Irrelevant", CriticalEncounterStage.Register, 10, 1, 32, 0),
        };

        var byName = PhantomJobData.UnclaimedShardEncounters(names, levels);
        var byState = PhantomJobData.UnclaimedShardEncountersWithStage(states, levels);

        Assert.Equal(byName.Count, byState.Count);
        Assert.Equal(byName[0].Job, byState[0].Job);
    }

    // ── the join countdown ──────────────────────────────────────────────────────────────

    [Fact]
    public void A_populated_SecondsLeft_is_used_as_is()
    {
        Assert.Equal(240u, CriticalEncounterState.ResolveSecondsLeft(240, startTimestamp: 0, nowUnix: 1000));
    }

    [Fact]
    public void Register_falls_back_to_the_start_timestamp()
    {
        // The case this exists for: JOIN NOW with SecondsLeft reading 0. If StartTimestamp marks
        // when the battle begins, the gap to now IS the time left to get in.
        Assert.Equal(45u, CriticalEncounterState.ResolveSecondsLeft(0, startTimestamp: 1045, nowUnix: 1000));
    }

    [Fact]
    public void A_start_timestamp_in_the_past_shows_nothing_rather_than_a_wrong_number()
    {
        // If StartTimestamp turns out to mark when REGISTRATION opened rather than when the
        // battle starts, the difference goes negative — and the honest answer is no timer, which
        // is exactly the behaviour before this fallback existed. A wrong guess costs a missing
        // countdown, never a misleading one.
        Assert.Equal(0u, CriticalEncounterState.ResolveSecondsLeft(0, startTimestamp: 900, nowUnix: 1000));
    }

    [Fact]
    public void Implausible_values_are_refused_from_both_sources()
    {
        // A CE window is minutes. Anything past an hour means the field is not a countdown.
        Assert.Equal(0u, CriticalEncounterState.ResolveSecondsLeft(999_999, 0, 1000));
        Assert.Equal(0u, CriticalEncounterState.ResolveSecondsLeft(0, startTimestamp: 500_000, nowUnix: 1000));
    }

    [Fact]
    public void No_timing_information_at_all_reads_as_no_timer()
    {
        Assert.Equal(0u, CriticalEncounterState.ResolveSecondsLeft(0, startTimestamp: 0, nowUnix: 1000));
        Assert.Equal(string.Empty, Ce(CriticalEncounterStage.Register).TimeLeftLabel);
    }

    private static CriticalEncounterState Ce(CriticalEncounterStage stage)
        => new("Test Encounter", stage, 0, 0, 0, 0);
}
