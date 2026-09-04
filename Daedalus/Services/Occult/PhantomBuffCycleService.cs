using System;
using System.Collections.Generic;
using Daedalus.Data;

namespace Daedalus.Services.Occult;

/// <summary>Where the buff cycle is right now.</summary>
public enum BuffCycleState
{
    Idle,
    SwitchingJob,
    AwaitingJob,
    Casting,
    AwaitingBuff,
    RestoringJob,
    Faulted,
}

/// <summary>
/// Collects phantom self-buffs by cycling support jobs, then puts the job you started on back.
///
/// <para>
/// Buffs last ~30 minutes and survive a job switch, so one pass leaves you carrying all of them
/// on whatever you actually play. Cast beside a Knowledge Crystal they reach every party member
/// in the zone, so one character can cover a fleet. Design: docs/occult-buff-cycle.md.
/// </para>
///
/// <para>
/// The state machine is deliberately hand-rolled and driven by <see cref="Tick"/> from the
/// framework update — there is no chain/task library here, and a cycle that switches your job
/// four times is not something to run on a background thread.
/// </para>
///
/// <para>
/// <b>The job you started on is always restored.</b> Success, timeout, refusal, combat starting
/// mid-cycle, zoning out — every exit runs through <see cref="BeginRestore"/>. Leaving someone on
/// Phantom Bard because a cast timed out would be a worse bug than collecting no buffs at all.
/// </para>
/// </summary>
public sealed class PhantomBuffCycleService
{
    /// <summary>Per-step deadline — a job switch or a cast that stalls this long is abandoned.</summary>
    internal const float StepTimeoutSeconds = 15f;

    /// <summary>Whole-cycle deadline, including the restore.</summary>
    internal const float CycleTimeoutSeconds = 90f;

    /// <summary>
    /// Gap between attempts to hand the starting job back.
    ///
    /// <para>
    /// The game refuses a phantom job change while you are still locked from the buff you just
    /// cast, and it answers <em>every</em> refusal with a red "unable to change phantom jobs at
    /// this time" line. Retrying straight off the framework update, as this first did, meant one
    /// call and one error line per frame — a wall of red at the end of every otherwise successful
    /// cycle. Once a second clears the same lock in a handful of lines.
    /// </para>
    /// </summary>
    internal const float RestoreRetrySeconds = 1f;

    private readonly IPhantomBuffWorld _world;
    private readonly Action<string>? _log;

    private readonly List<BuffPlanEntry> _plan = new();
    private int _index;
    private int _castCount;

    /// <summary>
    /// Inquiring Mind standing in for the entire plan: one job switch, one cast, everything the
    /// character qualifies for. Null when the four-job tour is still the only way — see
    /// <see cref="PhantomBuffPolicy.CanUseInquiringMind"/>.
    /// </summary>
    private PhantomBuff? _oneShot;

    private float _stepElapsed;
    private float _cycleElapsed;

    /// <summary>Point on <see cref="_stepElapsed"/> at which the next restore attempt is due.</summary>
    private float _nextRestoreAttempt;

    /// <summary>Injectable so tests can drive time directly.</summary>
    internal Func<float> DeltaSeconds = () => 1f / 60f;

    public BuffCycleState State { get; private set; } = BuffCycleState.Idle;

    /// <summary>The job the user was on when they pressed the button. Restored at the end.</summary>
    public PhantomJob StartingJob { get; private set; } = PhantomJob.None;

    /// <summary>Live one-line status for the button/window.</summary>
    public string Status { get; private set; } = string.Empty;

    /// <summary>Result of the last completed cycle, e.g. "Buffed 2 of 4 · Dancer not unlocked".</summary>
    public string LastOutcome { get; private set; } = string.Empty;

    public bool IsRunning => State != BuffCycleState.Idle && State != BuffCycleState.Faulted;

    public PhantomBuffCycleService(IPhantomBuffWorld world, Action<string>? log = null)
    {
        _world = world;
        _log = log;
    }

    /// <summary>
    /// Why the cycle cannot start right now, or empty when it can. The button shows this instead
    /// of greying out mutely — an unexplained disabled control is what gets reported as broken.
    /// </summary>
    public string BlockedReason()
    {
        if (IsRunning)
            return "Already running";
        if (!_world.InOccultZone)
            return "Not in the Occult Crescent";
        if (_world.InCombat)
            return "In combat";
        return string.Empty;
    }

