using System;
using Daedalus.Services.Targeting;
using Xunit;

namespace Daedalus.Tests.Services.Targeting;

/// <summary>
/// Manual-click grace (field 2026-07-30): left-clicking a target made the positional anchor and
/// the BMR GoToPositional transient re-pulse against the pick, stutter-stepping under the
/// player's hands. A target change no Daedalus writer registered is manual — movement holds.
/// Static state: tests run serialized in one collection and reset around each.
/// </summary>
[Collection("ManualControlGraceState")]
public class ManualControlGraceTests : IDisposable
{
    private DateTime _now = new(2026, 7, 30, 12, 0, 0, DateTimeKind.Utc);

    public ManualControlGraceTests()
    {
        ManualControlGrace.Reset();
        ManualControlGrace.UtcNow = () => _now;
    }

    public void Dispose() => ManualControlGrace.Reset();

    private void Advance(double seconds) => _now = _now.AddSeconds(seconds);

    [Fact]
    public void UnregisteredTargetChange_ArmsGrace()
    {
        ManualControlGrace.NoteFrame(0);
        ManualControlGrace.NoteFrame(1234); // user clicked something

        Assert.True(ManualControlGrace.IsActive);
    }

    [Fact]
    public void OwnWrite_DoesNotArmGrace()
    {
        ManualControlGrace.NoteFrame(0);
        ManualControlGrace.RecordOwnWrite(1234);
        ManualControlGrace.NoteFrame(1234); // our own retarget landing

        Assert.False(ManualControlGrace.IsActive);
    }

    [Fact]
    public void ClearingTarget_DoesNotArmGrace()
    {
        ManualControlGrace.RecordOwnWrite(1234);
        ManualControlGrace.NoteFrame(1234);
        ManualControlGrace.NoteFrame(0); // escape / target died — never freeze movement for this

        Assert.False(ManualControlGrace.IsActive);
    }

    [Fact]
    public void Grace_ExpiresAfterWindow()
    {
        ManualControlGrace.NoteFrame(1234);
        Assert.True(ManualControlGrace.IsActive);

        Advance(ManualControlGrace.GraceSeconds + 0.1);
        Assert.False(ManualControlGrace.IsActive);
    }

    [Fact]
    public void ExternallyRegisteredWrite_DoesNotArmGrace()
    {
        // What Daedalus.Targeting.RecordExternalWrite buys: call gates are per-process, so a
        // companion plugin (Theseus) driving our character cannot reach RecordOwnWrite directly.
        // Unregistered, its retarget reads as a user click and silently suppresses our movement
        // pulses for the full grace — the failure is a stutter, not an error.
        ManualControlGrace.NoteFrame(0);
        ManualControlGrace.RecordOwnWrite(4321); // arrives over IPC from the driving plugin
        ManualControlGrace.NoteFrame(4321);

        Assert.False(ManualControlGrace.IsActive);
    }

    [Fact]
    public void ExternalWriteClaimedTooEarly_StillReadsAsManual()
    {
        // Attribution is deliberately short-lived, so a caller that claims a write and then takes
        // its time cannot launder a genuine click that lands afterwards.
        ManualControlGrace.RecordOwnWrite(4321);
        Advance(1.5);
        ManualControlGrace.NoteFrame(4321);

        Assert.True(ManualControlGrace.IsActive);
    }

    [Fact]
    public void StaleOwnWrite_NoLongerAttributes()
    {
        // Our write from long ago must not launder a much-later manual click to the same mob.
        ManualControlGrace.RecordOwnWrite(1234);
        ManualControlGrace.NoteFrame(1234);
        ManualControlGrace.NoteFrame(0);
        Advance(5.0);
        ManualControlGrace.NoteFrame(1234); // user re-clicked the same mob

        Assert.True(ManualControlGrace.IsActive);
    }
}
