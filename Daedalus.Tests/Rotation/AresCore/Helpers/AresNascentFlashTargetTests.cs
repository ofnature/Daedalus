using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Moq;
using Daedalus.Data;
using Daedalus.Rotation.AresCore.Helpers;
using Daedalus.Tests.Mocks;
using Xunit;

namespace Daedalus.Tests.Rotation.AresCore.Helpers;

/// <summary>
/// RSR WAR_Reborn NascentFlashCanUse + StatusHelper.CanBeHealed (6b52e1df): Nascent Flash skips a
/// member whose invuln or heal block lasts beyond two GCDs. Without it, a co-tank sitting at 1 HP in
/// Superbolide won the lowest-HP pick. Dalamud's StatusList can't be faked, so the per-member status
/// read goes through the helper's seam; the status-id list is checked directly.
/// </summary>
public class AresNascentFlashTargetTests
{
    private const ulong CoTankId = 2;
    private const ulong HealerId = 3;

    [Fact]
    public void LowestAllyInSuperbolide_IsSkippedForNextLowest()
    {
        var coTank = CreateTrustNpc(CoTankId, currentHp: 1u);       // Superbolide drops HP to 1
        var healer = CreateTrustNpc(HealerId, currentHp: 20000u);  // 40%
        var helper = CreateTrustModeHelper(coTank.Object, healer.Object);

        var target = helper.FindNascentFlashTarget(
            MockBuilders.CreateMockPlayerCharacter().Object, 0.60f, gcdDuration: 2.5f,
            noHealStatusRemaining: m => m.GameObjectId == CoTankId ? 8f : 0f);

        Assert.NotNull(target);
        Assert.Equal(HealerId, target!.GameObjectId);
    }

    [Fact]
    public void OnlyLowAllyInvulnerable_NoTarget()
    {
        var coTank = CreateTrustNpc(CoTankId, currentHp: 1u);
        var healer = CreateTrustNpc(HealerId, currentHp: 50000u);
        var helper = CreateTrustModeHelper(coTank.Object, healer.Object);

        var target = helper.FindNascentFlashTarget(
            MockBuilders.CreateMockPlayerCharacter().Object, 0.60f, gcdDuration: 2.5f,
            noHealStatusRemaining: m => m.GameObjectId == CoTankId ? 8f : 0f);

        Assert.Null(target);
    }

    [Theory]
    [InlineData(4.9f, 2.5f)]   // invuln ends within two GCDs
    [InlineData(5.0f, 2.5f)]   // exactly two GCDs
    public void InvulnEndingWithinTwoGcds_StillTargeted(float invulnRemaining, float gcd)
    {
        // RSR's CanBeHealed uses WillStatusEndGCD(2): the tank is about to take damage again.
        var coTank = CreateTrustNpc(CoTankId, currentHp: 1u);
        var healer = CreateTrustNpc(HealerId, currentHp: 20000u);
        var helper = CreateTrustModeHelper(coTank.Object, healer.Object);

        var target = helper.FindNascentFlashTarget(
            MockBuilders.CreateMockPlayerCharacter().Object, 0.60f, gcd,
            noHealStatusRemaining: m => m.GameObjectId == CoTankId ? invulnRemaining : 0f);

        Assert.Equal(CoTankId, target!.GameObjectId);
    }

    [Fact]
    public void ShorterGcd_ShrinksTheWindow()
    {
        // 4.9s left is inside two 2.5s GCDs but beyond two 2.0s GCDs.
        var coTank = CreateTrustNpc(CoTankId, currentHp: 1u);
        var healer = CreateTrustNpc(HealerId, currentHp: 20000u);
        var helper = CreateTrustModeHelper(coTank.Object, healer.Object);

        var target = helper.FindNascentFlashTarget(
            MockBuilders.CreateMockPlayerCharacter().Object, 0.60f, gcdDuration: 2.0f,
            noHealStatusRemaining: m => m.GameObjectId == CoTankId ? 4.9f : 0f);

        Assert.Equal(HealerId, target!.GameObjectId);
    }

    [Theory]
    [InlineData(409u)]   // Holmgang
    [InlineData(810u)]   // Living Dead
    [InlineData(1836u)]  // Superbolide
    [InlineData(82u)]    // Hallowed Ground
    [InlineData(2852u)]  // HP Recovery Down (RSR NoNeedHealingStatus)
    public void InvulnAndHealBlockStatuses_BlockNascentFlash(uint statusId)
    {
        Assert.True(AresPartyHelper.IsNascentFlashBlockingStatus(statusId));
    }

    [Theory]
    [InlineData(811u)]   // Walking Dead: the DRK must be healed to full or dies (RSR leaves it out too)
    [InlineData(89u)]    // Vengeance: ordinary mitigation
    [InlineData(0u)]
    public void OtherStatuses_DoNotBlockNascentFlash(uint statusId)
    {
        Assert.False(AresPartyHelper.IsNascentFlashBlockingStatus(statusId));
    }

    private static Mock<IBattleNpc> CreateTrustNpc(ulong gameObjectId, uint currentHp)
    {
        var npc = new Mock<IBattleNpc>();
        npc.Setup(x => x.ObjectKind).Returns(ObjectKind.BattleNpc);
        npc.Setup(x => x.SubKind).Returns((byte)FFXIVConstants.TrustNpcSubKind);
        npc.Setup(x => x.GameObjectId).Returns(gameObjectId);
        npc.Setup(x => x.EntityId).Returns((uint)gameObjectId);
        npc.Setup(x => x.CurrentHp).Returns(currentHp);
        npc.Setup(x => x.MaxHp).Returns(50000u);
        return npc;
    }

    private static AresPartyHelper CreateTrustModeHelper(params IGameObject[] objects)
    {
        var objectTable = MockBuilders.CreateMockObjectTable();
        objectTable.Setup(x => x.GetEnumerator())
            .Returns(() => ((IEnumerable<IGameObject>)objects.ToList()).GetEnumerator());
        var partyList = MockBuilders.CreateMockPartyList(length: 0);
        return new AresPartyHelper(objectTable.Object, partyList.Object);
    }
}
