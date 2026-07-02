using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Moq;
using Daedalus.Data;
using Daedalus.Rotation.CirceCore.Abilities;
using Daedalus.Rotation.CirceCore.Modules;
using Daedalus.Services;
using Daedalus.Services.Targeting;
using Daedalus.Tests.Mocks;
using Daedalus.Tests.Rotation.Common.Scheduling;
using Xunit;

namespace Daedalus.Tests.Rotation.CirceCore.Modules;

/// <summary>
/// Solo burst chain regression tests (Vanguard log 2026-07-02): Embolden fired unpaired,
/// Manafication deadlocked on "Hold for Embolden CD" forever, and the melee combo was parked
/// through the live Embolden window. The chain must fire Manafication → Embolden → combo, and
/// every hold must have an escape when the thing it waits for cannot happen.
/// </summary>
public class BuffModuleSoloBurstTests
{
    [Fact]
    public void SoloBurst_Manafication_FiresIntoActiveEmbolden_IgnoringManaGates()
    {
        var (module, context, scheduler) = Setup(hasEmbolden: true, manaficationReady: true, lowerMana: 10);

        module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.Contains(scheduler.InspectOgcdQueue(), c => c.Behavior == CirceAbilities.Manafication);
    }

    [Fact]
    public void SoloBurst_Manafication_FiresOnCooldown_WhenEmboldenDesynced()
    {
        // Embolden 60s away: pairing is impossible this window — never drift behind it.
        var (module, context, scheduler) = Setup(
            manaficationReady: true, emboldenReady: false, emboldenCd: 60f, lowerMana: 30);

        module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.Contains(scheduler.InspectOgcdQueue(), c => c.Behavior == CirceAbilities.Manafication);
    }

    [Fact]
    public void SoloBurst_Manafication_HeldBelowManaFloor_WhenEmboldenDesynced()
    {
        var (module, context, scheduler) = Setup(
            manaficationReady: true, emboldenReady: false, emboldenCd: 60f, lowerMana: 20);

        module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.DoesNotContain(scheduler.InspectOgcdQueue(), c => c.Behavior == CirceAbilities.Manafication);
    }

    [Fact]
    public void SoloBurst_Embolden_Held_WhileManaficationReadyUnfired()
    {
        // Mana still building: Manafication holds for mana, and Embolden must wait for it
        // instead of burning the window (chain order Manafication → Embolden).
        var (module, context, scheduler) = Setup(
            emboldenReady: true, manaficationReady: true, lowerMana: 20);

        module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.DoesNotContain(scheduler.InspectOgcdQueue(), c => c.Behavior == CirceAbilities.Embolden);
    }

    [Fact]
    public void SoloBurst_Embolden_Fires_WhenManaficationDesynced()
    {
        var (module, context, scheduler) = Setup(
            emboldenReady: true, manaficationReady: false, manaficationCd: 60f, lowerMana: 20);

        module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.Contains(scheduler.InspectOgcdQueue(), c => c.Behavior == CirceAbilities.Embolden);
    }

    [Fact]
    public void SoloBurst_ManaReady_ManaficationFires_EmboldenWaitsOneCycle()
    {
        var (module, context, scheduler) = Setup(
            emboldenReady: true, manaficationReady: true, lowerMana: 80);

        module.CollectCandidates(context, scheduler, isMoving: false);

        var ogcds = scheduler.InspectOgcdQueue();
        Assert.Contains(ogcds, c => c.Behavior == CirceAbilities.Manafication);
        Assert.DoesNotContain(ogcds, c => c.Behavior == CirceAbilities.Embolden);
    }

    [Fact]
    public void SoloBurst_Embolden_FollowsActiveManafication_EvenWhenPackNotViable()
    {
        // Regression (Mistwake log 2026-07-02): Manafication fired while the pack was viable,
        // mobs died one GCD later, and Embolden was then held on "pack dying/small" forever —
        // Manafication burned alone. Once the chain leader has fired, the follower goes out.
        var (module, context, scheduler) = Setup(
            emboldenReady: true, hasManafication: true, enemiesNearTarget: 1, targetCurrentHp: 100000u);

        module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.Contains(scheduler.InspectOgcdQueue(), c => c.Behavior == CirceAbilities.Embolden);
    }

    [Fact]
    public void SoloBurst_Manafication_FiresIntoActiveEmbolden_EvenWhenPackNotViable()
    {
        var (module, context, scheduler) = Setup(
            manaficationReady: true, hasEmbolden: true, lowerMana: 10, enemiesNearTarget: 1, targetCurrentHp: 100000u);

        module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.Contains(scheduler.InspectOgcdQueue(), c => c.Behavior == CirceAbilities.Manafication);
    }

    [Fact]
    public void SoloBurst_FillerOgcds_YieldWeaveSlot_WhileEmboldenFollows()
    {
        // Regression (Mistwake log 2026-07-02): Contre Sixte and Prefulgence (priority 1) ate
        // the scarce hardcast-phase weave slots and delayed Embolden 15s behind Manafication.
        var (module, context, scheduler) = Setup(
            emboldenReady: true, hasManafication: true, flecheReady: true, contreSixteReady: true);

        module.CollectCandidates(context, scheduler, isMoving: false);

        var ogcds = scheduler.InspectOgcdQueue();
        Assert.Contains(ogcds, c => c.Behavior == CirceAbilities.Embolden);
        Assert.DoesNotContain(ogcds, c => c.Behavior == CirceAbilities.Fleche);
        Assert.DoesNotContain(ogcds, c => c.Behavior == CirceAbilities.ContreSixte);
    }

