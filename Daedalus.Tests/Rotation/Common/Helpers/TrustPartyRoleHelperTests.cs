using Moq;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Plugin.Services;
using Daedalus.Data;
using Daedalus.Rotation.Common.Helpers;
using Daedalus.Tests.Mocks;
using Xunit;

namespace Daedalus.Tests.Rotation.Common.Helpers;

/// <summary>
/// Trust role resolution for party buff targeting (AST cards, etc.).
/// </summary>
public sealed class TrustPartyRoleHelperTests
{
    [Fact]
    public void IsTank_UsesTankStanceWhenJobMissing()
    {
        var member = MockBuilders.CreateMockBattleChara(2);
        var partyList = MockBuilders.CreateMockPartyList(0);

        Assert.True(TrustPartyRoleHelper.IsTank(
            member.Object,
            partyList.Object,
            _ => true));

        Assert.False(TrustPartyRoleHelper.IsTank(
            member.Object,
            partyList.Object,
            _ => false));
    }

    [Fact]
    public void FindTankInParty_NullObjectTable_ReturnsNullWithoutThrowing()
    {
        var player = MockBuilders.CreateMockPlayerCharacter();
        var partyList = MockBuilders.CreateMockPartyList(0);

        var tank = TrustPartyRoleHelper.FindTankInParty(
            player.Object,
            members: Array.Empty<IBattleChara>(),
            objectTable: null!,
            partyList: partyList.Object);

        Assert.Null(tank);
    }

    // ---- Two-tank main-tank preference (Kardia/heal anchors must sit on the MT) ----

    private static Mock<IBattleChara> CreateTank(uint entityId, string name)
    {
        var mock = MockBuilders.CreateMockBattleChara(entityId, name);
        mock.Setup(x => x.Name).Returns(new SeString(new TextPayload(name)));
        return mock;
    }

    private static Mock<IObjectTable> CreateObjectTableWithEnemies(params (ulong targetId, uint maxHp)[] enemies)
    {
        var objects = new List<IGameObject>();
        foreach (var (targetId, maxHp) in enemies)
        {
            var enemy = new Mock<IBattleNpc>();
            enemy.Setup(x => x.TargetObjectId).Returns(targetId);
            enemy.Setup(x => x.MaxHp).Returns(maxHp);
            objects.Add(enemy.Object);
        }

        var table = new Mock<IObjectTable>();
        table.Setup(x => x.GetEnumerator()).Returns(() => objects.GetEnumerator());
        return table;
    }

    private static bool TankStance(IBattleChara chara) => chara.EntityId is 10u or 11u;

    [Fact]
    public void FindTankInParty_TwoTanks_PrefersEnemyTargetedTank()
    {
        var player = MockBuilders.CreateMockPlayerCharacter();
        var partyList = MockBuilders.CreateMockPartyList(0);
        var offTank = CreateTank(10, "Off Tank");   // listed FIRST — party order would pick this
        var mainTank = CreateTank(11, "Main Tank");
        var objectTable = CreateObjectTableWithEnemies((targetId: 11ul, maxHp: 1_000_000));

        var tank = TrustPartyRoleHelper.FindTankInParty(
            player.Object,
            new[] { offTank.Object, mainTank.Object },
            objectTable.Object,
            partyList.Object,
            TankStance);

        Assert.Equal(11u, tank?.EntityId);
    }

    [Fact]
    public void FindTankInParty_TwoTanks_BiggestEnemyOutvotesTrashMob()
    {
        var player = MockBuilders.CreateMockPlayerCharacter();
        var partyList = MockBuilders.CreateMockPartyList(0);
        var addTank = CreateTank(10, "Add Tank");
        var bossTank = CreateTank(11, "Boss Tank");
        // Trash mob (small MaxHp) on the first tank, boss (huge MaxHp) on the second.
        var objectTable = CreateObjectTableWithEnemies(
            (targetId: 10ul, maxHp: 50_000),
            (targetId: 11ul, maxHp: 5_000_000));

        var tank = TrustPartyRoleHelper.FindTankInParty(
            player.Object,
            new[] { addTank.Object, bossTank.Object },
            objectTable.Object,
            partyList.Object,
            TankStance);

        Assert.Equal(11u, tank?.EntityId);
    }

