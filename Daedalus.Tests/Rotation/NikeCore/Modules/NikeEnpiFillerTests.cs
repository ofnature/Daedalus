using System.Numerics;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Moq;
using Daedalus.Data;
using Daedalus.Rotation.NikeCore.Abilities;
using Daedalus.Rotation.NikeCore.Modules;
using Daedalus.Services.Targeting;
using Daedalus.Tests.Mocks;
using Daedalus.Tests.Rotation.Common.Scheduling;
using Xunit;

namespace Daedalus.Tests.Rotation.NikeCore.Modules;

/// <summary>
/// Ranged filler (Enpi) parity with NIN Throwing Dagger: when the target is beyond melee reach the
/// rotation must keep GCD uptime with Enpi instead of idling until the player walks back in. The gate
/// is position-based and fails open in melee — it must never divert a valid combo to the weak ranged toss.
/// </summary>
public class NikeEnpiFillerTests
{
    private readonly DamageModule _module = new();

    // Hakaze reach (3y) + zero hitboxes in the mocks. Anything past 3y is "out of melee".
    private const float OutOfMeleeX = 20f;
    private const float InMeleeX = 2f;

    [Fact]
    public void OutOfMelee_QueuesEnpi()
    {
        var gcd = Collect(enemyX: OutOfMeleeX, level: 100);
        Assert.Contains(gcd, c => c.Behavior == NikeAbilities.Enpi);
    }

    [Fact]
    public void OutOfMelee_DoesNotQueueMeleeCombo()
    {
        // The whole point: no melee starter should be queued when out of range, or the toon would
        // just re-issue a blocked Hakaze/Gyofu every GCD and stand around (the reported bug).
        var gcd = Collect(enemyX: OutOfMeleeX, level: 100);
        Assert.DoesNotContain(gcd, c => c.Behavior == NikeAbilities.Hakaze || c.Behavior == NikeAbilities.Gyofu);
    }

    [Fact]
    public void InMelee_DoesNotQueueEnpi_AndQueuesCombo()
    {
        var gcd = Collect(enemyX: InMeleeX, level: 100);
        Assert.DoesNotContain(gcd, c => c.Behavior == NikeAbilities.Enpi);
        Assert.Contains(gcd, c => c.Behavior == NikeAbilities.Gyofu || c.Behavior == NikeAbilities.Hakaze);
    }

    [Fact]
    public void OutOfMelee_BelowEnpiLevel_DoesNotQueueEnpi()
    {
        // Enpi is learned at Lv.15. Below that there is no ranged filler to fall back to.
        var gcd = Collect(enemyX: OutOfMeleeX, level: 10);
        Assert.DoesNotContain(gcd, c => c.Behavior == NikeAbilities.Enpi);
    }

    [Fact]
    public void OutOfMelee_EnpiOnCooldown_DoesNotQueueEnpi()
    {
        var actionService = MockBuilders.CreateMockActionService(
            isActionReady: id => id != SAMActions.Enpi.ActionId);
        var gcd = Collect(enemyX: OutOfMeleeX, level: 100, actionService: actionService);
        Assert.DoesNotContain(gcd, c => c.Behavior == NikeAbilities.Enpi);
    }

    private System.Collections.Generic.IReadOnlyList<Daedalus.Rotation.Common.Scheduling.AbilityCandidate>
        Collect(float enemyX, byte level, Mock<Daedalus.Services.Action.IActionService>? actionService = null)
    {
        var enemy = CreateMockEnemy(position: new Vector3(enemyX, 0f, 0f));
        var targeting = MockBuilders.CreateMockTargetingService();
        targeting.Setup(x => x.FindEnemyForAction(
                It.IsAny<EnemyTargetingStrategy>(), It.IsAny<uint>(), It.IsAny<IPlayerCharacter>()))
            .Returns(enemy.Object);
        targeting.Setup(x => x.FindEnemy(
                It.IsAny<EnemyTargetingStrategy>(), It.IsAny<float>(), It.IsAny<IPlayerCharacter>()))
            .Returns(enemy.Object);
        MockBuilders.SetupEnemyPackCount(targeting, 1);

        var config = NikeTestContext.CreateDefaultSamuraiConfiguration();
        var scheduler = SchedulerFactory.CreateForTest(config: config);
        var context = NikeTestContext.Create(
            config: config,
            actionService: actionService,
            targetingService: targeting,
            level: level);

        _module.CollectCandidates(context, scheduler, isMoving: false);
        return scheduler.InspectGcdQueue();
    }

    private static Mock<IBattleNpc> CreateMockEnemy(Vector3 position, ulong objectId = 99999UL)
    {
        var mock = new Mock<IBattleNpc>();
        mock.Setup(x => x.GameObjectId).Returns(objectId);
        mock.Setup(x => x.CurrentHp).Returns(10000u);
        mock.Setup(x => x.MaxHp).Returns(10000u);
        mock.Setup(x => x.Position).Returns(position);
        return mock;
    }
}
