using Daedalus.Rotation.NyxCore.Modules;
using Xunit;

namespace Daedalus.Tests.Rotation.NyxCore.Modules;

/// <summary>
/// DRK Edge/Flood MP-spend logic (RSR CheckDarkSide parity): refresh Darkside before it lapses, dump MP
/// near cap outside burst, and spend down to a TBN reserve during burst.
/// </summary>
public sealed class NyxDarksideSpendTests
{
    private const float Gcd = 2.5f;

    [Fact]
    public void Activates_WhenNoDarkside()
    {
        var (spend, _) = DamageModule.ResolveDarksideSpend(
            hasDarkside: false, darksideRemaining: 0f, currentMp: 3000, keepTbnReserve: true, inBurst: false, Gcd);
        Assert.True(spend);
    }

    [Fact]
    public void Refreshes_WhenDarksideEndsWithinThreeGcds()
    {
        var (spend, reason) = DamageModule.ResolveDarksideSpend(
            hasDarkside: true, darksideRemaining: 5f, currentMp: 4000, keepTbnReserve: true, inBurst: false, Gcd);
        Assert.True(spend);
        Assert.Contains("refresh", reason);
    }

    [Fact]
    public void DoesNotRefresh_WhenDarksideHealthy_AndMpLow()
    {
        var (spend, _) = DamageModule.ResolveDarksideSpend(
            hasDarkside: true, darksideRemaining: 30f, currentMp: 5000, keepTbnReserve: true, inBurst: false, Gcd);
        Assert.False(spend);
    }

    [Fact]
    public void Dumps_NearCap_OutsideBurst()
    {
        var (spend, _) = DamageModule.ResolveDarksideSpend(
            hasDarkside: true, darksideRemaining: 30f, currentMp: 9000, keepTbnReserve: true, inBurst: false, Gcd);
        Assert.True(spend);
    }

    [Fact]
    public void DoesNotDump_BelowCap_OutsideBurst()
    {
        var (spend, _) = DamageModule.ResolveDarksideSpend(
            hasDarkside: true, darksideRemaining: 30f, currentMp: 8000, keepTbnReserve: true, inBurst: false, Gcd);
        Assert.False(spend);
    }

    [Fact]
    public void SpendsDownToReserve_InBurst()
    {
        // 6500 MP, burst, TBN reserve on → reserve 6000, spends (leaves ~3000 for TBN).
        var (spend, _) = DamageModule.ResolveDarksideSpend(
            hasDarkside: true, darksideRemaining: 30f, currentMp: 6500, keepTbnReserve: true, inBurst: true, Gcd);
        Assert.True(spend);
    }

    [Fact]
    public void HoldsTbnReserve_InBurst()
    {
        // 5500 MP, burst, TBN reserve on → below 6000+ reserve, holds so TBN can still cast.
        var (spend, _) = DamageModule.ResolveDarksideSpend(
            hasDarkside: true, darksideRemaining: 30f, currentMp: 5500, keepTbnReserve: true, inBurst: true, Gcd);
        Assert.False(spend);
    }

    [Fact]
    public void NoReserve_WhenTbnDisabled()
    {
        // TBN off → only the 3000 Edge cost is reserved; spends down further in burst.
        var (spend, _) = DamageModule.ResolveDarksideSpend(
            hasDarkside: true, darksideRemaining: 30f, currentMp: 3500, keepTbnReserve: false, inBurst: true, Gcd);
        Assert.True(spend);
    }
}
