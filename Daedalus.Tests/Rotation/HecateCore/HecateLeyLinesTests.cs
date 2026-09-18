using System.IO;
using Daedalus.Data;
using Daedalus.Models.Action;
using Daedalus.Rotation.HecateCore.Helpers;
using Xunit;

namespace Daedalus.Tests.Rotation.HecateCore;

/// <summary>
/// Black Mage's Ley Lines circle: the catalog facts, and Retrace bringing the circle back.
/// <para>
/// Checked against the game data 2026-09-17. Ley Lines is <c>TargetArea</c> — a circle placed on the
/// ground, not a self-buff — lasts 20s, and has two charges. It was catalogued as a 30s self-targeted
/// buff and dispatched as one, unlike every other ground action in the codebase.
/// </para>
/// </summary>
public sealed class HecateLeyLinesTests
{
    // ── catalog, against the game's own numbers ────────────────────────────────────────

    /// <summary>
    /// Ground-placed, or the dispatcher sends it as a self-target and the circle never lands where it
    /// should. 20s, not the 30 recorded before — a third longer than the buff really lasts.
    /// </summary>
    [Fact]
    public void LeyLinesIsAGroundPlacedTwentySecondCircle()
    {
        Assert.Equal(ActionTargetType.GroundAoE, BLMActions.LeyLines.TargetType);
        Assert.Equal(20f, BLMActions.LeyLines.AppliedStatusDuration);
        Assert.Equal(3f, BLMActions.LeyLines.Radius);
        Assert.Equal(120f, BLMActions.LeyLines.RecastTime);   // per charge; the action has two
    }

    /// <summary>Retrace is 40s, not the 3s that was recorded — 3s is Between the Lines' recast.</summary>
    [Fact]
    public void RetraceIsAGroundPlacedFortySecondRecast()
    {
        Assert.Equal(ActionTargetType.GroundAoE, BLMActions.Retrace.TargetType);
        Assert.Equal(40f, BLMActions.Retrace.RecastTime);
        Assert.Equal(96, BLMActions.Retrace.MinLevel);
    }

    /// <summary>
    /// Two different statuses, conflated in the catalog before: 737 means a circle exists, 738 means the
    /// player is standing in it. Only 738 is the haste, and only the pair can tell "moved out" from "fine".
    /// </summary>
    [Fact]
    public void DrawingTheCircleAndStandingInItAreDifferentStatuses()
    {
        Assert.Equal(737u, BLMActions.StatusIds.LeyLines);
        Assert.Equal(738u, BLMActions.StatusIds.CircleOfPower);
        Assert.NotEqual(BLMActions.StatusIds.LeyLines, BLMActions.StatusIds.CircleOfPower);
    }

    // ── Retrace ────────────────────────────────────────────────────────────────────────

    private static bool Retrace(
        bool enabled = true, byte level = 100, bool ready = true, bool hasLeyLines = true,
        bool inCircle = false, float remaining = 15f, bool moving = false, bool movementImminent = false)
        => HecateLeyLineRules.ShouldRetrace(enabled, level, ready, hasLeyLines, inCircle, remaining, moving, movementImminent);

    /// <summary>The case it exists for: the circle is up and a mechanic has moved the player out of it.</summary>
    [Fact]
    public void RetracesWhenTheCircleExistsAndThePlayerIsOutsideIt()
        => Assert.True(Retrace());

    /// <summary>Standing in it already needs nothing; spending a 40s recast on it is pure waste.</summary>
    [Fact]
    public void DoesNotRetraceWhileStandingInTheCircle()
        => Assert.False(Retrace(inCircle: true));

    /// <summary>No circle at all is Ley Lines' job — Retrace cannot even be executed without one.</summary>
    [Fact]
    public void DoesNotRetraceWithNoCircleDrawn()
        => Assert.False(Retrace(hasLeyLines: false));

    /// <summary>
    /// A circle about to expire is not worth the recast: a couple of seconds of haste now, and no Retrace
    /// available for the next circle.
    /// </summary>
    [Theory]
    [InlineData(0f, false)]
    [InlineData(2f, false)]
    [InlineData(HecateLeyLineRules.RetraceMinRemainingSeconds - 0.1f, false)]
    [InlineData(HecateLeyLineRules.RetraceMinRemainingSeconds, true)]
    [InlineData(18f, true)]
    public void RetraceNeedsEnoughOfTheCircleLeftToBeWorthIt(float remaining, bool expected)
        => Assert.Equal(expected, Retrace(remaining: remaining));

    /// <summary>Re-placing the circle under a character who is moving, or about to be moved, just wastes it.</summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void DoesNotRetraceIntoMovement(bool moving, bool movementImminent)
        => Assert.False(Retrace(moving: moving, movementImminent: movementImminent));

    [Fact]
    public void RespectsTheLevelGateAndTheToggleAndTheCooldown()
    {
        Assert.False(Retrace(level: 95));      // Retrace is Lv96
        Assert.False(Retrace(enabled: false));
        Assert.False(Retrace(ready: false));
    }

    // ── wiring, against the source ─────────────────────────────────────────────────────

    /// <summary>
    /// Both must go out through the ground path. Ley Lines was pushed as a self-targeted oGCD, which is
    /// how a ground-placed action gets dispatched without a position — the bug this fixes.
    /// </summary>
    [Fact]
    public void BothCirclesArePushedThroughTheGroundPath()
    {
        var source = File.ReadAllText(BuffModuleSourcePath());

        Assert.Matches(@"PushGroundTargetedOgcd\(HecateAbilities\.LeyLines,\s*player\.Position", source);
        Assert.Matches(@"PushGroundTargetedOgcd\(HecateAbilities\.Retrace,\s*context\.Player\.Position", source);

        // And neither is still going out as a self/target-addressed oGCD.
        Assert.DoesNotMatch(@"PushOgcd\(HecateAbilities\.(LeyLines|Retrace)\b", source);
    }

    private static string BuffModuleSourcePath()
    {
        var dir = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Daedalus.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "Daedalus", "Rotation", "HecateCore", "Modules", "BuffModule.cs");
    }
}
