using System.IO;
using System.Text.RegularExpressions;
using Daedalus.Config;
using Daedalus.Rotation.Phantom;
using Xunit;
using C = Daedalus.Rotation.Phantom.PhantomBandRules.EtherCandidate;
using P = Daedalus.Rotation.Phantom.PhantomBandRules.PotionCandidate;

namespace Daedalus.Tests.Rotation.Phantom;

/// <summary>
/// Phantom Chemist's Occult Ether: the self-only toggle and the HP potion reserve.
/// <para>
/// Game data: Occult Ether and Occult Potion both "Consume an Occult Potion" — one item for the HP
/// and MP restores. <c>ChemistEtherSelfOnly</c> was shown in Settings but read by nothing, so Ether
/// always went to the player whatever the toggle said.
/// </para>
/// </summary>
public sealed class PhantomChemistEtherTests
{
    private static PhantomConfig Cfg(int reserve = 1) => new() { ChemistEtherMpThreshold = 2000, ChemistPotionReserve = reserve };

    // ── the potion reserve ──────────────────────────────────────────────────────────────

    /// <summary>With the default reserve of one, the last potion is never spent on MP.</summary>
    [Theory]
    [InlineData(0u, false)]
    [InlineData(1u, false)]   // exactly the reserve — kept for HP
    [InlineData(2u, true)]
    [InlineData(5u, true)]
    public void EtherLeavesTheReserveAlone(uint potions, bool expected)
        => Assert.Equal(expected, PhantomBandRules.ShouldUseEther(Cfg(reserve: 1), currentMp: 500, maxMp: 10000, potions, inCombat: true));

    /// <summary>Reserve 0 restores the old behaviour: a single potion may go on MP.</summary>
    [Fact]
    public void AReserveOfZeroLetsTheLastPotionGoOnMp()
        => Assert.True(PhantomBandRules.ShouldUseEther(Cfg(reserve: 0), currentMp: 500, maxMp: 10000, potionCount: 1, inCombat: true));

    /// <summary>
    /// The reserve exists FOR the HP potion, so the HP potion must be able to use it — a reserve that
    /// also blocked Occult Potion would keep the last potion back and then refuse to drink it.
    /// </summary>
    [Fact]
    public void TheHpPotionIgnoresTheReserve()
    {
        var cfg = Cfg(reserve: 3);
        cfg.ChemistPotionHpPct = 0.5f;

        Assert.True(PhantomBandRules.ShouldUsePotion(cfg, selfHpPct: 0.2f, potionCount: 1, inCombat: true));
    }

    /// <summary>A hand-edited negative reserve is treated as zero, not as "always spare".</summary>
    [Fact]
    public void ANegativeReserveClampsToZero()
    {
        Assert.False(PhantomBandRules.HasPotionToSpareForEther(Cfg(reserve: -4), potionCount: 0));
        Assert.True(PhantomBandRules.HasPotionToSpareForEther(Cfg(reserve: -4), potionCount: 1));
    }

    // ── party targeting (self-only off) ─────────────────────────────────────────────────

    /// <summary>The lowest MP pool below the threshold wins, so a starving caster is never passed over.</summary>
    [Fact]
    public void PicksTheLowestMpBelowTheThreshold()
    {
        C[] party =
        [
            new(1, CurrentMp: 1900, MaxMp: 10000, DistanceYalms: 0, IsDead: false),  // self
            new(2, CurrentMp: 400,  MaxMp: 10000, DistanceYalms: 12, IsDead: false),
            new(3, CurrentMp: 1200, MaxMp: 10000, DistanceYalms: 8,  IsDead: false),
        ];

        Assert.Equal(2ul, PhantomBandRules.PickEtherTarget(party, mpThreshold: 2000));
    }

    [Fact]
    public void NobodyBelowTheThresholdMeansNoTarget()
    {
        C[] party =
        [
            new(1, 5000, 10000, 0, false),
            new(2, 2000, 10000, 5, false),   // exactly the threshold is not "below"
        ];

        Assert.Null(PhantomBandRules.PickEtherTarget(party, mpThreshold: 2000));
    }

    /// <summary>The dead, the out-of-range, and the MP-less are never picked, however low they read.</summary>
    [Fact]
    public void SkipsDeadOutOfRangeAndMpLessMembers()
    {
        C[] party =
        [
            new(1, 1500, 10000, 0,  false),
            new(2, 0,    10000, 5,  true),                                         // dead
            new(3, 100,  10000, PhantomBandRules.OccultEtherRangeYalms + 1, false), // beyond 30y
            new(4, 0,    0,     5,  false),                                        // no MP pool
        ];

        Assert.Equal(1ul, PhantomBandRules.PickEtherTarget(party, mpThreshold: 2000));
    }

