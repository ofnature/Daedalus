using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Moq;
using Daedalus.Compat;
using Daedalus.Services.Targeting;
using Xunit;

namespace Daedalus.Tests.Services.Targeting;

public sealed class EnemyAttackabilityTests
{
    [Theory]
    [InlineData(BattleNpcKinds.Pet)]
    [InlineData(BattleNpcKinds.Chocobo)]
    [InlineData(BattleNpcKinds.NpcPartyMember)]
    public void IsExcludedBattleNpcKind_ExcludesCompanionTypes(byte kind)
    {
        var npc = new Mock<IBattleNpc>();
        npc.Setup(x => x.BattleNpcKind).Returns((BattleNpcSubKind)kind);

        Assert.True(EnemyAttackability.IsExcludedBattleNpcKind(npc.Object));
    }

    [Fact]
    public void IsExcludedBattleNpcKind_DoesNotExcludeCombatants()
    {
        var npc = new Mock<IBattleNpc>();
        npc.Setup(x => x.BattleNpcKind).Returns((BattleNpcSubKind)BattleNpcKinds.Combatant);

        Assert.False(EnemyAttackability.IsExcludedBattleNpcKind(npc.Object));
    }

    [Fact]
    public void IsPlayerAttackable_ReturnsFalseForExcludedKinds()
    {
        var npc = new Mock<IBattleNpc>();
        npc.Setup(x => x.BattleNpcKind).Returns(BattleNpcSubKind.Pet);
        npc.Setup(x => x.IsTargetable).Returns(true);
        npc.Setup(x => x.IsDead).Returns(false);

        Assert.False(EnemyAttackability.IsPlayerAttackable(npc.Object));
    }

    [Fact]
    public void IsPlayerAttackable_ReturnsFalseForNonBattleNpc()
    {
        var obj = new Mock<IGameObject>();
        Assert.False(EnemyAttackability.IsPlayerAttackable(obj.Object));
    }

    // --- IsMovementApproachable: probe-free movement gate (2026-07-04 field regression) ---
    // The attackability probe (CanUseActionOnTarget) false-negatives while the mob is still
    // out of range, so movement gated on it parked melee outside walk-in distance (NIN/rogue
    // "sometimes doesn't move into melee"). Movement resolution must pass on kind checks alone.

    [Fact]
    public void IsMovementApproachable_LiveHostileCombatant_Passes()
    {
        var npc = MovementNpc(kind: BattleNpcKinds.Combatant, subKind: 5);
        Assert.True(EnemyAttackability.IsMovementApproachable(npc.Object));
    }

    [Fact]
    public void IsMovementApproachable_SubKindZero_Passes()
    {
        // Quest/overworld mobs report SubKind 0 — same acceptance the combat filters use.
        var npc = MovementNpc(kind: 0, subKind: 0);
        Assert.True(EnemyAttackability.IsMovementApproachable(npc.Object));
    }

    [Theory]
    [InlineData(BattleNpcKinds.Pet)]
    [InlineData(BattleNpcKinds.Chocobo)]
    [InlineData(BattleNpcKinds.NpcPartyMember)]
    public void IsMovementApproachable_CompanionKinds_Fail(byte kind)
    {
        var npc = MovementNpc(kind: kind, subKind: kind);
        Assert.False(EnemyAttackability.IsMovementApproachable(npc.Object));
    }

    [Fact]
    public void IsMovementApproachable_DeadOrUntargetable_Fails()
    {
        var dead = MovementNpc(kind: BattleNpcKinds.Combatant, subKind: 5);
        dead.Setup(x => x.IsDead).Returns(true);
        Assert.False(EnemyAttackability.IsMovementApproachable(dead.Object));

        var untargetable = MovementNpc(kind: BattleNpcKinds.Combatant, subKind: 5);
        untargetable.Setup(x => x.IsTargetable).Returns(false);
        Assert.False(EnemyAttackability.IsMovementApproachable(untargetable.Object));
    }

    [Fact]
    public void IsMovementApproachable_NullOrNonBattleNpc_Fails()
    {
        Assert.False(EnemyAttackability.IsMovementApproachable(null));
        Assert.False(EnemyAttackability.IsMovementApproachable(new Mock<IGameObject>().Object));
    }

    private static Mock<IBattleNpc> MovementNpc(byte kind, byte subKind)
    {
        var npc = new Mock<IBattleNpc>();
        npc.Setup(x => x.BattleNpcKind).Returns((BattleNpcSubKind)kind);
        npc.Setup(x => x.SubKind).Returns(subKind);
        npc.Setup(x => x.IsTargetable).Returns(true);
        npc.Setup(x => x.IsDead).Returns(false);
        return npc;
    }
}
