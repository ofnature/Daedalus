using System.Collections.Generic;
using Daedalus.Data;
using Daedalus.Rotation.Phantom;
using Xunit;

namespace Daedalus.Tests.Rotation.Phantom;

/// <summary>
/// Tests for the Phase 5 Oracle prediction deck tracker and the affordable-shard
/// banner helper (docs/occult-phantom-plan.md).
/// </summary>
public class OracleDeckTrackerTests
{
    [Fact]
    public void PredictDispatch_OpensAFreshFourCardDeck()
    {
        var deck = new OracleDeckTracker();

        deck.OnPredictDispatched();

        Assert.Equal(4, deck.RemainingCount);
        Assert.False(deck.IsLastCard(41640));
    }

    [Fact]
    public void CardRotation_DiscardsThePreviousCard()
    {
        var deck = new OracleDeckTracker();
        deck.OnPredictDispatched();

        deck.Update(41637);          // Judgment offered
        deck.Update(41638);          // rotated to Cleansing → Judgment discarded
        Assert.Equal(3, deck.RemainingCount);

        deck.Update(41639);          // rotated → Cleansing discarded
        deck.Update(41640);          // rotated → Blessing discarded
        Assert.Equal(1, deck.RemainingCount);
        Assert.True(deck.IsLastCard(41640));
    }

    [Fact]
    public void HoldingTheSameCard_DoesNotDiscardIt()
    {
        var deck = new OracleDeckTracker();
        deck.OnPredictDispatched();

        deck.Update(41640);
        deck.Update(41640);
        deck.Update(41640);

        Assert.Equal(4, deck.RemainingCount);
        Assert.False(deck.IsLastCard(41640));
    }

    [Fact]
    public void CardExpiring_ToNoCard_ThenNewWindow_ResetsCleanly()
    {
        var deck = new OracleDeckTracker();
        deck.OnPredictDispatched();
        deck.Update(41637);
        deck.Update(0);              // window ended with Judgment unplayed → discarded

        Assert.Equal(3, deck.RemainingCount);

        deck.OnPredictDispatched();  // new Predict → fresh deck
        Assert.Equal(4, deck.RemainingCount);
    }

    // ── Affordable-shard banner helper ──

    private static Dictionary<PhantomJob, byte> Levels(params (PhantomJob Job, byte Level)[] entries)
    {
        var result = new Dictionary<PhantomJob, byte>();
        foreach (var entry in PhantomJobData.LevelStatuses)
            result[entry.Key] = 1;
        foreach (var (job, level) in entries)
            result[job] = level;
        return result;
    }

    [Fact]
    public void AffordableShards_ListsOnlyLockedPurchasableJobsWithinBudget()
    {
        // Locked: Thief (gold 1600), Dancer (silver 1000), Berserker (CE drop — never listed).
        var levels = Levels((PhantomJob.Thief, 0), (PhantomJob.Dancer, 0), (PhantomJob.Berserker, 0));

        var rich = PhantomJobData.GetAffordableLockedShards(levels, silver: 1200, gold: 2000, PhantomJobData.SouthHornTerritoryId);
        Assert.Contains(rich, e => e.Job == PhantomJob.Thief && e.Price == 1600);
        Assert.Contains(rich, e => e.Job == PhantomJob.Dancer && e.Price == 1000);
        Assert.DoesNotContain(rich, e => e.Job == PhantomJob.Berserker);

        var broke = PhantomJobData.GetAffordableLockedShards(levels, silver: 900, gold: 100, PhantomJobData.SouthHornTerritoryId);
        Assert.Empty(broke);
    }

    [Fact]
    public void AffordableShards_AreScopedToTheZonesOwnExchange()
    {
        // Field 2026-07-31: North Horn sells NIN/BLM/WHM/RDM at 1,000 Silver OBOLS and
        // DRG/SMN at 1,600 Gold. The balances are that zone's currency, so a South Horn
        // shard must never be offered against an Obol purse (or vice versa).
        var levels = Levels(
            (PhantomJob.Dancer, 0),        // South Horn, 1,000 silver pieces
            (PhantomJob.PhantomNinja, 0)); // North Horn, 1,000 silver obols

        var north = PhantomJobData.GetAffordableLockedShards(
            levels, silver: 5000, gold: 5000, PhantomJobData.NorthHornTerritoryId);
        Assert.Contains(north, e => e.Job == PhantomJob.PhantomNinja && e.Price == 1000);
        Assert.DoesNotContain(north, e => e.Job == PhantomJob.Dancer);

        var south = PhantomJobData.GetAffordableLockedShards(
            levels, silver: 5000, gold: 5000, PhantomJobData.SouthHornTerritoryId);
        Assert.Contains(south, e => e.Job == PhantomJob.Dancer);
        Assert.DoesNotContain(south, e => e.Job == PhantomJob.PhantomNinja);
    }

    [Fact]
    public void AffordableShards_NecromancerIsADropNotAPurchase()
    {
        var levels = Levels((PhantomJob.Necromancer, 0));

        var result = PhantomJobData.GetAffordableLockedShards(
            levels, silver: 9999, gold: 9999, PhantomJobData.NorthHornTerritoryId);

        Assert.Empty(result); // Dark Artistry CE drop — no price, never bannered
    }

    [Fact]
    public void AffordableShards_UnlockedJobsNeverListed()
    {
        var levels = Levels(); // everything unlocked at Lv.1

        var result = PhantomJobData.GetAffordableLockedShards(levels, silver: 9999, gold: 9999, PhantomJobData.SouthHornTerritoryId);

        Assert.Empty(result);
    }

    [Fact]
    public void UnlockCosts_MatchVendorPrices()
    {
        Assert.Equal((PhantomJobData.UnlockKind.SilverShard, 1000), PhantomJobData.GetUnlockCost(PhantomJob.Dancer));
        Assert.Equal((PhantomJobData.UnlockKind.GoldShard, 1600), PhantomJobData.GetUnlockCost(PhantomJob.Gladiator));
        Assert.Equal((PhantomJobData.UnlockKind.CriticalEncounter, 0), PhantomJobData.GetUnlockCost(PhantomJob.Oracle));
        Assert.Equal((PhantomJobData.UnlockKind.Quest, 0), PhantomJobData.GetUnlockCost(PhantomJob.Knight));
        Assert.Equal((PhantomJobData.UnlockKind.Default, 0), PhantomJobData.GetUnlockCost(PhantomJob.Freelancer));
    }
}
