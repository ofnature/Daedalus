using System.Numerics;
using Daedalus.Data;
using Daedalus.Rotation.ArtemisCore.Modules;
using Daedalus.Services.Beastmaster;
using Xunit;

namespace Daedalus.Tests.Rotation.ArtemisCore;

/// <summary>
/// The Beastmaster capture research ledger and the auto-capture timing rule.
/// </summary>
public sealed class BeastCaptureTests
{
    private static BeastCaptureLedger Ledger() => new(configDirectory: null);

    // ── the ledger ───────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Scanning is never gated on "not yet captured": a scan of a beast already in the Bestiary
    /// still confirms its zone and level, which is most of what the table is for.
    /// </summary>
    [Fact]
    public void AnAlreadyCapturedBeastIsStillRecorded()
    {
        var ledger = Ledger();

        var entry = ledger.Record(
            "Wild Coeurl", 42, BeastCaptureDifficulty.Easy, capturable: true,
            territoryId: 156, territoryName: "Mor Dhona", playerPosition: new Vector3(1, 2, 3),
            alreadyCaptured: true, rawText: "raw");

        Assert.Equal(1, ledger.Count);
        Assert.True(entry.AlreadyCaptured);
        Assert.Equal(1, entry.Scans);
    }

    /// <summary>
    /// The raw text is the whole insurance policy: the phrase table is guessing at wording nobody
    /// has confirmed, so a corrected parser has to be able to re-derive fields from samples
    /// already collected. A later scan must never blank it.
    /// </summary>
    [Fact]
    public void RawTextIsKeptAndNeverBlankedByALaterScan()
    {
        var ledger = Ledger();
        ledger.Record("Beast", 10, BeastCaptureDifficulty.Easy, true, 1, "Zone", Vector3.Zero, null, "ORIGINAL TEXT");
        ledger.Record("Beast", 0, BeastCaptureDifficulty.Unknown, null, 1, "Zone", Vector3.Zero, null, "");

        var entry = ledger.Find("Beast")!;
        Assert.Equal("ORIGINAL TEXT", entry.RawText);
    }

    /// <summary>A worse-informed rescan must not erase what an earlier one established.</summary>
    [Fact]
    public void ARescanThatParsesNothingDoesNotDestroyKnownFields()
    {
        var ledger = Ledger();
        ledger.Record("Beast", 33, BeastCaptureDifficulty.Hard, true, 1, "Zone", Vector3.Zero, null, "a");
        ledger.Record("Beast", 0, BeastCaptureDifficulty.Unknown, null, 1, "Zone", Vector3.Zero, null, "b");

        var entry = ledger.Find("Beast")!;
        Assert.Equal(33, entry.Level);
        Assert.Equal(BeastCaptureDifficulty.Hard, entry.Difficulty);
        Assert.True(entry.Capturable);
        Assert.Equal(2, entry.Scans);
    }

    /// <summary>
    /// The shipped auto-capture rule must not act on a guess. An unscanned beast, an unparsed
    /// scan, and an explicitly-impossible one all fail the gate — only positive evidence passes.
    /// </summary>
    [Fact]
    public void OnlyPositiveEvidenceCountsAsKnownCapturable()
    {
        var ledger = Ledger();
        ledger.Record("Yes", 1, BeastCaptureDifficulty.Easy, true, 1, "Z", Vector3.Zero, null, "t");
        ledger.Record("Unparsed", 1, BeastCaptureDifficulty.Unknown, null, 1, "Z", Vector3.Zero, null, "t");
        ledger.Record("Nope", 1, BeastCaptureDifficulty.Impossible, false, 1, "Z", Vector3.Zero, null, "t");

        Assert.True(ledger.IsKnownCapturable("Yes"));
        Assert.False(ledger.IsKnownCapturable("Unparsed"));
        Assert.False(ledger.IsKnownCapturable("Nope"));
        Assert.False(ledger.IsKnownCapturable("Never Scanned"));
        Assert.False(ledger.IsKnownCapturable(null));
    }