    /// <summary>Ties go to the first candidate; the layer lists the player first, so self wins.</summary>
    [Fact]
    public void ATieGoesToTheFirstCandidate()
    {
        C[] party = [new(1, 800, 10000, 0, false), new(2, 800, 10000, 6, false)];

        Assert.Equal(1ul, PhantomBandRules.PickEtherTarget(party, mpThreshold: 2000));
    }

    [Fact]
    public void ReachExactlyAtRangeCounts()
        => Assert.Equal(7ul, PhantomBandRules.PickEtherTarget(
            [new C(7, 100, 10000, PhantomBandRules.OccultEtherRangeYalms, false)], mpThreshold: 2000));

    // ── Occult Potion party targeting (self-only off) ───────────────────────────────────

    /// <summary>
    /// Lowest HP FRACTION wins, not lowest raw HP — a tank on 40% of a big pool is in more danger
    /// than a caster on 45% of a small one, even with more HP left.
    /// </summary>
    [Fact]
    public void PotionPicksTheLowestHpFractionNotTheLowestRawHp()
    {
        P[] party =
        [
            new(1, CurrentHp: 9000,  MaxHp: 20000, DistanceYalms: 0,  IsDead: false),  // self, 45%
            new(2, CurrentHp: 16000, MaxHp: 40000, DistanceYalms: 10, IsDead: false),  // tank, 40%
        ];

        Assert.Equal(2ul, PhantomBandRules.PickPotionTarget(party, hpFraction: 0.5f));
    }

    [Fact]
    public void PotionFindsNobodyWhenEveryoneIsAboveTheThreshold()
        => Assert.Null(PhantomBandRules.PickPotionTarget(
            [new P(1, 8000, 10000, 0, false), new P(2, 5000, 10000, 5, false)],   // 80%, exactly 50%
            hpFraction: 0.5f));

    [Fact]
    public void PotionSkipsDeadOutOfRangeAndUnreadableMembers()
    {
        P[] party =
        [
            new(1, 4000, 10000, 0, false),                                            // self, 40%
            new(2, 0,    10000, 5, true),                                             // dead
            new(3, 100,  10000, PhantomBandRules.OccultPotionRangeYalms + 1, false),  // beyond 30y
            new(4, 0,    0,     5, false),                                            // no HP read
        ];

        Assert.Equal(1ul, PhantomBandRules.PickPotionTarget(party, hpFraction: 0.5f));
    }

    [Fact]
    public void PotionTieGoesToTheFirstCandidate()
        => Assert.Equal(1ul, PhantomBandRules.PickPotionTarget(
            [new P(1, 3000, 10000, 0, false), new P(2, 3000, 10000, 7, false)], hpFraction: 0.5f));

    // ── wiring, against the source ─────────────────────────────────────────────────────

    /// <summary>
    /// The potion toggle had the same defect as the ether one: shown in Settings, read by nothing.
    /// The survival block must consult it — and must NOT consult the reserve, which exists for it.
    /// </summary>
    [Fact]
    public void TheChemistSurvivalBlockReadsThePotionToggleButNotTheReserve()
    {
        var source = File.ReadAllText(LayerSourcePath());
        var block = Regex.Match(source, @"private void PushSurvival\((.*?)if \(job == PhantomJob\.Monk", RegexOptions.Singleline);
        Assert.True(block.Success, "Chemist survival block not found in PhantomActionLayer");

        var body = block.Groups[1].Value;
        Assert.Contains("cfg.ChemistPotionSelfOnly", body);
        Assert.Contains("FindPotionTarget", body);
        Assert.DoesNotContain("HasPotionToSpareForEther", body);
    }

    /// <summary>
    /// The regression this change exists for: the toggle was a setting nothing read. The Chemist MP
    /// block must consult it, and the reserve, or the Settings UI is lying again.
    /// </summary>
    [Fact]
    public void TheChemistMpBlockReadsTheToggleAndTheReserve()
    {
        var source = File.ReadAllText(LayerSourcePath());
        var block = Regex.Match(source, @"if \(job == PhantomJob\.Chemist && inCombat\)(.*?)private static IBattleChara\? FindEtherTarget", RegexOptions.Singleline);
        Assert.True(block.Success, "Chemist MP block not found in PhantomActionLayer");

        var body = block.Groups[1].Value;
        Assert.Contains("cfg.ChemistEtherSelfOnly", body);
        Assert.Contains("HasPotionToSpareForEther", body);
        Assert.Contains("FindEtherTarget", body);
    }

    private static string LayerSourcePath()
    {
        var dir = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Daedalus.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "Daedalus", "Rotation", "Phantom", "PhantomActionLayer.cs");
    }
}
