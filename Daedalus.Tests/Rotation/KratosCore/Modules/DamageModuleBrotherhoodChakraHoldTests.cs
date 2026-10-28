using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Daedalus.Data;
using Daedalus.Rotation.KratosCore.Modules;
using Daedalus.Services;
using Daedalus.Services.Targeting;
using Daedalus.Tests.Mocks;
using Daedalus.Tests.Rotation.Common.Scheduling;
using Moq;
using Xunit;

namespace Daedalus.Tests.Rotation.KratosCore.Modules;

/// <summary>
/// RSR MNK_Reborn (6b52e1df): outside Brotherhood, The Forbidden Chakra waits only while Brotherhood is
/// cooling down and back within one GCD; it is used when Brotherhood is ready, further away, or not
/// learned. The AoE spender (Enlightenment) is never held. The old 8s party-burst hold and the
/// Chakra &lt; 5 Brotherhood hold are gone.
/// </summary>
public class DamageModuleBrotherhoodChakraHoldTests
{
    private static readonly uint ForbiddenChakra = MNKActions.TheForbiddenChakra.ActionId;
    private static readonly uint Enlightenment = MNKActions.Enlightenment.ActionId;

    [Fact]
    public void BrotherhoodBackWithinOneGcd_HoldsForbiddenChakra()
    {
        var (module, context, scheduler) = Setup(brotherhoodCooldown: 2f);

        module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.DoesNotContain(scheduler.InspectOgcdQueue(), c => c.Behavior.Action.ActionId == ForbiddenChakra);
    }

    [Fact]
    public void BrotherhoodMoreThanOneGcdAway_SpendsForbiddenChakra()
    {
        // GCD is 2.5s. The old hold kept Chakra 1-4 for up to 10s (reachable only with a lower
        // ChakraMinGauge); RSR's hold is one GCD.
        var (module, context, scheduler) = Setup(brotherhoodCooldown: 5f, chakra: 4, chakraMinGauge: 4);

        module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.Contains(scheduler.InspectOgcdQueue(), c => c.Behavior.Action.ActionId == ForbiddenChakra);
    }

    [Fact]
    public void BrotherhoodReady_SpendsForbiddenChakra()
    {
        // RSR spends when Brotherhood isn't cooling down.
        var (module, context, scheduler) = Setup(brotherhoodCooldown: 0f);

        module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.Contains(scheduler.InspectOgcdQueue(), c => c.Behavior.Action.ActionId == ForbiddenChakra);
    }

    [Fact]
    public void BrotherhoodNotLearned_SpendsForbiddenChakra()
    {
        // Level 100 but Brotherhood's quest isn't done: nothing to wait for. The old level-only check
        // held here (with ChakraMinGauge 4).
        var (module, context, scheduler) = Setup(
            brotherhoodCooldown: 2f, chakra: 4, chakraMinGauge: 4, brotherhoodLearned: false);

        module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.Contains(scheduler.InspectOgcdQueue(), c => c.Behavior.Action.ActionId == ForbiddenChakra);
    }

    [Fact]
    public void BelowBrotherhoodLevel_SpendsForbiddenChakra()
    {
        // Lv60: The Forbidden Chakra (54) is learned, Brotherhood (70) isn't.
        var (module, context, scheduler) = Setup(brotherhoodCooldown: 2f, level: 60);

        module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.Contains(scheduler.InspectOgcdQueue(), c => c.Behavior.Action.ActionId == ForbiddenChakra);
    }

    [Fact]
    public void AoE_EnlightenmentNotHeldForBrotherhood()
    {
        // RSR's Enlightenment has no Brotherhood hold.
        var (module, context, scheduler) = Setup(brotherhoodCooldown: 2f, chakra: 4, chakraMinGauge: 4, enemies: 3);

        module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.Contains(scheduler.InspectOgcdQueue(), c => c.Behavior.Action.ActionId == Enlightenment);
    }

    [Fact]
    public void PartyBurstIn8s_NoLongerHoldsForbiddenChakra()
    {
        // The removed hold: its "Chakra < 45" never failed, so a full gauge sat for 8s before every burst.
        var burst = new Mock<IBurstWindowService>();
        burst.Setup(x => x.IsBurstImminent(It.IsAny<float>())).Returns(true);
        burst.Setup(x => x.IsInBurstWindow).Returns(false);
        var (module, context, scheduler) = Setup(brotherhoodCooldown: 60f, burstWindow: burst.Object);

        module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.Contains(scheduler.InspectOgcdQueue(), c => c.Behavior.Action.ActionId == ForbiddenChakra);
    }

    [Fact]
    public void BurstPoolingOff_DoesNotHold()
    {
        var (module, context, scheduler) = Setup(brotherhoodCooldown: 2f, burstPooling: false);

        module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.Contains(scheduler.InspectOgcdQueue(), c => c.Behavior.Action.ActionId == ForbiddenChakra);
    }

    private static (DamageModule module,
        Daedalus.Rotation.KratosCore.Context.IKratosContext context,
        Daedalus.Rotation.Common.Scheduling.RotationScheduler scheduler) Setup(
        float brotherhoodCooldown,
        int chakra = 5,
        int chakraMinGauge = 5,
        byte level = 100,
        bool brotherhoodLearned = true,
        bool burstPooling = true,
        int enemies = 1,
        IBurstWindowService? burstWindow = null)
    {
        var enemy = new Mock<IBattleNpc>();
        enemy.Setup(x => x.GameObjectId).Returns(99999UL);
        enemy.Setup(x => x.CurrentHp).Returns(10000u);
        enemy.Setup(x => x.MaxHp).Returns(10000u);

        var targeting = MockBuilders.CreateMockTargetingService();
        targeting.Setup(x => x.FindEnemyForAction(
                It.IsAny<EnemyTargetingStrategy>(), It.IsAny<uint>(), It.IsAny<IPlayerCharacter>()))
            .Returns(enemy.Object);
        targeting.Setup(x => x.FindEnemy(
                It.IsAny<EnemyTargetingStrategy>(), It.IsAny<float>(), It.IsAny<IPlayerCharacter>()))
            .Returns(enemy.Object);
        MockBuilders.SetupEnemyPackCount(targeting, enemies);

        var config = KratosTestContext.CreateDefaultMonkConfiguration();
        config.Monk.EnableAoERotation = true;
        config.Monk.ChakraMinGauge = chakraMinGauge;
        config.Monk.EnableBurstPooling = burstPooling;

        var brotherhood = MNKActions.Brotherhood.ActionId;
        var actionService = MockBuilders.CreateMockActionService(canExecuteGcd: true);
        actionService.Setup(x => x.GetCooldownRemaining(brotherhood)).Returns(brotherhoodCooldown);
        actionService.Setup(x => x.IsActionLearned(brotherhood)).Returns(brotherhoodLearned);

        var scheduler = SchedulerFactory.CreateForTest(actionService: actionService);
        var context = KratosTestContext.Create(
            config: config,
            actionService: actionService,
            targetingService: targeting,
            level: level,
            chakra: chakra);

        return (new DamageModule(burstWindow), context, scheduler);
    }
}
