using Daedalus.Data;
using Daedalus.Services.Consumables;
using Xunit;

namespace Daedalus.Tests.Services.Consumables;

/// <summary>
/// The deep dungeon's regen potion on low HP. Item rows verified 2026-11-24 against XIVAPI (20309/23163/38944/47102):
/// each is usable only in its own dungeon, so the tests pin the dungeon-to-potion table.
/// </summary>
public sealed class DeepDungeonPotionPolicyTests
{
    private const byte PilgrimsTraverse = 4;

    // a wounded character mid-fight in Pilgrim's Traverse with potions to spare: the press goes out
    private static DeepDungeonPotionSituation Wounded() => new(
        Enabled: true,
        DungeonId: PilgrimsTraverse,
        Alive: true,
        InCombat: true,
        CurrentHp: 40_000,
        MaxHp: 100_000,
        HpThreshold: 0.5f,
        Regenerating: false,
        ItemsBlocked: false,
        RaiseImmune: false,
        Held: 12,
        IsCasting: false,
        SecondsSinceAttempt: 600,
        UnconfirmedAttempts: 0);

    [Theory]
    [InlineData(1, DeepDungeonPotions.Sustaining)]
    [InlineData(2, DeepDungeonPotions.Empyrean)]
    [InlineData(3, DeepDungeonPotions.Orthos)]
    [InlineData(4, DeepDungeonPotions.Pilgrims)]
    public void EachDungeon_DrinksItsOwnPotion(byte dungeonId, uint itemId)
    {
        var decision = DeepDungeonPotionPolicy.Decide(Wounded() with { DungeonId = dungeonId });

        Assert.True(decision.Fire);
        Assert.Equal(itemId, decision.Potion.ItemId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public void OutsideAKnownDeepDungeon_Nothing(byte dungeonId)
    {
        var decision = DeepDungeonPotionPolicy.Decide(Wounded() with { DungeonId = dungeonId });

        Assert.False(decision.Fire);
        Assert.False(decision.Wanted);
    }

    [Fact]
    public void Disabled_Nothing()
        => Assert.False(DeepDungeonPotionPolicy.Decide(Wounded() with { Enabled = false }).Fire);

    [Fact]
    public void Dead_Nothing()
        => Assert.False(DeepDungeonPotionPolicy.Decide(Wounded() with { Alive = false }).Fire);

    [Fact]
    public void OutOfCombat_Nothing()
    {
        var decision = DeepDungeonPotionPolicy.Decide(Wounded() with { InCombat = false });

        Assert.False(decision.Fire);
        Assert.False(decision.Wanted);
    }

    [Theory]
    [InlineData(50_000, false)] // at the threshold: not below it
    [InlineData(49_999, true)]
    [InlineData(100_000, false)]
    public void Drinks_OnlyBelowTheThreshold(uint hp, bool fires)
        => Assert.Equal(fires, DeepDungeonPotionPolicy.Decide(Wounded() with { CurrentHp = hp }).Fire);

    [Fact]
    public void RegenAlreadyRunning_IsNotStacked()
    {
        var decision = DeepDungeonPotionPolicy.Decide(Wounded() with { Regenerating = true });

        Assert.False(decision.Fire);
        Assert.False(decision.Wanted);
    }

    [Fact]
    public void ItemPenaltyFloor_HoldsAndSaysSo()
    {
        var decision = DeepDungeonPotionPolicy.Decide(Wounded() with { ItemsBlocked = true });

        Assert.False(decision.Fire);
        Assert.True(decision.Wanted);
        Assert.Contains("Item Penalty", decision.Reason);
    }

    [Fact]
    public void EmptyBag_HoldsAndNamesThePotion()
    {
        var decision = DeepDungeonPotionPolicy.Decide(Wounded() with { Held = 0 });

        Assert.False(decision.Fire);
        Assert.True(decision.Wanted);
        Assert.Contains("Pilgrim's Potion", decision.Reason);
    }

    [Fact]
    public void MidCast_Waits()
    {
        var decision = DeepDungeonPotionPolicy.Decide(Wounded() with { IsCasting = true });

        Assert.False(decision.Fire);
        Assert.Equal(DeepDungeonPotionPolicy.MidCast, decision.Reason);
    }

    [Fact]
    public void JustPressed_WaitsForTheRegenToShow()
    {
        var early = Wounded() with { SecondsSinceAttempt = DeepDungeonPotionPolicy.RetrySeconds - 0.1, UnconfirmedAttempts = 1 };
        var later = early with { SecondsSinceAttempt = DeepDungeonPotionPolicy.RetrySeconds };

        Assert.Equal(DeepDungeonPotionPolicy.JustPressed, DeepDungeonPotionPolicy.Decide(early).Reason);
        Assert.True(DeepDungeonPotionPolicy.Decide(later).Fire);
    }

    [Fact]
    public void PressesThatNeverTake_BackOff_ThenTryAgain()
    {
        var refused = Wounded() with
        {
            UnconfirmedAttempts = DeepDungeonPotionPolicy.AttemptsBeforeBackoff,
            SecondsSinceAttempt = DeepDungeonPotionPolicy.RetrySeconds + 1,
        };

        var held = DeepDungeonPotionPolicy.Decide(refused);
        Assert.False(held.Fire);
        Assert.Contains("backing off", held.Reason);

        Assert.True(DeepDungeonPotionPolicy.Decide(refused with { SecondsSinceAttempt = DeepDungeonPotionPolicy.BackoffSeconds }).Fire);
    }

    [Fact]
    public void RegenStatuses_CoverThePalaceAndTheTraverse()
    {
        Assert.Contains(648u, DeepDungeonPotions.RegenStatusIds);  // Rehabilitation, Palace of the Dead
        Assert.Contains(4191u, DeepDungeonPotions.RegenStatusIds); // Rehabilitation, Pilgrim's Traverse
        Assert.Contains(4587u, DeepDungeonPotions.RegenStatusIds); // Anointed
    }

    // just raised: Transcendent ends on the first thing used, and nothing can hurt the character until then
    [Fact]
    public void JustRaised_ThePotionWaits_SoTheImmunityIsKept()
    {
        var decision = DeepDungeonPotionPolicy.Decide(Wounded() with { RaiseImmune = true });

        Assert.False(decision.Fire);
        Assert.False(decision.Wanted);
    }
}
