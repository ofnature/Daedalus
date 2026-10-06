using Dalamud.Game.ClientState.Objects.Types;
using Daedalus.Config;
using Daedalus.Data;
using Daedalus.Models.Action;
using Daedalus.Rotation.AstraeaCore.Modules;
using Daedalus.Rotation.AstraeaCore.Modules.Healing;
using Daedalus.Services.Targeting;
using Daedalus.Tests.Mocks;
using Daedalus.Tests.Rotation.Common.Scheduling;
using Moq;
using Xunit;

namespace Daedalus.Tests.Rotation.AstraeaCore;

/// <summary>
/// Astrologian synced to 71 (Holminster Switch). Every value in <see cref="GameData"/> was read from the
/// game's own Action and Status sheets on 2026-10-05; the kit had only ever been validated at 100.
/// </summary>
public class AstraeaLevel71Tests
{
    // ── the data, as the game has it ─────────────────────────────────────────────────────

    [Fact]
    public void GameData()
    {
        Assert.Equal(8f, ASTActions.Gravity.Radius);            // around the target
        Assert.Equal(8f, ASTActions.GravityII.Radius);
        Assert.Equal(20f, ASTActions.Helios.Radius);
        Assert.Equal(20f, ASTActions.AspectedHelios.Radius);
        Assert.Equal(20f, ASTActions.CelestialOpposition.Radius);
        Assert.Equal(20f, ASTActions.EarthlyStar.Radius);
        Assert.Equal(30f, ASTActions.CollectiveUnconscious.Radius);
        Assert.Equal(2, ASTActions.Benefic.MinLevel);
        Assert.Equal(40, ASTActions.AspectedHelios.MinLevel);
        Assert.Equal(30, ASTActions.PlayIII.MinLevel);
        Assert.Equal((uint)3894, ASTActions.HeliosConjunction.AppliedStatusId);   // 3988 is Neutral Sect
        Assert.Equal((ushort)956, ASTActions.WheelOfFortuneStatusId);             // 848 is Collective Unconscious
    }

    /// <summary>Lord of Crowns is a 20y burst around the Astrologian, not an enemy-targeted skill.</summary>
    [Fact]
    public void LordOfCrownsIsSelfCentred()
    {
        Assert.Equal(ActionTargetType.Self, ASTActions.LordOfCrowns.TargetType);
        Assert.Equal(0f, ASTActions.LordOfCrowns.Range);
        Assert.Equal(20f, ASTActions.LordOfCrowns.Radius);
    }

    // ── the synced ranks ─────────────────────────────────────────────────────────────────

    [Fact]
    public void At71_TheRanksAreMaleficIII_CombustII_Gravity()
    {
        Assert.Equal(ASTActions.MaleficIII.ActionId, ASTActions.GetDamageGcdForLevel(71).ActionId);
        Assert.Equal(ASTActions.CombustII.ActionId, ASTActions.GetDotForLevel(71).ActionId);
        Assert.Equal((uint)843, ASTActions.GetDotStatusId(71));
        Assert.Equal(ASTActions.Gravity.ActionId, ASTActions.GetAoEDamageForLevel(71)!.ActionId);
    }

    [Fact]
    public void At72_TheyUpgrade()
    {
        Assert.Equal(ASTActions.MaleficIV.ActionId, ASTActions.GetDamageGcdForLevel(72).ActionId);
        Assert.Equal(ASTActions.CombustIII.ActionId, ASTActions.GetDotForLevel(72).ActionId);
        Assert.Equal((uint)1881, ASTActions.GetDotStatusId(72));
    }

    /// <summary>Nothing above 71 is ever picked by the level helpers at 71.</summary>
    [Fact]
    public void At71_NoHelperPicksAnythingAbove71()
    {
        Assert.True(ASTActions.GetDamageGcdForLevel(71).MinLevel <= 71);
        Assert.True(ASTActions.GetDotForLevel(71).MinLevel <= 71);
        Assert.True(ASTActions.GetAoEDamageForLevel(71)!.MinLevel <= 71);
        Assert.True(ASTActions.GetSingleHealGcdForLevel(71).MinLevel <= 71);
        Assert.True(ASTActions.GetAoEHealGcdForLevel(71).MinLevel <= 71);
    }

    // ── Gravity: the pack around the TARGET ──────────────────────────────────────────────

