using System.Collections.Generic;
using Daedalus.Services.Network;
using Daedalus.Services.Party;
using Xunit;

namespace Daedalus.Tests.Services.Party;

/// <summary>The coordination window's FATE sync button (user, 2026-11-17).</summary>
public class LevelSyncServiceTests
{
    [Fact]
    public void NothingIsSentUntilTheGroupCallsForIt()
    {
        var sent = new List<string>();
        var svc = new LevelSyncService(text => { sent.Add(text); return true; });
        svc.Update();
        Assert.Empty(sent);
    }

    [Fact]
    public void ACallSendsTheGamesOwnCommandOnce()
    {
        var sent = new List<string>();
        var svc = new LevelSyncService(text => { sent.Add(text); return true; });
        svc.Request();
        svc.Update();
        svc.Update();
        Assert.Equal(["/levelsync on"], sent);
    }

    /// <summary>"on", never the bare command: bare toggles and would un-sync a toon already synced by hand.</summary>
    [Fact]
    public void TheCommandNeverToggles()
        => Assert.EndsWith(" on", LevelSyncService.Command);

    /// <summary>The LAN frame goes out three times; one press is still one sync.</summary>
    [Fact]
    public void RepeatedCallsBeforeTheFrameAreOneSync()
    {
        var sent = new List<string>();
        var svc = new LevelSyncService(text => { sent.Add(text); return true; });
        svc.Request();
        svc.Request();
        svc.Request();
        svc.Update();
        Assert.Single(sent);
    }

    [Fact]
    public void ACallIsKeptWhileTheGameIsNotReady()
    {
        var ready = false;
        var sent = 0;
        var svc = new LevelSyncService(_ => { if (ready) sent++; return ready; });
        svc.Request();
        svc.Update();
        Assert.Equal(0, sent);
        ready = true;
        svc.Update();
        Assert.Equal(1, sent);
    }

    [Fact]
    public void TheWireValueIsFixed()
        => Assert.Equal(21, (int)LanMessageType.LevelSync);
}
