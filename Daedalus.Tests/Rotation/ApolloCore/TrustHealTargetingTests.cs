using System.Collections.Generic;
using System.Numerics;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using Moq;
using Daedalus.Data;
using Daedalus.Rotation.ApolloCore.Helpers;
using Daedalus.Services;
using Daedalus.Services.Prediction;
using Daedalus.Tests.Mocks;
using Xunit;

namespace Daedalus.Tests.Rotation.ApolloCore;

/// <summary>
/// Field requirement 2026-07-23 (Lv.34 CNJ dungeon report): the healing target pickers MUST see
/// trust/duty-support NPC allies. In Trust and Duty Support content the PARTY LIST IS EMPTY (bug
/// class 6a); allies exist only in the object table as BattleNpcs with SubKind
/// NpcPartyMember (9). These tests run Apollo's real PartyHelper against that exact shape.
/// </summary>
public class TrustHealTargetingTests
{
    private static Mock<IBattleNpc> TrustNpc(uint entityId, uint currentHp, uint maxHp, byte subKind = 9)
    {
        var npc = new Mock<IBattleNpc>();
        npc.Setup(x => x.ObjectKind).Returns(ObjectKind.BattleNpc);
        npc.Setup(x => x.SubKind).Returns(subKind);
        npc.Setup(x => x.EntityId).Returns(entityId);
        npc.Setup(x => x.GameObjectId).Returns(entityId);
        npc.Setup(x => x.CurrentHp).Returns(currentHp);
        npc.Setup(x => x.MaxHp).Returns(maxHp);
        npc.Setup(x => x.IsDead).Returns(currentHp == 0);
        npc.Setup(x => x.Position).Returns(Vector3.Zero);
        npc.Setup(x => x.StatusList).Returns((Dalamud.Game.ClientState.Statuses.StatusList?)null!);
        return npc;
    }

    private static PartyHelper BuildHelper(params IGameObject[] objects)
    {
        var objectTable = new Mock<IObjectTable>();
        objectTable.Setup(x => x.GetEnumerator()).Returns(((IEnumerable<IGameObject>)objects).GetEnumerator());

        // Duty Support / Trust: the party list is EMPTY.
        var partyList = MockBuilders.CreateMockPartyList(length: 0);

        var config = new Configuration();
        // Shadow HP must pass through the live value — a raw mock returns 0 and makes everyone
        // look dead to the prediction service.
        var combatEvents = new Mock<ICombatEventService>();
        combatEvents.Setup(x => x.GetShadowHp(It.IsAny<uint>(), It.IsAny<uint>()))
            .Returns((uint _, uint currentHp) => currentHp);
        var hpPrediction = new HpPredictionService(combatEvents.Object, config);
        return new PartyHelper(objectTable.Object, partyList.Object, hpPrediction, config);
    }

    [Fact]
    public void FindLowestHpPartyMember_EmptyPartyList_PicksHurtSupportNpc()
    {
        // Duty-support tank at 50% HP — the exact "Lv.34 CNJ isn't healing" scenario shape.
        var tankNpc = TrustNpc(entityId: 9001, currentHp: 2500, maxHp: 5000);
        var healthyNpc = TrustNpc(entityId: 9002, currentHp: 4000, maxHp: 4000);

        var helper = BuildHelper(tankNpc.Object, healthyNpc.Object);
        var player = MockBuilders.CreateMockPlayerCharacter(level: 34, currentHp: 4000, maxHp: 4000);
        player.Setup(x => x.Position).Returns(Vector3.Zero);
        player.Setup(x => x.StatusList).Returns((Dalamud.Game.ClientState.Statuses.StatusList?)null!);

        var target = helper.FindLowestHpPartyMember(player.Object, rangeSquared: 30f * 30f);

        Assert.NotNull(target);
        Assert.Equal(9001u, target!.EntityId);
    }

    [Fact]
    public void FindLowestHpPartyMember_EnemyBattleNpc_NeverPicked()
    {
        // Enemies are BattleNpcs too but never SubKind 9 — a hurt enemy must not become a heal target.
        var enemy = TrustNpc(entityId: 6666, currentHp: 100, maxHp: 5000, subKind: 5);

        var helper = BuildHelper(enemy.Object);
        var player = MockBuilders.CreateMockPlayerCharacter(level: 34, currentHp: 4000, maxHp: 4000);
        player.Setup(x => x.Position).Returns(Vector3.Zero);
        player.Setup(x => x.StatusList).Returns((Dalamud.Game.ClientState.Statuses.StatusList?)null!);

        var target = helper.FindLowestHpPartyMember(player.Object, rangeSquared: 30f * 30f);

        Assert.Null(target);
    }

    [Fact]
    public void GetPartySize_EmptyPartyList_CountsPlayerPlusSupportNpcs()
    {
        var helper = BuildHelper(
            TrustNpc(9001, 5000, 5000).Object,
            TrustNpc(9002, 5000, 5000).Object,
            TrustNpc(9003, 5000, 5000).Object);
        var player = MockBuilders.CreateMockPlayerCharacter(level: 34);
        player.Setup(x => x.Position).Returns(Vector3.Zero);

        Assert.Equal(4, helper.GetPartySize(player.Object));
    }
}
