using Daedalus.Ipc;
using Xunit;

namespace Daedalus.Tests.Ipc;

public class AutomationBridgeTests
{
    [Fact]
    public void Observe_IdleFromStart_NoAction()
    {
        var tracker = new AutomationOverrideTracker();
        Assert.Equal(AutomationOverrideTracker.OverrideAction.None, tracker.Observe(false));
        Assert.Equal(AutomationOverrideTracker.OverrideAction.None, tracker.Observe(false));
    }

    [Fact]
    public void Observe_Busy_AssertsEveryPoll()
    {
        // Level-triggered on: if something else (Questionable fight-end Off, dummy guard)
        // cleared the override mid-task, the next poll must re-assert it.
        var tracker = new AutomationOverrideTracker();
        Assert.Equal(AutomationOverrideTracker.OverrideAction.Assert, tracker.Observe(true));
        Assert.Equal(AutomationOverrideTracker.OverrideAction.Assert, tracker.Observe(true));
        Assert.Equal(AutomationOverrideTracker.OverrideAction.Assert, tracker.Observe(true));
    }

    [Fact]
    public void Observe_BusyToIdle_ClearsExactlyOnce()
    {
        // Edge-triggered off: clear once when the task ends, then never again — an idle
        // Henchman must not stomp an override that Questionable set for its own fight.
        var tracker = new AutomationOverrideTracker();
        tracker.Observe(true);
        Assert.Equal(AutomationOverrideTracker.OverrideAction.Clear, tracker.Observe(false));
        Assert.Equal(AutomationOverrideTracker.OverrideAction.None, tracker.Observe(false));
        Assert.Equal(AutomationOverrideTracker.OverrideAction.None, tracker.Observe(false));
    }

    [Fact]
    public void Observe_BusyIdleBusy_ReassertsAfterRestart()
    {
        var tracker = new AutomationOverrideTracker();
        Assert.Equal(AutomationOverrideTracker.OverrideAction.Assert, tracker.Observe(true));
        Assert.Equal(AutomationOverrideTracker.OverrideAction.Clear, tracker.Observe(false));
        Assert.Equal(AutomationOverrideTracker.OverrideAction.Assert, tracker.Observe(true));
        Assert.Equal(AutomationOverrideTracker.OverrideAction.Clear, tracker.Observe(false));
    }
}

/// <summary>
/// Pull cadence gate (2026-07-20): quest/hunt kill loops pull ONE fresh objective mob at a time —
/// clear everything aggroed, then a brief calm pause before the next fresh pull. Aggroed mobs are
/// never delayed.
/// </summary>
public class FreshPullSettleGateTests
{
    [Fact]
    public void FreshPull_AllowedImmediately_WhenNeverInCombat()
    {
        // Bridge just started, toon walked up to the camp out of combat — first pull is instant.
        var gate = new QuestionableIpc.FreshPullSettleGate();
        Assert.True(gate.CanFreshPull());
    }

    [Fact]
    public void FreshPull_Blocked_UntilSettleWindowElapses()
    {
        var now = new DateTime(2026, 7, 20, 12, 0, 0, DateTimeKind.Utc);
        var gate = new QuestionableIpc.FreshPullSettleGate { UtcNow = () => now };

        gate.ReportCombatContact(); // last mob of the pull just died

        now = now.AddSeconds(1);
        Assert.False(gate.CanFreshPull()); // next poll: still settling

        now = now.AddSeconds(1.5);
        Assert.False(gate.CanFreshPull()); // 2.5s: still settling

        now = now.AddSeconds(0.6);
        Assert.True(gate.CanFreshPull()); // 3.1s of calm: next deliberate pull allowed
    }

    [Fact]
    public void FreshPull_WindowRestarts_OnNewCombatContact()
    {
        // A wanderer aggroed during the pause: finishing it resets the calm clock, so the next
        // FRESH pull waits for a full quiet window again.
        var now = new DateTime(2026, 7, 20, 12, 0, 0, DateTimeKind.Utc);
        var gate = new QuestionableIpc.FreshPullSettleGate { UtcNow = () => now };

        gate.ReportCombatContact();
        now = now.AddSeconds(2);
        gate.ReportCombatContact();
        now = now.AddSeconds(2);
        Assert.False(gate.CanFreshPull());
        now = now.AddSeconds(1.1);
        Assert.True(gate.CanFreshPull());
    }
}
