using System;
using System.Linq;
using Daedalus.Data;
using Daedalus.Rotation.ArtemisCore.Helpers;
using Daedalus.Rotation.ArtemisCore.Modules;
using Xunit;

namespace Daedalus.Tests.Rotation.ArtemisCore;

/// <summary>
/// The Beastmaster combo is the familiar's Trick answered by the clockwise axe (Trick first, so our own
/// skill finishes it and banks Mastered Instinct). Before this, Artemis fired axes on their own and
/// Trick blind, so the pair only happened by accident.
/// </summary>
public class ArtemisTrickPairingTests
{
    private DateTime _now = new(2026, 9, 29, 20, 0, 0, DateTimeKind.Utc);
    private readonly ArtemisTrickPairing _pairing;

    public ArtemisTrickPairingTests() => _pairing = new ArtemisTrickPairing { UtcNow = () => _now };

    private void Advance(double s) => _now = _now.AddSeconds(s);

    [Theory]
    [InlineData(InstinctAffinity.Rampant, InstinctAffinity.Durant)]   // red → blue: Mistral Axe
    [InlineData(InstinctAffinity.Durant, InstinctAffinity.Eldritch)]  // blue → yellow: Spinning Axe
    [InlineData(InstinctAffinity.Eldritch, InstinctAffinity.Volant)]  // yellow → green: Gale Axe
    [InlineData(InstinctAffinity.Volant, InstinctAffinity.Rampant)]   // green → red: Avalanche Axe
    public void TheAnswerIsTheClockwiseAxe(InstinctAffinity pet, InstinctAffinity axe)
    {
        _pairing.OnTrickDispatched(pet);
        Assert.Equal(axe, _pairing.AnswerAffinity);
    }

    /// <summary>
    /// The familiar is slow to act. Answer straight away and our axe resolves first — the combo comes out
    /// backwards and banks the wrong diamond.
    /// </summary>
    [Fact]
    public void DoesNotAnswerBeforeTheFamiliarActs()
    {
        _pairing.OnTrickDispatched(InstinctAffinity.Rampant);
        Advance(0.3);
        Assert.True(_pairing.IsAwaitingAnswer);
        Assert.False(_pairing.ReadyToAnswer);
    }

    [Fact]
    public void AnswersOnceTheFamiliarActs()
    {
        _pairing.OnTrickDispatched(InstinctAffinity.Rampant);
        Advance(0.6);
        _pairing.OnFamiliarAction();
        Assert.True(_pairing.ReadyToAnswer);
    }

    /// <summary>If the familiar's action is never seen, answer after the fallback delay anyway.</summary>
    [Fact]
    public void AnswersAfterTheFallbackDelayWithoutSeeingTheFamiliar()
    {
        _pairing.OnTrickDispatched(InstinctAffinity.Durant);
        Advance(ArtemisTrickPairing.FallbackAnswerSeconds - 0.1);
        Assert.False(_pairing.ReadyToAnswer);
        Advance(0.2);
        Assert.True(_pairing.ReadyToAnswer);
    }

    /// <summary>The Heart lasts 7 s; past the deadline the pending answer is dropped.</summary>
    [Fact]
    public void GivesUpAtTheDeadline()
    {
        _pairing.OnTrickDispatched(InstinctAffinity.Volant);
        Advance(ArtemisTrickPairing.AnswerDeadlineSeconds + 0.1);
        Assert.False(_pairing.IsAwaitingAnswer);
        Assert.Equal(InstinctAffinity.None, _pairing.AnswerAffinity);
    }

    /// <summary>The familiar swinging before any Trick was sent says nothing.</summary>
    [Fact]
    public void AFamiliarActionWithNoTrickPendingCountsForNothing()
    {
        _pairing.OnFamiliarAction();
        _pairing.OnTrickDispatched(InstinctAffinity.Rampant);
        Assert.False(_pairing.ReadyToAnswer);
    }

    [Fact]
    public void AnsweringClosesThePair()
    {
        _pairing.OnTrickDispatched(InstinctAffinity.Rampant);
        _pairing.OnFamiliarAction();
        _pairing.OnAnswered();
        Assert.False(_pairing.IsAwaitingAnswer);
    }

    [Fact]
    public void AHoldExpiresAfterTheLimit_AndClearsWhenAsked()
    {
        Assert.False(_pairing.HoldExpired());
        Advance(ArtemisTrickPairing.MaxHoldForTrickSeconds - 0.1);
        Assert.False(_pairing.HoldExpired());
        Advance(0.2);
        Assert.True(_pairing.HoldExpired());

        _pairing.ClearHold();
        Assert.False(_pairing.HoldExpired());
    }
}

