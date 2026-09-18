using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.ClientState.Party;
using Dalamud.Plugin.Services;
using Moq;
using Daedalus.Services.Targeting;
using Daedalus.Tests.Mocks;
using Xunit;

namespace Daedalus.Tests.Services.Targeting;

/// <summary>
/// Which enemies a character may pick up, when its own InCombat flag is not the whole story.
/// <para>
/// Field 2026-09-17: casters pulled random mobs while the tank was fighting. An ally in combat
/// waived the enemy-side requirement entirely, so every hostile in scan range became a candidate
/// — and the nearest hostile to a backline caster is very often a pack nobody has touched.
/// "My party is fighting" and "this mob is fair game" are separate questions now.
/// </para>
/// </summary>
public sealed class EnemyEngagementPolicyTests
{
    private static bool Include(
        Mock<IBattleNpc> enemy, ulong currentTargetId = 0, bool playerInCombat = true,
        bool allowsUnclaimed = false, bool allyInCombat = false)
        => EnemyEngagementPolicy.ShouldIncludeEnemyForTargeting(
            enemy.Object, currentTargetId, playerInCombat, allowsUnclaimed, allyInCombat);

    // ── the field bug ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// THE regression. Tank is fighting, so the caster is "effectively in combat"; the mob next to
    /// it is untouched — full HP, nothing held. It must not be a candidate, or the caster hits it
    /// and the pack comes.
    /// </summary>
    [Fact]
    public void AnAllyFighting_DoesNotMakeAnUntouchedMobFairGame()
        => Assert.False(Include(Untouched(), playerInCombat: true, allyInCombat: true));

    /// <summary>
    /// The other half: while an ally fights, a mob that IS in the fight but whose own combat flag
    /// has not arrived yet still counts — that is the case the relaxation exists for.
    /// </summary>
    [Theory]
    [InlineData(true, false)]   // holding someone's attention
    [InlineData(false, true)]   // already wounded
    [InlineData(true, true)]
    public void AnAllyFighting_StillAcceptsAMobAlreadyInTheFight(bool hasTarget, bool wounded)
    {
        var enemy = CreateEnemy(inCombat: false, targetObjectId: hasTarget ? 900UL : 0UL,
                                currentHp: wounded ? 500u : 1000u, maxHp: 1000u);
        Assert.True(Include(enemy, playerInCombat: true, allyInCombat: true));
    }

    [Theory]
    [InlineData(0UL, 1000u, false)]   // full health, nothing held -> the next pack
    [InlineData(900UL, 1000u, true)]  // fighting something
    [InlineData(0UL, 999u, true)]     // someone has hit it
    public void EngagementEvidenceIsATargetOrAWound(ulong targetId, uint hp, bool expected)
        => Assert.Equal(expected, EnemyEngagementPolicy.HasEngagementEvidence(
            CreateEnemy(inCombat: false, targetObjectId: targetId, currentHp: hp, maxHp: 1000u).Object));

    // ── the things that must keep working ──────────────────────────────────────────────

    /// <summary>An enemy carrying its own combat flag never needs any of the reasoning below it.</summary>
    [Fact]
    public void AnEnemyWithItsOwnCombatFlagAlwaysCounts()
        => Assert.True(Include(CreateEnemy(inCombat: true), playerInCombat: false));

    /// <summary>
    /// A hard target is a deliberate instruction. Pulled or not, in combat or not, if the user
    /// pointed at it we attack it — this is what makes a manual pull work at all.
    /// </summary>
    [Fact]
    public void TheHardTargetIsAlwaysHonoured()
    {
        const ulong id = 42;
        var enemy = CreateEnemy(inCombat: false, gameObjectId: id, currentHp: 1000u, maxHp: 1000u);
        Assert.True(Include(enemy, currentTargetId: id, playerInCombat: false));
    }

    /// <summary>The explicit opt-in is blanket by design: turning it on asks for the unpulled ones.</summary>
    [Fact]
    public void TheExplicitOptInStillTakesUnclaimedHostiles()
        => Assert.True(Include(Untouched(), playerInCombat: false, allowsUnclaimed: true));

    /// <summary>Out of combat with no ally fighting and no opt-in, nothing unclaimed is touched.</summary>
    [Fact]
    public void NothingUnclaimedOutOfCombat()
        => Assert.False(Include(Untouched(), playerInCombat: false));

    /// <summary>
    /// In combat personally but with no ally fighting and no opt-in: still nothing unclaimed. This
    /// was already the behaviour with the relax flag off, and it stays.
    /// </summary>
    [Fact]
    public void NothingUnclaimedOnOurOwnCombatFlagAlone()
        => Assert.False(Include(Untouched(), playerInCombat: true));

    // ── the two relaxation reasons are separate ────────────────────────────────────────

    [Fact]
    public void TheOptInIsReadFromConfigAndDefaultsOff()
    {
        var config = MockBuilders.CreateDefaultConfiguration();
        Assert.False(EnemyEngagementPolicy.AllowsUnclaimedHostiles(config));

        config.Targeting.IncludeHostilesWithoutPersonalCombatFlag = true;
        Assert.True(EnemyEngagementPolicy.AllowsUnclaimedHostiles(config));
    }

