using System;
using System.IO;
using Daedalus.Data;
using Daedalus.Services.Combat;
using Xunit;

namespace Daedalus.Tests.Services.Combat;

/// <summary>
/// Not attacking out of the post-raise invulnerability.
/// <para>
/// Reported 2026-09-19: raised inside an AoE, the character died again on the same frame. Transcendent
/// does not lock actions — it grants damage immunity for 10s <b>until the character acts</b> — so the
/// rotation resuming immediately spent that immunity on an attack and handed the AoE the kill. Holding
/// actions keeps it, and movement does not break it, so the dodge walks the character clear while it is
/// still immune. RSR refuses all actions while the buff is up, which is the behaviour mirrored here.
/// </para>
/// </summary>
public sealed class ReviveGraceTests
{
    private const double Cap = 10;
    private DateTime _now = new(2026, 9, 19, 12, 0, 0, DateTimeKind.Utc);
    private readonly ReviveGraceTracker _grace;

    public ReviveGraceTests() => _grace = new ReviveGraceTracker(() => _now);

    private void Advance(double seconds) => _now = _now.AddSeconds(seconds);

    // ── the reported case ──────────────────────────────────────────────────────────────

    /// <summary>
    /// THE regression: the first frame back. This is where the attack used to go out, ending the
    /// immunity while the character was still standing in the AoE.
    /// </summary>
    [Fact]
    public void HoldsOnTheVeryFirstFrameBack()
    {
        _grace.NoteFrame(postReviveInvulnerable: true, clearOfDanger: false, Cap);
        Assert.True(_grace.IsActive);
    }

