using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Moq;
using Daedalus.Rotation.IrisCore.Abilities;
using Daedalus.Rotation.IrisCore.Modules;
using Daedalus.Services.Targeting;
using Daedalus.Tests.Mocks;
using Daedalus.Tests.Rotation.Common.Scheduling;
using Daedalus.Tests.Rotation.IrisCore;
using Xunit;

namespace Daedalus.Tests.Rotation.IrisCore.Modules;

/// <summary>
/// Portrait dispatch from the gauge flags (see IrisPortraitFlagsTests for the bit decoding).
/// </summary>
public class BuffModulePortraitTests
{
    [Fact]
    public void MadeenReady_MogNotReady_QueuesRetributionOfTheMadeen()
    {
        var scheduler = Collect(madeenReady: true, mogReady: false);
        Assert.Contains(scheduler.InspectOgcdQueue(), c => c.Behavior == IrisAbilities.RetributionOfTheMadeen);
        Assert.DoesNotContain(scheduler.InspectOgcdQueue(), c => c.Behavior == IrisAbilities.MogOfTheAges);
    }

    [Fact]
    public void NeitherReady_QueuesNoPortrait()
    {
        var scheduler = Collect(madeenReady: false, mogReady: false);
        Assert.DoesNotContain(scheduler.InspectOgcdQueue(), c => c.Behavior == IrisAbilities.RetributionOfTheMadeen);
        Assert.DoesNotContain(scheduler.InspectOgcdQueue(), c => c.Behavior == IrisAbilities.MogOfTheAges);
    }

    private static Daedalus.Rotation.Common.Scheduling.RotationScheduler Collect(bool madeenReady, bool mogReady)
    {
        var enemy = new Mock<IBattleNpc>();
        enemy.Setup(x => x.GameObjectId).Returns(99999UL);
        enemy.Setup(x => x.CurrentHp).Returns(10000u);
        enemy.Setup(x => x.MaxHp).Returns(10000u);
        var targeting = MockBuilders.CreateMockTargetingService();
        targeting.Setup(x => x.FindEnemy(
                It.IsAny<EnemyTargetingStrategy>(), It.IsAny<float>(), It.IsAny<IPlayerCharacter>()))
            .Returns(enemy.Object);

        var actionService = MockBuilders.CreateMockActionService();
        var scheduler = SchedulerFactory.CreateForTest(actionService: actionService);
        var context = IrisTestContext.Create(
            actionService: actionService,
            targetingService: targeting,
            level: 100,
            inCombat: true,
            canExecuteOgcd: true,
            madeenReady: madeenReady,
            mogReady: mogReady);

        new BuffModule().CollectCandidates(context, scheduler, isMoving: false);
        return scheduler;
    }
}
