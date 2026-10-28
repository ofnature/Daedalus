using System.Linq;
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
/// The twinblade step comes from the raw gauge byte. The game (ClientStructs/Dalamud DreadCombo) stores the LAST
/// twinblade action: 1 Dreadwinder (Vicewinder), 2 Hunter's Coil, 3 Swiftskin's Coil, 4 Pit of Dread (Vicepit),
/// 5 Hunter's Den, 6 Swiftskin's Den. RSR reads it the same way (ViperRotation: "has HuntersCoil" →
/// SwiftskinsCoilOnly). The raw casts below pin the game's values, not our enum names.
/// </summary>
public class DamageModuleTwinbladeTests
{
    [Theory]
    [InlineData(1, "HuntersCoil")]    // Vicewinder used → either Coil, Hunter's first
    [InlineData(2, "SwiftskinsCoil")] // Hunter's Coil used → Swiftskin's Coil
    [InlineData(3, "HuntersCoil")]    // Swiftskin's Coil used → Hunter's Coil
    [InlineData(4, "HuntersDen")]     // Vicepit used → either Den, Hunter's first
    [InlineData(5, "SwiftskinsDen")]  // Hunter's Den used → Swiftskin's Den
    [InlineData(6, "HuntersDen")]     // Swiftskin's Den used → Hunter's Den
    public void GaugeState_PushesTheTwinbladeStepThatIsStillOwed(byte gaugeDreadCombo, string expected)
    {
        var enemy = new Mock<IBattleNpc>();
        enemy.Setup(x => x.GameObjectId).Returns(99999UL);
        enemy.Setup(x => x.CurrentHp).Returns(10000u);
        enemy.Setup(x => x.MaxHp).Returns(10000u);

        var targeting = MockBuilders.CreateMockTargetingService();
        targeting.Setup(x => x.FindEnemyForAction(
                It.IsAny<EnemyTargetingStrategy>(), It.IsAny<uint>(), It.IsAny<IPlayerCharacter>()))
            .Returns(enemy.Object);

        var actions = MockBuilders.CreateMockActionService();
        actions.Setup(x => x.IsActionReady(It.IsAny<uint>())).Returns(true);

        var context = EchidnaTestContext.Create(actionService: actions, targetingService: targeting,
            dreadCombo: (VPRActions.DreadCombo)gaugeDreadCombo);
        var scheduler = SchedulerFactory.CreateForTest(actionService: actions);

        new DamageModule().CollectCandidates(context, scheduler, isMoving: false);

        var twinblade = new[]
        {
            EchidnaAbilities.HuntersCoil, EchidnaAbilities.SwiftskinsCoil,
            EchidnaAbilities.HuntersDen, EchidnaAbilities.SwiftskinsDen,
        };
        var pushed = scheduler.InspectGcdQueue()
            .Where(c => twinblade.Contains(c.Behavior))
            .Select(c => c.Behavior)
            .ToList();

        var expectedBehavior = expected switch
        {
            "HuntersCoil" => EchidnaAbilities.HuntersCoil,
            "SwiftskinsCoil" => EchidnaAbilities.SwiftskinsCoil,
            "HuntersDen" => EchidnaAbilities.HuntersDen,
            _ => EchidnaAbilities.SwiftskinsDen,
        };
        Assert.Equal(new[] { expectedBehavior }, pushed);
    }
}
