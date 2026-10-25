using Daedalus.Rotation.Common.Helpers;
using Xunit;

namespace Daedalus.Tests.Rotation.Common;

/// <summary>Max-melee range-keeping: the general switches, and the Crucible's exception for Beastmaster.</summary>
public class MeleeRangeKeepingTests
{
    /// <summary>
    /// Field 2026-10-25: Saar's Beastmaster (limited job, Maintain max melee off, solo) never walked into reach on the
    /// board. On a board the Crucible setting decides.
    /// </summary>
    [Fact]
    public void OnACrucibleBoardTheBeastmasterWalksIn()
        => Assert.True(MeleeRangeKeeping.Allowed(
            autoMovement: true, maintainMaxMelee: false, limitedJob: true, soloPositionLock: true, solo: true,
            onCrucibleBoard: true, crucibleKeepInMelee: true));

    [Fact]
    public void TheCrucibleSettingCanBeTurnedOff()
        => Assert.False(MeleeRangeKeeping.Allowed(true, false, true, false, true, onCrucibleBoard: true, crucibleKeepInMelee: false));

    /// <summary>The master movement toggle still wins everywhere.</summary>
    [Fact]
    public void NoMovementWithAutoMovementOff()
        => Assert.False(MeleeRangeKeeping.Allowed(false, true, true, false, true, onCrucibleBoard: true, crucibleKeepInMelee: true));

    /// <summary>Off the board a limited job keeps the old rule: no inherited range-keeping.</summary>
    [Fact]
    public void OffTheBoardALimitedJobDoesNotKeepRange()
        => Assert.False(MeleeRangeKeeping.Allowed(true, true, limitedJob: true, false, false, onCrucibleBoard: false, crucibleKeepInMelee: true));

    [Fact]
    public void ARegularJobFollowsMaintainMaxMelee()
    {
        Assert.True(MeleeRangeKeeping.Allowed(true, maintainMaxMelee: true, false, false, false, false, true));
        Assert.False(MeleeRangeKeeping.Allowed(true, maintainMaxMelee: false, false, false, false, false, true));
    }

    [Fact]
    public void SoloPositionLockStillAppliesOffTheBoard()
        => Assert.False(MeleeRangeKeeping.Allowed(true, true, false, soloPositionLock: true, solo: true, false, true));
}
