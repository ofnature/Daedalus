using System;
using Dalamud.Plugin.Services;
using Moq;
using Daedalus.Config;
using Daedalus.Ipc;
using Daedalus.Services.Party;
using Xunit;

namespace Daedalus.Tests.Services.Party;

/// <summary>
/// Occult Missile is a 35% coin flip for 75% of the target's CURRENT HP on a 30s recast, and a
/// fleet of Phantom Blue Mages shares one target and one frame — so without coordination all four
/// spend that recast on the same mob at once and three of them buy nothing.
/// <para>
/// The reservation is deliberately narrow: it holds off the OTHER toons on the SAME action and
/// the SAME enemy, briefly. It is not an ownership claim.
/// </para>
/// </summary>
public class PhantomActionCoordinationTests
{
    private const uint OccultMissile = 49086;
    private const uint AquaBreath = 49087;
    private const uint Goblin = 0x4001;
    private const uint OtherGoblin = 0x4002;

    private static PartyCoordinationService CreateService(
        bool enableCoordination = true, bool enablePhantom = true, int expiryMs = 3000)
    {
        var config = new PartyCoordinationConfig
        {
            EnablePartyCoordination = enableCoordination,
            EnablePhantomActionCoordination = enablePhantom,
            PhantomActionReservationExpiryMs = expiryMs,
        };
        return new PartyCoordinationService(config, new Mock<IPluginLog>().Object, null);
    }

    private static void RemoteFires(PartyCoordinationService service, uint entityId, uint actionId)
        => service.HandleRemotePhantomActionIntent(
            new PhantomActionIntentMessage(Guid.NewGuid(), entityId, actionId));

    [Fact]
    public void NothingReserved_FiresFreely()
    {
        var service = CreateService();
        Assert.False(service.IsPhantomActionReservedByOther(Goblin, OccultMissile));
    }

    [Fact]
    public void AnotherToonJustFired_HoldsThisOne()
    {
        var service = CreateService();
        RemoteFires(service, Goblin, OccultMissile);
        Assert.True(service.IsPhantomActionReservedByOther(Goblin, OccultMissile));
    }

    /// <summary>The next mob over is a different problem — the hold must not spread.</summary>
    [Fact]
    public void OtherEnemy_IsUnaffected()
    {
        var service = CreateService();
        RemoteFires(service, Goblin, OccultMissile);
        Assert.False(service.IsPhantomActionReservedByOther(OtherGoblin, OccultMissile));
    }

    /// <summary>
    /// Keyed by action as well as target on purpose: someone else's Missile is no reason to
    /// stop this toon casting Aqua Breath at the same mob.
    /// </summary>
    [Fact]
    public void OtherAction_OnTheSameEnemy_IsUnaffected()
    {
        var service = CreateService();
        RemoteFires(service, Goblin, OccultMissile);
        Assert.False(service.IsPhantomActionReservedByOther(Goblin, AquaBreath));
    }

    /// <summary>A toon must never be blocked by the echo of its own broadcast.</summary>
    [Fact]
    public void OwnBroadcast_DoesNotBlockItself()
    {
        var service = CreateService();
        PhantomActionIntentMessage? sent = null;
        service.OnPhantomActionIntentReady += m => sent = m;

        Assert.True(service.ReservePhantomAction(Goblin, OccultMissile));
        Assert.NotNull(sent);

        service.HandleRemotePhantomActionIntent(sent!);
        Assert.False(service.IsPhantomActionReservedByOther(Goblin, OccultMissile));
    }

    [Fact]
    public void Reserving_TellsTheFleetOnce()
    {
        var service = CreateService();
        var sends = 0;
        service.OnPhantomActionIntentReady += _ => sends++;

        service.ReservePhantomAction(Goblin, OccultMissile);

        Assert.Equal(1, sends);
    }

    [Fact]
    public void ReserveFails_WhenAnotherToonHoldsIt()
    {
        var service = CreateService();
        RemoteFires(service, Goblin, OccultMissile);
        Assert.False(service.ReservePhantomAction(Goblin, OccultMissile));
    }

    /// <summary>
    /// The hold length is the configured one. It is short deliberately — Missile misses about
    /// two thirds of the time, and a long hold would waste the mechanic rather than spread it.
    /// </summary>
    [Fact]
    public void HoldLength_ComesFromConfig()
    {
        var service = CreateService(expiryMs: 2000);
        RemoteFires(service, Goblin, OccultMissile);

        var reservation = service.GetRemotePhantomActionReservations()[(Goblin, OccultMissile)];
        Assert.Equal(2000, (reservation.ExpiresAt - reservation.ReservedAt).TotalMilliseconds, 1);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void Disabled_NeverHolds(bool coordination, bool phantom)
    {
        var service = CreateService(enableCoordination: coordination, enablePhantom: phantom);
        RemoteFires(service, Goblin, OccultMissile);

        Assert.False(service.IsPhantomActionReservedByOther(Goblin, OccultMissile));
        Assert.True(service.ReservePhantomAction(Goblin, OccultMissile));
    }

    [Fact]
    public void Clear_DropsRemoteHolds()
    {
        var service = CreateService();
        RemoteFires(service, Goblin, OccultMissile);

        service.Clear();

        Assert.False(service.IsPhantomActionReservedByOther(Goblin, OccultMissile));
        Assert.Empty(service.GetRemotePhantomActionReservations());
    }
}
