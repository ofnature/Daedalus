using Daedalus.Rotation.ThemisCore.Abilities;
using Daedalus.Rotation.ThemisCore.Modules;
using Daedalus.Tests.Mocks;
using Daedalus.Tests.Rotation.Common.Scheduling;
using Xunit;

namespace Daedalus.Tests.Rotation.ThemisCore;

/// <summary>
/// 2026-07-20 move/cast-gate audit: Clemency (1.5s hard cast, no instant proc) was the only
/// ungated cast-time push site left in the codebase. While moving it would start the cast and get
/// interrupted every GCD at priority 1 — no heal AND a stalled rotation. It now routes through
/// MechanicCastGate like every other hard cast.
/// </summary>
public sealed class ThemisClemencyCastGateTests
{
    private readonly MitigationModule _module = new();

    private static Configuration ClemencyEnabledConfig()
    {
        var config = ThemisTestContext.CreateDefaultPaladinConfiguration();
        config.Tank.EnableClemency = true; // opt-in feature — off by default
        return config;
    }

    [Fact]
    public void Clemency_Held_WhileMoving()
    {
        var actionService = MockBuilders.CreateMockActionService();
        var config = ClemencyEnabledConfig();
        var scheduler = SchedulerFactory.CreateForTest(config: config, actionService: actionService);
        // Self at 20% HP — well below the 30% Clemency threshold.
        var context = ThemisTestContext.Create(
            config: config, actionService: actionService, currentHp: 10000, maxHp: 50000, isMoving: true);

        _module.CollectCandidates(context, scheduler, isMoving: true);

        Assert.DoesNotContain(scheduler.InspectGcdQueue(), c => c.Behavior == ThemisAbilities.Clemency);
    }

    [Fact]
    public void Clemency_Fires_WhenPlanted()
    {
        var actionService = MockBuilders.CreateMockActionService();
        var config = ClemencyEnabledConfig();
        var scheduler = SchedulerFactory.CreateForTest(config: config, actionService: actionService);
        var context = ThemisTestContext.Create(
            config: config, actionService: actionService, currentHp: 10000, maxHp: 50000, isMoving: false);

        _module.CollectCandidates(context, scheduler, isMoving: false);

        Assert.Contains(scheduler.InspectGcdQueue(), c => c.Behavior == ThemisAbilities.Clemency);
    }
}
