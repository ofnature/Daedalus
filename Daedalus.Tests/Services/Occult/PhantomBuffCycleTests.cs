using Daedalus.Data;
using Daedalus.Services.Occult;
using Xunit;

namespace Daedalus.Tests.Services.Occult;

/// <summary>
/// The phantom buff cycle switches your support job up to four times and must always give it
/// back. Buffs are worth ~30 minutes; being stranded on Phantom Bard is worth a lot more.
/// </summary>
public class PhantomBuffCycleTests
{
    private sealed class FakeWorld : IPhantomBuffWorld
    {
        public PhantomJob ActiveJob { get; set; } = PhantomJob.Cannoneer;
        public Dictionary<PhantomJob, byte> Levels { get; } = new()
        {
            [PhantomJob.Knight] = 5,
            [PhantomJob.Bard] = 5,
            [PhantomJob.Monk] = 5,
            [PhantomJob.Dancer] = 5,
        };

        public IReadOnlyDictionary<PhantomJob, byte> JobLevels => Levels;
        public bool InOccultZone { get; set; } = true;
        public bool InCombat { get; set; }
        public bool NearKnowledgeCrystal { get; set; } = true;

        public bool RefuseSwitch { get; set; }
        public bool RefuseCast { get; set; }
        public bool CastNeverLands { get; set; }
        public PhantomJob? FailSwitchTo { get; set; }

        /// <summary>
        /// Refuse the first N switches TO this job, then let them through — the game's behaviour
        /// while you are still locked from the buff you just cast. Targeted at one job so a test
        /// can refuse the hand-back without also refusing the buff switches.
        /// </summary>
        public PhantomJob? RefuseFirstSwitchesTo { get; set; }

        public int RefuseFirstSwitchesCount { get; set; }

        public readonly List<PhantomJob> SwitchLog = new();
        public readonly List<uint> CastLog = new();
        public readonly Dictionary<uint, float> Statuses = new();

        public bool ChangeSupportJob(PhantomJob job)
        {
            SwitchLog.Add(job);
            if (RefuseSwitch || FailSwitchTo == job)
                return false;

            if (RefuseFirstSwitchesTo == job && RefuseFirstSwitchesCount > 0)
            {
                RefuseFirstSwitchesCount--;
                return false;
            }

            ActiveJob = job;
            return true;
        }

        public bool CanCast(uint actionId) => !RefuseCast;

        public bool Cast(uint actionId, string actionName)
        {
            CastLog.Add(actionId);
            if (!CastNeverLands)
            {
                var buff = FindBuff(actionId);
                if (buff is { } b)
                    Statuses[b.StatusId] = 1800f;
            }

            return true;
        }

        public float StatusRemaining(uint statusId)
            => Statuses.TryGetValue(statusId, out var v) ? v : 0f;

        private static PhantomBuff? FindBuff(uint actionId)
        {
            foreach (var b in PhantomBuffs.All)
            {
                if (b.ActionId == actionId)
                    return b;
            }

            return null;
        }
    }

    private static PhantomBuffCycleService Cycle(FakeWorld world)
        => new(world) { DeltaSeconds = () => 0.5f };

    private static void RunToCompletion(PhantomBuffCycleService cycle, int maxTicks = 2000)
    {
        for (var i = 0; i < maxTicks && cycle.IsRunning; i++)
            cycle.Tick();
    }

    // ── the restore guarantee ────────────────────────────────────────────────────────────

    [Fact]
    public void Returns_to_the_job_you_started_on()
    {
        var world = new FakeWorld { ActiveJob = PhantomJob.Cannoneer };
        var cycle = Cycle(world);

        Assert.True(cycle.Start(_ => true));
        Assert.Equal(PhantomJob.Cannoneer, cycle.StartingJob);
        RunToCompletion(cycle);

        Assert.Equal(PhantomJob.Cannoneer, world.ActiveJob);
        Assert.Equal(BuffCycleState.Idle, cycle.State);
    }