    /// <summary>
    /// A fighting tank still reports an ally in combat — the signal is intact, it just no longer
    /// doubles as permission to hit anything nearby.
    /// </summary>
    [Fact]
    public void AFightingTankIsReportedAsAnAllyInCombat()
    {
        const uint playerEntityId = 100;
        const uint tankEntityId = 200;

        var player = CreatePlayer(inCombat: false, entityId: playerEntityId);
        var tank = CreateBattleChara(inCombat: true, entityId: tankEntityId);

        var partyMember = new Mock<IPartyMember>();
        partyMember.Setup(x => x.EntityId).Returns(tankEntityId);

        var partyList = new Mock<IPartyList>();
        partyList.Setup(x => x.Length).Returns(2);
        // A fresh enumerator per call: Returns(<instance>) hands the SAME one back every time, so a
        // second read of the party list sees it already exhausted and reports nobody in combat.
        partyList.Setup(x => x.GetEnumerator())
            .Returns(() => new List<IPartyMember> { partyMember.Object }.GetEnumerator());

        var objectTable = new Mock<IObjectTable>();
        objectTable.Setup(x => x.SearchByEntityId(tankEntityId)).Returns(tank.Object);

        var config = MockBuilders.CreateDefaultConfiguration();
        config.EnableOnPartyInCombat = true;

        Assert.True(EnemyEngagementPolicy.IsAnyAllyInCombat(
            config, player.Object, partyList.Object, objectTable.Object));

        // ... and the rotation still runs off that signal.
        Assert.True(EnemyEngagementPolicy.IsPlayerEffectivelyInCombat(
            player.Object, config, partyList.Object, objectTable.Object));
    }

    // ── is this mob part of the fight? (AoE counts, pack TTK, nearest-enemy pick) ──────

    private const ulong Me = 7UL;

    /// <summary>
    /// THE other half of the field bug. An untouched hostile standing near the pull used to count,
    /// because our own combat flag plus its Hostile flag was enough. That inflated the AoE
    /// thresholds until an AoE went out and pulled it, and offered it to the nearest-enemy pick.
    /// </summary>
    [Fact]
    public void AnUntouchedHostileIsNotPartOfTheFight()
        => Assert.False(EnemyEngagementPolicy.IsEnemyInTheFight(Hostile().Object, Me));

    [Fact]
    public void AMobComingForUsIsPartOfTheFightBeforeAnythingLands()
        => Assert.True(EnemyEngagementPolicy.IsEnemyInTheFight(
            CreateEnemy(inCombat: false, targetObjectId: Me, hostile: true).Object, Me));

    [Fact]
    public void AMobFightingSomeoneElseIsPartOfTheFight()
        => Assert.True(EnemyEngagementPolicy.IsEnemyInTheFight(
            CreateEnemy(inCombat: false, targetObjectId: 999UL, hostile: true).Object, Me));

    [Fact]
    public void AWoundedMobIsPartOfTheFight()
        => Assert.True(EnemyEngagementPolicy.IsEnemyInTheFight(
            CreateEnemy(inCombat: false, currentHp: 900u, maxHp: 1000u, hostile: true).Object, Me));

    [Fact]
    public void ItsOwnCombatFlagIsEnoughWithoutTheHostileFlag()
        => Assert.True(EnemyEngagementPolicy.IsEnemyInTheFight(
            CreateEnemy(inCombat: true, hostile: false).Object, Me));

    /// <summary>
    /// Being in the fight is about the enemy, not about us: our own combat state is not an argument
    /// here at all, which is exactly what stopped the next pack qualifying the moment we pulled.
    /// </summary>
    [Fact]
    public void OurOwnCombatStateDoesNotEnrolAnyone()
    {
        // Same untouched mob, and nothing about the caller can change the answer.
        Assert.False(EnemyEngagementPolicy.IsEnemyInTheFight(Hostile().Object, Me));
        Assert.False(EnemyEngagementPolicy.IsEnemyInTheFight(Hostile().Object, playerGameObjectId: 0UL));
    }

    private static Mock<IBattleNpc> Hostile()
        => CreateEnemy(inCombat: false, targetObjectId: 0, currentHp: 1000u, maxHp: 1000u, hostile: true);

    private static Mock<IBattleNpc> Untouched()
        => CreateEnemy(inCombat: false, targetObjectId: 0, currentHp: 1000u, maxHp: 1000u);

    private static Mock<IPlayerCharacter> CreatePlayer(bool inCombat, uint entityId = 100)
    {
        var mock = new Mock<IPlayerCharacter>();
        mock.Setup(x => x.EntityId).Returns(entityId);
        mock.Setup(x => x.StatusFlags).Returns(inCombat ? StatusFlags.InCombat : 0);
        return mock;
    }

    private static Mock<IBattleNpc> CreateEnemy(
        bool inCombat, ulong gameObjectId = 500, ulong targetObjectId = 0,
        uint currentHp = 1000, uint maxHp = 1000, bool hostile = false)
    {
        var flags = inCombat ? StatusFlags.InCombat : 0;
        if (hostile) flags |= StatusFlags.Hostile;

        var mock = new Mock<IBattleNpc>();
        mock.Setup(x => x.GameObjectId).Returns(gameObjectId);
        mock.Setup(x => x.TargetObjectId).Returns(targetObjectId);
        mock.Setup(x => x.CurrentHp).Returns(currentHp);
        mock.Setup(x => x.MaxHp).Returns(maxHp);
        mock.Setup(x => x.StatusFlags).Returns(flags);
        return mock;
    }

    private static Mock<IBattleChara> CreateBattleChara(bool inCombat, uint entityId)
    {
        var mock = new Mock<IBattleChara>();
        mock.Setup(x => x.EntityId).Returns(entityId);
        mock.Setup(x => x.IsDead).Returns(false);
        mock.Setup(x => x.StatusFlags).Returns(inCombat ? StatusFlags.InCombat : 0);
        return mock;
    }
}
