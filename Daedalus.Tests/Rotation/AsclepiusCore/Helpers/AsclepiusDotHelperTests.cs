using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Moq;
using Daedalus.Data;
using Daedalus.Rotation.AsclepiusCore.Helpers;
using Daedalus.Tests.Mocks;
using Xunit;

namespace Daedalus.Tests.Rotation.AsclepiusCore.Helpers;

/// <summary>
/// Eukrasian Dosis target choice. FindEnemyNeedingDot already skips enemies about to die (the DoT time-to-kill
/// gate, RSR parity; RSR 64413cce reworked when a DoT pays off before death). The helper used to fall back to the
/// plain enemy pick whenever that returned null, so for Sage alone the dying target got Eukrasia + E. Dosis anyway.
/// </summary>
public class AsclepiusDotHelperTests
{
    [Fact]
    public void FindEnemyNeedingEukrasianDosis_EnemyNeedsDot_ReturnsIt()
    {
        var enemy = CreateEnemy();
        var targeting = MockBuilders.CreateMockTargetingService(findEnemy: (_, _, _) => enemy.Object);
        targeting.Setup(x => x.FindEnemyNeedingDot(
                It.IsAny<uint>(), It.IsAny<float>(), It.IsAny<float>(), It.IsAny<IPlayerCharacter>()))
            .Returns(enemy.Object);

        var context = AsclepiusTestContext.Create(targetingService: targeting, level: 100);

        Assert.Same(enemy.Object,
            AsclepiusDotHelper.FindEnemyNeedingEukrasianDosis(context, SGEActions.EukrasianDosisIII));
    }

    [Fact]
    public void FindEnemyNeedingEukrasianDosis_OnlyEnemyIsDying_ReturnsNull()
    {
        // No E. Dosis anywhere, the only enemy is in range, but FindEnemyNeedingDot skipped it (dies too soon).
        var enemy = CreateEnemy();
        var targeting = MockBuilders.CreateMockTargetingService(findEnemy: (_, _, _) => enemy.Object);

        var context = AsclepiusTestContext.Create(targetingService: targeting, level: 100);

        Assert.Null(AsclepiusDotHelper.FindEnemyNeedingEukrasianDosis(context, SGEActions.EukrasianDosisIII));
    }

    private static Mock<IBattleNpc> CreateEnemy()
    {
        var enemy = new Mock<IBattleNpc>();
        enemy.Setup(x => x.GameObjectId).Returns(0xBEEFUL);
        return enemy;
    }
}
