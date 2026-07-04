using Daedalus.Ipc;
using Xunit;

namespace Daedalus.Tests.Ipc;

public class HenchmanIpcTests
{
    [Fact]
    public void Observe_IdleFromStart_NoAction()
    {
        var tracker = new HenchmanOverrideTracker();
        Assert.Equal(HenchmanOverrideTracker.OverrideAction.None, tracker.Observe(false));
        Assert.Equal(HenchmanOverrideTracker.OverrideAction.None, tracker.Observe(false));
    }

    [Fact]
    public void Observe_Busy_AssertsEveryPoll()
    {
        // Level-triggered on: if something else (Questionable fight-end Off, dummy guard)
        // cleared the override mid-task, the next poll must re-assert it.
        var tracker = new HenchmanOverrideTracker();
        Assert.Equal(HenchmanOverrideTracker.OverrideAction.Assert, tracker.Observe(true));
        Assert.Equal(HenchmanOverrideTracker.OverrideAction.Assert, tracker.Observe(true));
        Assert.Equal(HenchmanOverrideTracker.OverrideAction.Assert, tracker.Observe(true));
    }

    [Fact]
    public void Observe_BusyToIdle_ClearsExactlyOnce()
    {
        // Edge-triggered off: clear once when the task ends, then never again — an idle
        // Henchman must not stomp an override that Questionable set for its own fight.
        var tracker = new HenchmanOverrideTracker();
        tracker.Observe(true);
        Assert.Equal(HenchmanOverrideTracker.OverrideAction.Clear, tracker.Observe(false));
        Assert.Equal(HenchmanOverrideTracker.OverrideAction.None, tracker.Observe(false));
        Assert.Equal(HenchmanOverrideTracker.OverrideAction.None, tracker.Observe(false));
    }

    [Fact]
    public void Observe_BusyIdleBusy_ReassertsAfterRestart()
    {
        var tracker = new HenchmanOverrideTracker();
        Assert.Equal(HenchmanOverrideTracker.OverrideAction.Assert, tracker.Observe(true));
        Assert.Equal(HenchmanOverrideTracker.OverrideAction.Clear, tracker.Observe(false));
        Assert.Equal(HenchmanOverrideTracker.OverrideAction.Assert, tracker.Observe(true));
        Assert.Equal(HenchmanOverrideTracker.OverrideAction.Clear, tracker.Observe(false));
    }
}
