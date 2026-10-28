using System.Linq;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Moq;
using Daedalus.Config;
using Daedalus.Data;
using Daedalus.Rotation.ArtemisCore.Abilities;
using Daedalus.Rotation.ArtemisCore.Context;
using Daedalus.Rotation.ArtemisCore.Helpers;
using Daedalus.Rotation.ArtemisCore.Modules;
using Daedalus.Rotation.Common.Scheduling;
using Daedalus.Services.Targeting;
using Daedalus.Tests.Mocks;
using Daedalus.Tests.Rotation.Common.Scheduling;
using Xunit;

namespace Daedalus.Tests.Rotation.ArtemisCore;

/// <summary>
/// Trick and Parting Blow both order the familiar, and the familiar is slow to act on an order. Parting Blow right
/// after a Trick sends it away before the Trick lands (pet TP spent, no Heart); Trick right after Parting Blow goes
/// to a familiar that is leaving. RSR keeps them apart with !IsLastAction (8f258dbc for Parting Blow, 5ffd2205 for
/// Trick).
/// </summary>
public class ArtemisFamiliarOrderSpacingTests
{
    [Fact]
    public void PartingBlow_PushedWhenEnabled()
    {
        var queue = Collect(lastOgcd: 0).InspectOgcdQueue();

        Assert.Contains(queue, c => c.Behavior == ArtemisAbilities.PartingBlow);
    }

    [Fact]
    public void PartingBlow_NotRightAfterTrick()
    {
        var queue = Collect(lastOgcd: BSTActions.Trick.ActionId).InspectOgcdQueue();

        Assert.DoesNotContain(queue, c => c.Behavior == ArtemisAbilities.PartingBlow);
    }

    [Fact]
    public void Trick_NotRightAfterPartingBlow()
    {
        var queue = Collect(lastOgcd: BSTActions.PartingBlow.ActionId).InspectOgcdQueue();

        Assert.DoesNotContain(queue, c => c.Behavior == ArtemisAbilities.Trick);
    }

    [Fact]
    public void Trick_PushedOtherwise()
    {
        var queue = Collect(lastOgcd: 0).InspectOgcdQueue();

        Assert.Contains(queue, c => c.Behavior == ArtemisAbilities.Trick);
    }

    private static RotationScheduler Collect(uint lastOgcd)
    {
        var enemy = new Mock<IBattleNpc>();
        enemy.Setup(x => x.GameObjectId).Returns(99999UL);
        enemy.Setup(x => x.CurrentHp).Returns(10000u);
        enemy.Setup(x => x.MaxHp).Returns(10000u);

        var targeting = MockBuilders.CreateMockTargetingService();
        targeting.Setup(x => x.FindEnemyForAction(
                It.IsAny<EnemyTargetingStrategy>(), It.IsAny<uint>(), It.IsAny<IPlayerCharacter>()))
            .Returns(enemy.Object);

        // Every order is usable as far as the game is concerned; only the last oGCD differs.
        var actions = MockBuilders.CreateMockActionService();
        actions.Setup(x => x.GetActionStatusCode(It.IsAny<uint>(), It.IsAny<ulong>())).Returns(0u);
        actions.Setup(x => x.IsActionReady(It.IsAny<uint>())).Returns(true);
        actions.Setup(x => x.WasLastOgcd(It.IsAny<uint>())).Returns<uint>(id => id != 0 && id == lastOgcd);

        var config = new Configuration { Enabled = true, EnableDamage = true };
        config.Beastmaster.EnablePartingBlow = true;
        config.Beastmaster.EnableTemperedRelease = false;

        var player = MockBuilders.CreateMockPlayerCharacter(level: 100);
        player.Setup(x => x.StatusList).Returns((Dalamud.Game.ClientState.Statuses.StatusList?)null!);

        var context = new Mock<IArtemisContext>();
        context.Setup(x => x.Player).Returns(player.Object);
        context.Setup(x => x.InCombat).Returns(true);
        context.Setup(x => x.Configuration).Returns(config);
        context.Setup(x => x.ActionService).Returns(actions.Object);
        context.Setup(x => x.TargetingService).Returns(targeting.Object);
        // No familiar in the object table (OfType walks the non-generic enumerator, so set up both).
        var objectTable = new Mock<Dalamud.Plugin.Services.IObjectTable>();
        objectTable.Setup(x => x.GetEnumerator())
            .Returns(() => Enumerable.Empty<Dalamud.Game.ClientState.Objects.Types.IGameObject>().GetEnumerator());
        objectTable.As<System.Collections.IEnumerable>().Setup(x => x.GetEnumerator())
            .Returns(() => Enumerable.Empty<Dalamud.Game.ClientState.Objects.Types.IGameObject>().GetEnumerator());
        context.Setup(x => x.ObjectTable).Returns(objectTable.Object);
        context.Setup(x => x.DebuffDetectionService).Returns(MockBuilders.CreateMockDebuffDetectionService().Object);
        context.Setup(x => x.HasFamiliar).Returns(true);
        context.Setup(x => x.Instinct).Returns(new ArtemisInstinctTracker());
        context.Setup(x => x.TrickPairing).Returns(new ArtemisTrickPairing());
        context.Setup(x => x.Debug).Returns(new ArtemisDebugState());

        var scheduler = SchedulerFactory.CreateForTest(actionService: actions);
        new DamageModule().CollectCandidates(context.Object, scheduler, isMoving: false);
        return scheduler;
    }
}
