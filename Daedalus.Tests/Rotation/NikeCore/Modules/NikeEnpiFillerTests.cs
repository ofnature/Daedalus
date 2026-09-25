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


    // ── Gyoten before Enpi (field 2026-09-24, Shinryu Paradox) ───────────────────────────

    // Inside Gyoten's 20y reach, outside melee.
    private const float GyotenRangeX = 12f;

    /// <summary>
    /// THE regression. Out of melee with Kenki to spare: dash in with Gyoten instead of throwing Enpi.
    /// The old gate threw Enpi the instant the target was out of reach — Gyoten was never used at all.
    /// </summary>
    [Fact]
    public void OutOfMelee_WithKenki_GyotensInsteadOfEnpi()
    {
        var (gcd, ogcd) = CollectBoth(enemyX: GyotenRangeX, kenki: 50);

        Assert.Contains(ogcd, c => c.Behavior == NikeAbilities.Gyoten);
        Assert.DoesNotContain(gcd, c => c.Behavior == NikeAbilities.Enpi);
    }

    /// <summary>
    /// After the dash the next GCD must be a melee hit, so the combo is queued alongside Gyoten. The
    /// scheduler's range gate holds it quietly until the dash lands.
    /// </summary>
    [Fact]
    public void Gyoten_QueuesTheComboToLandStraightAfterTheDash()
    {
        var (gcd, _) = CollectBoth(enemyX: GyotenRangeX, kenki: 50);
        Assert.Contains(gcd, c => c.Behavior == NikeAbilities.Gyofu || c.Behavior == NikeAbilities.Hakaze);
    }

    [Fact]
    public void OutOfMelee_NotEnoughKenki_FallsBackToEnpi()
    {
        var (gcd, ogcd) = CollectBoth(enemyX: GyotenRangeX, kenki: SAMActions.Gyoten.GaugeCost - 1);

        Assert.DoesNotContain(ogcd, c => c.Behavior == NikeAbilities.Gyoten);
        Assert.Contains(gcd, c => c.Behavior == NikeAbilities.Enpi);
    }

    /// <summary>
    /// Gyoten is a dash, so it goes through the shared gap-closer safety check like every other melee
    /// gap closer. SAM was the only melee job that never did — because it never dashed.
    /// </summary>
    [Fact]
    public void Gyoten_RefusedWhereTheDashIsUnsafe_FallsBackToEnpi()
    {
        var (gcd, ogcd) = CollectBoth(enemyX: GyotenRangeX, kenki: 50, gapCloserBlocked: true);

        Assert.DoesNotContain(ogcd, c => c.Behavior == NikeAbilities.Gyoten);
        Assert.Contains(gcd, c => c.Behavior == NikeAbilities.Enpi);
    }

    [Fact]
    public void Gyoten_DisabledInSettings_FallsBackToEnpi()
    {
        var (gcd, ogcd) = CollectBoth(enemyX: GyotenRangeX, kenki: 50, gyotenEnabled: false);

        Assert.DoesNotContain(ogcd, c => c.Behavior == NikeAbilities.Gyoten);
        Assert.Contains(gcd, c => c.Behavior == NikeAbilities.Enpi);
    }

    [Fact]
    public void Gyoten_BelowItsLevel_FallsBackToEnpi()
    {
        var (gcd, ogcd) = CollectBoth(enemyX: GyotenRangeX, kenki: 50, level: 50);   // Gyoten is Lv54

        Assert.DoesNotContain(ogcd, c => c.Behavior == NikeAbilities.Gyoten);
        Assert.Contains(gcd, c => c.Behavior == NikeAbilities.Enpi);
    }

    /// <summary>Beyond Gyoten's reach there is nothing to dash to.</summary>
    [Fact]
    public void BeyondGyotenRange_DoesNotDash()
    {
        var (_, ogcd) = CollectBoth(enemyX: SAMActions.Gyoten.Range + 5f, kenki: 50);
        Assert.DoesNotContain(ogcd, c => c.Behavior == NikeAbilities.Gyoten);
    }

    [Fact]
    public void InMelee_NeverGyotens()
    {
        var (_, ogcd) = CollectBoth(enemyX: InMeleeX, kenki: 100);
        Assert.DoesNotContain(ogcd, c => c.Behavior == NikeAbilities.Gyoten);
    }

    // ── the catalog, against the live game data ─────────────────────────────────────────

    /// <summary>
    /// Verified against live game data (XIVAPI v2) 2026-09-24. Both of these feed the out-of-melee
    /// decision directly and both were stale: Gyoten's recast read 10s (it is 5s) and Enpi's range read
    /// 15y (it is 20y). Gyoten's 10 Kenki lived only in a comment; it is a field now so the decision
    /// reads it rather than a hardcoded number.
    /// </summary>
    [Fact]
    public void Catalog_MatchesTheLiveGameData()
    {
        Assert.Equal(20f, SAMActions.Gyoten.Range);
        Assert.Equal(5f, SAMActions.Gyoten.RecastTime);
        Assert.Equal(10, SAMActions.Gyoten.GaugeCost);
        Assert.Equal(54, SAMActions.Gyoten.MinLevel);

        Assert.Equal(20f, SAMActions.Enpi.Range);
        Assert.Equal(15, SAMActions.Enpi.MinLevel);
    }

    private (System.Collections.Generic.IReadOnlyList<Daedalus.Rotation.Common.Scheduling.AbilityCandidate> Gcd,
             System.Collections.Generic.IReadOnlyList<Daedalus.Rotation.Common.Scheduling.AbilityCandidate> Ogcd)
        CollectBoth(float enemyX, int kenki, byte level = 100, bool gapCloserBlocked = false, bool gyotenEnabled = true)
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

        if (gapCloserBlocked)
        {
            var safety = new Mock<IGapCloserSafetyService>();
            safety.Setup(x => x.ShouldBlockGapCloser(It.IsAny<IBattleChara>(), It.IsAny<IPlayerCharacter>()))
                .Returns(true);
            safety.SetupGet(x => x.LastBlockReason).Returns("ground is only safe for 0.4s");
            targeting.Setup(x => x.GapCloserSafety).Returns(safety.Object);
        }

        var config = NikeTestContext.CreateDefaultSamuraiConfiguration();
        config.Samurai.EnableGyoten = gyotenEnabled;
        var scheduler = SchedulerFactory.CreateForTest(config: config);
        var context = NikeTestContext.Create(
            config: config,
            targetingService: targeting,
            level: level,
            kenki: kenki);

        new DamageModule().CollectCandidates(context, scheduler, isMoving: false);
        return (scheduler.InspectGcdQueue(), scheduler.InspectOgcdQueue());
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
