using Daedalus.Services.Network;
using Daedalus.Services.Targeting;
using Xunit;

namespace Daedalus.Tests.Services.Targeting;

/// <summary>
/// Role gating for the party target modes. The load-bearing invariant: a Main Tank (any tank that is
/// not the designated off-tank) is NEVER eligible to be retargeted, under any mode.
/// </summary>
public class PartyTargetingCoordinatorTests
{
    private const uint Warrior = 21;   // tank
    private const uint WhiteMage = 24; // healer
    private const uint Samurai = 34;   // melee DPS

    [Theory]
    [InlineData(PartyTargetMode.Focus)]
    [InlineData(PartyTargetMode.Split)]
    [InlineData(PartyTargetMode.KillAdds)]
    public void MainTank_IsNeverEligible_UnderAnyMode(PartyTargetMode mode)
    {
        // Not the designated off-tank => the MT. Must stay on the boss in every mode.
        Assert.False(PartyTargetingCoordinator.IsEligible(Warrior, mode, isDesignatedOffTank: false));
    }

    [Fact]
    public void OffTank_IsEligible_OnlyForKillAdds()
    {
        Assert.True(PartyTargetingCoordinator.IsEligible(Warrior, PartyTargetMode.KillAdds, isDesignatedOffTank: true));
        Assert.False(PartyTargetingCoordinator.IsEligible(Warrior, PartyTargetMode.Focus, isDesignatedOffTank: true));
        Assert.False(PartyTargetingCoordinator.IsEligible(Warrior, PartyTargetMode.Split, isDesignatedOffTank: true));
    }

    [Theory]
    [InlineData(PartyTargetMode.Focus)]
    [InlineData(PartyTargetMode.Split)]
    [InlineData(PartyTargetMode.KillAdds)]
    public void Healer_IsNeverEligible(PartyTargetMode mode)
    {
        Assert.False(PartyTargetingCoordinator.IsEligible(WhiteMage, mode, isDesignatedOffTank: false));
    }

    [Theory]
    [InlineData(PartyTargetMode.Focus)]
    [InlineData(PartyTargetMode.Split)]
    [InlineData(PartyTargetMode.KillAdds)]
    public void Dps_IsEligible_InEveryMode(PartyTargetMode mode)
    {
        Assert.True(PartyTargetingCoordinator.IsEligible(Samurai, mode, isDesignatedOffTank: false));
    }

    [Fact]
    public void NoneMode_IsNeverEligible()
    {
        Assert.False(PartyTargetingCoordinator.IsEligible(Samurai, PartyTargetMode.None, isDesignatedOffTank: false));
        Assert.False(PartyTargetingCoordinator.IsEligible(Warrior, PartyTargetMode.None, isDesignatedOffTank: true));
    }
}
