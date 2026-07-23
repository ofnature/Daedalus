using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Moq;
using Daedalus.Data;
using Daedalus.Rotation.Common.Scheduling;
using Daedalus.Rotation.PersephoneCore.Abilities;
using Daedalus.Rotation.PersephoneCore.Modules;
using Daedalus.Services.Targeting;
using Daedalus.Tests.Mocks;
using Daedalus.Tests.Rotation.Common.Scheduling;
using Xunit;

namespace Daedalus.Tests.Rotation.PersephoneCore;

/// <summary>
/// Level-80 SMN "sim" (2026-07-23, user question: would the rotation change vs 90+?).
/// Runs the SAME game states through the scheduler at level 80 and level 100 and diffs what the
/// modules queue. The level-80 kit differences the modules must respect:
///   • Aetherflow spender: Fester (until 92 upgrades it to Necrotize)
///   • No Searing Flash (96) — Searing Light windows have one fewer oGCD
///   • No Mountain Buster / Crimson Cyclone→Strike / Slipstream (all 86) — Ifrit/Titan/Garuda
///     phases are bare gemshines at 80
///   • Demi cycle is Bahamut↔Phoenix (Solar Bahamut is 100 and only ever reachable via live
///     status flags the game can't grant at 80 — the module keys off those flags, not level)
/// </summary>
public class PersephoneLevel80SimTests
{
    private static (Daedalus.Rotation.PersephoneCore.Context.IPersephoneContext context, RotationScheduler scheduler)
        Setup(byte level,
            bool hasAetherflow = false, int aetherflowStacks = 0,
            bool hasSearingLight = false, bool hasRubysGlimmer = false,
            bool hasIfritsFavor = false, bool hasGarudasFavor = false,
            bool mountainBusterReady = false, bool hasTitansFavor = false)
    {
        var enemy = new Mock<IBattleNpc>();
        enemy.Setup(x => x.GameObjectId).Returns(4242UL);
        enemy.Setup(x => x.CurrentHp).Returns(1000000u);
        enemy.Setup(x => x.MaxHp).Returns(1000000u);

        var targeting = MockBuilders.CreateMockTargetingService();
        targeting.Setup(x => x.FindEnemyForAction(
                It.IsAny<EnemyTargetingStrategy>(), It.IsAny<uint>(), It.IsAny<IPlayerCharacter>()))
            .Returns(enemy.Object);
        targeting.Setup(x => x.FindEnemy(
                It.IsAny<EnemyTargetingStrategy>(), It.IsAny<float>(), It.IsAny<IPlayerCharacter>()))
            .Returns(enemy.Object);

        var actionService = MockBuilders.CreateMockActionService(canExecuteGcd: true, canExecuteOgcd: true);
        var config = PersephoneTestContext.CreateDefaultSmnConfiguration();
        var scheduler = SchedulerFactory.CreateForTest(config: config, actionService: actionService);
        var context = PersephoneTestContext.Create(
            config: config,
            actionService: actionService,
            targetingService: targeting,
            level: level,
            canExecuteOgcd: true,
            hasAetherflow: hasAetherflow,
            aetherflowStacks: aetherflowStacks,
            hasSearingLight: hasSearingLight,
            searingLightRemaining: hasSearingLight ? 20f : 0f,
            hasRubysGlimmer: hasRubysGlimmer,
            hasIfritsFavor: hasIfritsFavor,
            hasGarudasFavor: hasGarudasFavor,
            mountainBusterReady: mountainBusterReady,
            hasTitansFavor: hasTitansFavor,
            isIfritAttuned: hasIfritsFavor,
            isGarudaAttuned: hasGarudasFavor,
            hasPetSummoned: true); // Carbuncle out — otherwise the DamageModule stops at "summon pet first"

        return (context, scheduler);
    }

    // ── Aetherflow spender: Fester at 80, Necrotize at 92+ ──

    [Fact]
    public void Level80_AetherflowSpender_IsFester()
    {
        var (context, scheduler) = Setup(80, hasAetherflow: true, aetherflowStacks: 2, hasSearingLight: true);
        new BuffModule().CollectCandidates(context, scheduler, isMoving: false);

        var ogcds = scheduler.InspectOgcdQueue();
        Assert.Contains(ogcds, c => c.Behavior == PersephoneAbilities.Fester);
        Assert.DoesNotContain(ogcds, c => c.Behavior == PersephoneAbilities.Necrotize);
    }

