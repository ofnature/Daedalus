using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Moq;
using Daedalus.Data;
using Daedalus.Rotation.Common.Scheduling;
using Daedalus.Rotation.HermesCore.Abilities;
using Daedalus.Rotation.HermesCore.Modules;
using Daedalus.Services.Targeting;
using Daedalus.Tests.Mocks;
using Daedalus.Tests.Rotation.Common.Scheduling;
using Daedalus.Tests.Rotation.HermesCore;
using Xunit;

namespace Daedalus.Tests.Rotation.HermesCore.Modules;

/// <summary>
/// RSR CustomRotation_Ability (8153eea2): automatic True North only goes in the last weave slot
/// (GCD remaining &lt;= 1.35s). In the first slot the player can still walk to the positional, so a
/// charge spent there can be wasted.
/// </summary>
[Collection("HermesMudraStaticState")] // HermesNinjutsuMudraExecutor holds per-frame statics
public class HermesTrueNorthLastWeaveTests
{
    private readonly BuffModule _module = new();

    [Fact]
    public void FirstWeaveSlot_FinisherNext_HoldsTrueNorth()
    {
        var scheduler = Collect(gcdRemaining: 2.0f);

        Assert.DoesNotContain(scheduler.InspectOgcdQueue(), c => c.Behavior == HermesAbilities.TrueNorth);
    }

    [Theory]
    [InlineData(1.35f)]
    [InlineData(1.0f)]
    public void LastWeaveSlot_FinisherNext_UsesTrueNorth(float gcdRemaining)
    {
        var scheduler = Collect(gcdRemaining: gcdRemaining);

        Assert.Contains(scheduler.InspectOgcdQueue(), c => c.Behavior == HermesAbilities.TrueNorth);
    }

    [Fact]
    public void BelowTrueNorthLevel_LastWeaveSlot_NoTrueNorth()
    {
        // True North is Lv50.
        var scheduler = Collect(gcdRemaining: 1.0f, level: 40);

        Assert.DoesNotContain(scheduler.InspectOgcdQueue(), c => c.Behavior == HermesAbilities.TrueNorth);
    }

    [Fact]
    public void PositionalImmuneTarget_LastWeaveSlot_NoTrueNorth()
    {
        // RSR only spends it when a positional is owed.
        var scheduler = Collect(gcdRemaining: 1.0f, positionalImmune: true);

        Assert.DoesNotContain(scheduler.InspectOgcdQueue(), c => c.Behavior == HermesAbilities.TrueNorth);
    }

    private RotationScheduler Collect(float gcdRemaining, byte level = 100, bool positionalImmune = false)
    {
        var enemy = new Mock<IBattleNpc>();
        enemy.Setup(x => x.GameObjectId).Returns(99999UL);
        enemy.Setup(x => x.CurrentHp).Returns(100000u);
        enemy.Setup(x => x.MaxHp).Returns(100000u);

        var targeting = MockBuilders.CreateMockTargetingService();
        targeting.Setup(x => x.FindEnemyForAction(
                It.IsAny<EnemyTargetingStrategy>(), It.IsAny<uint>(), It.IsAny<IPlayerCharacter>()))
            .Returns(enemy.Object);

        var actionService = MockBuilders.CreateMockActionService(canExecuteGcd: false);
        actionService.Setup(x => x.GcdRemaining).Returns(gcdRemaining);

        var scheduler = SchedulerFactory.CreateForTest(actionService: actionService);
        // Gust Slash done, finisher next, standing at the front.
        var context = HermesTestContext.Create(
            actionService: actionService,
            targetingService: targeting,
            level: level,
            canExecuteGcd: false,
            comboStep: 2,
            lastComboAction: NINActions.GustSlash.ActionId,
            targetHasPositionalImmunity: positionalImmune);

        _module.CollectCandidates(context, scheduler, isMoving: false);
        return scheduler;
    }
}
