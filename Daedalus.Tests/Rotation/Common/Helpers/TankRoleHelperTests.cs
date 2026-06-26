using Dalamud.Game.ClientState.Objects.Types;
using Moq;
using Daedalus.Rotation.Common.Helpers;
using Daedalus.Services.Tank;
using Xunit;

namespace Daedalus.Tests.Rotation.Common.Helpers;

public class TankRoleHelperTests
{
    [Fact]
    public void ResolveIsMainTank_SoloDuty_ReturnsTrue_EvenWithoutAggro()
    {
        var enmity = new Mock<IEnmityService>();
        enmity.Setup(x => x.IsMainTankOn(It.IsAny<IBattleChara>(), It.IsAny<uint>())).Returns(false);

        var target = new Mock<IBattleChara>();

        var result = TankRoleHelper.ResolveIsMainTank(
            isMainTankOverride: null,
            currentTarget: target.Object,
            playerEntityId: 100,
            hasCoTank: false,
            enmityService: enmity.Object);

        Assert.True(result);
        enmity.Verify(x => x.IsMainTankOn(It.IsAny<IBattleChara>(), It.IsAny<uint>()), Times.Never);
    }

    [Fact]
    public void ResolveIsMainTank_SoloDuty_NoTarget_ReturnsTrue()
    {
        var enmity = new Mock<IEnmityService>();

        var result = TankRoleHelper.ResolveIsMainTank(
            isMainTankOverride: null,
            currentTarget: null,
            playerEntityId: 100,
            hasCoTank: false,
            enmityService: enmity.Object);

        Assert.True(result);
    }

    [Fact]
    public void ResolveIsMainTank_TwoTankContent_UsesEnmity()
    {
        var enmity = new Mock<IEnmityService>();
        enmity.Setup(x => x.IsMainTankOn(It.IsAny<IBattleChara>(), 100u)).Returns(true);

        var target = new Mock<IBattleChara>();

        var result = TankRoleHelper.ResolveIsMainTank(
            isMainTankOverride: null,
            currentTarget: target.Object,
            playerEntityId: 100,
            hasCoTank: true,
            enmityService: enmity.Object);

        Assert.True(result);
    }

    [Fact]
    public void ResolveIsMainTank_TwoTankContent_NoAggro_ReturnsFalse()
    {
        var enmity = new Mock<IEnmityService>();
        enmity.Setup(x => x.IsMainTankOn(It.IsAny<IBattleChara>(), 100u)).Returns(false);

        var target = new Mock<IBattleChara>();

        var result = TankRoleHelper.ResolveIsMainTank(
            isMainTankOverride: null,
            currentTarget: target.Object,
            playerEntityId: 100,
            hasCoTank: true,
            enmityService: enmity.Object);

        Assert.False(result);
    }

    [Fact]
    public void ResolveIsMainTank_OverrideTakesPrecedence()
    {
        var enmity = new Mock<IEnmityService>();
        enmity.Setup(x => x.IsMainTankOn(It.IsAny<IBattleChara>(), It.IsAny<uint>())).Returns(true);

        var target = new Mock<IBattleChara>();

        var result = TankRoleHelper.ResolveIsMainTank(
            isMainTankOverride: false,
            currentTarget: target.Object,
            playerEntityId: 100,
            hasCoTank: false,
            enmityService: enmity.Object);

        Assert.False(result);
        enmity.Verify(x => x.IsMainTankOn(It.IsAny<IBattleChara>(), It.IsAny<uint>()), Times.Never);
    }
}
