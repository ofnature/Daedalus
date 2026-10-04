using Daedalus.Services.Movement;
using Xunit;

namespace Daedalus.Tests.Services.Movement;

public class DodgeSprintPolicyTests
{
    private static DodgeSprintSituation Ready() => new(
        Enabled: true, Wanted: true, SelfAlive: true, Mounted: false, AlreadySprinting: false,
        SprintReady: true, AnimationLocked: false, SecondsSinceLastAttempt: 10);

    [Fact]
    public void MinervaAsking_WithSprintReady_Sprints()
        => Assert.Equal(DodgeSprintDecision.Sprint, DodgeSprintPolicy.Decide(Ready()));

    [Fact]
    public void NotAsked_DoesNothing()
        => Assert.Equal(DodgeSprintDecision.NotWanted, DodgeSprintPolicy.Decide(Ready() with { Wanted = false }));

    [Fact]
    public void AlreadySprinting_OrOnCooldown_DoesNotPressAgain()
    {
        Assert.Equal(DodgeSprintDecision.AlreadySprinting, DodgeSprintPolicy.Decide(Ready() with { AlreadySprinting = true }));
        Assert.Equal(DodgeSprintDecision.NotReady, DodgeSprintPolicy.Decide(Ready() with { SprintReady = false }));
    }

    [Fact]
    public void Disabled_Dead_Mounted_Locked_OrJustTried_Wait()
    {
        Assert.Equal(DodgeSprintDecision.Disabled, DodgeSprintPolicy.Decide(Ready() with { Enabled = false }));
        Assert.Equal(DodgeSprintDecision.Dead, DodgeSprintPolicy.Decide(Ready() with { SelfAlive = false }));
        Assert.Equal(DodgeSprintDecision.Mounted, DodgeSprintPolicy.Decide(Ready() with { Mounted = true }));
        Assert.Equal(DodgeSprintDecision.Busy, DodgeSprintPolicy.Decide(Ready() with { AnimationLocked = true }));
        Assert.Equal(DodgeSprintDecision.Throttled, DodgeSprintPolicy.Decide(Ready() with { SecondsSinceLastAttempt = 0.1 }));
    }
}
