using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.ClientState.Party;
using Dalamud.Plugin.Services;
using Moq;
using Daedalus.Data;
using Daedalus.Rotation.Common.Helpers;
using Daedalus.Tests.Mocks;
using Xunit;

namespace Daedalus.Tests.Rotation.Common.Helpers;

/// <summary>
/// Regression suite (2026-07-19 field report — WAR/SGE/SGE/PCT multibox party): the party
/// entity-id cache invalidated on party SIZE only. During staggered zone-ins a member still
/// loading reports an invalid EntityId (0 / 0xE0000000); a cache built in that window latched
/// the garbage id forever — party size never changes — leaving the member (the WAR tank)
/// invisible to every party scan on that client. Kardia then fell back to the PCT and, with
/// "home" also resolving to the PCT, never returned to the tank.
/// </summary>
public sealed class BasePartyHelperCacheTests
{
    private sealed class TestPartyHelper : BasePartyHelper
    {
        public TestPartyHelper(IObjectTable objectTable, IPartyList partyList)
            : base(objectTable, partyList)
        {
        }
    }

    private static Mock<IPartyMember> PartyMember(uint entityId)
    {
        var mock = new Mock<IPartyMember>();
        mock.Setup(x => x.EntityId).Returns(entityId);
        return mock;
    }

    private static Mock<IPartyList> PartyList(List<IPartyMember> members)
    {
        var mock = new Mock<IPartyList>();
        mock.Setup(x => x.Length).Returns(() => members.Count);
        mock.Setup(x => x.GetEnumerator()).Returns(() => members.GetEnumerator());
        return mock;
    }

    private static Mock<IObjectTable> ObjectTable(List<IGameObject> objects)
    {
        var mock = new Mock<IObjectTable>();
        mock.Setup(x => x.GetEnumerator()).Returns(() => objects.GetEnumerator());
        return mock;
    }

    private static List<uint> EnumeratedEntityIds(TestPartyHelper helper, IPlayerCharacter player) =>
        helper.GetAllPartyMembers(player).Select(m => m.EntityId).ToList();

    [Fact]
    public void GetAllPartyMembers_MemberLoadsAfterCacheBuilt_IsPickedUpWithoutSizeChange()
    {
        var player = MockBuilders.CreateMockPlayerCharacter();
        var sage2 = MockBuilders.CreateMockBattleChara(entityId: 2, currentHp: 50000, maxHp: 50000);
        var pct = MockBuilders.CreateMockBattleChara(entityId: 3, currentHp: 50000, maxHp: 50000);
        var war = MockBuilders.CreateMockBattleChara(entityId: 4, currentHp: 50000, maxHp: 50000);

        // Zone-in window: party list already shows 4 entries, but the WAR's machine is still
        // loading — its entry carries the invalid placeholder id.
        var warEntry = PartyMember(FFXIVConstants.InvalidTargetId);
        var partyMembers = new List<IPartyMember>
        {
            PartyMember(1).Object, // the player's own entry
            PartyMember(2).Object,
            PartyMember(3).Object,
            warEntry.Object,
        };
        var objects = new List<IGameObject> { sage2.Object, pct.Object };

        var helper = new TestPartyHelper(ObjectTable(objects).Object, PartyList(partyMembers).Object);

        var before = EnumeratedEntityIds(helper, player.Object);
        Assert.Equal(new List<uint> { 1, 2, 3 }, before);

        // The WAR finishes loading: same party size, but its entry now has a real id and its
        // object exists. The old size-only cache check never saw this transition.
        partyMembers[3] = PartyMember(4).Object;
        objects.Add(war.Object);

        var after = EnumeratedEntityIds(helper, player.Object);
        Assert.Contains(4u, after);
        Assert.Equal(4, after.Count);
    }

    [Fact]
    public void GetAllPartyMembers_MemberReplacedAtSamePartySize_CacheRebuilds()
    {
        var player = MockBuilders.CreateMockPlayerCharacter();
        var oldAlly = MockBuilders.CreateMockBattleChara(entityId: 2, currentHp: 50000, maxHp: 50000);
        var newAlly = MockBuilders.CreateMockBattleChara(entityId: 5, currentHp: 50000, maxHp: 50000);

        var partyMembers = new List<IPartyMember>
        {
            PartyMember(1).Object,
            PartyMember(2).Object,
        };
        var objects = new List<IGameObject> { oldAlly.Object, newAlly.Object };

        var helper = new TestPartyHelper(ObjectTable(objects).Object, PartyList(partyMembers).Object);

        Assert.Equal(new List<uint> { 1, 2 }, EnumeratedEntityIds(helper, player.Object));

        // Same party size, different member (toon swap / rejoin with a new entity id).
        partyMembers[1] = PartyMember(5).Object;

        var after = EnumeratedEntityIds(helper, player.Object);
        Assert.Contains(5u, after);
        Assert.DoesNotContain(2u, after);
    }

    [Fact]
    public void GetAllPartyMembers_InvalidEntityIdsNeverEnterCache()
    {
        var player = MockBuilders.CreateMockPlayerCharacter();
        // An object with EntityId 0 exists in the table; a party entry with placeholder id 0
        // must not accidentally match it.
        var zeroIdObject = MockBuilders.CreateMockBattleChara(entityId: 0, currentHp: 50000, maxHp: 50000);
        var ally = MockBuilders.CreateMockBattleChara(entityId: 2, currentHp: 50000, maxHp: 50000);

        var partyMembers = new List<IPartyMember>
        {
            PartyMember(1).Object,
            PartyMember(2).Object,
            PartyMember(0).Object,
        };
        var objects = new List<IGameObject> { zeroIdObject.Object, ally.Object };

        var helper = new TestPartyHelper(ObjectTable(objects).Object, PartyList(partyMembers).Object);

        var ids = EnumeratedEntityIds(helper, player.Object);
        Assert.Equal(new List<uint> { 1, 2 }, ids);
    }
}
