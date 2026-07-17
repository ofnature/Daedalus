using System;
using System.Linq;
using Daedalus.Services.Blu;
using Xunit;

namespace Daedalus.Tests.Services.Blu;

/// <summary>v3.4 fleet Final Sting: planner math (count, ordering, healer reserve) and the
/// static command's slot/lifetime behavior.</summary>
[Collection("BluStaticState")] // BluFleetStingCommand is static
public class BluFleetStingTests : IDisposable
{
    public BluFleetStingTests() => BluFleetStingCommand.Clear();
    public void Dispose() => BluFleetStingCommand.Clear();

    private static BluPeerCapability Peer(string id, BluCapabilities caps) => new(id, caps);

    private const BluCapabilities Sting = BluCapabilities.FinalSting;

    [Fact]
    public void Plan_CountFromCalculatorMath_OverProvisioned()
    {
        var roster = new[]
        {
            Peer("A@W", Sting), Peer("B@W", Sting), Peer("C@W", Sting),
            Peer("D@W", Sting), Peer("E@W", Sting),
        };

        // 100k boss, 20k est sting, 0.75 safety → ceil(100k / 15k) = 7 → clamped to the 5 capable.
        Assert.Equal(5, BluFleetStingPlanner.Plan(100_000, 20_000f, 0.75f, roster).Count);

        // 30k boss → ceil(30k / 15k) = 2 stingers, SenderId order.
        var two = BluFleetStingPlanner.Plan(30_000, 20_000f, 0.75f, roster);
        Assert.Equal(new[] { "A@W", "B@W" }, two);
    }

    [Fact]
    public void Plan_UncalibratedPlansEveryone_NoCapablePlansNobody()
    {
        var roster = new[] { Peer("A@W", Sting), Peer("B@W", Sting) };
        Assert.Equal(2, BluFleetStingPlanner.Plan(1_000_000, 0f, 0.75f, roster).Count);

        var none = new[] { Peer("A@W", BluCapabilities.MoonFlute) };
        Assert.Empty(BluFleetStingPlanner.Plan(1_000_000, 0f, 0.75f, none));
        Assert.Empty(BluFleetStingPlanner.Plan(0, 20_000f, 0.75f, roster)); // dead boss
    }

    [Fact]
    public void Plan_HealerMimicReserved_HealersStingLast()
    {
        var roster = new[]
        {
            Peer("A@W", Sting | BluCapabilities.HealerRole),
            Peer("B@W", Sting),
            Peer("C@W", Sting | BluCapabilities.HealerRole),
            Peer("D@W", Sting),
        };

        // Even at full commitment, one healer never stings (Angel Whisper for the cleanup).
        var all = BluFleetStingPlanner.Plan(10_000_000, 0f, 0.75f, roster);
        Assert.Equal(3, all.Count);
        Assert.Equal(new[] { "B@W", "D@W", "A@W" }, all); // non-healers first, C reserved

        // A lone capable toon still stings even as a healer (operator judgment beats the reserve).
        var loneHealer = new[] { Peer("A@W", Sting | BluCapabilities.HealerRole) };
        Assert.Single(BluFleetStingPlanner.Plan(10_000_000, 0f, 0.75f, loneHealer));
    }

    [Fact]
    public void Command_SlotTiming_LifetimeAndClear()
    {
        var armed = DateTime.UtcNow;
        BluFleetStingCommand.Arm(4242UL, ["A@W", "B@W", "C@W"], armed);
        Assert.True(BluFleetStingCommand.IsArmed(armed));

        // Pump assigned us slot 2 → fire at +6s.
        BluFleetStingCommand.SetMySlot(2);
        Assert.True(BluFleetStingCommand.TryGetMyOrder(armed, out var target, out var fireAt, out var slot));
        Assert.Equal(4242UL, target);
        Assert.Equal(2, slot);
        Assert.Equal(armed.AddSeconds(6), fireAt);

        // Not a stinger → no order.
        BluFleetStingCommand.SetMySlot(-1);
        Assert.False(BluFleetStingCommand.TryGetMyOrder(armed, out _, out _, out _));

        // Orders die of old age (a stale 90s+ signal must never sting anyone).
        BluFleetStingCommand.SetMySlot(0);
        Assert.False(BluFleetStingCommand.TryGetMyOrder(armed.AddSeconds(120), out _, out _, out _));

        BluFleetStingCommand.Clear();
        Assert.False(BluFleetStingCommand.IsArmed(armed));
    }
}
