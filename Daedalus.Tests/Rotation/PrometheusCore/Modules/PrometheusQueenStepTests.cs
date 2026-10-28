using Dalamud.Game.ClientState.Objects.Types;
using Moq;
using Daedalus.Rotation.PrometheusCore.Helpers;
using Daedalus.Rotation.PrometheusCore.Modules;
using Daedalus.Tests.Mocks;
using Daedalus.Tests.Rotation.Common.Scheduling;
using Xunit;

namespace Daedalus.Tests.Rotation.PrometheusCore.Modules;

/// <summary>
/// The raid Queen step pairs advance once per summon (RSR 8153eea2): the gauge's last-summon Battery keeps its
/// value between pulls and can repeat, so it can't be what advances the step. Step 11 follows step 10's
/// 50→80 summon, so it is 80→100 (the old 70→100 could never match).
/// </summary>
public class PrometheusQueenStepTests
{
    [Fact]
    public void NewPull_StaleLastSummonBattery_DoesNotAdvanceStep()
    {
        var tracker = new PrometheusQueenTracker();
        var module = new BuffModule(null, null, tracker);

        // Out of combat after the previous pull, then the first in-combat frame of the next one: the gauge still
        // remembers the last pull's 50-Battery summon, but no Queen has been summoned this pull.
        Frame(module, inCombat: false, queenActive: false, lastQueenBattery: 50);
        Frame(module, inCombat: true, queenActive: false, lastQueenBattery: 50);

        Assert.Equal(0, tracker.CurrentStep);
    }

    [Fact]
    public void EleventhSummon_ExpectsEightyToHundred()
    {
        var tracker = new PrometheusQueenTracker();
        var module = new BuffModule(null, null, tracker);

        // The first 11 summons of the table; each Queen is out for a while, then gone.
        foreach (var summonBattery in new[] { 60, 90, 100, 50, 60, 100, 50, 70, 100, 50, 80 })
        {
            Frame(module, inCombat: true, queenActive: true, lastQueenBattery: summonBattery);
            Frame(module, inCombat: true, queenActive: false, lastQueenBattery: summonBattery);
        }

        Assert.Equal(11, tracker.CurrentStep);
        Assert.True(tracker.MatchesCurrentStep(lastQueenBattery: 80, battery: 100));
    }

    [Fact]
    public void TwoSummonsAtTheSameBattery_AdvanceTwice()
    {
        var tracker = new PrometheusQueenTracker();
        var module = new BuffModule(null, null, tracker);

        for (var i = 0; i < 2; i++)
        {
            Frame(module, inCombat: true, queenActive: true, lastQueenBattery: 50);
            Frame(module, inCombat: true, queenActive: false, lastQueenBattery: 50);
        }

        Assert.Equal(2, tracker.CurrentStep);
    }

    private static void Frame(BuffModule module, bool inCombat, bool queenActive, int lastQueenBattery)
    {
        var enemy = new Mock<IBattleNpc>();
        enemy.Setup(x => x.GameObjectId).Returns(0xBEEFUL);
        var targeting = MockBuilders.CreateMockTargetingService(findEnemy: (_, _, _) => enemy.Object);
        var actionService = MockBuilders.CreateMockActionService();

        var context = PrometheusTestContext.Create(actionService: actionService, targetingService: targeting,
            inCombat: inCombat, isQueenActive: queenActive, lastQueenBattery: lastQueenBattery);

        module.CollectCandidates(context, SchedulerFactory.CreateForTest(actionService: actionService), isMoving: false);
    }
}
