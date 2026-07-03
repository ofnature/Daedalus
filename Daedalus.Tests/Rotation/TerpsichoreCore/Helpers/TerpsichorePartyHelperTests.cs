using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using Moq;
using Daedalus.Data;
using Daedalus.Rotation.TerpsichoreCore.Helpers;
using Daedalus.Tests.Mocks;
using Xunit;

namespace Daedalus.Tests.Rotation.TerpsichoreCore.Helpers;

/// <summary>
/// Pure-policy tests for the dance-partner priority table and the strict-upgrade decision,
/// plus Trust-duty partner selection (the party list is EMPTY in Trust content — allies come
/// from the object table; regression for the Lv81 "no Closed Position with trusts" bug).
/// (Live status resolution in <see cref="TerpsichorePartyHelper.ShouldUpdatePartner"/> needs
/// the game's status lists, so it is in-game-validation-only; the decision core is here.)
/// </summary>
public class TerpsichorePartyHelperTests
{
    // --- Trust-duty partner selection ---

    private static Mock<IBattleNpc> CreateTrustNpc(uint entityId, uint currentHp = 50000u)
    {
        var npc = new Mock<IBattleNpc>();
        npc.Setup(x => x.ObjectKind).Returns(ObjectKind.BattleNpc);
        npc.Setup(x => x.SubKind).Returns((byte)FFXIVConstants.TrustNpcSubKind);
        npc.Setup(x => x.EntityId).Returns(entityId);
        npc.Setup(x => x.CurrentHp).Returns(currentHp);
        npc.Setup(x => x.MaxHp).Returns(50000u);
        npc.Setup(x => x.StatusFlags).Returns((StatusFlags)0);
        // ClassJob left default (RowId 0) — trust avatars often report no job; they must
        // still be selectable as last-resort partners.
        return npc;
    }

    private static Mock<IBattleNpc> CreateEnemyNpc(uint entityId)
    {
        var npc = new Mock<IBattleNpc>();
        npc.Setup(x => x.ObjectKind).Returns(ObjectKind.BattleNpc);
        npc.Setup(x => x.SubKind).Returns(Daedalus.Compat.BattleNpcKinds.Combatant);
        npc.Setup(x => x.EntityId).Returns(entityId);
        npc.Setup(x => x.CurrentHp).Returns(1000000u);
        npc.Setup(x => x.MaxHp).Returns(1000000u);
        return npc;
    }

    private static TerpsichorePartyHelper CreateTrustModeHelper(params IGameObject[] objects)
    {
        var objectTable = MockBuilders.CreateMockObjectTable();
        objectTable.Setup(x => x.GetEnumerator())
            .Returns(() => ((IEnumerable<IGameObject>)objects.ToList()).GetEnumerator());
        var partyList = MockBuilders.CreateMockPartyList(length: 0);
        return new TerpsichorePartyHelper(objectTable.Object, partyList.Object);
    }

    [Fact]
    public void TrustDuty_SelectsTrustNpcAsPartner()
    {
        var trust = CreateTrustNpc(entityId: 2);
        var enemy = CreateEnemyNpc(entityId: 3);
        var helper = CreateTrustModeHelper(trust.Object, enemy.Object);
        var player = MockBuilders.CreateMockPlayerCharacter();

        var partner = helper.SelectDancePartner(player.Object);

        Assert.NotNull(partner);
        Assert.Equal(2u, partner!.EntityId);
    }

    [Fact]
    public void TrustDuty_MeleePriorityMode_FallsBackToTrustNpc()
    {
        // Avatars report job 0 (not melee) — role-priority mode must still land on one.
        var trust = CreateTrustNpc(entityId: 2);
        var helper = CreateTrustModeHelper(trust.Object);
        var player = MockBuilders.CreateMockPlayerCharacter();

        var partner = helper.SelectDancePartner(player.Object, Daedalus.Config.DPS.PartnerSelection.MeleePriority);

        Assert.NotNull(partner);
        Assert.Equal(2u, partner!.EntityId);
    }

    [Fact]
    public void TrustDuty_DeadTrustNpc_NotSelected()
    {
        var deadTrust = CreateTrustNpc(entityId: 2, currentHp: 0);
        var helper = CreateTrustModeHelper(deadTrust.Object);
        var player = MockBuilders.CreateMockPlayerCharacter();

        Assert.Null(helper.SelectDancePartner(player.Object));
    }

    [Fact]
    public void Solo_NoTrusts_NoPartner()
    {
        var helper = CreateTrustModeHelper();
        var player = MockBuilders.CreateMockPlayerCharacter();

        Assert.Null(helper.SelectDancePartner(player.Object));
    }