    [Fact]
    public void Engagement_NotPushed_WhenOutOfMeleeRange()
    {
        // Regression (Mistwake log 2026-07-02): Engagement (3y) dispatched from caster range is
        // queue-accepted then dropped by the game — no charge consumed — so it re-fired every
        // weave slot as a phantom. Manafication extends the sword GCDs to 25y, not Engagement.
        var (module, context, scheduler) = Setup(engagementCharges: 2, targetDistance: 20f);

        module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.DoesNotContain(scheduler.InspectOgcdQueue(), c => c.Behavior == CirceAbilities.Engagement);
    }

    [Fact]
    public void Engagement_Pushed_WhenInMeleeRange()
    {
        var (module, context, scheduler) = Setup(engagementCharges: 2, targetDistance: 2f);

        module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.Contains(scheduler.InspectOgcdQueue(), c => c.Behavior == CirceAbilities.Engagement);
    }

    [Fact]
    public void Prefulgence_OutranksFleche_ForTheWeaveSlot()
    {
        // Regression (Mistwake log 2026-07-02): both at priority 1, Fleche took the two
        // post-Embolden weave slots and the 900p Prefulgence proc timed out with the pull.
        var (module, context, scheduler) = Setup(
            hasEmbolden: true, hasManafication: true, flecheReady: true, hasPrefulgenceReady: true);

        module.CollectCandidates(context, scheduler, isMoving: false);

        var ogcds = scheduler.InspectOgcdQueue();
        var prefulgence = Assert.Single(ogcds, c => c.Behavior == CirceAbilities.Prefulgence);
        var fleche = Assert.Single(ogcds, c => c.Behavior == CirceAbilities.Fleche);
        Assert.True(prefulgence.Priority < fleche.Priority);
    }

    private static (BuffModule module, Daedalus.Rotation.CirceCore.Context.ICirceContext context,
        Daedalus.Rotation.Common.Scheduling.RotationScheduler scheduler) Setup(
        bool emboldenReady = false,
        bool manaficationReady = false,
        bool hasEmbolden = false,
        bool hasManafication = false,
        bool flecheReady = false,
        bool contreSixteReady = false,
        bool hasPrefulgenceReady = false,
        float emboldenCd = 120f,
        float manaficationCd = 120f,
        int lowerMana = 0,
        int enemiesNearTarget = 3,
        uint targetCurrentHp = 1000000u,
        float targetDistance = 0f,
        int engagementCharges = 0)
    {
        var burst = new Mock<IBurstWindowService>();
        burst.Setup(b => b.UseSoloBurstFallback).Returns(true);
        var module = new BuffModule(burst.Object);

        var enemy = CreateMockEnemy(currentHp: targetCurrentHp, distance: targetDistance);
        // countEnemiesInRange feeds CountEnemiesInRangeOfTarget → pack viability (min 2 enemies).
        var targeting = MockBuilders.CreateMockTargetingService(countEnemiesInRange: enemiesNearTarget);
        targeting.Setup(x => x.FindEnemy(
                It.IsAny<EnemyTargetingStrategy>(), It.IsAny<float>(), It.IsAny<IPlayerCharacter>()))
            .Returns(enemy.Object);

        var actionService = MockBuilders.CreateMockActionService();
        actionService.Setup(x => x.GetCooldownRemaining(RDMActions.Embolden.ActionId)).Returns(emboldenCd);
        actionService.Setup(x => x.GetCooldownRemaining(RDMActions.Manafication.ActionId)).Returns(manaficationCd);

        var scheduler = SchedulerFactory.CreateForTest(actionService: actionService);
        var context = CirceTestContext.Create(
            actionService: actionService,
            targetingService: targeting,
            level: 100,
            emboldenReady: emboldenReady,
            manaficationReady: manaficationReady,
            hasEmbolden: hasEmbolden,
            hasManafication: hasManafication,
            flecheReady: flecheReady,
            contreSixteReady: contreSixteReady,
            hasPrefulgenceReady: hasPrefulgenceReady,
            engagementCharges: engagementCharges,
            lowerMana: lowerMana);

        return (module, context, scheduler);
    }

    private static Mock<IBattleNpc> CreateMockEnemy(
        ulong objectId = 99999UL, uint currentHp = 1000000u, float distance = 0f)
    {
        var mock = new Mock<IBattleNpc>();
        mock.Setup(x => x.GameObjectId).Returns(objectId);
        mock.Setup(x => x.CurrentHp).Returns(currentHp);
        mock.Setup(x => x.MaxHp).Returns(1000000u);
        mock.Setup(x => x.Position).Returns(new System.Numerics.Vector3(distance, 0f, 0f));
        mock.Setup(x => x.HitboxRadius).Returns(0.5f);
        return mock;
    }
}
