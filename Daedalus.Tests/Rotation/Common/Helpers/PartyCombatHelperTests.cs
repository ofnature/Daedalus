using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.ClientState.Party;
using Dalamud.Plugin.Services;
using Moq;
using Daedalus.Rotation.Common.Helpers;
using Daedalus.Tests.Mocks;
using Xunit;

namespace Daedalus.Tests.Rotation.Common.Helpers;

public class PartyCombatHelperTests
{
    [Fact]
    public void IsAnyGroupMemberInCombat_WhenPlayerAlone_ReturnsFalse()
    {
        var player = CreatePlayer(inCombat: false);
        var partyList = MockBuilders.CreateMockPartyList(length: 0);
        var objectTable = MockBuilders.CreateMockObjectTable();

        Assert.False(PartyCombatHelper.IsAnyGroupMemberInCombat(
            player.Object, partyList.Object, objectTable.Object));
    }

    [Fact]
    public void IsAnyGroupMemberInCombat_WhenPartyMemberInCombat_ReturnsTrue()
    {
        const uint playerEntityId = 100;
        const uint tankEntityId = 200;

        var player = CreatePlayer(inCombat: false, entityId: playerEntityId);
        var tank = CreateBattleChara(inCombat: true, entityId: tankEntityId);

        var partyMember = new Mock<IPartyMember>();
        partyMember.Setup(x => x.EntityId).Returns(tankEntityId);

        var partyList = new Mock<IPartyList>();
        partyList.Setup(x => x.Length).Returns(2);
        partyList.Setup(x => x.GetEnumerator())
            .Returns(new List<IPartyMember> { partyMember.Object }.GetEnumerator());

        var objectTable = MockBuilders.CreateMockObjectTable();
        objectTable.Setup(x => x.SearchByEntityId(tankEntityId)).Returns(tank.Object);

        Assert.True(PartyCombatHelper.IsAnyGroupMemberInCombat(
            player.Object, partyList.Object, objectTable.Object));
    }

    [Fact]
    public void IsAnyGroupMemberInCombat_IgnoresSelfAndDeadMembers()
    {
        const uint playerEntityId = 100;
        const uint deadEntityId = 300;

        var player = CreatePlayer(inCombat: false, entityId: playerEntityId);
        var deadMember = CreateBattleChara(inCombat: true, entityId: deadEntityId, isDead: true);

        var selfMember = new Mock<IPartyMember>();
        selfMember.Setup(x => x.EntityId).Returns(playerEntityId);

        var deadPartyMember = new Mock<IPartyMember>();
        deadPartyMember.Setup(x => x.EntityId).Returns(deadEntityId);

        var partyList = new Mock<IPartyList>();
        partyList.Setup(x => x.Length).Returns(2);
        partyList.Setup(x => x.GetEnumerator())
            .Returns(new List<IPartyMember> { selfMember.Object, deadPartyMember.Object }.GetEnumerator());

        var objectTable = MockBuilders.CreateMockObjectTable();
        objectTable.Setup(x => x.SearchByEntityId(deadEntityId)).Returns(deadMember.Object);

        Assert.False(PartyCombatHelper.IsAnyGroupMemberInCombat(
            player.Object, partyList.Object, objectTable.Object));
    }

    private static Mock<IPlayerCharacter> CreatePlayer(bool inCombat, uint entityId = 100)
    {
        var mock = new Mock<IPlayerCharacter>();
        mock.Setup(x => x.EntityId).Returns(entityId);
        mock.Setup(x => x.StatusFlags).Returns(inCombat ? StatusFlags.InCombat : 0);
        mock.Setup(x => x.Position).Returns(System.Numerics.Vector3.Zero);
        return mock;
    }

    private static Mock<IBattleChara> CreateBattleChara(bool inCombat, uint entityId, bool isDead = false)
    {
        var mock = new Mock<IBattleChara>();
        mock.Setup(x => x.EntityId).Returns(entityId);
        mock.Setup(x => x.IsDead).Returns(isDead);
        mock.Setup(x => x.StatusFlags).Returns(inCombat ? StatusFlags.InCombat : 0);
        return mock;
    }
}
