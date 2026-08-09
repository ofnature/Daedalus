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

    private readonly IPhantomBuffWorld _world;
    private readonly Action<string>? _log;

    private readonly List<BuffPlanEntry> _plan = new();
    private int _index;
    private int _castCount;

    private float _stepElapsed;
    private float _cycleElapsed;

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

    private PhantomBuff Current => _plan[_index].Buff;

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
            _castCount++;
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

        _world.ChangeSupportJob(StartingJob);
    }

    private void SkipCurrent()
    {
        AdvanceOrFinish();
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