    [Fact]
    public void Returns_to_the_starting_job_even_when_combat_interrupts()
    {
        var world = new FakeWorld { ActiveJob = PhantomJob.Oracle };
        var cycle = Cycle(world);
        cycle.Start(_ => true);

        cycle.Tick();
        cycle.Tick();
        world.InCombat = true;

        // Combat aborts, but the restore still has to run — so combat must clear for the
        // restore state to complete, exactly as it would in game once the fight ends.
        cycle.Tick();
        world.InCombat = false;
        RunToCompletion(cycle);

        Assert.Equal(PhantomJob.Oracle, world.ActiveJob);
    }

    [Fact]
    public void Returns_to_the_starting_job_when_a_cast_never_lands()
    {
        var world = new FakeWorld { ActiveJob = PhantomJob.Thief, CastNeverLands = true };
        var cycle = Cycle(world);
        cycle.Start(_ => true);
        RunToCompletion(cycle);

        Assert.Equal(PhantomJob.Thief, world.ActiveJob);
    }

    [Fact]
    public void Does_not_switch_jobs_at_all_when_there_is_nothing_to_collect()
    {
        // Every buff off — switching away and back would be pure disruption for no gain.
        var world = new FakeWorld { ActiveJob = PhantomJob.Chemist };
        var cycle = Cycle(world);

        Assert.False(cycle.Start(_ => false));
        Assert.Empty(world.SwitchLog);
        Assert.Equal(PhantomJob.Chemist, world.ActiveJob);
    }

    // ── partial buff sets ───────────────────────────────────────────────────────────────

    [Fact]
    public void Skips_locked_and_underlevelled_jobs_and_says_which()
    {
        var world = new FakeWorld();
        world.Levels.Remove(PhantomJob.Dancer);   // never unlocked
        world.Levels[PhantomJob.Monk] = 1;        // Counterstance needs 3

        var cycle = Cycle(world);
        cycle.Start(_ => true);
        RunToCompletion(cycle);

        Assert.Equal(2, world.CastLog.Count);
        Assert.Contains("Buffed 2 of 2", cycle.LastOutcome);
        Assert.Contains("Monk Lv1 (needs 3)", cycle.LastOutcome);
        Assert.Contains("Dancer not unlocked", cycle.LastOutcome);
    }

    [Fact]
    public void A_disabled_buff_is_skipped_silently()
    {
        // The user turned it off; reporting it back as a problem is noise.
        var world = new FakeWorld();
        var cycle = Cycle(world);
        cycle.Start(b => b.Job != PhantomJob.Bard);
        RunToCompletion(cycle);

        Assert.Equal(3, world.CastLog.Count);
        Assert.DoesNotContain("Bard", cycle.LastOutcome);
        Assert.Contains("Buffed 3 of 3", cycle.LastOutcome);
    }

    [Fact]
    public void One_refused_job_does_not_abort_the_rest()
    {
        var world = new FakeWorld { FailSwitchTo = PhantomJob.Monk };
        var cycle = Cycle(world);
        cycle.Start(_ => true);
        RunToCompletion(cycle);

        Assert.Equal(3, world.CastLog.Count);
        Assert.Equal(PhantomJob.Cannoneer, world.ActiveJob);
    }

    // ── preconditions ───────────────────────────────────────────────────────────────────

    [Fact]
    public void Refuses_to_start_outside_the_zone_or_in_combat()
    {
        var outside = new FakeWorld { InOccultZone = false };
        Assert.False(Cycle(outside).Start(_ => true));
        Assert.Contains("Occult", Cycle(outside).BlockedReason());

        var fighting = new FakeWorld { InCombat = true };
        Assert.False(Cycle(fighting).Start(_ => true));
        Assert.Equal("In combat", Cycle(fighting).BlockedReason());
    }

    [Fact]
    public void A_running_cycle_cannot_be_started_twice()
    {
        var world = new FakeWorld();
        var cycle = Cycle(world);
        cycle.Start(_ => true);
        cycle.Tick();

        Assert.False(cycle.Start(_ => true));
        Assert.Equal("Already running", cycle.BlockedReason());
    }

