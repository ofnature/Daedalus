using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Moq;
using Daedalus.Data;
using Daedalus.Rotation.HermesCore.Helpers;
using Daedalus.Rotation.HermesCore.Modules;
using Daedalus.Services.Targeting;
using Daedalus.Tests.Mocks;
using Daedalus.Tests.Rotation.Common.Scheduling;
using Xunit;

namespace Daedalus.Tests.Rotation.HermesCore.Modules;

/// <summary>
/// Leaving combat must clear the mudra sequence even when NO mudra has been pressed yet.
/// A sequence goes active the moment an aim is picked, so MudraCount == 0 is the state holding
/// a DECISION made against the last pull's enemy count and Kassatsu state. It used to survive
/// combat ending, and a re-pull inside MudraHelper's 7s window resumed the stale aim — Katon at
/// a single mob, or a Goka Mekkyaku aim with Kassatsu long gone. Chain-pulling trash hits that
/// window constantly. Matches RSR `cb2e8fbc`, which made its equivalent clear unconditional.
/// </summary>
[Collection("HermesMudraStaticState")] // HermesNinjutsuMudraExecutor holds per-frame statics
public class NinjutsuModuleCombatEndTests
{
    private static Daedalus.Rotation.HermesCore.Context.IHermesContext OutOfCombat(MudraHelper helper)
    {
        var enemy = new Mock<IBattleNpc>();
        enemy.Setup(x => x.GameObjectId).Returns(99999UL);

        var targeting = MockBuilders.CreateMockTargetingService();
        targeting.Setup(x => x.FindEnemy(
                It.IsAny<EnemyTargetingStrategy>(), It.IsAny<float>(), It.IsAny<IPlayerCharacter>()))
            .Returns(enemy.Object);
        MockBuilders.SetupEnemyPackCount(targeting, aoeRange: 1);

        return HermesTestContext.Create(
            actionService: MockBuilders.CreateMockActionService(),
            targetingService: targeting,
            mudraHelper: helper,
            canExecuteGcd: true,
            inCombat: false);
    }

    [Fact]
    public void CombatEnds_WithAimButNoMudraPressed_ClearsTheSequence()
    {
        var helper = new MudraHelper();
        helper.StartSequence(NINActions.NinjutsuType.Katon); // AoE aim from the pull that just died
        Assert.True(helper.IsSequenceActive);
        Assert.Equal(0, helper.MudraCount);

        new NinjutsuModule().CollectCandidates(
            OutOfCombat(helper), SchedulerFactory.CreateForTest(), isMoving: false);

        Assert.False(helper.IsSequenceActive);
        Assert.Equal(NINActions.NinjutsuType.None, helper.TargetNinjutsu);
    }

    /// <summary>
    /// Nothing was committed to the game, so there is no desync to back off from — arming the
    /// abort cooldown here would only delay the NEXT pull's opening ninjutsu by ~45 frames.
    /// </summary>
    [Fact]
    public void CombatEnds_WithNoMudraPressed_DoesNotArmTheAbortCooldown()
    {
        var helper = new MudraHelper();
        helper.StartSequence(NINActions.NinjutsuType.Katon);
        var context = OutOfCombat(helper);

        new NinjutsuModule().CollectCandidates(context, SchedulerFactory.CreateForTest(), isMoving: false);

        Assert.Equal(0, context.Debug.NinjutsuAbortCooldownFrames);
    }

    /// <summary>
    /// A half-pressed sequence DID register signs with the game, so that path keeps its cooldown
    /// — this is the pre-existing behaviour and must not regress.
    /// </summary>
    [Fact]
    public void CombatEnds_WithMudraAlreadyPressed_ClearsAndKeepsTheCooldown()
    {
        var helper = new MudraHelper();
        helper.StartSequence(NINActions.NinjutsuType.Suiton);
        helper.NotifyMudraPressed();
        Assert.Equal(1, helper.MudraCount);

        var context = OutOfCombat(helper);
        new NinjutsuModule().CollectCandidates(context, SchedulerFactory.CreateForTest(), isMoving: false);

        Assert.False(helper.IsSequenceActive);
        Assert.True(context.Debug.NinjutsuAbortCooldownFrames > 0);
    }

    [Fact]
    public void CombatEnds_WithNoSequence_IsANoOp()
    {
        var helper = new MudraHelper();
        var context = OutOfCombat(helper);

        new NinjutsuModule().CollectCandidates(context, SchedulerFactory.CreateForTest(), isMoving: false);

        Assert.False(helper.IsSequenceActive);
        Assert.Equal(0, context.Debug.NinjutsuAbortCooldownFrames);
        Assert.Equal("Not in combat", context.Debug.NinjutsuState);
    }

    /// <summary>
    /// The regression this fixes end to end: the stale AoE aim must not survive into a fresh
    /// single-target pull that starts inside the 7s sequence window.
    /// </summary>
    [Fact]
    public void StaleAoeAim_DoesNotSurviveIntoTheNextPull()
    {
        var helper = new MudraHelper();
        helper.StartSequence(NINActions.NinjutsuType.Katon);

        new NinjutsuModule().CollectCandidates(
            OutOfCombat(helper), SchedulerFactory.CreateForTest(), isMoving: false);

        Assert.NotEqual(NINActions.NinjutsuType.Katon, helper.TargetNinjutsu);
    }
}
