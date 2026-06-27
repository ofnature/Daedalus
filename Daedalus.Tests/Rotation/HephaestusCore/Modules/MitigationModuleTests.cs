using Dalamud.Game.ClientState.Objects.Types;
using Moq;
using Daedalus.Data;
using Daedalus.Rotation.Common.Scheduling;
using Daedalus.Rotation.HephaestusCore.Abilities;
using Daedalus.Rotation.HephaestusCore.Context;
using Daedalus.Rotation.HephaestusCore.Helpers;
using Daedalus.Rotation.HephaestusCore.Modules;
using Daedalus.Services.Party;
using Daedalus.Services.Tank;
using Daedalus.Services.Training;
using Daedalus.Tests.Mocks;
using Daedalus.Tests.Rotation.Common.Scheduling;

namespace Daedalus.Tests.Rotation.HephaestusCore.Modules;


public class MitigationModuleCollectCandidatesTests
{
    private readonly MitigationModule _module = new();

    // Test 1: EnableMitigation=false pushes nothing
    [Fact]
    public void CollectCandidates_MitigationDisabled_PushesNothing()
    {
        var config = HephaestusTestContext.CreateDefaultGunbreakerConfiguration();
        config.Tank.EnableMitigation = false;

        var scheduler = SchedulerFactory.CreateForTest();
        var context = CreateContext(inCombat: true, config: config);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.Empty(scheduler.InspectOgcdQueue());
        Assert.Equal("Disabled", context.Debug.MitigationState);
    }

    // Test 2: Not in combat pushes nothing
    [Fact]
    public void CollectCandidates_NotInCombat_PushesNothing()
    {
        var scheduler = SchedulerFactory.CreateForTest();
        var context = CreateContext(inCombat: false);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.Empty(scheduler.InspectOgcdQueue());
        Assert.Equal("Not in combat", context.Debug.MitigationState);
    }

    // Test 3: Superbolide fires at critical HP (priority 2)
    [Fact]
    public void CollectCandidates_CriticallyLowHp_PushesSuperbolideAtPriority2()
    {
        var scheduler = SchedulerFactory.CreateForTest();
        // HP at 10% of 50000 = 5000
        var context = CreateContext(inCombat: true, currentHp: 5000, maxHp: 50000);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        var queue = scheduler.InspectOgcdQueue();
        Assert.Contains(queue, c => c.Behavior == GnbAbilities.Superbolide && c.Priority == 2);
    }

    // Test 4: Superbolide not pushed when HP is healthy
    [Fact]
    public void CollectCandidates_HealthyHp_SuperbolideNotPushed()
    {
        var scheduler = SchedulerFactory.CreateForTest();
        // HP at 50% — above 15% threshold
        var context = CreateContext(inCombat: true, currentHp: 25000, maxHp: 50000);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.DoesNotContain(scheduler.InspectOgcdQueue(), c => c.Behavior == GnbAbilities.Superbolide);
    }

    // Test 5: HoC not pushed when target is under Superbolide (module early-returns on HasSuperbolide)
    [Fact]
    public void CollectCandidates_SuperbolideActive_PushesNothing()
    {
        var scheduler = SchedulerFactory.CreateForTest();
        var context = CreateContext(inCombat: true, hasSuperbolide: true);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.Empty(scheduler.InspectOgcdQueue());
        Assert.Equal("Superbolide active", context.Debug.MitigationState);
    }

    // Test 6: Nebula pushed when ShouldUseMajorCooldown is true
    [Fact]
    public void CollectCandidates_ShouldUseMajorCooldown_PushesNebulaAtPriority4()
    {
        var tankCooldown = new Mock<ITankCooldownService>();
        tankCooldown.Setup(x => x.ShouldUseMajorCooldown(It.IsAny<float>(), It.IsAny<float>())).Returns(true);
        tankCooldown.Setup(x => x.ShouldUseMitigation(It.IsAny<float>(), It.IsAny<float>(), It.IsAny<bool>())).Returns(false);

        var scheduler = SchedulerFactory.CreateForTest();
        var context = CreateContext(inCombat: true, tankCooldownService: tankCooldown);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.Contains(scheduler.InspectOgcdQueue(), c => c.Behavior == GnbAbilities.Nebula && c.Priority == 4);
    }