    /// <summary>Name matching is case-insensitive; the game's casing is not worth trusting.</summary>
    [Fact]
    public void LookupIsCaseInsensitive()
    {
        var ledger = Ledger();
        ledger.Record("Wild Coeurl", 1, BeastCaptureDifficulty.Easy, true, 1, "Z", Vector3.Zero, null, "t");

        Assert.True(ledger.IsKnownCapturable("wild coeurl"));
        Assert.NotNull(ledger.Find("WILD COEURL"));
    }

    // ── the timing rule ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// No confident TTK means no action. The service reports float.MaxValue for "unknown", and
    /// treating that as "loads of time" would apply Capture at the top of every pull — exactly the
    /// case that wastes the window.
    /// </summary>
    [Theory]
    [InlineData(float.MaxValue)]
    [InlineData(float.NaN)]
    [InlineData(CaptureModule.NoConfidentEstimateSeconds)]
    public void NoConfidentEstimateMeansNoCapture(float ttk)
    {
        var decision = CaptureModule.Decide(ttk, leadSeconds: 20f, safetyMargin: 10f);

        Assert.False(decision.Fire);
        Assert.Contains("no confident TTK", decision.Why);
    }

    /// <summary>
    /// A beast that will outlive the debuff must not be tagged yet — the 120s would expire before
    /// the kill and the attempt would be wasted.
    /// </summary>
    [Fact]
    public void DoesNotFireWhenTheTargetWillOutliveTheDebuff()
    {
        var decision = CaptureModule.Decide(ttkSeconds: 300f, leadSeconds: 20f, safetyMargin: 10f);

        Assert.False(decision.Fire);
        Assert.Contains("expire", decision.Why);
    }

    /// <summary>Inside the window but not yet at the lead: hold, to waste less of the debuff.</summary>
    [Fact]
    public void HoldsWhileTheKillIsStillFurtherOutThanTheLead()
    {
        var decision = CaptureModule.Decide(ttkSeconds: 90f, leadSeconds: 20f, safetyMargin: 10f);

        Assert.False(decision.Fire);
        Assert.Contains("holding", decision.Why);
    }

    /// <summary>At the lead, fire.</summary>
    [Theory]
    [InlineData(20f)]
    [InlineData(10f)]
    [InlineData(1f)]
    public void FiresOnceTheKillIsWithinTheLead(float ttk)
        => Assert.True(CaptureModule.Decide(ttk, leadSeconds: 20f, safetyMargin: 10f).Fire);

    /// <summary>
    /// The lead is clamped to what the debuff can actually survive. A user setting a 300s lead
    /// must not be able to make the module apply a 120s debuff five minutes before the kill —
    /// the clamp is what makes the safety margin a guarantee rather than a suggestion.
    /// </summary>
    [Fact]
    public void AnAbsurdLeadCannotOutrunTheDebuffWindow()
    {
        // 119s to live, with a 10s margin the latest safe application is at 110s.
        var justOutside = CaptureModule.Decide(ttkSeconds: 119f, leadSeconds: 300f, safetyMargin: 10f);
        Assert.False(justOutside.Fire);

        var justInside = CaptureModule.Decide(ttkSeconds: 109f, leadSeconds: 300f, safetyMargin: 10f);
        Assert.True(justInside.Fire);
    }

    /// <summary>A bigger safety margin must narrow the window, not widen it.</summary>
    [Fact]
    public void ABiggerSafetyMarginAppliesEarlier()
    {
        // With no margin the full 120s is usable; with 30s only 90s is.
        Assert.True(CaptureModule.Decide(115f, leadSeconds: 300f, safetyMargin: 0f).Fire);
        Assert.False(CaptureModule.Decide(115f, leadSeconds: 300f, safetyMargin: 30f).Fire);
    }

    /// <summary>The window the whole rule is built around, pinned against a silent edit.</summary>
    [Fact]
    public void TheCaptureWindowIsTwoMinutes()
        => Assert.Equal(120f, BSTActions.CaptureWindowSeconds);
}
