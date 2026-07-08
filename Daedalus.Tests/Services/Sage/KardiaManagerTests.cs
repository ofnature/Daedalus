using System.Reflection;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Moq;
using Daedalus.Services.Sage;
using Daedalus.Tests.Mocks;
using Xunit;

namespace Daedalus.Tests.Services.Sage;

public class KardiaManagerTests
{
    [Fact]
    public void ShouldBlockKardiaRecast_LatchedTankPlacement_BlocksWhenLiveScanWouldMiss()
    {
        var partyList = MockBuilders.CreateMockPartyList();
        var objectTable = MockBuilders.CreateMockObjectTable();

        var tank = MockBuilders.CreateMockBattleChara(entityId: 42u, currentHp: 100000, maxHp: 100000);
        tank.Setup(x => x.GameObjectId).Returns(0xABC00001ul);

        var player = MockBuilders.CreateMockPlayerCharacter(level: 90);

        var manager = new KardiaManager(partyList.Object, objectTable.Object);
        manager.ConfirmTankKardion(tank.Object);

        var blocked = manager.ShouldBlockKardiaRecast(
            player.Object,
            tank.Object,
            objectTable.Object,
            partyList.Object,
            tank.Object);

        Assert.True(blocked);
        Assert.True(manager.IsTankKardionLatched(tank.Object.EntityId));
    }