    [Fact]
    public void FindTankInParty_TwoTanks_NoAggro_SkipsDesignatedOffTank()
    {
        var player = MockBuilders.CreateMockPlayerCharacter();
        var partyList = MockBuilders.CreateMockPartyList(0);
        var offTank = CreateTank(10, "Xia Ferryman");
        var mainTank = CreateTank(11, "Brick Wall");
        var objectTable = CreateObjectTableWithEnemies(); // pre-pull: nothing has aggro

        TrustPartyRoleHelper.DesignatedOffTankNameSource = () => "Xia Ferryman";
        try
        {
            var tank = TrustPartyRoleHelper.FindTankInParty(
                player.Object,
                new[] { offTank.Object, mainTank.Object },
                objectTable.Object,
                partyList.Object,
                TankStance);

            Assert.Equal(11u, tank?.EntityId);
        }
        finally
        {
            TrustPartyRoleHelper.DesignatedOffTankNameSource = null;
        }
    }

    [Fact]
    public void FindTankInParty_TwoTanks_NoAggroNoDesignation_FallsBackToPartyOrder()
    {
        var player = MockBuilders.CreateMockPlayerCharacter();
        var partyList = MockBuilders.CreateMockPartyList(0);
        var tankA = CreateTank(10, "Tank A");
        var tankB = CreateTank(11, "Tank B");
        var objectTable = CreateObjectTableWithEnemies();

        var tank = TrustPartyRoleHelper.FindTankInParty(
            player.Object,
            new[] { tankA.Object, tankB.Object },
            objectTable.Object,
            partyList.Object,
            TankStance);

        Assert.Equal(10u, tank?.EntityId);
    }

    [Fact]
    public void FindTankInParty_SingleTank_DesignationCannotUnseatIt()
    {
        var player = MockBuilders.CreateMockPlayerCharacter();
        var partyList = MockBuilders.CreateMockPartyList(0);
        var onlyTank = CreateTank(10, "Solo Tank");
        var dps = CreateTank(20, "Some Dps"); // EntityId 20 -> TankStance false
        var objectTable = CreateObjectTableWithEnemies();

        TrustPartyRoleHelper.DesignatedOffTankNameSource = () => "Solo Tank";
        try
        {
            var tank = TrustPartyRoleHelper.FindTankInParty(
                player.Object,
                new[] { onlyTank.Object, dps.Object },
                objectTable.Object,
                partyList.Object,
                TankStance);

            Assert.Equal(10u, tank?.EntityId);
        }
        finally
        {
            TrustPartyRoleHelper.DesignatedOffTankNameSource = null;
        }
    }

    [Fact]
    public void FindTankInParty_TwoTanks_AggroBeatsDesignation()
    {
        var player = MockBuilders.CreateMockPlayerCharacter();
        var partyList = MockBuilders.CreateMockPartyList(0);
        var designatedOt = CreateTank(10, "Designated Ot");
        var designatedMt = CreateTank(11, "Designated Mt");
        // Post tank-swap reality: the DESIGNATED off-tank now holds the boss.
        var objectTable = CreateObjectTableWithEnemies((targetId: 10ul, maxHp: 5_000_000));

        TrustPartyRoleHelper.DesignatedOffTankNameSource = () => "Designated Ot";
        try
        {
            var tank = TrustPartyRoleHelper.FindTankInParty(
                player.Object,
                new[] { designatedOt.Object, designatedMt.Object },
                objectTable.Object,
                partyList.Object,
                TankStance);

            Assert.Equal(10u, tank?.EntityId);
        }
        finally
        {
            TrustPartyRoleHelper.DesignatedOffTankNameSource = null;
        }
    }

    [Fact]
    public void TrustCardTargeting_Documentation()
    {
        // Trust WAR/RDM/PCT expose ClassJob on IBattleChara in-game.
        // DPS cards must target IsDps allies only — never tank/healer fallbacks.
        Assert.True(JobRegistry.IsTank(JobRegistry.Warrior));
        Assert.True(JobRegistry.IsCasterDps(JobRegistry.RedMage));
        Assert.True(JobRegistry.IsCasterDps(JobRegistry.Pictomancer));
        Assert.True(JobRegistry.IsHealer(JobRegistry.Astrologian));
    }
}