    private static (Daedalus.Rotation.AstraeaCore.Context.AstraeaContext Context, Daedalus.Rotation.Common.Scheduling.RotationScheduler Scheduler, Mock<IBattleNpc> Enemy)
        GravityCase(int hitsAroundTarget, int aroundAstrologian)
    {
        var config = AstraeaTestContext.CreateDefaultAstrologianConfiguration();
        config.Astrologian.EnableAoEDamage = true;
        config.Astrologian.AoEDamageMinTargets = 3;

        var enemy = new Mock<IBattleNpc>();
        enemy.Setup(e => e.GameObjectId).Returns(0x40001234UL);

        var targeting = MockBuilders.CreateMockTargetingService(findEnemy: (_, _, _) => enemy.Object,
            countEnemiesInRange: aroundAstrologian);
        targeting.Setup(t => t.FindBestAoETarget(It.IsAny<float>(), It.IsAny<float>(), It.IsAny<Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter>()))
            .Returns((enemy.Object, hitsAroundTarget));

        var actionService = MockBuilders.CreateMockActionService(canExecuteGcd: true);
        var context = AstraeaTestContext.Create(config: config, actionService: actionService,
            targetingService: targeting, level: 71, inCombat: true);
        return (context, SchedulerFactory.CreateForTest(actionService), enemy);
    }

    /// <summary>
    /// Three enemies around the target, none near a healer standing back: Gravity, on that target.
    /// Before, the pack was counted around the Astrologian with a 5y radius, so this was Malefic.
    /// </summary>
    [Fact]
    public void Gravity_CountsThePackAroundTheTarget()
    {
        var (context, scheduler, enemy) = GravityCase(hitsAroundTarget: 3, aroundAstrologian: 0);
        new DamageModule().CollectCandidates(context, scheduler, isMoving: false);

        var gravity = Assert.Single(scheduler.InspectGcdQueue(), c => c.Behavior.Action.ActionId == ASTActions.Gravity.ActionId);
        Assert.Equal(enemy.Object.GameObjectId, gravity.TargetId);
    }

    [Fact]
    public void Gravity_NotForTwo()
    {
        var (context, scheduler, _) = GravityCase(hitsAroundTarget: 2, aroundAstrologian: 5);
        new DamageModule().CollectCandidates(context, scheduler, isMoving: false);

        Assert.DoesNotContain(scheduler.InspectGcdQueue(), c => c.Behavior.Action.ActionId == ASTActions.Gravity.ActionId);
    }

    // ── Earthly Star and Lady of Crowns ──────────────────────────────────────────────────

    /// <summary>A 4-man detonates the star at 2 injured, not the configured 3 (CLAUDE.md: never 3 in 4-man).</summary>
    [Theory]
    [InlineData(3, 2, 2)]
    [InlineData(3, 3, 3)]
    [InlineData(1, 2, 1)]
    public void StarDetonatesAtThePartySizeCount(int configured, int partyRule, int expected)
        => Assert.Equal(expected, EarthlyStarDetonationHandler.MinInjuredToDetonate(configured, partyRule));

    /// <summary>Lady used to fire only under EmergencyOnly, so on the default (OnCooldown) she never did.</summary>
    [Fact]
    public void Lady_PlaysForAGroupHealOnTheDefaultStrategy()
        => Assert.True(LadyOfCrownsHandler.ShouldPlayLady(MinorArcanaUsageStrategy.OnCooldown,
            avgHp: 0.6f, threshold: 0.7f, injured: 2, drawCooldownRemaining: 30f, expireBeforeDrawSeconds: 3f));

    [Fact]
    public void Lady_IsPlayedBeforeTheNextDrawReplacesHer()
    {
        Assert.True(LadyOfCrownsHandler.ShouldPlayLady(MinorArcanaUsageStrategy.OnCooldown,
            avgHp: 0.95f, threshold: 0.7f, injured: 1, drawCooldownRemaining: 2f, expireBeforeDrawSeconds: 3f));
        Assert.False(LadyOfCrownsHandler.ShouldPlayLady(MinorArcanaUsageStrategy.OnCooldown,
            avgHp: 0.95f, threshold: 0.7f, injured: 1, drawCooldownRemaining: 20f, expireBeforeDrawSeconds: 3f));
    }

    [Fact]
    public void Lady_IsNotWastedOnAFullParty()
        => Assert.False(LadyOfCrownsHandler.ShouldPlayLady(MinorArcanaUsageStrategy.OnCooldown,
            avgHp: 1f, threshold: 0.7f, injured: 0, drawCooldownRemaining: 1f, expireBeforeDrawSeconds: 3f));

    [Fact]
    public void Lady_EmergencyOnlyStillWaitsForAnEmergency()
        => Assert.False(LadyOfCrownsHandler.ShouldPlayLady(MinorArcanaUsageStrategy.EmergencyOnly,
            avgHp: 0.95f, threshold: 0.7f, injured: 1, drawCooldownRemaining: 1f, expireBeforeDrawSeconds: 3f));
}