    [Fact]
    public void ShouldBlockKardiaRecast_AfterSwapCooldownExpires_StillBlocksWhenTankLatched()
    {
        var partyList = MockBuilders.CreateMockPartyList();
        var objectTable = MockBuilders.CreateMockObjectTable();

        var tank = MockBuilders.CreateMockBattleChara(entityId: 42u, currentHp: 100000, maxHp: 100000);
        tank.Setup(x => x.GameObjectId).Returns(0xABC00001ul);

        var player = MockBuilders.CreateMockPlayerCharacter(level: 90);

        var manager = new KardiaManager(partyList.Object, objectTable.Object);
        manager.ConfirmTankKardion(tank.Object);
        manager.RecordSwap(tank.Object.GameObjectId, tank.Object.EntityId);

        typeof(KardiaManager)
            .GetField("_lastSwapTime", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(manager, DateTime.Now.AddSeconds(-(KardiaManager.SwapCooldown + 0.1)));

        var blocked = manager.ShouldBlockKardiaRecast(
            player.Object,
            tank.Object,
            objectTable.Object,
            partyList.Object,
            tank.Object);

        Assert.True(blocked);
        Assert.True(manager.IsTankKardionLatched(tank.Object.EntityId));
    }

    [Fact]
    public void ResetSession_AfterTankLatched_ClearsLatchSoKardiaRecasts()
    {
        var partyList = MockBuilders.CreateMockPartyList();
        var objectTable = MockBuilders.CreateMockObjectTable();

        var tank = MockBuilders.CreateMockBattleChara(entityId: 42u, currentHp: 100000, maxHp: 100000);
        tank.Setup(x => x.GameObjectId).Returns(0xABC00001ul);

        var player = MockBuilders.CreateMockPlayerCharacter(level: 90);

        var manager = new KardiaManager(partyList.Object, objectTable.Object);
        manager.ConfirmTankKardion(tank.Object);

        Assert.True(manager.IsTankKardionLatched(tank.Object.EntityId));

        // Duty start: stale latch must be cleared so Kardion is re-applied on the new tank.
        manager.ResetSession();

        Assert.False(manager.IsTankKardionLatched(tank.Object.EntityId));
        Assert.False(manager.HasKardia);
        Assert.False(manager.ShouldBlockKardiaRecast(
            player.Object,
            tank.Object,
            objectTable.Object,
            partyList.Object,
            tank.Object));
    }

    [Fact]
    public void IsPostZoneWarmupActive_TrueAfterReset_FalseAfterGraceElapses()
    {
        var partyList = MockBuilders.CreateMockPartyList();
        var objectTable = MockBuilders.CreateMockObjectTable();

        var manager = new KardiaManager(partyList.Object, objectTable.Object);

        // Fresh manager (no zone change yet) is not in warmup.
        Assert.False(manager.IsPostZoneWarmupActive);

        // Zone change starts the grace period.
        manager.ResetSession();
        Assert.True(manager.IsPostZoneWarmupActive);

        // Once the grace period elapses, placement is allowed again.
        typeof(KardiaManager)
            .GetField("_sessionResetTime", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(manager, DateTime.Now.AddSeconds(-(KardiaManager.PostZoneWarmupSeconds + 0.1)));

        Assert.False(manager.IsPostZoneWarmupActive);
    }

    [Fact]
    public void IsSolo_NoPartyAndNoTrustAllies_ReturnsTrue()
    {
        var partyList = MockBuilders.CreateMockPartyList(length: 0);
        var objectTable = MockBuilders.CreateMockObjectTable();
        var player = MockBuilders.CreateMockPlayerCharacter(level: 90);

        var manager = new KardiaManager(partyList.Object, objectTable.Object);

        Assert.True(manager.IsSolo(player.Object));
    }

    [Fact]
    public void IsSolo_WithParty_ReturnsFalse()
    {
        var partyList = MockBuilders.CreateMockPartyList(length: 4);
        var objectTable = MockBuilders.CreateMockObjectTable();
        var player = MockBuilders.CreateMockPlayerCharacter(level: 90);

        var manager = new KardiaManager(partyList.Object, objectTable.Object);

        Assert.False(manager.IsSolo(player.Object));
    }

    [Fact]
    public void IsKardionOnTarget_SoloSelfWithoutKardiaBuff_AllowsFirstCast()
    {
        var partyList = MockBuilders.CreateMockPartyList(length: 0);
        var objectTable = MockBuilders.CreateMockObjectTable();
        var player = MockBuilders.CreateMockPlayerCharacter(level: 90);

        var manager = new KardiaManager(partyList.Object, objectTable.Object);

        // No Kardia self-buff yet → self is not considered to already have Kardion, so the
        // first solo self-cast is allowed (not suppressed).
        Assert.False(manager.IsKardionOnTarget(
            player.Object, player.Object, objectTable.Object, partyList.Object));
    }

    [Fact]
    public void UpdateKardiaTarget_BearerUnresolvable_KeepsLatchDuringGrace()
    {
        var partyList = MockBuilders.CreateMockPartyList();
        var objectTable = MockBuilders.CreateMockObjectTable(); // SearchByEntityId → null (absent)
        var tank = MockBuilders.CreateMockBattleChara(entityId: 42u, currentHp: 100000, maxHp: 100000);
        var player = MockBuilders.CreateMockPlayerCharacter(level: 90);

        var manager = new KardiaManager(partyList.Object, objectTable.Object);
        manager.ConfirmTankKardion(tank.Object);

        // First frame of absence only STARTS the grace timer — object-table flicker must not
        // instantly kill the latch.
        manager.UpdateKardiaTarget(player.Object);

        Assert.True(manager.IsTankKardionLatched(42u));
        Assert.True(manager.HasKardia);
    }

    [Fact]
    public void UpdateKardiaTarget_BearerGonePastGrace_DropsLatch()
    {
        var partyList = MockBuilders.CreateMockPartyList();
        var objectTable = MockBuilders.CreateMockObjectTable(); // SearchByEntityId → null (left zone)
        var tank = MockBuilders.CreateMockBattleChara(entityId: 42u, currentHp: 100000, maxHp: 100000);
        var player = MockBuilders.CreateMockPlayerCharacter(level: 90);

        var manager = new KardiaManager(partyList.Object, objectTable.Object);
        manager.ConfirmTankKardion(tank.Object);
        manager.UpdateKardiaTarget(player.Object); // starts the absence timer

        typeof(KardiaManager)
            .GetField("_bearerAbsentSinceUtc", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(manager, DateTime.Now.AddSeconds(-(KardiaManager.BearerAbsenceGraceSeconds + 0.1)));

        // Tank left the zone: the latch must drop and the manager must stop asserting a phantom
        // placement (the chain-cast bug was this latch surviving forever mid-zone).
        manager.UpdateKardiaTarget(player.Object);

        Assert.False(manager.IsTankKardionLatched(42u));
        Assert.False(manager.HasKardia);
        Assert.Equal(0ul, manager.CurrentKardiaTarget);
    }

    [Fact]
    public void UpdateKardiaTarget_BearerDead_DropsLatchImmediately()
    {
        var partyList = MockBuilders.CreateMockPartyList();
        var objectTable = MockBuilders.CreateMockObjectTable();
        var deadTank = MockBuilders.CreateMockBattleChara(entityId: 42u, currentHp: 0, maxHp: 100000);
        objectTable.Setup(x => x.SearchByEntityId(42u)).Returns(deadTank.Object);
        var player = MockBuilders.CreateMockPlayerCharacter(level: 90);

        var manager = new KardiaManager(partyList.Object, objectTable.Object);
        manager.ConfirmTankKardion(deadTank.Object);

        // Kardion strips on death — no grace period, the placement is gone with certainty.
        manager.UpdateKardiaTarget(player.Object);

        Assert.False(manager.IsTankKardionLatched(42u));
        Assert.False(manager.HasKardia);
    }

    [Fact]
    public void UpdateKardiaTarget_BearerAliveAndPresent_KeepsLatch()
    {
        var partyList = MockBuilders.CreateMockPartyList();
        var objectTable = MockBuilders.CreateMockObjectTable();
        var tank = MockBuilders.CreateMockBattleChara(entityId: 42u, currentHp: 90000, maxHp: 100000);
        objectTable.Setup(x => x.SearchByEntityId(42u)).Returns(tank.Object);
        var player = MockBuilders.CreateMockPlayerCharacter(level: 90);

        var manager = new KardiaManager(partyList.Object, objectTable.Object);
        manager.ConfirmTankKardion(tank.Object);

        manager.UpdateKardiaTarget(player.Object);

        Assert.True(manager.IsTankKardionLatched(42u));
        Assert.True(manager.HasKardia);
    }

    [Fact]
    public void IsKardionOnTarget_SelfInParty_ReturnsFalse()
    {
        var partyList = MockBuilders.CreateMockPartyList(length: 4);
        var objectTable = MockBuilders.CreateMockObjectTable();
        var player = MockBuilders.CreateMockPlayerCharacter(level: 90);

        var manager = new KardiaManager(partyList.Object, objectTable.Object);

        // In a party the Sage is never the Kardia target, so self always reports no Kardion.
        Assert.False(manager.IsKardionOnTarget(
            player.Object, player.Object, objectTable.Object, partyList.Object));
    }
}
