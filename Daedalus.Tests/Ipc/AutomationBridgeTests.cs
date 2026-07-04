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
