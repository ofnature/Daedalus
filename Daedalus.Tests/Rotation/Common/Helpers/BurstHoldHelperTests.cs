using System.IO;
using System.Linq;
using Moq;
using Daedalus.Rotation.Common.Helpers;
using Daedalus.Services;
using Daedalus.Services.Input;
using Xunit;

namespace Daedalus.Tests.Rotation.Common.Helpers;

/// <summary>
/// Verifies the modifier-override layer added on top of BurstHoldHelper.
/// <para>
/// These tests pass the modifier service in through the <c>*With</c> overloads and never assign the
/// static <see cref="BurstHoldHelper.ModifierKeys"/>. They used to set it (resetting it afterwards),
/// but the static is process-wide and xUnit runs test classes in parallel: while one of these had it
/// on "conservative", any rotation test reading the helper held its burst. The Red Mage
/// SoloBurst_Embolden_FollowsActiveManafication test failed about one full run in six (2026-09-25).
/// </para>
/// </summary>
public class BurstHoldHelperTests
{
    private static Mock<IBurstWindowService> BurstService(bool inBurst = false, bool imminent = false)
    {
        var m = new Mock<IBurstWindowService>();
        m.Setup(s => s.IsInBurstWindow).Returns(inBurst);
        m.Setup(s => s.IsBurstImminent(It.IsAny<float>())).Returns(imminent);
        return m;
    }

    private static IModifierKeyService Modifier(bool burst = false, bool conservative = false)
    {
        var m = new Mock<IModifierKeyService>();
        m.Setup(x => x.IsBurstOverride).Returns(burst);
        m.Setup(x => x.IsConservativeOverride).Returns(conservative);
        return m.Object;
    }

    // ---------------- IsInBurst ----------------

    [Fact]
    public void IsInBurst_NoModifier_DelegatesToService()
        => Assert.True(BurstHoldHelper.IsInBurstWith(null, BurstService(inBurst: true).Object));

    [Fact]
    public void IsInBurst_BurstOverride_ReturnsTrueEvenWhenServiceReportsFalse()
        => Assert.True(BurstHoldHelper.IsInBurstWith(Modifier(burst: true), BurstService(inBurst: false).Object));

    [Fact]
    public void IsInBurst_ConservativeOverride_ReturnsFalseEvenWhenServiceReportsTrue()
        => Assert.False(BurstHoldHelper.IsInBurstWith(Modifier(conservative: true), BurstService(inBurst: true).Object));

    // ---------------- ShouldHoldForBurst ----------------

    [Fact]
    public void ShouldHoldForBurst_BurstImminentNotInBurst_ReturnsTrue()
        => Assert.True(BurstHoldHelper.ShouldHoldForBurstWith(null, BurstService(inBurst: false, imminent: true).Object));

    [Fact]
    public void ShouldHoldForBurst_BurstOverride_ForcesFalse()
        => Assert.False(BurstHoldHelper.ShouldHoldForBurstWith(
            Modifier(burst: true), BurstService(inBurst: false, imminent: true).Object));

    [Fact]
    public void ShouldHoldForBurst_ConservativeOverride_ForcesTrue()
        => Assert.True(BurstHoldHelper.ShouldHoldForBurstWith(
            Modifier(conservative: true), BurstService(inBurst: false, imminent: false).Object));

    [Fact]
    public void ShouldHoldForBurst_AlreadyInBurst_ReturnsFalse()
        => Assert.False(BurstHoldHelper.ShouldHoldForBurstWith(null, BurstService(inBurst: true, imminent: true).Object));

    [Fact]
    public void ShouldHoldForBurst_NullService_ReturnsFalse()
        => Assert.False(BurstHoldHelper.ShouldHoldForBurstWith(null, null));

    [Fact]
    public void ShouldHoldForBurst_NullServiceWithBurstOverride_ReturnsFalse()
        => Assert.False(BurstHoldHelper.ShouldHoldForBurstWith(Modifier(burst: true), null));

    [Fact]
    public void ShouldHoldForBurst_NullServiceWithConservativeOverride_ReturnsTrue()
        => Assert.True(BurstHoldHelper.ShouldHoldForBurstWith(Modifier(conservative: true), null));

    // ---------------- ShouldHoldForPhaseTransition ----------------

    [Fact]
    public void ShouldHoldForPhaseTransition_BurstOverride_ForcesFalse()
        => Assert.False(BurstHoldHelper.ShouldHoldForPhaseTransitionWith(Modifier(burst: true), null));

    [Fact]
    public void ShouldHoldForPhaseTransition_ConservativeOverride_ForcesTrue()
        => Assert.True(BurstHoldHelper.ShouldHoldForPhaseTransitionWith(Modifier(conservative: true), null));

    [Fact]
    public void ShouldHoldForPhaseTransition_NoModifierAndNoTimeline_ReturnsFalse()
        => Assert.False(BurstHoldHelper.ShouldHoldForPhaseTransitionWith(null, null));

    // ---------------- guard ----------------

    /// <summary>
    /// No test may assign the static again: hundreds of rotation tests read it through the helper, in
    /// parallel with whatever test set it.
    /// </summary>
    [Fact]
    public void NoTestAssignsTheStaticModifierKeys()
    {
        var root = FindTestsRoot();
        var offenders = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(f => File.ReadAllText(f).Contains(Assignment))
            .Select(Path.GetFileName)
            .ToList();

        Assert.Empty(offenders);
    }

    // Built at runtime so this file doesn't match its own search.
    private static readonly string Assignment = "BurstHoldHelper." + "ModifierKeys =";

    private static string FindTestsRoot()
    {
        var dir = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Daedalus.Tests.csproj")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
