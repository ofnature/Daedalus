using Daedalus.Services.Targeting;
using Xunit;

namespace Daedalus.Tests.Services.Targeting;

/// <summary>
/// Where a DoT goes when the strategy's pick can't take it. Saar's Astrologian in Holminster Switch
/// (2026-10-05, Tank Assist): the party burns the tank's target, its time-to-kill drops under the cutoff,
/// and nothing else in the pack was ever dotted.
/// </summary>
public class DotSpreadPolicyTests
{
    private static DotSpreadCandidate Mob(ulong id, float ttk, float dot = 0f, bool fighting = true) => new(id, fighting, dot, ttk);

    [Fact]
    public void DotsTheOneThatLivesLongest()
        => Assert.Equal(3UL, DotSpreadPolicy.Choose([Mob(2, 20f), Mob(3, 40f), Mob(4, 15f)], 3f, 10f));

    /// <summary>An unpulled mob, or a stranger's, is never touched: dotting it pulls it.</summary>
    [Fact]
    public void NeverAMobThatIsNotFightingUs()
        => Assert.Null(DotSpreadPolicy.Choose([Mob(2, 40f, fighting: false)], 3f, 10f));

    [Fact]
    public void NotOneThatIsAboutToDie()
        => Assert.Equal(3UL, DotSpreadPolicy.Choose([Mob(2, 6f), Mob(3, 12f)], 3f, 10f));

    [Fact]
    public void NotOneAlreadyDotted()
        => Assert.Equal(3UL, DotSpreadPolicy.Choose([Mob(2, 40f, dot: 20f), Mob(3, 30f)], 3f, 10f));

    /// <summary>A DoT inside the refresh window counts as needing it.</summary>
    [Fact]
    public void RefreshesOneAboutToFallOff()
        => Assert.Equal(2UL, DotSpreadPolicy.Choose([Mob(2, 40f, dot: 2f)], 3f, 10f));

    /// <summary>No estimate yet reads as "lives long enough", like the strategy's own pick.</summary>
    [Fact]
    public void UnknownTimeToKillIsFine()
        => Assert.Equal(2UL, DotSpreadPolicy.Choose([Mob(2, float.MaxValue)], 3f, 10f));

    [Fact]
    public void WithTheTimeToKillCheckOffDyingMobsCount()
        => Assert.Equal(2UL, DotSpreadPolicy.Choose([Mob(2, 4f)], 3f, ttkCutoff: null));

    [Fact]
    public void NothingSuitableIsNull()
        => Assert.Null(DotSpreadPolicy.Choose([], 3f, 10f));
}