/// <summary>What the pairing presses each frame.</summary>
public class ArtemisPairingDecisionTests
{
    [Theory]
    // awaiting, readyToAnswer, axeReady, trickReady, holdExpired, expected
    [InlineData(true, true, true, false, false, DamageModule.PairingStep.AnswerTrick)]
    [InlineData(true, false, true, false, false, DamageModule.PairingStep.WaitForFamiliar)]
    [InlineData(false, false, true, true, false, DamageModule.PairingStep.SendTrick)]
    [InlineData(false, false, true, false, false, DamageModule.PairingStep.Hold)]      // axe waits for the Trick
    [InlineData(false, false, false, true, false, DamageModule.PairingStep.Hold)]      // Trick waits for the axe
    [InlineData(false, false, true, false, true, DamageModule.PairingStep.AxeAlone)]   // don't sit on capped TP
    [InlineData(false, false, false, true, true, DamageModule.PairingStep.TrickAlone)] // nor on capped Pet TP
    [InlineData(false, false, false, false, false, DamageModule.PairingStep.Nothing)]
    public void Decide(bool awaiting, bool ready, bool axe, bool trick, bool expired, DamageModule.PairingStep step)
        => Assert.Equal(step, DamageModule.DecidePairing(awaiting, ready, axe, trick, expired));

    /// <summary>While a Trick is pending, nothing new starts — not even a fresh Trick.</summary>
    [Fact]
    public void APendingTrickBlocksEverythingElse()
        => Assert.Equal(DamageModule.PairingStep.WaitForFamiliar,
            DamageModule.DecidePairing(true, false, axeReady: true, trickReady: true, holdExpired: true));
}

/// <summary>The 50-beast table the pairing reads the Trick colour from.</summary>
public class BstFamiliarsTests
{
    [Fact]
    public void FiftyBeasts_NumberedOneToFifty()
        => Assert.Equal(Enumerable.Range(1, 50), BstFamiliars.All.Select(b => b.BestiaryNo));

    /// <summary>The guide's colour spread, so a mis-typed row shows up.</summary>
    [Fact]
    public void TrickColourSpread()
    {
        Assert.Equal(15, BstFamiliars.All.Count(b => b.TrickAffinity == InstinctAffinity.Rampant));
        Assert.Equal(14, BstFamiliars.All.Count(b => b.TrickAffinity == InstinctAffinity.Durant));
        Assert.Equal(13, BstFamiliars.All.Count(b => b.TrickAffinity == InstinctAffinity.Eldritch));
        Assert.Equal(8, BstFamiliars.All.Count(b => b.TrickAffinity == InstinctAffinity.Volant));
    }

    [Theory]
    [InlineData(1u, "Cu Sith", InstinctAffinity.Rampant)]
    [InlineData(10u, "Wespe", InstinctAffinity.Volant)]
    [InlineData(16u, "Mantis", InstinctAffinity.Durant)]
    [InlineData(36u, "Treant", InstinctAffinity.Eldritch)]
    [InlineData(38u, "Chimera", InstinctAffinity.Rampant)]
    [InlineData(44u, "Damselfly", InstinctAffinity.Volant)]
    public void SpotChecks(uint no, string name, InstinctAffinity affinity)
    {
        var beast = BstFamiliars.ByBestiaryNo(no)!;
        Assert.Equal(name, beast.Name);
        Assert.Equal(affinity, beast.TrickAffinity);
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(51u)]
    public void OutOfRangeRowsAreNull(uint no) => Assert.Null(BstFamiliars.ByBestiaryNo(no));

    /// <summary>The game writes common beasts in lower case ("squirrel", "opo-opo").</summary>
    [Theory]
    [InlineData("squirrel", 2)]
    [InlineData("opo-opo", 5)]
    [InlineData(" Cu Sith ", 1)]
    public void ByNameIgnoresCaseAndSpace(string name, int no)
        => Assert.Equal(no, BstFamiliars.ByName(name)!.BestiaryNo);

    [Fact]
    public void UnknownNameIsNull() => Assert.Null(BstFamiliars.ByName("Carbuncle"));
}

public class ArtemisFamiliarIdentityTests
{
    /// <summary>The name is what the game shows; it wins over a possibly stale horn record.</summary>
    [Fact]
    public void TheNameWinsOverTheHorn()
        => Assert.Equal("Squirrel", ArtemisFamiliarIdentity.Resolve(true, "squirrel", 16)!.Name);

    /// <summary>A name the table does not know (another client language) falls back to the horn.</summary>
    [Fact]
    public void AnUnknownNameFallsBackToTheHorn()
        => Assert.Equal("Mantis", ArtemisFamiliarIdentity.Resolve(true, "Eichhörnchen", 16)!.Name);

    [Fact]
    public void NoFamiliar_NoBeast() => Assert.Null(ArtemisFamiliarIdentity.Resolve(false, "squirrel", 2));

    [Fact]
    public void NothingToGoOn_NoBeast() => Assert.Null(ArtemisFamiliarIdentity.Resolve(true, "???", 0));
}