    [Fact]
    public void TrustDuty_GetAllPartyMembers_YieldsPlayerAndTrusts()
    {
        var trust1 = CreateTrustNpc(entityId: 2);
        var trust2 = CreateTrustNpc(entityId: 3);
        var enemy = CreateEnemyNpc(entityId: 4);
        var helper = CreateTrustModeHelper(trust1.Object, trust2.Object, enemy.Object);
        var player = MockBuilders.CreateMockPlayerCharacter();

        var members = helper.GetAllPartyMembers(player.Object).ToList();

        Assert.Equal(3, members.Count); // player + 2 trusts, enemy excluded
        Assert.Contains(members, m => m.EntityId == 1u);
        Assert.Contains(members, m => m.EntityId == 2u);
        Assert.Contains(members, m => m.EntityId == 3u);
    }
    // --- Priority table (refreshed for Dawntrail 7.x) ---

    [Fact]
    public void Pictomancer_OutranksAllMelee()
    {
        var pct = TerpsichorePartyHelper.GetJobPriority(JobRegistry.Pictomancer);
        Assert.True(pct < TerpsichorePartyHelper.GetJobPriority(JobRegistry.Samurai));
        Assert.True(pct < TerpsichorePartyHelper.GetJobPriority(JobRegistry.Viper));
        Assert.True(pct < TerpsichorePartyHelper.GetJobPriority(JobRegistry.Dragoon));
        Assert.True(pct < TerpsichorePartyHelper.GetJobPriority(JobRegistry.Monk));
        Assert.True(pct < TerpsichorePartyHelper.GetJobPriority(JobRegistry.Reaper));
        Assert.True(pct < TerpsichorePartyHelper.GetJobPriority(JobRegistry.Ninja));
    }

    [Fact]
    public void Melee_OutranksCastersRangedAndSupport()
    {
        var dragoon = TerpsichorePartyHelper.GetJobPriority(JobRegistry.Dragoon);
        Assert.True(dragoon < TerpsichorePartyHelper.GetJobPriority(JobRegistry.BlackMage));
        Assert.True(dragoon < TerpsichorePartyHelper.GetJobPriority(JobRegistry.Machinist));
        Assert.True(dragoon < TerpsichorePartyHelper.GetJobPriority(JobRegistry.Bard));
    }

    [Fact]
    public void Dps_OutranksTanks_AndTanks_OutrankHealers()
    {
        var bard = TerpsichorePartyHelper.GetJobPriority(JobRegistry.Bard);
        var paladin = TerpsichorePartyHelper.GetJobPriority(JobRegistry.Paladin);
        var whiteMage = TerpsichorePartyHelper.GetJobPriority(JobRegistry.WhiteMage);

        Assert.True(bard < paladin);     // any DPS over a tank
        Assert.True(paladin < whiteMage); // tank over a healer
    }

    [Fact]
    public void UnknownJob_IsLowestPriority_ButSelectable()
    {
        // Lowest rank of all — but NOT int.MaxValue: that could never win the `< bestPriority`
        // scan, leaving the dancer partnerless instead of taking a last-resort partner.
        var unknown = TerpsichorePartyHelper.GetJobPriority(9999);
        Assert.True(unknown > TerpsichorePartyHelper.GetJobPriority(JobRegistry.Sage));
        Assert.True(unknown < int.MaxValue);
    }

    // --- Strict-upgrade decision ---

    [Fact]
    public void ShouldUpgrade_WhenCandidateStrictlyBetter()
    {
        // Pictomancer (idx 0) available while partnered to Dragoon (idx 4).
        var current = TerpsichorePartyHelper.GetJobPriority(JobRegistry.Dragoon);
        var candidate = TerpsichorePartyHelper.GetJobPriority(JobRegistry.Pictomancer);
        Assert.True(TerpsichorePartyHelper.ShouldUpgradePartner(current, candidate));
    }

    [Fact]
    public void ShouldNotUpgrade_WhenCandidateEqualPriority()
    {
        // Two Samurai — no thrash.
        var sam = TerpsichorePartyHelper.GetJobPriority(JobRegistry.Samurai);
        Assert.False(TerpsichorePartyHelper.ShouldUpgradePartner(sam, sam));
    }

    [Fact]
    public void ShouldNotUpgrade_WhenCandidateWorse()
    {
        var current = TerpsichorePartyHelper.GetJobPriority(JobRegistry.Pictomancer);
        var candidate = TerpsichorePartyHelper.GetJobPriority(JobRegistry.WhiteMage);
        Assert.False(TerpsichorePartyHelper.ShouldUpgradePartner(current, candidate));
    }

    [Fact]
    public void ShouldUpgrade_FromUnknownToKnownJob()
    {
        // Currently partnered to an unrecognized job (MaxValue) — any known job is an upgrade.
        var current = TerpsichorePartyHelper.GetJobPriority(9999);
        var candidate = TerpsichorePartyHelper.GetJobPriority(JobRegistry.Bard);
        Assert.True(TerpsichorePartyHelper.ShouldUpgradePartner(current, candidate));
    }
}
