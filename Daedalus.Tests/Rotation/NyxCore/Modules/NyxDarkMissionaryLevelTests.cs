using Daedalus.Data;
using Daedalus.Rotation.NyxCore.Abilities;
using Daedalus.Rotation.NyxCore.Modules;
using Daedalus.Tests.Mocks;
using Daedalus.Tests.Rotation.Common.Scheduling;
using Xunit;

namespace Daedalus.Tests.Rotation.NyxCore.Modules;

/// <summary>
/// Dark Missionary (16471) is learned at Lv66 per the game Action sheet; it was gated at 76, so the party
/// magic mitigation never fired at 66-75.
/// </summary>
public sealed class NyxDarkMissionaryLevelTests
{
    [Fact]
    public void DarkMissionary_MinLevel_MatchesGameSheet()
    {
        Assert.Equal(66, DRKActions.DarkMissionary.MinLevel);
    }

    [Theory]
    [InlineData(70)]
    [InlineData(66)]
    public void PartyInjured_BelowOldGate_PushesDarkMissionary(byte level)
    {
        var scheduler = Collect(level);

        Assert.Contains(scheduler.InspectOgcdQueue(), c => c.Behavior == NyxAbilities.DarkMissionary);
    }

    [Fact]
    public void PartyInjured_Lv65_DoesNotPushDarkMissionary()
    {
        var scheduler = Collect(65);

        Assert.DoesNotContain(scheduler.InspectOgcdQueue(), c => c.Behavior == NyxAbilities.DarkMissionary);
    }

    [Fact]
    public void PartyInjured_Lv80_PushesDarkMissionary()
    {
        var scheduler = Collect(80);

        Assert.Contains(scheduler.InspectOgcdQueue(), c => c.Behavior == NyxAbilities.DarkMissionary);
    }

    private static Daedalus.Rotation.Common.Scheduling.RotationScheduler Collect(byte level)
    {
        var actionService = MockBuilders.CreateMockActionService();
        // Solo party = the player; 60% HP puts the party average under Dark Missionary's 85% gate.
        var context = NyxTestContext.Create(actionService: actionService, currentHp: 30000, maxHp: 50000,
            level: level, inCombat: true);
        var scheduler = SchedulerFactory.CreateForTest(actionService: actionService);

        new MitigationModule().CollectCandidates(context, scheduler, isMoving: false);
        return scheduler;
    }
}
