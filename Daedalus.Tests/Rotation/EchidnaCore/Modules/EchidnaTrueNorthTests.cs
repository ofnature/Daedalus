using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Moq;
using Daedalus.Data;
using Daedalus.Rotation.Common.Scheduling;
using Daedalus.Rotation.EchidnaCore.Abilities;
using Daedalus.Rotation.EchidnaCore.Modules;
using Daedalus.Services.Targeting;
using Daedalus.Tests.Mocks;
using Daedalus.Tests.Rotation.Common.Scheduling;
using Xunit;

namespace Daedalus.Tests.Rotation.EchidnaCore.Modules;

/// <summary>
/// RSR only presses True North automatically when the NEXT GCD owes a positional the player isn't at
/// (StateUpdater.ShouldAddPositional), and only in the last weave slot (CustomRotation_Ability,
/// WeaponRemain &lt;= 1.35s). Viper used to press it whenever it stood at the front — in packs, during
/// Reawaken, before Dens — and in any weave slot.
/// </summary>
public class EchidnaTrueNorthTests
{
    [Fact]
    public void FirstWeaveSlot_HuntersCoilNext_HoldsTrueNorth()
    {
        var scheduler = Collect(gcdRemaining: 2.0f, dreadCombo: VPRActions.DreadCombo.DreadwindyReady);

        Assert.DoesNotContain(scheduler.InspectOgcdQueue(), c => c.Behavior == EchidnaAbilities.TrueNorth);
    }

    [Theory]
    [InlineData(1.35f)]
    [InlineData(1.0f)]
    public void LastWeaveSlot_HuntersCoilNext_UsesTrueNorth(float gcdRemaining)
    {
        var scheduler = Collect(gcdRemaining, dreadCombo: VPRActions.DreadCombo.DreadwindyReady);

        Assert.Contains(scheduler.InspectOgcdQueue(), c => c.Behavior == EchidnaAbilities.TrueNorth);
    }

    [Fact]
    public void AtFlank_SwiftskinsCoilNext_UsesTrueNorth()
    {
        // Hunter's Coil used (game byte 2) → Swiftskin's Coil is a rear hit; the flank doesn't cover it.
        var scheduler = Collect(1.0f, dreadCombo: VPRActions.DreadCombo.SwiftskinCoilReady, isAtFlank: true);

        Assert.Contains(scheduler.InspectOgcdQueue(), c => c.Behavior == EchidnaAbilities.TrueNorth);
    }

    [Fact]
    public void AtFlank_HuntersCoilNext_NoTrueNorth()
    {
        var scheduler = Collect(1.0f, dreadCombo: VPRActions.DreadCombo.DreadwindyReady, isAtFlank: true);

        Assert.DoesNotContain(scheduler.InspectOgcdQueue(), c => c.Behavior == EchidnaAbilities.TrueNorth);
    }

    [Fact]
    public void PackComboStep_NoPositionalOwed_NoTrueNorth()
    {
        // After Hunter's Bite (AoE step 2) the next GCD is a Bane — no positional.
        var scheduler = Collect(1.0f, lastComboAction: 34616, comboStep: 2);

        Assert.DoesNotContain(scheduler.InspectOgcdQueue(), c => c.Behavior == EchidnaAbilities.TrueNorth);
    }

    [Fact]
    public void Reawakened_NoTrueNorth()
    {
        var scheduler = Collect(1.0f, dreadCombo: VPRActions.DreadCombo.DreadwindyReady, isReawakened: true);

        Assert.DoesNotContain(scheduler.InspectOgcdQueue(), c => c.Behavior == EchidnaAbilities.TrueNorth);
    }

    [Fact]
    public void BelowTrueNorthLevel_NoTrueNorth()
    {
        // True North is Lv50; the Sting finisher (rear venom) is owed.
        var scheduler = Collect(1.0f, lastComboAction: 34608, comboStep: 2, hasHindstungVenom: true, level: 40);

        Assert.DoesNotContain(scheduler.InspectOgcdQueue(), c => c.Behavior == EchidnaAbilities.TrueNorth);
    }

    [Fact]
    public void FinisherStep_RearVenom_LastWeave_UsesTrueNorth()
    {
        var scheduler = Collect(1.0f, lastComboAction: 34608, comboStep: 2, hasHindstungVenom: true);

        Assert.Contains(scheduler.InspectOgcdQueue(), c => c.Behavior == EchidnaAbilities.TrueNorth);
    }

    private static RotationScheduler Collect(
        float gcdRemaining,
        VPRActions.DreadCombo dreadCombo = VPRActions.DreadCombo.None,
        uint lastComboAction = 0,
        int comboStep = 0,
        bool isAtFlank = false,
        bool isReawakened = false,
        bool hasHindstungVenom = false,
        byte level = 100)
    {
        var enemy = new Mock<IBattleNpc>();
        enemy.Setup(x => x.GameObjectId).Returns(99999UL);
        enemy.Setup(x => x.CurrentHp).Returns(100000u);
        enemy.Setup(x => x.MaxHp).Returns(100000u);

        var targeting = MockBuilders.CreateMockTargetingService();
        targeting.Setup(x => x.FindEnemyForAction(
                It.IsAny<EnemyTargetingStrategy>(), It.IsAny<uint>(), It.IsAny<IPlayerCharacter>()))
            .Returns(enemy.Object);

        var actions = MockBuilders.CreateMockActionService();
        actions.Setup(x => x.IsActionReady(It.IsAny<uint>())).Returns(true);
        actions.Setup(x => x.GcdRemaining).Returns(gcdRemaining);

        var context = EchidnaTestContext.Create(
            actionService: actions,
            targetingService: targeting,
            level: level,
            comboStep: comboStep,
            lastComboAction: lastComboAction,
            comboTimeRemaining: lastComboAction != 0 ? 20f : 0f,
            isAtFlank: isAtFlank,
            isReawakened: isReawakened,
            dreadCombo: dreadCombo,
            hasHindstungVenom: hasHindstungVenom);
        var scheduler = SchedulerFactory.CreateForTest(actionService: actions);

        new DamageModule().CollectCandidates(context, scheduler, isMoving: false);
        return scheduler;
    }
}
