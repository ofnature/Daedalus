using System;
using Daedalus.Rotation.NikeCore.Helpers;
using Xunit;

namespace Daedalus.Tests.Rotation.NikeCore.Helpers;

/// <summary>
/// Samurai's out-of-melee priority: Gyoten, else let an arriving melee hit land, else Enpi.
/// <para>
/// Field 2026-09-24, Shinryu Paradox at the Hollow King spawn: with the boss invulnerable and an add
/// up, SAM targeted the add but stood at the boss and spammed Enpi. The old gate threw Enpi the moment
/// the target was out of reach, with no gap closer and no idea it might already be walking in.
/// </para>
/// </summary>
public sealed class NikeOutOfMeleePolicyTests
{
    private const float Gcd = 2.5f;

    private static NikeOutOfMeleeAction Decide(
        bool inMelee = false, bool gyoten = false, bool closing = false,
        float secondsToMelee = 10f, float held = 0f, bool enpi = true)
        => NikeOutOfMeleePolicy.Decide(new NikeOutOfMeleeSituation(
            InMelee: inMelee, GyotenUsable: gyoten, Closing: closing,
            SecondsToMelee: secondsToMelee, GcdSeconds: Gcd,
            SecondsAlreadyHeld: held, EnpiUsable: enpi));

    // ── the reported case ──────────────────────────────────────────────────────────────

    /// <summary>
    /// THE regression: out of reach, not moving, nothing else available — that one case is still Enpi.
    /// What changed is that it is now the last resort instead of the first thing tried.
    /// </summary>
    [Fact]
    public void StuckOutOfRange_ThrowsEnpi()
        => Assert.Equal(NikeOutOfMeleeAction.Enpi, Decide());

    [Fact]
    public void InMelee_IsJustTheCombo()
        => Assert.Equal(NikeOutOfMeleeAction.Melee, Decide(inMelee: true, gyoten: true, closing: true));

    // ── (a) Gyoten first ───────────────────────────────────────────────────────────────

    /// <summary>Gyoten is an oGCD, so closing with it costs no GCD at all — it outranks everything.</summary>
    [Fact]
    public void GyotenAvailable_DashesIn()
        => Assert.Equal(NikeOutOfMeleeAction.Gyoten, Decide(gyoten: true));

    [Fact]
    public void GyotenWinsEvenWhileAlreadyClosing()
        => Assert.Equal(NikeOutOfMeleeAction.Gyoten, Decide(gyoten: true, closing: true, secondsToMelee: 1f));

    // ── (b) arriving anyway ────────────────────────────────────────────────────────────

    /// <summary>A melee hit is due within a GCD: don't spend that GCD on Enpi.</summary>
    [Theory]
    [InlineData(0.1f)]
    [InlineData(1.5f)]
    [InlineData(Gcd)]
    public void ClosingAndArrivingWithinAGcd_HoldsForTheMeleeHit(float secondsToMelee)
        => Assert.Equal(NikeOutOfMeleeAction.HoldForApproach, Decide(closing: true, secondsToMelee: secondsToMelee));

    /// <summary>Closing, but too far to arrive inside a GCD: that GCD is Enpi's.</summary>
    [Fact]
    public void ClosingButTooFar_StillThrowsEnpi()
        => Assert.Equal(NikeOutOfMeleeAction.Enpi, Decide(closing: true, secondsToMelee: Gcd + 0.1f));

    /// <summary>
    /// The hold is capped at one GCD per approach, so a bad estimate — a blocked path, a target that
    /// sidesteps, a dodge that looked like an approach — can never cost more than one GCD.
    /// </summary>
    [Theory]
    [InlineData(Gcd - 0.01f, NikeOutOfMeleeAction.HoldForApproach)]
    [InlineData(Gcd, NikeOutOfMeleeAction.Enpi)]
    [InlineData(10f, NikeOutOfMeleeAction.Enpi)]
    public void TheHoldNeverOutlastsOneGcd(float alreadyHeld, NikeOutOfMeleeAction expected)
        => Assert.Equal(expected, Decide(closing: true, secondsToMelee: 0.5f, held: alreadyHeld));

    /// <summary>Not moving toward the target at all: nothing is going to arrive.</summary>
    [Fact]
    public void NotClosing_ThrowsEnpiEvenWhenNear()
        => Assert.Equal(NikeOutOfMeleeAction.Enpi, Decide(closing: false, secondsToMelee: 0.1f));

    // ── (c) Enpi unavailable ───────────────────────────────────────────────────────────

    /// <summary>
    /// No Enpi (below Lv15, unlearned, or recasting) falls back to the combo push, which the scheduler
    /// holds quietly out of range — never an empty GCD decision.
    /// </summary>
    [Fact]
    public void NoEnpi_FallsBackToTheCombo()
        => Assert.Equal(NikeOutOfMeleeAction.Melee, Decide(enpi: false));
}

/// <summary>
/// Measures whether the character is actually closing on its target — from the distance itself,
/// because a movement flag says nothing about direction and a dodge away also counts as moving.
/// </summary>
public sealed class NikeApproachTrackerTests
{
    private DateTime _now = new(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc);
    private readonly NikeApproachTracker _tracker;

    public NikeApproachTrackerTests() => _tracker = new NikeApproachTracker(() => _now);

    private void Advance(double s) => _now = _now.AddSeconds(s);

    [Fact]
    public void WalkingIn_ReadsAsClosingAtThatSpeed()
    {
        _tracker.Sample(1, distance: 15f);
        Advance(0.5);
        _tracker.Sample(1, distance: 12.5f);   // 2.5y in 0.5s

        Assert.Equal(5f, _tracker.ClosingSpeed, precision: 3);
        Assert.False(_tracker.DistanceIncreasing);
    }

    /// <summary>A dodge away moves the character too — it must read as the gap GROWING, not closing.</summary>
    [Fact]
    public void BeingMovedAway_ReadsAsDistanceIncreasing()
    {
        _tracker.Sample(1, distance: 10f);
        Advance(0.5);
        _tracker.Sample(1, distance: 12.5f);

        Assert.True(_tracker.ClosingSpeed < 0f);
        Assert.True(_tracker.DistanceIncreasing);
    }

    /// <summary>Frame-to-frame jitter must not register as movement in either direction.</summary>
    [Fact]
    public void SamplesInsideTheWindowAreIgnored()
    {
        _tracker.Sample(1, distance: 10f);
        Advance(NikeApproachTracker.SampleSeconds / 3);
        _tracker.Sample(1, distance: 9f);

        Assert.Equal(0f, _tracker.ClosingSpeed);
    }

    /// <summary>A new target is a new approach; the old one's speed and hold do not carry over.</summary>
    [Fact]
    public void SwitchingTarget_ResetsEverything()
    {
        _tracker.Sample(1, distance: 15f);
        Advance(0.5);
        _tracker.Sample(1, distance: 12.5f);
        _tracker.NoteHolding(true);
        Advance(1);

        _tracker.Sample(2, distance: 12.5f);

        Assert.Equal(0f, _tracker.ClosingSpeed);
        Assert.Equal(0f, _tracker.SecondsHeld);
    }

    [Fact]
    public void HoldTimeAccumulatesAcrossFramesAndClearsWhenReleased()
    {
        _tracker.NoteHolding(true);
        Advance(0.8);
        _tracker.NoteHolding(true);            // later frames must not restart the clock
        Assert.Equal(0.8f, _tracker.SecondsHeld, precision: 3);

        _tracker.NoteHolding(false);
        Assert.Equal(0f, _tracker.SecondsHeld);
    }
}
