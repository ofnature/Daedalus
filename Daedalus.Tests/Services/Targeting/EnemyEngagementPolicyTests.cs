using System;
using System.Collections.Generic;
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
/// Which hostiles are part of OUR fight, as opposed to merely being in one.
/// <para>
/// Field 2026-09-25, Bozja's Southern Front: a PCT pulled random mobs and ignored its party's target —
/// only there. In a dungeon every nearby mob is fighting your party or nobody, so "has a target" or "is
/// wounded" was a fine test. In an open zone full of strangers both are true of everyone else's fights,
/// so the PCT (on Lowest HP) went for strangers' half-dead mobs and pulled untouched ones. The test is
/// now "is it fighting our side" — us, our party, our alliance when opted in, a Trust ally, or a pet.
/// </para>
/// </summary>
public sealed class EnemyEngagementPolicyTests
{
    private const ulong Me = 7UL;
    private const ulong PartyMate = 100UL;
    private const ulong AllianceMate = 200UL;
    private const ulong Stranger = 300UL;
    private const ulong TrustAlly = 400UL;
    private const ulong PartyPet = 500UL;
    private const ulong StrangerPet = 600UL;
    private const uint PartyMateEntity = 1100;
    private const uint StrangerEntity = 1300;

    private readonly Dictionary<ulong, IGameObject> _byId = new();
    private readonly Dictionary<uint, IGameObject> _byEntity = new();

    public EnemyEngagementPolicyTests()
    {
        Add(Player(PartyMate, PartyMateEntity, StatusFlags.PartyMember));
        Add(Player(AllianceMate, 1200, StatusFlags.AllianceMember));
        Add(Player(Stranger, StrangerEntity, StatusFlags.None));
        Add(TrustNpc(TrustAlly));
        Add(Pet(PartyPet, ownerEntity: PartyMateEntity));
        Add(Pet(StrangerPet, ownerEntity: StrangerEntity));
    }

    // The same resolver production uses — deliberately without the local player in the table, which is
    // what caught our own id depending on a lookup it should never need.
    private Func<ulong, bool> OurSide(bool includeAlliance = false)
        => EnemyEngagementPolicy.OurSideResolver(
            Me, includeAlliance, id => _byId.GetValueOrDefault(id), e => _byEntity.GetValueOrDefault(e));

    // ── the Bozja case ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// THE regression. A mob a stranger is fighting — it has a target and it is wounded, which is
    /// everything the old test asked for — must not be a candidate for our toons.
    /// </summary>
    [Fact]
    public void AStrangersFight_IsNotOurFight()
    {
        var enemy = Enemy(target: Stranger, wounded: true);
        Assert.False(Include(enemy));
        Assert.False(EnemyEngagementPolicy.IsEnemyInTheFight(enemy.Object, OurSide()));
    }

    /// <summary>A wound on its own proves nothing in a shared zone; strangers wound mobs constantly.</summary>
    [Fact]
    public void WoundedButFightingNobody_IsNotOurFight()
        => Assert.False(Include(Enemy(target: 0, wounded: true)));

    [Fact]
    public void AStrangersPet_DoesNotMakeItOurs()
        => Assert.False(Include(Enemy(target: StrangerPet)));

    // ── what still counts ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(Me)]
    [InlineData(PartyMate)]
    [InlineData(TrustAlly)]      // Trust content: allies are NPCs and the party list is empty
    [InlineData(PartyPet)]       // a carbuncle, fairy or chocobo is fighting for its owner
    public void AMobFightingOurSide_IsOurs(ulong target)
    {
        var enemy = Enemy(target: target);
        Assert.True(Include(enemy));
        Assert.True(EnemyEngagementPolicy.IsEnemyInTheFight(enemy.Object, OurSide()));
    }

    [Fact]
    public void ItsOwnCombatFlag_AlwaysCounts()
        => Assert.True(Include(Enemy(target: 0, inCombat: true), playerInCombat: false));

    /// <summary>A hard target is a deliberate instruction — a manual pull must still work.</summary>
    [Fact]
    public void TheHardTargetIsAlwaysHonoured()
        => Assert.True(Include(Enemy(target: 0, id: 42), currentTargetId: 42, playerInCombat: false));

    // ── the alliance setting ───────────────────────────────────────────────────────────

    /// <summary>
    /// What the setting was added for: in an alliance raid another party tags a mob first. Scoped to
    /// the alliance it covers that — and only that.
    /// </summary>
    [Fact]
    public void AMobFightingTheAlliance_CountsOnlyWhenTheSettingIsOn()
    {
        var enemy = Enemy(target: AllianceMate);
        Assert.False(Include(enemy, includeAlliance: false));
        Assert.True(Include(enemy, includeAlliance: true));
    }

    /// <summary>
    /// And it is no longer a blanket. A stranger is not in your alliance, so the setting being on —
    /// as it was on all four boxes — must not bring their fights back in.
    /// </summary>
    [Fact]
    public void TheSettingNeverAdmitsAStrangersFight()
        => Assert.False(Include(Enemy(target: Stranger, wounded: true), includeAlliance: true));

    [Fact]
    public void TheSettingIsReadFromConfigAndDefaultsOff()
    {
        var config = MockBuilders.CreateDefaultConfiguration();
        Assert.False(EnemyEngagementPolicy.IncludesAlliance(config));

        config.Targeting.IncludeHostilesWithoutPersonalCombatFlag = true;
        Assert.True(EnemyEngagementPolicy.IncludesAlliance(config));
    }

