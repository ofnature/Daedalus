using System.Collections.Generic;
using System.Linq;
using Daedalus.Data;
using Daedalus.Services.Consumables;
using Xunit;

namespace Daedalus.Tests.Services.Consumables;

/// <summary>
/// Cascading ether selection. Sheet values verified 2026-08-10 against XIVAPI Item rows
/// 4555/4556/4557/4558/13638/23168 (ItemAction Data + DataHQ) — these tests pin them so a
/// future edit can't quietly invent potencies.
/// </summary>
public sealed class EtherPolicyTests
{
    private static Dictionary<uint, uint> Stock(params (uint ItemId, bool Hq, uint Count)[] held)
    {
        var stock = new Dictionary<uint, uint>();
        foreach (var (itemId, hq, count) in held)
            stock[hq ? itemId + ConsumableIds.HqOffset : itemId] = count;
        return stock;
    }

    [Fact]
    public void Ladder_IsSortedByRealRestore_NotGradeName()
    {
        var order = EtherItems.BestFirst.Select(v => v.Cap).ToList();

        Assert.Equal(order.OrderByDescending(c => c), order);

        // The case that makes grade-order wrong: Max-Ether HQ (1,500) outranks Super-Ether NQ
        // (1,400), so a naive "best grade first" cascade would reach past the stronger item.
        var maxHq = EtherItems.BestFirst.ToList().FindIndex(v => v.ItemId == EtherItems.MaxEther && v.Hq);
        var superNq = EtherItems.BestFirst.ToList().FindIndex(v => v.ItemId == EtherItems.SuperEther && !v.Hq);
        Assert.True(maxHq < superNq, "Max-Ether HQ restores 1500 vs Super-Ether NQ 1400");
    }

    [Fact]
    public void Select_TakesTheStrongestHeld()
    {
        var stock = Stock((EtherItems.Ether, false, 99), (EtherItems.SuperEther, false, 1));

        Assert.True(EtherPolicy.TrySelect(stock, out var choice));
        Assert.Equal(EtherItems.SuperEther, choice.ItemId);
        Assert.False(choice.Hq);
    }

    [Fact]
    public void Select_CascadesDownAsGradesRunOut()
    {
        // Out of the top three grades entirely — the cascade must keep walking down.
        var stock = Stock(
            (EtherItems.SuperEther, false, 0),
            (EtherItems.MaxEther, false, 0),
            (EtherItems.XEther, false, 0),
            (EtherItems.MegaEther, false, 2),
            (EtherItems.Ether, false, 40));

        Assert.True(EtherPolicy.TrySelect(stock, out var choice));
        Assert.Equal(EtherItems.MegaEther, choice.ItemId);
    }

    [Fact]
    public void Select_EqualValue_PrefersNq_ToConserveHq()
    {
        // X-Ether HQ and Max-Ether NQ both restore 1,200 — burn the normal-quality one.
        var stock = Stock((EtherItems.XEther, true, 5), (EtherItems.MaxEther, false, 5));

        Assert.True(EtherPolicy.TrySelect(stock, out var choice));
        Assert.Equal(EtherItems.MaxEther, choice.ItemId);
        Assert.False(choice.Hq);
    }

    [Fact]
    public void Select_EmptyBag_Fails()
    {
        Assert.False(EtherPolicy.TrySelect(new Dictionary<uint, uint>(), out _));
        Assert.False(EtherPolicy.TrySelect(Stock((EtherItems.Ether, false, 0)), out _));
    }

    [Theory]
    [InlineData(10000u, 1400)] // level cap: the percentage term equals the cap exactly
    [InlineData(5000u, 700)]   // smaller pool: 14% of 5,000 lands under the cap
    public void RestoreAt_AppliesBothPercentAndCap(uint maxMp, int expected)
    {
        var superNq = EtherItems.All.Single(v => v.ItemId == EtherItems.SuperEther && !v.Hq);

        Assert.Equal(expected, superNq.RestoreAt(maxMp));
    }

    [Fact]
    public void RunningLow_FiresWhileStockRemains_NotAtZero()
    {
        Assert.False(EtherPolicy.IsRunningLow(Stock((EtherItems.Ether, false, 0))));
        Assert.True(EtherPolicy.IsRunningLow(Stock((EtherItems.Ether, false, (uint)EtherPolicy.RunningLowCount))));
        Assert.False(EtherPolicy.IsRunningLow(Stock((EtherItems.Ether, false, (uint)EtherPolicy.RunningLowCount + 1))));

        // Counts across grades AND qualities, not per-slot.
        var spread = Stock((EtherItems.Ether, false, 2), (EtherItems.SuperEther, true, 2));
        Assert.Equal(4, EtherPolicy.TotalHeld(spread));
        Assert.True(EtherPolicy.IsRunningLow(spread));
    }

    private static EtherSituation Firing() => new(
        Enabled: true,
        Alive: true,
        UsesMp: true,
        CurrentMp: 200,
        MaxMp: 10000,
        MpThreshold: 0.35f,
        Stock: Stock((EtherItems.SuperEther, false, 3)),
        SecondsSinceOwnUse: 60,
        SecondsSinceRefusal: 600,
        IsCasting: false);

    [Fact]
    public void Decide_AtTwoHundredMp_DrinksTheBest()
    {
        var decision = EtherPolicy.Decide(Firing());

        Assert.True(decision.Fire, decision.Reason);
        Assert.Equal(EtherItems.SuperEther, decision.Choice.ItemId);
        Assert.Contains("1,400", decision.Reason);
    }

    [Fact]
    public void Decide_HealthyMp_Holds()
    {
        var s = Firing() with { CurrentMp = 9000 };

        Assert.False(EtherPolicy.Decide(s).Fire);
    }

    [Fact]
    public void Decide_AfterRefusal_BacksOff_ThenResumes()
    {
        // An item-blocked duty must not be hammered once a second for the whole fight.
        Assert.False(EtherPolicy.Decide(Firing() with { SecondsSinceRefusal = 5 }).Fire);
        Assert.True(EtherPolicy.Decide(Firing() with { SecondsSinceRefusal = EtherPolicy.RefusalBackoffSeconds }).Fire);
    }

    [Theory]
    [InlineData("disabled")]
    [InlineData("dead")]
    [InlineData("nomp")]
    [InlineData("recast")]
    [InlineData("casting")]
    [InlineData("empty")]
    public void EachGate_Holds(string gate)
    {
        var s = gate switch
        {
            "disabled" => Firing() with { Enabled = false },
            "dead" => Firing() with { Alive = false },
            "nomp" => Firing() with { UsesMp = false, MaxMp = 0 },
            "recast" => Firing() with { SecondsSinceOwnUse = 1 },
            "casting" => Firing() with { IsCasting = true },
            _ => Firing() with { Stock = new Dictionary<uint, uint>() },
        };

        Assert.False(EtherPolicy.Decide(s).Fire);
    }

    [Fact]
    public void EtherThreshold_SitsUnderLucidDreaming()
    {
        // Lucid (free, 60s) must always be the first answer to low MP; ethers cost real gil.
        var consumables = new Daedalus.Config.ConsumablesConfig();
        var healer = new Daedalus.Config.HealerSharedConfig();

        Assert.True(consumables.EtherMpThreshold < healer.LucidDreamingThreshold);
    }
}
