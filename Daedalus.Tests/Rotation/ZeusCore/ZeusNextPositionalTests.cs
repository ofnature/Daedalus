using Daedalus.Rotation;
using Daedalus.Services.Positional;
using Xunit;

namespace Daedalus.Tests.Rotation.ZeusCore;

/// <summary>
/// DRG next-positional map — LIVE-SHEET facts (2026-07-20 correction: a user tooltip disproved
/// the "7.0 removed all DRG positionals" claim from the stale RSR checkout): Chaotic Spring and
/// Wheeling Thrust are REAR, Fang and Claw is FLANK; Drakesbane/Heavens'/Spiral Blow have none.
/// Anticipation is combo-position based (the proc statuses died in 7.0) and must list base AND
/// upgrade ids per step (the dual-id combo lesson).
/// </summary>
public class ZeusNextPositionalTests
{
    [Theory]
    [InlineData(87u)]     // Disembowel → Chaotic Spring next
    [InlineData(36955u)]  // Spiral Blow (Lv96 upgrade) → Chaotic Spring next
    [InlineData(88u)]     // Chaos Thrust → Wheeling Thrust next
    [InlineData(25772u)]  // Chaotic Spring (Lv86 upgrade) → Wheeling Thrust next
    public void RearAnticipated_AfterDisembowelAndChaosSteps(uint lastComboAction)
    {
        Assert.Equal(PositionalType.Rear, Zeus.ComputeNextPositional(lastComboAction));
    }

    [Theory]
    [InlineData(84u)]     // Full Thrust → Fang and Claw next
    [InlineData(25771u)]  // Heavens' Thrust (upgrade) → Fang and Claw next
    public void FlankAnticipated_AfterFullThrustStep(uint lastComboAction)
    {
        Assert.Equal(PositionalType.Flank, Zeus.ComputeNextPositional(lastComboAction));
    }

    [Theory]
    [InlineData(0u)]      // no combo
    [InlineData(75u)]     // True Thrust → step 2 next (no positional)
    [InlineData(16479u)]  // Raiden Thrust
    [InlineData(78u)]     // Vorpal Thrust → Heavens' next (no positional)
    [InlineData(3554u)]   // Fang and Claw → Drakesbane next (no positional)
    [InlineData(3556u)]   // Wheeling Thrust → Drakesbane next (no positional)
    public void NoPositional_ForNonPositionalNextSteps(uint lastComboAction)
    {
        Assert.Null(Zeus.ComputeNextPositional(lastComboAction));
    }
}
