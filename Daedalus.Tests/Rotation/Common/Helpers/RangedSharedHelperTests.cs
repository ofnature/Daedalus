using Moq;
using Daedalus.Data;
using Daedalus.Models.Action;
using Daedalus.Rotation.Common.Helpers;
using Daedalus.Services.Action;
using Daedalus.Tests.Mocks;
using Xunit;

namespace Daedalus.Tests.Rotation.Common.Helpers;

/// <summary>
/// Peloton auto-cast for physical ranged DPS: only out of combat, moving, not already buffed, enabled.
/// </summary>
public sealed class RangedSharedHelperTests
{
    private static Configuration Config(bool enablePeloton = true)
    {
        var c = new Configuration();
        c.RangedShared.EnablePeloton = enablePeloton;
        return c;
    }

    private static (Mock<IActionService> svc, Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter player) Setup()
    {
        var svc = MockBuilders.CreateMockActionService();
        svc.Setup(x => x.IsActionReady(RoleActions.Peloton.ActionId)).Returns(true);
        var player = MockBuilders.CreateMockPlayerCharacter(level: 90);
        player.Setup(x => x.StatusList).Returns((Dalamud.Game.ClientState.Statuses.StatusList?)null!);
        return (svc, player.Object);
    }

    private static void Verify(Mock<IActionService> svc, Times times) =>
        svc.Verify(x => x.ExecuteOgcd(
            It.Is<ActionDefinition>(a => a.ActionId == RoleActions.Peloton.ActionId), It.IsAny<ulong>()), times);

    [Fact]
    public void Casts_WhenOutOfCombatAndMoving()
    {
        var (svc, player) = Setup();
        RangedSharedHelper.TryCastPeloton(Config(), svc.Object, player, inCombat: false, isMoving: true);
        Verify(svc, Times.Once());
    }

    [Fact]
    public void DoesNotCast_InCombat()
    {
        var (svc, player) = Setup();
        RangedSharedHelper.TryCastPeloton(Config(), svc.Object, player, inCombat: true, isMoving: true);
        Verify(svc, Times.Never());
    }

    [Fact]
    public void DoesNotCast_WhenStationary()
    {
        var (svc, player) = Setup();
        RangedSharedHelper.TryCastPeloton(Config(), svc.Object, player, inCombat: false, isMoving: false);
        Verify(svc, Times.Never());
    }

    [Fact]
    public void DoesNotCast_WhenDisabled()
    {
        var (svc, player) = Setup();
        RangedSharedHelper.TryCastPeloton(Config(enablePeloton: false), svc.Object, player, inCombat: false, isMoving: true);
        Verify(svc, Times.Never());
    }
}
