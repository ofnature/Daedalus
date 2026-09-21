using System.Threading;
using Daedalus.Services.Diagnostics;
using Xunit;

namespace Daedalus.Tests.Services.Diagnostics;

/// <summary>
/// The one place every revive path reports to.
/// <para>
/// Built 2026-09-20 after "no reviving is getting done from healers, phoenix downs, nor variants".
/// Each path already had a precise verdict and dropped it somewhere different — a job tab, a line
/// nobody read, or nowhere — so the question took four wrong guesses to answer from source.
/// </para>
/// </summary>
public sealed class ReviveDiagnosticsTests
{
    public ReviveDiagnosticsTests() => ReviveDiagnostics.Reset();

    [Fact]
    public void APathThatHasNeverReportedIsDistinguishableFromOneSayingNothingIsWrong()
    {
        Assert.Null(ReviveDiagnostics.For(ReviveSource.PhoenixDown));

        ReviveDiagnostics.Report(ReviveSource.PhoenixDown, "a healer lives");

        Assert.Equal("a healer lives", ReviveDiagnostics.For(ReviveSource.PhoenixDown)?.State);
        Assert.Null(ReviveDiagnostics.For(ReviveSource.VariantRaise));
    }

    /// <summary>All four report independently — the point is seeing them side by side.</summary>
    [Fact]
    public void EveryPathIsTrackedSeparately()
    {
        ReviveDiagnostics.Report(ReviveSource.HealerRaise, "Dead member found");
        ReviveDiagnostics.Report(ReviveSource.PhoenixDown, "disabled in settings");
        ReviveDiagnostics.Report(ReviveSource.VariantRaise, "waiting on the healer (4s)");
        ReviveDiagnostics.Report(ReviveSource.PhantomRaise, "job has no raise");

        Assert.Equal(4, ReviveDiagnostics.Snapshot().Count);
        Assert.Equal("Dead member found", ReviveDiagnostics.For(ReviveSource.HealerRaise)?.State);
        Assert.Equal("job has no raise", ReviveDiagnostics.For(ReviveSource.PhantomRaise)?.State);
    }

    /// <summary>
    /// The age is "how long has it been saying this", not "how long since the last frame". These are
    /// reported every frame, so refreshing the timestamp on an unchanged verdict would peg every line
    /// at 0s and hide the very thing worth seeing — that a path has been stuck on one answer.
    /// </summary>
    [Fact]
    public void RepeatingTheSameVerdictKeepsTheOriginalTimestamp()
    {
        ReviveDiagnostics.Report(ReviveSource.VariantRaise, "not selected for this run");
        var first = ReviveDiagnostics.For(ReviveSource.VariantRaise)!.Value.AtUtc;

        Thread.Sleep(10);
        ReviveDiagnostics.Report(ReviveSource.VariantRaise, "not selected for this run");

        Assert.Equal(first, ReviveDiagnostics.For(ReviveSource.VariantRaise)!.Value.AtUtc);
    }

    [Fact]
    public void AChangedVerdictStartsANewClock()
    {
        ReviveDiagnostics.Report(ReviveSource.HealerRaise, "None needed");
        var first = ReviveDiagnostics.For(ReviveSource.HealerRaise)!.Value.AtUtc;

        Thread.Sleep(10);
        ReviveDiagnostics.Report(ReviveSource.HealerRaise, "Dead member found");

        Assert.True(ReviveDiagnostics.For(ReviveSource.HealerRaise)!.Value.AtUtc > first);
    }

    /// <summary>Blank is not a verdict — it would overwrite a real one with nothing.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void BlankReportsAreIgnored(string? state)
    {
        ReviveDiagnostics.Report(ReviveSource.PhoenixDown, "no Phoenix Down in inventory");
        ReviveDiagnostics.Report(ReviveSource.PhoenixDown, state!);

        Assert.Equal("no Phoenix Down in inventory", ReviveDiagnostics.For(ReviveSource.PhoenixDown)?.State);
    }

    [Fact]
    public void TheSnapshotIsNewestFirst()
    {
        ReviveDiagnostics.Report(ReviveSource.HealerRaise, "older");
        Thread.Sleep(10);
        ReviveDiagnostics.Report(ReviveSource.PhoenixDown, "newer");

        var snapshot = ReviveDiagnostics.Snapshot();
        Assert.Equal(ReviveSource.PhoenixDown, snapshot[0].Source);
    }
}