    // ── freshness ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void A_fresh_application_is_required_not_merely_the_status_existing()
    {
        // A buff already up from 20 minutes ago must not make a failed cast look successful.
        var world = new FakeWorld { CastNeverLands = true };
        foreach (var b in PhantomBuffs.All)
            world.Statuses[b.StatusId] = 300f;   // present, but old

        var cycle = Cycle(world);
        cycle.Start(_ => true);
        RunToCompletion(cycle);

        Assert.Contains("Buffed 0 of 4", cycle.LastOutcome);
    }

    [Fact]
    public void An_automatic_refresh_leaves_buffs_that_are_still_up()
    {
        var world = new FakeWorld();
        foreach (var b in PhantomBuffs.All)
            world.Statuses[b.StatusId] = 1500f;   // all comfortably up

        var cycle = Cycle(world);
        var started = cycle.Start(_ => true, skipFresh: true, freshThresholdSeconds: 600f);

        Assert.False(started);
        Assert.Empty(world.SwitchLog);
    }

    [Fact]
    public void An_automatic_refresh_still_tops_up_the_one_running_out()
    {
        var world = new FakeWorld();
        foreach (var b in PhantomBuffs.All)
            world.Statuses[b.StatusId] = 1500f;
        world.Statuses[PhantomBuffs.All[2].StatusId] = 60f;   // Monk about to drop

        var cycle = Cycle(world);
        Assert.True(cycle.Start(_ => true, skipFresh: true, freshThresholdSeconds: 600f));
        RunToCompletion(cycle);

        Assert.Equal(PhantomBuffs.All[2].ActionId, Assert.Single(world.CastLog));
        Assert.Equal(PhantomJob.Cannoneer, world.ActiveJob);
    }

    // ── restoring the starting job ──────────────────────────────────────────────────────

    /// <summary>
    /// Field report: the cycle buffed correctly then "spammed job change" with a wall of red
    /// "unable to change phantom jobs at this time". The game refuses the switch while you are
    /// still locked from the last buff and complains once per refusal, and the restore was
    /// retrying every framework update — roughly sixty error lines a second.
    /// </summary>
    [Fact]
    public void A_refused_restore_is_retried_about_once_a_second_not_every_frame()
    {
        var world = new FakeWorld { FailSwitchTo = PhantomJob.Cannoneer };   // never lets it back
        var cycle = Cycle(world);   // 0.5s per tick
        Assert.True(cycle.Start(_ => true));
        RunToCompletion(cycle, maxTicks: 400);

        var restoreAttempts = world.SwitchLog.FindAll(j => j == PhantomJob.Cannoneer).Count;

        // The 15s step timeout at one attempt a second, not one per tick. Ticking that long
        // unthrottled would be 30 attempts here and ~900 in game; the magnitude is the point.
        Assert.InRange(restoreAttempts, 1, 20);

        // And when it truly cannot get back, it says so rather than retrying forever.
        Assert.Equal(BuffCycleState.Faulted, cycle.State);
    }

    [Fact]
    public void The_first_restore_attempt_is_not_delayed()
    {
        // Throttling must not cost a second on the common path, where nothing refuses.
        var world = new FakeWorld();
        var cycle = Cycle(world);
        Assert.True(cycle.Start(_ => true));
        RunToCompletion(cycle);

        Assert.Equal(PhantomJob.Cannoneer, world.ActiveJob);
        Assert.Equal(BuffCycleState.Idle, cycle.State);
    }

    [Fact]
    public void A_restore_that_is_refused_briefly_still_succeeds()
    {
        // The real case: a couple of refusals while the cast lock expires, then it goes through.
        var world = new FakeWorld
        {
            RefuseFirstSwitchesTo = PhantomJob.Cannoneer,
            RefuseFirstSwitchesCount = 3,
        };

        var cycle = Cycle(world);
        Assert.True(cycle.Start(_ => true));
        RunToCompletion(cycle, maxTicks: 400);

        Assert.Equal(PhantomJob.Cannoneer, world.ActiveJob);
        Assert.Equal(BuffCycleState.Idle, cycle.State);
    }
}
