using System.Linq;
using Daedalus.Services.Debug;
using Xunit;

namespace Daedalus.Tests.Services.Debug;

/// <summary>
/// Tests the curated debug-log ring buffer + coalescing. File output is skipped (no config directory),
/// so these cover the in-memory behaviour that drives the Debug Log tab.
/// </summary>
public class DebugLogServiceTests
{
    private static DebugLogService Create() => new(configuration: null, logDirectory: null, log: null);

    [Fact]
    public void Log_AddsEntry()
    {
        var svc = Create();
        svc.Log(DebugLogCategory.Action, DebugLogSeverity.Warning, "Unable to cast Fast Blade — not facing target");

        var snap = svc.GetSnapshot();
        Assert.Single(snap);
        Assert.Equal("Unable to cast Fast Blade — not facing target", snap[0].Message);
        Assert.Equal(1, snap[0].Count);
        Assert.Equal(DebugLogCategory.Action, snap[0].Category);
    }

    [Fact]
    public void Log_IdenticalWithinWindow_CoalescesWithCount()
    {
        var svc = Create();
        for (var i = 0; i < 5; i++)
            svc.Log(DebugLogCategory.Action, DebugLogSeverity.Warning, "Unable to cast Gyofu — facing");

        var snap = svc.GetSnapshot();
        Assert.Single(snap);
        Assert.Equal(5, snap[0].Count);
    }

    [Fact]
    public void Log_DifferentMessages_AreSeparateEntries()
    {
        var svc = Create();
        svc.Log(DebugLogCategory.Action, DebugLogSeverity.Warning, "Unable to cast A");
        svc.Log(DebugLogCategory.Action, DebugLogSeverity.Warning, "Unable to cast B");

        Assert.Equal(2, svc.GetSnapshot().Count);
    }

    [Fact]
    public void Log_SameMessageDifferentCategory_AreSeparateEntries()
    {
        var svc = Create();
        svc.Log(DebugLogCategory.Action, DebugLogSeverity.Warning, "same text");
        svc.Log(DebugLogCategory.Nav, DebugLogSeverity.Warning, "same text");

        Assert.Equal(2, svc.GetSnapshot().Count);
    }

    [Fact]
    public void Log_EmptyMessage_Ignored()
    {
        var svc = Create();
        svc.Log(DebugLogCategory.General, DebugLogSeverity.Info, "");
        Assert.Empty(svc.GetSnapshot());
    }

    [Fact]
    public void Snapshot_IsNewestFirst()
    {
        var svc = Create();
        svc.Log(DebugLogCategory.Action, DebugLogSeverity.Warning, "first");
        svc.Log(DebugLogCategory.Action, DebugLogSeverity.Warning, "second");

        var snap = svc.GetSnapshot();
        Assert.Equal("second", snap[0].Message);
        Assert.Equal("first", snap[1].Message);
    }

    [Fact]
    public void Clear_RemovesAllEntries()
    {
        var svc = Create();
        svc.Log(DebugLogCategory.Action, DebugLogSeverity.Warning, "x");
        svc.Clear();
        Assert.Empty(svc.GetSnapshot());
    }

    [Fact]
    public void RingBuffer_CapsAtMaxEntries()
    {
        var svc = Create();
        // 350 unique messages — none coalesce; buffer caps at 300 keeping the most recent.
        for (var i = 0; i < 350; i++)
            svc.Log(DebugLogCategory.Action, DebugLogSeverity.Warning, $"msg {i}");

        var snap = svc.GetSnapshot();
        Assert.Equal(300, snap.Count);
        Assert.Equal("msg 349", snap[0].Message);            // newest retained
        Assert.DoesNotContain(snap, e => e.Message == "msg 0"); // oldest evicted
    }
}
