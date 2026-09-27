using Daedalus.Services.Interaction;
using Xunit;

namespace Daedalus.Tests.Services.Interaction;

/// <summary>
/// When a toon clicks the object Minerva names for a mechanic (Aulus mal Asina's Empty Vessel, 2026-09-27: walked
/// back by hand, clicked by hand). Each test changes one fact of a situation where the click should go out.
/// </summary>
public sealed class MechanicInteractPolicyTests
{
    private static MechanicInteractSituation Ready() => new(
        Enabled: true,
        TargetId: 0x40034F4A,
        TargetFound: true,
        TargetTargetable: true,
        InRange: true,
        SelfAlive: true,
        SelfCasting: false,
        AnimationLocked: false,
        SecondsSinceLastAttempt: 10);

    [Fact]
    public void InRangeAndIdle_Interacts()
        => Assert.Equal(MechanicInteractDecision.Interact, MechanicInteractPolicy.Decide(Ready()));

    [Fact]
    public void ToggledOff_DoesNothing()
        => Assert.Equal(MechanicInteractDecision.Disabled, MechanicInteractPolicy.Decide(Ready() with { Enabled = false }));

    [Fact]
    public void NothingNamed_DoesNothing()
        => Assert.Equal(MechanicInteractDecision.NoTarget, MechanicInteractPolicy.Decide(Ready() with { TargetId = 0 }));

    [Fact]
    public void NamedObjectGoneOrUntargetable_DoesNothing()
    {
        Assert.Equal(MechanicInteractDecision.TargetMissing, MechanicInteractPolicy.Decide(Ready() with { TargetFound = false }));
        Assert.Equal(MechanicInteractDecision.TargetMissing, MechanicInteractPolicy.Decide(Ready() with { TargetTargetable = false }));
    }

    [Fact]
    public void Dead_DoesNothing()
        => Assert.Equal(MechanicInteractDecision.Dead, MechanicInteractPolicy.Decide(Ready() with { SelfAlive = false }));

    [Fact]
    public void OutOfTheGamesInteractRange_WaitsForTheWalk()
        => Assert.Equal(MechanicInteractDecision.OutOfRange, MechanicInteractPolicy.Decide(Ready() with { InRange = false }));

    [Fact]
    public void CastingOrAnimationLocked_Waits()
    {
        Assert.Equal(MechanicInteractDecision.Busy, MechanicInteractPolicy.Decide(Ready() with { SelfCasting = true }));
        Assert.Equal(MechanicInteractDecision.Busy, MechanicInteractPolicy.Decide(Ready() with { AnimationLocked = true }));
    }

    [Fact]
    public void JustClicked_DoesNotClickAgain_TheSecondClickWouldCancelTheFirst()
    {
        Assert.Equal(MechanicInteractDecision.Throttled,
            MechanicInteractPolicy.Decide(Ready() with { SecondsSinceLastAttempt = MechanicInteractPolicy.RetrySeconds - 0.1 }));
        Assert.Equal(MechanicInteractDecision.Interact,
            MechanicInteractPolicy.Decide(Ready() with { SecondsSinceLastAttempt = MechanicInteractPolicy.RetrySeconds }));
    }
}
