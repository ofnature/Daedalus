using System;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Moq;
using Daedalus.Config.DPS;
using Daedalus.Rotation.Common.Scheduling;
using Daedalus.Rotation.HecateCore.Modules;
using Daedalus.Services.Action;
using Daedalus.Services.Targeting;
using Daedalus.Tests.Mocks;
using Daedalus.Tests.Rotation.Common.Scheduling;

namespace Daedalus.Tests.Rotation.HecateCore.Modules;

/// <summary>
/// One-call DamageModule scenario for BLM phase tests: builds the enemy/targeting/action mocks,
/// runs CollectCandidates and returns the scheduler for queue inspection.
/// </summary>
internal static class HecateDamageScenario
{
    public static RotationScheduler Run(
        byte level,
        bool inAstralFire = false,
        bool inUmbralIce = false,
        int elementStacks = 3,
        int currentMp = 10000,
        int umbralHearts = 0,
        int astralSoulStacks = 0,
        int polyglotStacks = 0,
        bool hasParadox = false,
        float elementTimer = 15f,
        bool manafontReady = false,
        bool hasThunderhead = false,
        float thunderheadRemaining = 0f,
        bool hasThunderDoT = true,
        float thunderDoTRemaining = 20f,
        uint targetHp = 100000u,
        int packSize = 1,
        bool isMoving = false,
        uint[]? notLearned = null,
        Action<BlackMageConfig>? configure = null)
    {
        var enemy = new Mock<IBattleNpc>();
        enemy.Setup(x => x.GameObjectId).Returns(99999UL);
        enemy.Setup(x => x.CurrentHp).Returns(targetHp);
        enemy.Setup(x => x.MaxHp).Returns(100000u);

        var targeting = MockBuilders.CreateMockTargetingService();
        targeting.Setup(x => x.FindEnemy(
                It.IsAny<EnemyTargetingStrategy>(), It.IsAny<float>(), It.IsAny<IPlayerCharacter>()))
            .Returns(enemy.Object);
        MockBuilders.SetupEnemyPackCount(targeting, packSize);

        var actionService = MockBuilders.CreateMockActionService();
        actionService.Setup(x => x.IsActionReady(It.IsAny<uint>())).Returns(true);
        foreach (var id in notLearned ?? Array.Empty<uint>())
            actionService.Setup(x => x.IsActionLearned(id)).Returns(false);

        var config = HecateTestContext.CreateDefaultBlmConfiguration();
        configure?.Invoke(config.BlackMage);

        var scheduler = SchedulerFactory.CreateForTest(actionService: actionService);
        var context = HecateTestContext.Create(
            config: config,
            actionService: actionService,
            targetingService: targeting,
            level: level,
            currentMp: currentMp,
            mpPercent: currentMp / 10000f,
            inAstralFire: inAstralFire,
            inUmbralIce: inUmbralIce,
            elementStacks: elementStacks,
            astralFireStacks: inAstralFire ? elementStacks : 0,
            umbralIceStacks: inUmbralIce ? elementStacks : 0,
            elementTimer: elementTimer,
            isEnochianActive: inAstralFire || inUmbralIce,
            umbralHearts: umbralHearts,
            astralSoulStacks: astralSoulStacks,
            polyglotStacks: polyglotStacks,
            hasParadox: hasParadox,
            manafontReady: manafontReady,
            hasThunderhead: hasThunderhead,
            thunderheadRemaining: thunderheadRemaining,
            hasThunderDoT: hasThunderDoT,
            thunderDoTRemaining: thunderDoTRemaining);

        new DamageModule().CollectCandidates(context, scheduler, isMoving);
        return scheduler;
    }
}
