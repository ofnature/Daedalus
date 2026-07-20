using Daedalus.Data;
using Daedalus.Services.Network;
using Xunit;

namespace Daedalus.Tests.Services.Network;

/// <summary>
/// Burst-readiness rules for the LAN readiness pips / auto-fire (2026-07-20 field report: pips
/// were ALWAYS red — BroadcastBurstReady's only caller was the BLU Moon Flute path, so non-BLU
/// jobs never reported ready and the all-ready auto-fire could never trigger).
/// </summary>
public class BurstReadinessHelperTests
{
    [Fact]
    public void RaidBuffJob_ReadyOnlyWhenBuffOffCooldown()
    {
        // PCT with Starry Muse ready → ready; on cooldown → not ready.
        Assert.True(BurstReadinessHelper.IsBurstReady(
            JobRegistry.Pictomancer, inCombat: true, _ => true, _ => true));
        Assert.False(BurstReadinessHelper.IsBurstReady(
            JobRegistry.Pictomancer, inCombat: true, _ => false, _ => true));
    }

    [Fact]
    public void NoRaidBuffJob_ReadyWheneverInCombat()
    {
        // WAR/SGE bring no 2-min party buff — nothing to wait on.
        Assert.True(BurstReadinessHelper.IsBurstReady(
            JobRegistry.Warrior, inCombat: true, _ => false, _ => false));
    }

    [Fact]
    public void OutOfCombat_NeverReady()
    {
        Assert.False(BurstReadinessHelper.IsBurstReady(
            JobRegistry.Pictomancer, inCombat: false, _ => true, _ => true));
        Assert.False(BurstReadinessHelper.IsBurstReady(
            JobRegistry.Warrior, inCombat: false, _ => true, _ => true));
    }

    [Fact]
    public void RaidBuffNotLearned_ReadyInCombat()
    {
        // Low-level DRG below Battle Litany: nothing to align yet.
        Assert.True(BurstReadinessHelper.IsBurstReady(
            JobRegistry.Dragoon, inCombat: true, _ => false, _ => false));
    }

    [Fact]
    public void BlueMage_NeverReportsHere_MoonFlutePathOwnsIt()
    {
        Assert.False(BurstReadinessHelper.IsBurstReady(
            JobRegistry.BlueMage, inCombat: true, _ => true, _ => true));
        // But BLU still counts as alignable so BLU fleets auto-fire their Moon Flute sync.
        Assert.True(BurstReadinessHelper.HasAlignableRaidBuff(JobRegistry.BlueMage));
    }

    [Fact]
    public void AlignableRaidBuff_ClassifiesJobs()
    {
        Assert.True(BurstReadinessHelper.HasAlignableRaidBuff(JobRegistry.Pictomancer));
        Assert.True(BurstReadinessHelper.HasAlignableRaidBuff(JobRegistry.Astrologian));
        Assert.True(BurstReadinessHelper.HasAlignableRaidBuff(JobRegistry.Scholar));
        Assert.False(BurstReadinessHelper.HasAlignableRaidBuff(JobRegistry.Warrior));
        Assert.False(BurstReadinessHelper.HasAlignableRaidBuff(JobRegistry.Sage));
    }
}
