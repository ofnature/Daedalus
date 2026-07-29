using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Moq;
using Daedalus.Data;
using Daedalus.Rotation.Common.Scheduling;
using Daedalus.Rotation.NikeCore.Abilities;
using Daedalus.Rotation.NikeCore.Modules;
using Daedalus.Services.Targeting;
using Daedalus.Tests.Mocks;
using Daedalus.Tests.Rotation.Common.Scheduling;
using Xunit;

namespace Daedalus.Tests.Rotation.NikeCore.Modules;

/// <summary>
/// Field report 2026-07-28 (Mistwake, first Lv100 SAM content): while the Tendo status (Meikyo at
/// 100) was up, plain Midare/Tenka submits were rejected server-side with 572 "Cannot use yet."
/// for the whole window — RSR gates the plain Iaijutsu on <c>!HasTendo</c> and the game's button
/// replacement is anchored on the Iaijutsu BUTTON id, so the module must pick the Tendo variants
/// itself.
/// </summary>
public class NikeTendoIaijutsuTests
{
    private readonly DamageModule _module = new();

    private RotationScheduler Collect(bool hasTendo, byte level = 100,
        SAMActions.SenType sen = SAMActions.SenType.Setsu | SAMActions.SenType.Getsu | SAMActions.SenType.Ka)
    {
        var enemy = new Mock<IBattleNpc>();
        enemy.Setup(x => x.GameObjectId).Returns(99999UL);
        enemy.Setup(x => x.CurrentHp).Returns(100000u);
        enemy.Setup(x => x.MaxHp).Returns(100000u);

        var targeting = MockBuilders.CreateMockTargetingService();
        targeting.Setup(x => x.FindEnemyForAction(
                It.IsAny<EnemyTargetingStrategy>(), It.IsAny<uint>(), It.IsAny<IPlayerCharacter>()))
            .Returns(enemy.Object);
        targeting.Setup(x => x.FindEnemy(
                It.IsAny<EnemyTargetingStrategy>(), It.IsAny<float>(), It.IsAny<IPlayerCharacter>()))
            .Returns(enemy.Object);

        var actionService = MockBuilders.CreateMockActionService();
        actionService.Setup(x => x.PlayerHasStatus(SAMActions.StatusIds.Tendo)).Returns(hasTendo);

        var config = NikeTestContext.CreateDefaultSamuraiConfiguration();
        var scheduler = SchedulerFactory.CreateForTest(config: config, actionService: actionService);
        var context = NikeTestContext.Create(
            config: config,
            actionService: actionService,
            targetingService: targeting,
            level: level,
            hasFugetsu: true,
            fugetsuRemaining: 30f,
            hasFuka: true,
            fukaRemaining: 30f,
            sen: sen);

        _module.CollectCandidates(context, scheduler, isMoving: false);
        return scheduler;
    }

    [Fact]
    public void ThreeSen_UnderTendo_PushesTendoSetsugekka_NotMidare()
    {
        var scheduler = Collect(hasTendo: true);
        Assert.Contains(scheduler.InspectGcdQueue(), c => c.Behavior == NikeAbilities.TendoSetsugekka);
        Assert.DoesNotContain(scheduler.InspectGcdQueue(), c => c.Behavior == NikeAbilities.MidareSetsugekka);
    }

    [Fact]
    public void ThreeSen_WithoutTendo_PushesPlainMidare()
    {
        var scheduler = Collect(hasTendo: false);
        Assert.Contains(scheduler.InspectGcdQueue(), c => c.Behavior == NikeAbilities.MidareSetsugekka);
        Assert.DoesNotContain(scheduler.InspectGcdQueue(), c => c.Behavior == NikeAbilities.TendoSetsugekka);
    }

    [Fact]
    public void SubHundred_TendoStatusIgnored_StillPlainMidare()
    {
        // Tendo only exists at 100 — a stray status read below the trait level must not divert.
        var scheduler = Collect(hasTendo: true, level: 90);
        Assert.Contains(scheduler.InspectGcdQueue(), c => c.Behavior == NikeAbilities.MidareSetsugekka);
        Assert.DoesNotContain(scheduler.InspectGcdQueue(), c => c.Behavior == NikeAbilities.TendoSetsugekka);
    }

    [Fact]
    public void TendoIaijutsuBehaviors_CarrySpenderGuards()
    {
        // Same full-gauge-spender protections as the plain versions.
        Assert.True(NikeAbilities.TendoSetsugekka.BlockImmediateRepeat);
        Assert.True(NikeAbilities.TendoGoken.BlockImmediateRepeat);
        Assert.Equal(SAMActions.TendoSetsugekka.ActionId, NikeAbilities.TendoSetsugekka.Action.ActionId);
        Assert.Equal(SAMActions.TendoGoken.ActionId, NikeAbilities.TendoGoken.Action.ActionId);
    }
}