    /// <summary>
    /// Restored contract: only while we are effectively fighting. A 2026-09-17 rewrite made the setting
    /// apply out of combat too, against its own documentation.
    /// </summary>
    [Fact]
    public void NothingIsAdmittedOnSideAloneWhileWeAreNotFighting()
        => Assert.False(Include(Enemy(target: AllianceMate), playerInCombat: false, includeAlliance: true));

    // ── who is on our side ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(Me, false, true)]
    [InlineData(PartyMate, false, true)]
    [InlineData(AllianceMate, false, false)]
    [InlineData(AllianceMate, true, true)]
    [InlineData(Stranger, true, false)]
    [InlineData(TrustAlly, false, true)]
    [InlineData(PartyPet, false, true)]
    [InlineData(StrangerPet, true, false)]
    [InlineData(999UL, true, false)]         // not in the object table at all
    public void OurSideIs(ulong id, bool includeAlliance, bool expected)
        => Assert.Equal(expected, OurSide(includeAlliance)(id));

    /// <summary>"No target" in either encoding is nobody, never a match for a missing id.</summary>
    [Theory]
    [InlineData(0UL)]
    [InlineData(0xE0000000UL)]
    public void NoTarget_IsNeverFightingOurSide(ulong target)
        => Assert.False(EnemyEngagementPolicy.IsFightingOurSide(Enemy(target: target).Object, _ => true));

    /// <summary>
    /// The rotation still enters combat off a fighting tank — that signal is untouched; it just no
    /// longer decides which enemies are fair game.
    /// </summary>
    [Fact]
    public void AFightingTankStillPutsUsInCombat()
    {
        var player = new Mock<IPlayerCharacter>();
        player.Setup(x => x.EntityId).Returns(100u);
        player.Setup(x => x.StatusFlags).Returns(StatusFlags.None);

        var tank = new Mock<IBattleChara>();
        tank.Setup(x => x.EntityId).Returns(200u);
        tank.Setup(x => x.IsDead).Returns(false);
        tank.Setup(x => x.StatusFlags).Returns(StatusFlags.InCombat);

        var member = new Mock<IPartyMember>();
        member.Setup(x => x.EntityId).Returns(200u);
        var partyList = new Mock<IPartyList>();
        partyList.Setup(x => x.Length).Returns(2);
        partyList.Setup(x => x.GetEnumerator()).Returns(() => new List<IPartyMember> { member.Object }.GetEnumerator());
        var objectTable = new Mock<IObjectTable>();
        objectTable.Setup(x => x.SearchByEntityId(200u)).Returns(tank.Object);

        var config = MockBuilders.CreateDefaultConfiguration();
        config.EnableOnPartyInCombat = true;

        Assert.True(EnemyEngagementPolicy.IsPlayerEffectivelyInCombat(
            player.Object, config, partyList.Object, objectTable.Object));
    }

    // ── helpers ────────────────────────────────────────────────────────────────────────

    private bool Include(Mock<IBattleNpc> enemy, ulong currentTargetId = 0, bool playerInCombat = true,
        bool includeAlliance = false)
        => EnemyEngagementPolicy.ShouldIncludeEnemyForTargeting(
            enemy.Object, currentTargetId, playerInCombat, OurSide(includeAlliance));

    private void Add(IGameObject obj)
    {
        _byId[obj.GameObjectId] = obj;
        _byEntity[obj.EntityId] = obj;
    }

    private static Mock<IBattleNpc> Enemy(ulong target, bool inCombat = false, bool wounded = false, ulong id = 900)
    {
        var mock = new Mock<IBattleNpc>();
        mock.Setup(x => x.GameObjectId).Returns(id);
        mock.Setup(x => x.TargetObjectId).Returns(target);
        mock.Setup(x => x.CurrentHp).Returns(wounded ? 400u : 1000u);
        mock.Setup(x => x.MaxHp).Returns(1000u);
        mock.Setup(x => x.StatusFlags).Returns(inCombat ? StatusFlags.InCombat | StatusFlags.Hostile : StatusFlags.Hostile);
        return mock;
    }

    private static IGameObject Player(ulong id, uint entity, StatusFlags flags)
    {
        var mock = new Mock<IPlayerCharacter>();
        mock.Setup(x => x.GameObjectId).Returns(id);
        mock.Setup(x => x.EntityId).Returns(entity);
        mock.Setup(x => x.ObjectKind).Returns(ObjectKind.Pc);
        mock.Setup(x => x.StatusFlags).Returns(flags);
        return mock.Object;
    }

    private static IGameObject TrustNpc(ulong id)
    {
        var mock = new Mock<IBattleNpc>();
        mock.Setup(x => x.GameObjectId).Returns(id);
        mock.Setup(x => x.EntityId).Returns(1400u);
        mock.Setup(x => x.ObjectKind).Returns(ObjectKind.BattleNpc);
        mock.Setup(x => x.SubKind).Returns((byte)9);   // NpcPartyMember
        mock.Setup(x => x.CurrentHp).Returns(1000u);
        mock.Setup(x => x.MaxHp).Returns(1000u);
        return mock.Object;
    }

    private static IGameObject Pet(ulong id, uint ownerEntity)
    {
        var mock = new Mock<IBattleNpc>();
        mock.Setup(x => x.GameObjectId).Returns(id);
        mock.Setup(x => x.EntityId).Returns((uint)(id + 10000));
        mock.Setup(x => x.ObjectKind).Returns(ObjectKind.BattleNpc);
        mock.Setup(x => x.SubKind).Returns((byte)2);   // Pet
        mock.Setup(x => x.OwnerId).Returns(ownerEntity);
        mock.Setup(x => x.CurrentHp).Returns(1000u);
        mock.Setup(x => x.MaxHp).Returns(1000u);
        return mock.Object;
    }
}
