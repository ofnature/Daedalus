using System;
using Dalamud.Plugin.Services;
using Moq;
using Daedalus.Config;
using Daedalus.Ipc;
using Daedalus.Services.Party;
using Xunit;

namespace Daedalus.Tests.Services.Party;

/// <summary>
/// Tank-swap coordination state on PartyCoordinationService: confirmations stored apart from requests,
/// the post-swap giver memory (anti-ping-pong), and the manual-swap arm window. Clock is injected so
/// the time-based windows are deterministic.
/// </summary>
public class TankSwapCoordinationTests
{
    private const uint Boss = 0x4000_0002u;

    private static PartyCoordinationService CreateService(Func<DateTime>? clock = null)
    {
        var config = new PartyCoordinationConfig
        {
            EnablePartyCoordination = true,
            EnableTankSwapCoordination = true,
        };
        return new PartyCoordinationService(config, new Mock<IPluginLog>().Object, clock);
    }

    private static TankSwapIntentMessage RemoteMsg(bool take, bool confirm)
        => new(Guid.NewGuid(), Boss, take, confirm, swapPriority: 2);

    [Fact]
    public void Confirmation_StoredApartFromRequest()
    {
        var svc = CreateService();

        // A remote confirmation must NOT masquerade as a pending request...
        svc.HandleRemoteTankSwapIntent(RemoteMsg(take: false, confirm: true));
        Assert.True(svc.HasSwapConfirmation(Boss));
        Assert.Null(svc.GetPendingTankSwapRequest(Boss));
    }

    [Fact]
    public void Request_IsPending_ButNotAConfirmation()
    {
        var svc = CreateService();

        svc.HandleRemoteTankSwapIntent(RemoteMsg(take: true, confirm: false));
        Assert.NotNull(svc.GetPendingTankSwapRequest(Boss));
        Assert.True(svc.GetPendingTankSwapRequest(Boss)!.IntendToTakeAggro);
        Assert.False(svc.HasSwapConfirmation(Boss));
    }

    [Fact]
    public void HasSwapConfirmation_None_False() => Assert.False(CreateService().HasSwapConfirmation(Boss));

    [Fact]
    public void RecordSwapCompleted_Giver_SuppressesTakeBack_ThenExpires()
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var svc = CreateService(() => now);

        svc.RecordSwapCompleted(Boss, tookAggro: false); // we gave aggro away
        Assert.True(svc.WasRecentSwapGiver(Boss));

        now = now.AddSeconds(11); // past the ~10s window
        Assert.False(svc.WasRecentSwapGiver(Boss));
    }

    [Fact]
    public void RecordSwapCompleted_Taker_DoesNotSuppress()
    {
        var svc = CreateService();
        svc.RecordSwapCompleted(Boss, tookAggro: true); // we took aggro — free to hold it
        Assert.False(svc.WasRecentSwapGiver(Boss));
    }

    [Fact]
    public void ManualSwap_ConsumedOnSwapCompletion()
    {
        // The press that requested a swap must not survive it — with manual bypassing the
        // recent-giver hold, a lingering arm would bounce the boss straight back.
        var svc = CreateService();
        svc.ArmManualSwap();
        Assert.True(svc.IsManualSwapArmed());

        svc.RecordSwapCompleted(Boss, tookAggro: true);

        Assert.False(svc.IsManualSwapArmed());
    }

    [Fact]
    public void ManualSwap_Armed_ThenExpires()
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var svc = CreateService(() => now);

        Assert.False(svc.IsManualSwapArmed());
        svc.ArmManualSwap();
        Assert.True(svc.IsManualSwapArmed());

        now = now.AddSeconds(6); // past the ~5s arm window
        Assert.False(svc.IsManualSwapArmed());
    }

    [Fact]
    public void LocalTankSwapRole_DefaultsUndesignated_AndRoundTrips()
    {
        var svc = CreateService();
        Assert.Equal(TankSwapRole.Undesignated, svc.LocalTankSwapRole);
        svc.LocalTankSwapRole = TankSwapRole.DesignatedOffTank;
        Assert.Equal(TankSwapRole.DesignatedOffTank, svc.LocalTankSwapRole);
    }
}