    /// <summary>
    /// Start a cycle. <paramref name="skipFresh"/> leaves buffs alone that are still comfortably
    /// up — off for a manual press (do what I asked), on for an automatic refresh (top up only
    /// what is running out).
    /// </summary>
    public bool Start(
        Func<PhantomBuff, bool> isEnabled,
        bool skipFresh = false,
        float freshThresholdSeconds = 600f)
    {
        if (BlockedReason().Length > 0)
            return false;

        _plan.Clear();
        _plan.AddRange(PhantomBuffPolicy.Plan(
            _world.JobLevels, isEnabled, Remaining, skipFresh, freshThresholdSeconds));

        // Capture BEFORE anything moves. Everything after this point is obliged to put it back.
        StartingJob = _world.ActiveJob;
        _index = -1;
        _castCount = 0;
        _cycleElapsed = 0f;
        LastOutcome = string.Empty;

        // A Lv15 Freelancer at a crystal gets the lot from one button, so tour nothing. The plan
        // is still built and still reported — it is what says WHICH buffs this cast will grant
        // and which jobs are too low to contribute.
        _oneShot = PhantomBuffPolicy.CanUseInquiringMind(_world.JobLevels, _world.NearKnowledgeCrystal)
            ? PhantomBuffPolicy.InquiringMindStandIn(_plan)
            : null;

        if (!AdvanceToNextCastable())
        {
            // Nothing to do — do not switch jobs just to switch back.
            State = BuffCycleState.Idle;
            LastOutcome = PhantomBuffPolicy.DescribeOutcome(_plan, 0);
            Status = LastOutcome;
            return false;
        }

        return true;

        float Remaining(PhantomBuff b) => _world.StatusRemaining(b.StatusId);
    }

    /// <summary>
    /// Would a press collect the whole set with a single Inquiring Mind rather than touring the
    /// jobs? For the button's subtitle, which otherwise promises a job cycle that will not happen.
    /// </summary>
    public bool WouldUseInquiringMind
    {
        get
        {
            try { return PhantomBuffPolicy.CanUseInquiringMind(_world.JobLevels, _world.NearKnowledgeCrystal); }
            catch { return false; }
        }
    }

    /// <summary>Seconds left on a buff's status (0 = absent). For the window's timer readout.</summary>
    public float RemainingFor(PhantomBuff buff)
    {
        try { return _world.StatusRemaining(buff.StatusId); }
        catch { return 0f; }
    }

    /// <summary>Abandon the cycle and put the starting job back.</summary>
    public void Stop(string reason)
    {
        if (!IsRunning)
            return;

        _log?.Invoke($"Buff cycle stopped: {reason}");
        LastOutcome = $"{PhantomBuffPolicy.DescribeOutcome(_plan, _castCount)} — {reason}";
        BeginRestore();
    }

    /// <summary>Drive the machine. Call once per framework update; never throws.</summary>
    public void Tick()
    {
        if (State == BuffCycleState.Idle || State == BuffCycleState.Faulted)
            return;

        try
        {
            var dt = DeltaSeconds();
            _stepElapsed += dt;
            _cycleElapsed += dt;

            // Combat mid-cycle abandons it: a character stuck on the wrong phantom job in a fight
            // is far worse than a missing buff. Restore, then get out of the way.
            if (_world.InCombat && State != BuffCycleState.RestoringJob)
            {
                Stop("combat started");
                return;
            }

            if (_cycleElapsed > CycleTimeoutSeconds && State != BuffCycleState.RestoringJob)
            {
                Stop("cycle timed out");
                return;
            }

            switch (State)
            {
                case BuffCycleState.SwitchingJob: TickSwitchingJob(); break;
                case BuffCycleState.AwaitingJob: TickAwaitingJob(); break;
                case BuffCycleState.Casting: TickCasting(); break;
                case BuffCycleState.AwaitingBuff: TickAwaitingBuff(); break;
                case BuffCycleState.RestoringJob: TickRestoringJob(); break;
            }
        }
        catch (Exception ex)
        {
            _log?.Invoke($"Buff cycle faulted: {ex.Message}");
            Status = $"Faulted — {ex.Message}";
            // Best effort: still try to hand the job back before giving up.
            TryRestoreImmediate();
            State = BuffCycleState.Faulted;
        }
    }

    private PhantomBuff Current => _oneShot ?? _plan[_index].Buff;

    private void TickSwitchingJob()
    {
        if (_world.ActiveJob == Current.Job)
        {
            EnterState(BuffCycleState.Casting, $"Casting {Current.ActionName}");
            return;
        }

        if (!_world.ChangeSupportJob(Current.Job))
        {
            _log?.Invoke($"Buff cycle: the game refused the switch to {Current.Job}");
            SkipCurrent();
            return;
        }

        EnterState(BuffCycleState.AwaitingJob, $"Switching to {Current.Job}{Progress()}");
    }

    private void TickAwaitingJob()
    {
        // Wait for the job to actually be equipped rather than assuming the call took effect —
        // the switch is asynchronous and casting into the old job silently does nothing.
        if (_world.ActiveJob == Current.Job)
        {
            EnterState(BuffCycleState.Casting, $"Casting {Current.ActionName}{Progress()}");
            return;
        }

        if (_stepElapsed > StepTimeoutSeconds)
        {
            _log?.Invoke($"Buff cycle: timed out switching to {Current.Job}");
            SkipCurrent();
        }
    }