    // Test 7: HeartOfLight not pushed when party is healthy
    [Fact]
    public void CollectCandidates_PartyHealthy_HeartOfLightNotPushed()
    {
        // avgHp > 0.85 and injuredCount < 3 means no Heart of Light
        var scheduler = SchedulerFactory.CreateForTest();
        var context = CreateContext(
            inCombat: true,
            avgPartyHp: 1.0f,
            injuredPartyCount: 0);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.DoesNotContain(scheduler.InspectOgcdQueue(), c => c.Behavior == GnbAbilities.HeartOfLight);
    }

    // Test 8: HeartOfLight pushed when party is injured
    [Fact]
    public void CollectCandidates_PartyInjured_PushesHeartOfLightAtPriority8()
    {
        // 4 injured members and avg HP below 0.85 triggers HeartOfLight
        var scheduler = SchedulerFactory.CreateForTest();
        var context = CreateContext(
            inCombat: true,
            avgPartyHp: 0.70f,
            injuredPartyCount: 4);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.Contains(scheduler.InspectOgcdQueue(), c => c.Behavior == GnbAbilities.HeartOfLight && c.Priority == 8);
    }

    // Test 9: Reprisal pushed when party is injured and there is a current target
    [Fact]
    public void CollectCandidates_PartyInjuredWithTarget_PushesReprisalAtPriority9()
    {
        var enemy = new Mock<IBattleChara>();
        enemy.Setup(x => x.GameObjectId).Returns(99999UL);
        enemy.Setup(x => x.EntityId).Returns(99999u);

        var scheduler = SchedulerFactory.CreateForTest();
        var context = CreateContext(
            inCombat: true,
            currentTarget: enemy.Object,
            avgPartyHp: 0.70f,
            injuredPartyCount: 4);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.Contains(scheduler.InspectOgcdQueue(), c => c.Behavior == GnbAbilities.Reprisal && c.Priority == 9);
    }

    // Test 10: Reprisal not pushed when no current target
    [Fact]
    public void CollectCandidates_PartyInjuredNoTarget_ReprisalNotPushed()
    {
        var scheduler = SchedulerFactory.CreateForTest();
        var context = CreateContext(
            inCombat: true,
            currentTarget: null,
            avgPartyHp: 0.70f,
            injuredPartyCount: 4);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.DoesNotContain(scheduler.InspectOgcdQueue(), c => c.Behavior == GnbAbilities.Reprisal);
    }

    // Test 11: Camouflage fires proactively on a wall-to-wall pull even at healthy HP.
    [Fact]
    public void CollectCandidates_BigPullHealthyHp_PushesCamouflageProactively()
    {
        // HP healthy (100%) — the reactive HP gate would NOT fire Camouflage. A 5-mob pull should.
        var scheduler = SchedulerFactory.CreateForTest();
        var context = CreateContext(inCombat: true, currentHp: 50000, maxHp: 50000, enemyPackCount: 5);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.Contains(scheduler.InspectOgcdQueue(), c => c.Behavior == GnbAbilities.Camouflage);
    }

    // Test 12: Camouflage NOT fired at healthy HP on a small pull (proactive trigger respects min targets).
    [Fact]
    public void CollectCandidates_SmallPullHealthyHp_CamouflageNotPushed()
    {
        // 2 mobs is below the default ProactiveMitMinTargets of 3, and HP is healthy.
        var scheduler = SchedulerFactory.CreateForTest();
        var context = CreateContext(inCombat: true, currentHp: 50000, maxHp: 50000, enemyPackCount: 2);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.DoesNotContain(scheduler.InspectOgcdQueue(), c => c.Behavior == GnbAbilities.Camouflage);
    }

    // Test 13: Nebula fires proactively on a wall-to-wall pull even when the reactive major-CD gate is false.
    [Fact]
    public void CollectCandidates_BigPull_PushesNebulaProactively()
    {
        // tankCooldownService defaults ShouldUseMajorCooldown=false, so only the pull trigger can fire Nebula.
        var scheduler = SchedulerFactory.CreateForTest();
        var context = CreateContext(inCombat: true, currentHp: 50000, maxHp: 50000, enemyPackCount: 5);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.Contains(scheduler.InspectOgcdQueue(), c => c.Behavior == GnbAbilities.Nebula && c.Priority == 4);
    }

    // Test 14: Rampart fires proactively on a wall-to-wall pull when no mitigation is active.
    [Fact]
    public void CollectCandidates_BigPullNoActiveMit_PushesRampartProactively()
    {
        var scheduler = SchedulerFactory.CreateForTest();
        var context = CreateContext(inCombat: true, currentHp: 50000, maxHp: 50000,
            hasActiveMitigation: false, enemyPackCount: 5);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.Contains(scheduler.InspectOgcdQueue(), c => c.Behavior == GnbAbilities.Rampart);
    }

