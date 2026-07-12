using Daedalus.Rotation.Common.Helpers;
using Xunit;

namespace Daedalus.Tests.Rotation.Common.Helpers;

/// <summary>
/// Auto-tank-swap stack-trigger watchlist: the Status-sheet filter (detrimental + stackable +
/// damage-taken-up description), the per-content override registry, and the fail-open default.
/// Tests share process-wide static state — every test resets it in a finally block.
/// </summary>
public sealed class TankSwapDebuffWatchTests
{
    private const byte Beneficial = 1;
    private const byte Detrimental = 2;

    // ---- the pure sheet filter ----

    [Fact]
    public void VulnerabilityUpShape_Matches()
        => Assert.True(TankSwapDebuffWatch.MatchesDamageTakenDebuff(
            Detrimental, 16, "Damage taken is increased."));

    [Fact]
    public void PhysicalVulnerabilityWording_Matches()
        => Assert.True(TankSwapDebuffWatch.MatchesDamageTakenDebuff(
            Detrimental, 8, "Physical damage taken is increased."));

    [Fact]
    public void BeneficialStatus_NeverMatches()
        => Assert.False(TankSwapDebuffWatch.MatchesDamageTakenDebuff(
            Beneficial, 16, "Damage taken is increased.")); // a buff can't be a buster stack

    [Fact]
    public void NonStackingDebuff_DoesNotMatch()
        => Assert.False(TankSwapDebuffWatch.MatchesDamageTakenDebuff(
            Detrimental, 1, "Damage taken is increased."));

    [Fact]
    public void DetrimentalWithoutDamageTakenWording_DoesNotMatch()
        => Assert.False(TankSwapDebuffWatch.MatchesDamageTakenDebuff(
            Detrimental, 4, "Fire resistance is reduced."));

    [Fact]
    public void NullDescription_DoesNotMatch()
        => Assert.False(TankSwapDebuffWatch.MatchesDamageTakenDebuff(Detrimental, 4, null));

    // ---- watchlist lookup + overrides ----

    [Fact]
    public void Uninitialized_FailsOpen_WatchesEverything()
    {
        TankSwapDebuffWatch.InitializeForTest(defaults: null);
        try
        {
            Assert.True(TankSwapDebuffWatch.IsWatched(999999));
        }
        finally
        {
            TankSwapDebuffWatch.Shutdown();
        }
    }

    [Fact]
    public void Initialized_OnlyDefaultsMatch()
    {
        TankSwapDebuffWatch.InitializeForTest(new uint[] { 714, 1138 });
        try
        {
            Assert.True(TankSwapDebuffWatch.IsWatched(714));
            Assert.False(TankSwapDebuffWatch.IsWatched(4242)); // arbitrary non-watched stack buff
        }
        finally
        {
            TankSwapDebuffWatch.Shutdown();
        }
    }

    [Fact]
    public void GlobalOverride_AddsIdEverywhere()
    {
        TankSwapDebuffWatch.InitializeForTest(new uint[] { 714 });
        try
        {
            TankSwapDebuffWatch.RegisterContentOverride(0, 4242);
            Assert.True(TankSwapDebuffWatch.IsWatched(4242));
        }
        finally
        {
            TankSwapDebuffWatch.Shutdown();
        }
    }

    [Fact]
    public void TerritoryOverride_OnlyMatchesInItsTerritory()
    {
        ushort territory = 1122;
        TankSwapDebuffWatch.InitializeForTest(new uint[] { 714 }, () => territory);
        try
        {
            TankSwapDebuffWatch.RegisterContentOverride(1122, 4242);
            Assert.True(TankSwapDebuffWatch.IsWatched(4242));

            territory = 900; // zone change — the P3S-only id must stop matching
            Assert.False(TankSwapDebuffWatch.IsWatched(4242));
            Assert.True(TankSwapDebuffWatch.IsWatched(714)); // defaults unaffected
        }
        finally
        {
            TankSwapDebuffWatch.Shutdown();
        }
    }

    [Fact]
    public void Shutdown_RestoresFailOpen()
    {
        TankSwapDebuffWatch.InitializeForTest(new uint[] { 714 });
        TankSwapDebuffWatch.Shutdown();
        Assert.True(TankSwapDebuffWatch.IsWatched(4242));
    }
}