    private void TickCasting()
    {
        // One call answers level, learned, cooldown and — the one that actually bites — whether
        // the action is slotted on this job's duty bar.
        if (!_world.CanCast(Current.ActionId))
        {
            if (_stepElapsed > StepTimeoutSeconds)
            {
                _log?.Invoke($"Buff cycle: {Current.ActionName} never became castable "
                    + "(is it on the duty bar for this job?)");
                SkipCurrent();
            }

            return;
        }

        if (_world.Cast(Current.ActionId, Current.ActionName))
            EnterState(BuffCycleState.AwaitingBuff, $"Applying {Current.ActionName}{Progress()}");
        else if (_stepElapsed > StepTimeoutSeconds)
            SkipCurrent();
    }

    private void TickAwaitingBuff()
    {
        // Verify a FRESH application, not merely that the status exists — the buff already on you
        // from twenty minutes ago would otherwise make a failed cast look like a success.
        if (_world.StatusRemaining(Current.StatusId) >= PhantomBuffs.FreshApplicationSeconds)
        {
            // One Inquiring Mind grants every buff the character qualifies for, so the outcome
            // line counts them all rather than reporting "Buffed 1 of 3" for a complete set.
            _castCount += _oneShot is null ? 1 : CastableCount();
            AdvanceOrFinish();
            return;
        }

        if (_stepElapsed > StepTimeoutSeconds)
        {
            _log?.Invoke($"Buff cycle: {Current.ActionName} cast but never landed");
            SkipCurrent();
        }
    }

    private void TickRestoringJob()
    {
        if (StartingJob == PhantomJob.None || _world.ActiveJob == StartingJob)
        {
            State = BuffCycleState.Idle;
            Status = LastOutcome;
            return;
        }

        if (_stepElapsed > StepTimeoutSeconds)
        {
            // Loud, because the user is now on a job they did not choose.
            _log?.Invoke($"Buff cycle: FAILED to restore {StartingJob} — you are still on {_world.ActiveJob}");
            Status = $"Could not switch back to {StartingJob}";
            State = BuffCycleState.Faulted;
            return;
        }

        // Throttled: see RestoreRetrySeconds. The first attempt still goes out immediately, since
        // a cycle whose last buff was skipped has no lock to wait out.
        if (_stepElapsed < _nextRestoreAttempt)
            return;

        _nextRestoreAttempt = _stepElapsed + RestoreRetrySeconds;
        _world.ChangeSupportJob(StartingJob);
    }

    private void SkipCurrent()
    {
        AdvanceOrFinish();
    }

    /// <summary>How many buffs this cycle set out to collect.</summary>
    private int CastableCount()
    {
        var count = 0;
        foreach (var entry in _plan)
        {
            if (entry.WillCast)
                count++;
        }

        return count;
    }

    private void AdvanceOrFinish()
    {
        if (AdvanceToNextCastable())
            return;

        LastOutcome = PhantomBuffPolicy.DescribeOutcome(_plan, _castCount);
        BeginRestore();
    }

    /// <summary>Moves to the next buff worth casting; false when the plan is exhausted.</summary>
    private bool AdvanceToNextCastable()
    {
        // The one-shot is the whole cycle: one step, then done however it went.
        if (_oneShot is { } oneShot)
        {
            if (_index >= 0)
            {
                _index = _plan.Count;
                return false;
            }

            _index = 0;
            EnterState(BuffCycleState.SwitchingJob, $"Switching to {oneShot.Job}");
            return true;
        }

        for (var i = _index + 1; i < _plan.Count; i++)
        {
            if (!_plan[i].WillCast)
                continue;

            _index = i;
            EnterState(BuffCycleState.SwitchingJob, $"Switching to {_plan[i].Buff.Job}{Progress()}");
            return true;
        }

        _index = _plan.Count;
        return false;
    }

    private void BeginRestore()
    {
        if (StartingJob == PhantomJob.None || _world.ActiveJob == StartingJob)
        {
            State = BuffCycleState.Idle;
            Status = LastOutcome;
            return;
        }

        _nextRestoreAttempt = 0f;
        EnterState(BuffCycleState.RestoringJob, $"Switching back to {StartingJob}");
    }

    /// <summary>Last-ditch synchronous restore for the fault path.</summary>
    private void TryRestoreImmediate()
    {
        try
        {
            if (StartingJob != PhantomJob.None && _world.ActiveJob != StartingJob)
                _world.ChangeSupportJob(StartingJob);
        }
        catch
        {
            // Already faulting; nothing further to try.
        }
    }

    private void EnterState(BuffCycleState state, string status)
    {
        State = state;
        Status = status;
        _stepElapsed = 0f;
    }

    /// <summary>" (2/4)" — the cycle takes up to a minute, so silence reads as a hang.</summary>
    private string Progress()
    {
        // One cast, one step. "(1/3)" would promise two more that are never coming.
        if (_oneShot is not null)
            return string.Empty;

        var total = 0;
        var done = 0;
        for (var i = 0; i < _plan.Count; i++)
        {
            if (!_plan[i].WillCast)
                continue;
            total++;
            if (i < _index)
                done++;
        }

        return total > 1 ? $" ({done + 1}/{total})" : string.Empty;
    }
}