    [Fact]
    public void Level100_AetherflowSpender_IsNecrotize()
    {
        var (context, scheduler) = Setup(100, hasAetherflow: true, aetherflowStacks: 2, hasSearingLight: true);
        new BuffModule().CollectCandidates(context, scheduler, isMoving: false);

        var ogcds = scheduler.InspectOgcdQueue();
        Assert.Contains(ogcds, c => c.Behavior == PersephoneAbilities.Necrotize);
        Assert.DoesNotContain(ogcds, c => c.Behavior == PersephoneAbilities.Fester);
    }

    // ── Searing Flash: absent below 96 ──

    [Fact]
    public void Level80_SearingFlash_NeverQueued()
    {
        var (context, scheduler) = Setup(80, hasSearingLight: true, hasRubysGlimmer: true);
        new BuffModule().CollectCandidates(context, scheduler, isMoving: false);

        Assert.DoesNotContain(scheduler.InspectOgcdQueue(), c => c.Behavior == PersephoneAbilities.SearingFlash);
    }

    [Fact]
    public void Level100_SearingFlash_Queued()
    {
        var (context, scheduler) = Setup(100, hasSearingLight: true, hasRubysGlimmer: true);
        new BuffModule().CollectCandidates(context, scheduler, isMoving: false);

        Assert.Contains(scheduler.InspectOgcdQueue(), c => c.Behavior == PersephoneAbilities.SearingFlash);
    }

    // ── Mountain Buster: 86+ only ──

    [Fact]
    public void Level80_MountainBuster_NeverQueued()
    {
        var (context, scheduler) = Setup(80, mountainBusterReady: true, hasTitansFavor: true);
        new BuffModule().CollectCandidates(context, scheduler, isMoving: false);

        Assert.DoesNotContain(scheduler.InspectOgcdQueue(), c => c.Behavior == PersephoneAbilities.MountainBuster);
    }

    [Fact]
    public void Level100_MountainBuster_Queued()
    {
        var (context, scheduler) = Setup(100, mountainBusterReady: true, hasTitansFavor: true);
        new BuffModule().CollectCandidates(context, scheduler, isMoving: false);

        Assert.Contains(scheduler.InspectOgcdQueue(), c => c.Behavior == PersephoneAbilities.MountainBuster);
    }

    // ── Ifrit/Garuda favor GCDs: Crimson Cyclone / Slipstream are 86+ ──

    [Fact]
    public void Level80_IfritFavor_NoCrimsonCyclone()
    {
        var (context, scheduler) = Setup(80, hasIfritsFavor: true);
        new DamageModule().CollectCandidates(context, scheduler, isMoving: false);

        Assert.DoesNotContain(scheduler.InspectGcdQueue(), c => c.Behavior == PersephoneAbilities.CrimsonCyclone);
    }

    [Fact]
    public void Level100_IfritFavor_CrimsonCycloneQueued()
    {
        var (context, scheduler) = Setup(100, hasIfritsFavor: true);
        new DamageModule().CollectCandidates(context, scheduler, isMoving: false);

        Assert.Contains(scheduler.InspectGcdQueue(), c => c.Behavior == PersephoneAbilities.CrimsonCyclone);
    }

    [Fact]
    public void Level80_GarudaFavor_NoSlipstream()
    {
        var (context, scheduler) = Setup(80, hasGarudasFavor: true);
        new DamageModule().CollectCandidates(context, scheduler, isMoving: false);

        Assert.DoesNotContain(scheduler.InspectGcdQueue(), c => c.Behavior == PersephoneAbilities.Slipstream);
    }

    [Fact]
    public void Level100_GarudaFavor_SlipstreamQueued()
    {
        var (context, scheduler) = Setup(100, hasGarudasFavor: true);
        new DamageModule().CollectCandidates(context, scheduler, isMoving: false);

        Assert.Contains(scheduler.InspectGcdQueue(), c => c.Behavior == PersephoneAbilities.Slipstream);
    }
}
