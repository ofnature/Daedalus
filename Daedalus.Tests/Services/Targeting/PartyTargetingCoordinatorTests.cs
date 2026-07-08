using Dalamud.Plugin.Services;
using Moq;
using Daedalus.Services.Network;
using Daedalus.Services.Targeting;
using Daedalus.Tests.Mocks;
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

    [Fact]
    public void Tick_ModeTransition_RaisesEnforcementStateChanged_Once()
    {
        var log = new Mock<IPluginLog>().Object;
        var lan = new LanCoordinator(log, "machine-A", 47200) { SenderId = "Self@World" };
        var bus = new CoordinationBus(log, lan, partyService: null, localMachineId: "machine-A");
        var objectTable = MockBuilders.CreateMockObjectTable(); // LocalPlayer → null
        var targetManager = new Mock<ITargetManager>();

        var coordinator = new PartyTargetingCoordinator(bus, objectTable.Object, targetManager.Object);
        var changed = 0;
        coordinator.OnEnforcementStateChanged += () => changed++;

        // Initial state is (None, ineligible) — the first ticks must NOT fire (no transition).
        coordinator.Tick();
        coordinator.Tick();
        Assert.Equal(0, changed);

        // Mode flips None → Focus: one transition, one event — steady-state ticks stay quiet.
        bus.BroadcastTargetMode(PartyTargetMode.Focus, focusTargetId: 123, offTankSenderId: "");
        coordinator.Tick();
        coordinator.Tick();
        Assert.Equal(1, changed);

        // Back to None: second transition, second event.
        bus.BroadcastTargetMode(PartyTargetMode.None, focusTargetId: 0, offTankSenderId: "");
        coordinator.Tick();
        coordinator.Tick();
        Assert.Equal(2, changed);
    }
}