    /// <summary>While still in danger, held right across the immunity — the whole window is available.</summary>
    [Theory]
    [InlineData(0.1)]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(9.5)]
    public void HeldForAsLongAsTheDangerLasts(double elapsed)
    {
        _grace.NoteFrame(postReviveInvulnerable: true, clearOfDanger: false, Cap);
        Advance(elapsed);
        _grace.NoteFrame(postReviveInvulnerable: true, clearOfDanger: false, Cap);

        Assert.True(_grace.IsActive);
    }

    /// <summary>
    /// The buff going away releases the hold immediately — no trailing delay. An earlier version of
    /// this waited for the buff to clear and only THEN started its timer, on the mistaken belief that
    /// the buff was an action lock; because it never acted the buff always ran the full 10s, and the
    /// character then stood still for a further delay on top of that.
    /// </summary>
    [Fact]
    public void TheBuffEndingReleasesTheHoldWithNoTrailingDelay()
    {
        _grace.NoteFrame(postReviveInvulnerable: true, clearOfDanger: false, Cap);
        Advance(3);
        _grace.NoteFrame(postReviveInvulnerable: false, clearOfDanger: true, Cap);

        Assert.False(_grace.IsActive);
        Assert.Equal(0d, _grace.RemainingSeconds);
    }

    /// <summary>
    /// Which is also how a manual action hands control straight back: the player presses something, the
    /// immunity is consumed, the buff drops, and the rotation is live on the next frame.
    /// </summary>
    [Fact]
    public void AManualActionHandsControlStraightBack()
    {
        _grace.NoteFrame(postReviveInvulnerable: true, clearOfDanger: false, Cap);
        Advance(0.5);
        _grace.NoteFrame(postReviveInvulnerable: false, clearOfDanger: true, Cap);   // buff consumed by the player's action

        Assert.False(_grace.IsActive);
    }


    // ── handing the fight back ─────────────────────────────────────────────────────────

    /// <summary>
    /// The point of the whole thing: the dodge goes first, then the rotation takes over. Holding for
    /// the full immunity was safe and cost up to ten seconds of uptime per death for nothing once the
    /// character was already clear.
    /// </summary>
    [Fact]
    public void ReleasesAsSoonAsTheCharacterIsClear()
    {
        _grace.NoteFrame(postReviveInvulnerable: true, clearOfDanger: false, Cap);
        Advance(ReviveGraceTracker.MinHoldSeconds);
        _grace.NoteFrame(postReviveInvulnerable: true, clearOfDanger: true, Cap);

        Assert.False(_grace.IsActive);
        Assert.Equal(0d, _grace.RemainingSeconds);
    }

    /// <summary>
    /// Not on the FIRST frame though. The engine has not seen the mechanic yet, so "nothing is steering
    /// and the ground looks fine" is indistinguishable from being clear — acting on it is precisely the
    /// bug this exists to stop.
    /// </summary>
    [Fact]
    public void DoesNotReleaseOnTheFirstFrameEvenIfNothingLooksWrong()
    {
        _grace.NoteFrame(postReviveInvulnerable: true, clearOfDanger: true, Cap);
        Assert.True(_grace.IsActive);
    }

    [Theory]
    [InlineData(0d, true)]
    [InlineData(ReviveGraceTracker.MinHoldSeconds - 0.05, true)]
    [InlineData(ReviveGraceTracker.MinHoldSeconds, false)]
    [InlineData(2d, false)]
    public void TheFloorHasToPassBeforeClearCounts(double elapsed, bool stillHeld)
    {
        _grace.NoteFrame(postReviveInvulnerable: true, clearOfDanger: true, Cap);
        Advance(elapsed);
        _grace.NoteFrame(postReviveInvulnerable: true, clearOfDanger: true, Cap);

        Assert.Equal(stillHeld, _grace.IsActive);
    }

    /// <summary>
    /// Clear then not clear again — a second telegraph lands on the spot — must hold again rather than
    /// stay released, for as long as the immunity is there to protect it.
    /// </summary>
    [Fact]
    public void HoldsAgainIfDangerReturnsWhileStillImmune()
    {
        _grace.NoteFrame(postReviveInvulnerable: true, clearOfDanger: false, Cap);
        Advance(1);
        _grace.NoteFrame(postReviveInvulnerable: true, clearOfDanger: true, Cap);
        Assert.False(_grace.IsActive);

        _grace.NoteFrame(postReviveInvulnerable: true, clearOfDanger: false, Cap);
        Assert.True(_grace.IsActive);
    }

    /// <summary>
    /// A missing movement plugin costs a moment, not the whole window: with nothing to ask, the caller
    /// reports clear and the hold falls back to its floor.
    /// </summary>
    [Fact]
    public void WithNoEngineToAskTheHoldIsJustTheFloor()
    {
        _grace.NoteFrame(postReviveInvulnerable: true, clearOfDanger: true, Cap);
        Advance(ReviveGraceTracker.MinHoldSeconds + 0.01);
        _grace.NoteFrame(postReviveInvulnerable: true, clearOfDanger: true, Cap);

        Assert.False(_grace.IsActive);
    }


    // ── what counts as clear ───────────────────────────────────────────────────────────

    /// <summary>
    /// Mid-dodge is never clear, whatever the ground says — the character is still being moved, and the
    /// immunity is what is carrying it through. Standing still inside a telegraph is not clear either.
    /// </summary>
    [Theory]
    [InlineData(false, false, true, true)]    // still, nothing steering, ground safe -> clear
    [InlineData(true, false, true, false)]    // moving
    [InlineData(false, true, true, false)]    // engine steering
    [InlineData(false, false, false, false)]  // standing in a telegraph
    [InlineData(true, true, false, false)]
    public void ClearMeansStillAndUnsteeredAndOnSafeGround(
        bool isMoving, bool externalSteering, bool spotSafe, bool expected)
        => Assert.Equal(expected, ReviveGraceTracker.IsClearOfDanger(isMoving, externalSteering, spotSafe));

    // ── the cap ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The cap is measured from the first frame the buff was seen, so it bounds the hold even if the
    /// buff somehow outlives it. Nothing should be able to freeze a character indefinitely.
    /// </summary>
    [Fact]
    public void TheCapBoundsTheHoldEvenIfTheBuffPersists()
    {
        _grace.NoteFrame(postReviveInvulnerable: true, clearOfDanger: false, maxHoldSeconds: 3);
        Advance(2.9);
        _grace.NoteFrame(postReviveInvulnerable: true, clearOfDanger: false, maxHoldSeconds: 3);
        Assert.True(_grace.IsActive);

        Advance(0.2);
        _grace.NoteFrame(postReviveInvulnerable: true, clearOfDanger: false, maxHoldSeconds: 3);
        Assert.False(_grace.IsActive);
    }

    /// <summary>
    /// Frames keep arriving while the buff is up; the deadline must stay anchored to the first one
    /// rather than sliding forward with each, or the cap would never be reached.
    /// </summary>
    [Fact]
    public void TheDeadlineIsAnchoredToTheFirstFrameNotTheLatest()
    {
        for (var i = 0; i < 40; i++)
        {
            _grace.NoteFrame(postReviveInvulnerable: true, clearOfDanger: false, maxHoldSeconds: 2);
            Advance(0.1);
        }

        Assert.False(_grace.IsActive);   // 4s of frames against a 2s cap
    }

    [Fact]
    public void RemainingSecondsCountsTheHoldDown()
    {
        _grace.NoteFrame(postReviveInvulnerable: true, clearOfDanger: false, Cap);
        Assert.Equal(Cap, _grace.RemainingSeconds, precision: 3);

        Advance(4);
        _grace.NoteFrame(postReviveInvulnerable: true, clearOfDanger: false, Cap);
        Assert.Equal(Cap - 4, _grace.RemainingSeconds, precision: 3);
    }

    // ── nothing to do when nobody was raised ───────────────────────────────────────────

    /// <summary>The expensive failure would be holding the rotation in ordinary combat.</summary>
    [Fact]
    public void NeverHoldsWithoutTheBuff()
    {
        for (var i = 0; i < 100; i++)
        {
            _grace.NoteFrame(postReviveInvulnerable: false, clearOfDanger: true, Cap);
            Advance(0.1);
            Assert.False(_grace.IsActive);
        }
    }

    /// <summary>A second raise later in the fight gets its own full window, not a spent one.</summary>
    [Fact]
    public void ASecondRaiseGetsItsOwnWindow()
    {
        _grace.NoteFrame(postReviveInvulnerable: true, clearOfDanger: false, maxHoldSeconds: 2);
        Advance(5);
        _grace.NoteFrame(postReviveInvulnerable: false, clearOfDanger: false, maxHoldSeconds: 2);
        Assert.False(_grace.IsActive);

        _grace.NoteFrame(postReviveInvulnerable: true, clearOfDanger: false, maxHoldSeconds: 2);
        Assert.True(_grace.IsActive);
    }

    // ── the off switch ─────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0d)]
    [InlineData(-1d)]
    public void ZeroOrLessDisablesTheHold(double maxHoldSeconds)
    {
        _grace.NoteFrame(postReviveInvulnerable: true, clearOfDanger: false, maxHoldSeconds);
        Assert.False(_grace.IsActive);
    }

    /// <summary>Turning it off mid-hold must release the character, not strand it.</summary>
    [Fact]
    public void TurningItOffReleasesAHoldInProgress()
    {
        _grace.NoteFrame(postReviveInvulnerable: true, clearOfDanger: false, Cap);
        Assert.True(_grace.IsActive);

        _grace.NoteFrame(postReviveInvulnerable: true, clearOfDanger: false, maxHoldSeconds: 0d);
        Assert.False(_grace.IsActive);
    }

    // ── wiring, guarded at the source ──────────────────────────────────────────────────

    /// <summary>
    /// BaseRotation is too game-coupled to instantiate in a test, and the tracker being right is worth
    /// nothing if the rotation does not consult it and stand down.
    /// </summary>
    [Fact]
    public void TheRotationConsultsItAndStandsDown()
    {
        var source = File.ReadAllText(BaseRotationSourcePath());

        Assert.Matches(
            @"ReviveGrace\.NoteFrame\(\s*HasPostReviveInvulnerability\(player\),\s*IsClearOfDangerAfterRaise\(player, isMoving\),\s*Configuration\.ReviveHoldSeconds\s*\)",
            source);
        Assert.Matches(@"if \(ReviveGrace\.IsActive\)\s*\r?\n\s*return;", source);
    }

    private static string BaseRotationSourcePath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Daedalus.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "Daedalus", "Rotation", "Base", "BaseRotation.cs");
    }

    // ── the statuses it keys on ────────────────────────────────────────────────────────

    /// <summary>
    /// Transcendent is a normal raise; Willful is duty support's auto-revive. Both also appear in the
    /// incapacitation set, which classifies GCD downtime — a separate question from whether an action is
    /// possible, which for Transcendent it very much is.
    /// </summary>
    [Fact]
    public void TheRaiseMarkersAreTranscendentAndWillful()
    {
        Assert.Equal(2, FFXIVConstants.PostReviveInvulnerabilityStatusIds.Count);
        Assert.Contains(2656u, FFXIVConstants.PostReviveInvulnerabilityStatusIds);   // Transcendent
        Assert.Contains(3581u, FFXIVConstants.PostReviveInvulnerabilityStatusIds);   // Willful

        foreach (var id in FFXIVConstants.PostReviveInvulnerabilityStatusIds)
            Assert.Contains(id, FFXIVConstants.IncapacitationStatusIds);

        // A stun is not a raise: it stops you acting, but there is no immunity to protect.
        Assert.DoesNotContain(18u, FFXIVConstants.PostReviveInvulnerabilityStatusIds);
    }
}

/// <summary>
/// The production entry point is a static, so this only touches the one path that cannot leave a hold
/// armed for the test classes running alongside it. The behaviour is covered on an owned
/// <see cref="ReviveGraceTracker"/> in <see cref="ReviveGraceTests"/>.
/// </summary>
public sealed class ReviveGraceStaticTests
{
    [Fact]
    public void TheStaticEntryPointDelegatesAndHoldsNothingWhenDisabled()
    {
        ReviveGrace.NoteFrame(postReviveInvulnerable: true, clearOfDanger: false, maxHoldSeconds: 0d);

        Assert.False(ReviveGrace.IsActive);
        Assert.Equal(0d, ReviveGrace.RemainingSeconds);
    }
}