    #region Helpers

    private static IHephaestusContext CreateContext(
        bool inCombat,
        byte level = 100,
        uint currentHp = 50000,
        uint maxHp = 50000,
        Configuration? config = null,
        bool hasSuperbolide = false,
        bool hasNebula = false,
        bool hasHeartOfCorundum = false,
        bool hasCamouflage = false,
        bool hasAurora = false,
        bool hasActiveMitigation = false,
        float avgPartyHp = 1.0f,
        int injuredPartyCount = 0,
        IBattleChara? currentTarget = null,
        int enemyPackCount = 0,
        Mock<ITankCooldownService>? tankCooldownService = null)
    {
        config ??= HephaestusTestContext.CreateDefaultGunbreakerConfiguration();

        var player = MockBuilders.CreateMockPlayerCharacter(level: level, currentHp: currentHp, maxHp: maxHp);
        player.Setup(x => x.StatusList).Returns((Dalamud.Game.ClientState.Statuses.StatusList?)null!);
        player.Setup(x => x.Position).Returns(System.Numerics.Vector3.Zero);

        var damageIntakeService = MockBuilders.CreateMockDamageIntakeService();
        var targetingService = MockBuilders.CreateMockTargetingService(countEnemiesInRange: enemyPackCount);

        if (tankCooldownService == null)
        {
            tankCooldownService = new Mock<ITankCooldownService>();
            tankCooldownService.Setup(x => x.ShouldUseMitigation(It.IsAny<float>(), It.IsAny<float>(), It.IsAny<bool>())).Returns(false);
            tankCooldownService.Setup(x => x.ShouldUseMajorCooldown(It.IsAny<float>(), It.IsAny<float>())).Returns(false);
            tankCooldownService.Setup(x => x.ShouldUseShortCooldown(It.IsAny<float>(), It.IsAny<int>(), It.IsAny<int>())).Returns(false);
        }

        var statusHelper = new HephaestusStatusHelper();

        var partyHelper = new HephaestusPartyHelper(
            MockBuilders.CreateMockObjectTable().Object,
            MockBuilders.CreateMockPartyList().Object);

        var debugState = new HephaestusDebugState();

        var mock = new Mock<IHephaestusContext>();
        mock.Setup(x => x.Player).Returns(player.Object);
        mock.Setup(x => x.InCombat).Returns(inCombat);
        mock.Setup(x => x.IsMoving).Returns(false);
        mock.Setup(x => x.CanExecuteGcd).Returns(true);
        mock.Setup(x => x.CanExecuteOgcd).Returns(true);
        mock.Setup(x => x.Configuration).Returns(config);
        mock.Setup(x => x.ActionService).Returns(MockBuilders.CreateMockActionService().Object);
        mock.Setup(x => x.DamageIntakeService).Returns(damageIntakeService.Object);
        mock.Setup(x => x.TankCooldownService).Returns(tankCooldownService.Object);
        mock.Setup(x => x.TargetingService).Returns(targetingService.Object);
        mock.Setup(x => x.TrainingService).Returns((ITrainingService?)null);
        mock.Setup(x => x.PartyCoordinationService).Returns((IPartyCoordinationService?)null);
        mock.Setup(x => x.TimelineService).Returns((Daedalus.Timeline.ITimelineService?)null);
        mock.Setup(x => x.HasSuperbolide).Returns(hasSuperbolide);
        mock.Setup(x => x.HasNebula).Returns(hasNebula);
        mock.Setup(x => x.HasHeartOfCorundum).Returns(hasHeartOfCorundum);
        mock.Setup(x => x.HasCamouflage).Returns(hasCamouflage);
        mock.Setup(x => x.HasAurora).Returns(hasAurora);
        mock.Setup(x => x.HasActiveMitigation).Returns(hasActiveMitigation);
        mock.Setup(x => x.IsMainTank).Returns(false);
        mock.Setup(x => x.CurrentTarget).Returns(currentTarget);
        mock.Setup(x => x.StatusHelper).Returns(statusHelper);
        mock.Setup(x => x.PartyHelper).Returns(partyHelper);
        mock.Setup(x => x.Debug).Returns(debugState);
        mock.Setup(x => x.PartyHealthMetrics).Returns((avgPartyHp, avgPartyHp, injuredPartyCount));
        mock.Setup(x => x.ObjectTable).Returns((Dalamud.Plugin.Services.IObjectTable?)null);

        return mock.Object;
    }

    #endregion
}
