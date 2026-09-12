using System;
using System.IO;
using System.Numerics;
using Daedalus.Data;
using Daedalus.Rotation.ArtemisCore.Modules;
using Daedalus.Services.Beastmaster;
using Xunit;

namespace Daedalus.Tests.Rotation.ArtemisCore;

/// <summary>
/// The Beastmaster capture ledger and the auto-capture timing rule.
/// <para>
/// Ungated: these compile in Release too, because the ledger and the auto-capture rule ship. They
/// must therefore never reference the DEBUG-only parser — real-reply tests live in
/// <c>GaugeScanParserTests</c>, and re-parse is exercised here through a plain delegate.
/// </para>
/// </summary>
public sealed class BeastCaptureTests
{
    private static BeastCaptureLedger Ledger() => new(configDirectory: null);

    private static BeastCaptureEntry Rec(
        BeastCaptureLedger l, string name, string raw,
        BeastCaptureDifficulty tier = BeastCaptureDifficulty.Unknown,
        bool? cap = null, bool? owned = null, int level = 0)
        => l.Record(name, level, tier, cap, 1, "Zone", Vector3.Zero, owned, raw);

    // ── the ledger ───────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Scanning is never gated on "not yet captured": a scan of a beast already in the Bestiary
    /// still confirms its zone and level, which is most of what the table is for.
    /// </summary>
    [Fact]
    public void AnAlreadyCapturedBeastIsStillRecorded()
    {
        var entry = Rec(Ledger(), "Ground Squirrel", "owned", cap: true, owned: true);

        Assert.True(entry.AlreadyCaptured);
        Assert.Equal(1, entry.Scans);
    }

    /// <summary>
    /// Scanning a beast you already own replies "already befriended" — and that must not overwrite
    /// the one reply that carried its difficulty. One stored string was not enough; every distinct
    /// reply is kept.
    /// </summary>
    [Fact]
    public void AnOwnedRescanKeepsTheSampleThatCarriedTheDifficulty()
    {
        var ledger = Ledger();
        Rec(ledger, "Black Eft", "TIER REPLY", BeastCaptureDifficulty.Trivial, cap: true, owned: false);
        Rec(ledger, "Black Eft", "OWNED REPLY", cap: true, owned: true);

        var e = ledger.Find("Black Eft")!;
        Assert.Equal(["TIER REPLY", "OWNED REPLY"], e.RawSamples);
        Assert.Equal("OWNED REPLY", e.RawText);
        Assert.Equal(BeastCaptureDifficulty.Trivial, e.Difficulty);   // not erased by the owned reply
    }

    /// <summary>
    /// A reply seen again moves to the end, so the list stays in last-seen order — the order a
    /// re-parse relies on to let the newest reply win.
    /// </summary>
    [Fact]
    public void ARepeatedReplyMovesToTheEnd()
    {
        var ledger = Ledger();
        Rec(ledger, "B", "A");
        Rec(ledger, "B", "B");
        Rec(ledger, "B", "A");

        Assert.Equal(["B", "A"], ledger.Find("B")!.RawSamples);
    }

    [Fact]
    public void RawSamplesAreCapped()
    {
        var ledger = Ledger();
        for (var i = 0; i < BeastCaptureLedger.MaxRawSamples + 5; i++)
            Rec(ledger, "B", $"reply {i}");

        var samples = ledger.Find("B")!.RawSamples;
        Assert.Equal(BeastCaptureLedger.MaxRawSamples, samples.Count);
        Assert.Equal($"reply {BeastCaptureLedger.MaxRawSamples + 4}", samples[^1]);   // newest kept
    }

    /// <summary>A rescan that understood nothing must not erase what an earlier one established.</summary>
    [Fact]
    public void ARescanThatUnderstoodNothingDoesNotDestroyKnownFields()
    {
        var ledger = Ledger();
        Rec(ledger, "Beast", "a", BeastCaptureDifficulty.Trivial, cap: true, owned: false, level: 33);
        Rec(ledger, "Beast", "b");

        var e = ledger.Find("Beast")!;
        Assert.Equal(33, e.Level);
        Assert.Equal(BeastCaptureDifficulty.Trivial, e.Difficulty);
        Assert.True(e.Capturable);
        Assert.False(e.AlreadyCaptured);
        Assert.Equal(2, e.Scans);
    }

    /// <summary>
    /// The shipped auto-capture rule must not act on a guess. Unscanned, not-understood, level-gated
    /// and no-pact all fail — only positive evidence passes.
    /// </summary>
    [Fact]
    public void OnlyPositiveEvidenceCountsAsKnownCapturable()
    {
        var ledger = Ledger();
        Rec(ledger, "Yes", "t", BeastCaptureDifficulty.Trivial, cap: true, owned: false);
        Rec(ledger, "Unparsed", "t");
        Rec(ledger, "Too Strong", "t", BeastCaptureDifficulty.LevelGated, cap: false, owned: false);
        Rec(ledger, "No Pact", "t", BeastCaptureDifficulty.Impossible, cap: false, owned: false);

        Assert.True(ledger.IsKnownCapturable("Yes"));
        Assert.False(ledger.IsKnownCapturable("Unparsed"));
        Assert.False(ledger.IsKnownCapturable("Too Strong"));
        Assert.False(ledger.IsKnownCapturable("No Pact"));
        Assert.False(ledger.IsKnownCapturable("Never Scanned"));
        Assert.False(ledger.IsKnownCapturable(null));
    }

    /// <summary>
    /// Capturing a beast you own adds nothing, and owned beasts get scanned constantly while
    /// collecting data — so the auto-capture gate must exclude them even though they ARE capturable.
    /// </summary>
    [Fact]
    public void AutoCaptureSkipsBeastsAlreadyInTheBestiary()
    {
        var ledger = Ledger();
        Rec(ledger, "Black Eft", "t", BeastCaptureDifficulty.Trivial, cap: true, owned: false);
        Assert.True(ledger.ShouldAutoCapture("Black Eft"));

        Rec(ledger, "Black Eft", "owned", cap: true, owned: true);
        Assert.True(ledger.IsKnownCapturable("Black Eft"));     // still a fact about the beast
        Assert.False(ledger.ShouldAutoCapture("Black Eft"));    // but no longer worth a GCD
    }

    /// <summary>Name matching is case-insensitive; the game's casing is not worth trusting.</summary>
    [Fact]
    public void LookupIsCaseInsensitive()
    {
        var ledger = Ledger();
        Rec(ledger, "Wild Coeurl", "t", BeastCaptureDifficulty.Trivial, cap: true, owned: false);

        Assert.True(ledger.IsKnownCapturable("wild coeurl"));
        Assert.NotNull(ledger.Find("WILD COEURL"));
    }

    // ── re-parse ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Re-parse replays each beast's samples oldest-first, so the newest reply wins — the same rule
    /// Record uses. A beast that was too strong and later became befriendable ends up befriendable.
    /// </summary>
    [Fact]
    public void ReparseLetsTheNewestReplyWin()
    {
        var ledger = Ledger();
        Rec(ledger, "B", "gated");
        Rec(ledger, "B", "easy now");

        ledger.Reparse(s => s switch
        {
            "gated" => (BeastCaptureDifficulty.LevelGated, false, false),
            "easy now" => (BeastCaptureDifficulty.Trivial, true, false),
            _ => null,
        });

        var e = ledger.Find("B")!;
        Assert.Equal(BeastCaptureDifficulty.Trivial, e.Difficulty);
        Assert.True(e.Capturable);
    }

    /// <summary>
    /// A sample the table does not recognise contributes nothing — and is KEPT, not deleted, in case
    /// a later table learns it.
    /// </summary>
    [Fact]
    public void UnrecognisedSamplesAreKeptAndIgnored()
    {
        var ledger = Ledger();
        Rec(ledger, "B", "mystery");

        Assert.Equal(0, ledger.Reparse(_ => null));

        var e = ledger.Find("B")!;
        Assert.Null(e.Capturable);
        Assert.Equal(["mystery"], e.RawSamples);
    }

    /// <summary>
    /// Files written before RawSamples existed hold their only sample in RawText. Loading one must
    /// seed RawSamples from it, or re-parse would have nothing to read for exactly the rows that
    /// most needed correcting.
    /// </summary>
    [Fact]
    public void ALegacyFileSeedsRawSamplesFromRawText()
    {
        var dir = Path.Combine(Path.GetTempPath(), "daedalus-bst-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "beast-captures.json"),
                """[{"Name":"Black Eft","Difficulty":0,"RawText":"legacy reply"}]""");

            var e = new BeastCaptureLedger(dir).Find("Black Eft")!;

            Assert.Equal(["legacy reply"], e.RawSamples);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // ── hand-supplied facts ──────────────────────────────────────────────────────────────

    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "daedalus-bst-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>
    /// Levels read off the target by hand reach the live ledger through a one-shot import file —
    /// the only safe route, because the running plugin writes its in-memory table over any direct
    /// edit to the main file on its next save or reload.
    /// </summary>
    [Fact]
    public void AnImportFileSetsLevelsAndIsAppliedOnce()
    {
        var dir = TempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "beast-captures.json"),
                """[{"Name":"Black Eft","Level":0,"RawText":"r"}]""");
            File.WriteAllText(Path.Combine(dir, "beast-captures.import.json"),
                """[{"Name":"Black Eft","Level":6}]""");

            var ledger = new BeastCaptureLedger(dir);

            Assert.Equal(6, ledger.Find("Black Eft")!.Level);
            Assert.False(File.Exists(Path.Combine(dir, "beast-captures.import.json")));        // consumed
            Assert.Single(Directory.GetFiles(dir, "beast-captures.import.applied-*.json"));    // audit trail
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    /// <summary>
    /// An import may set informational fields only. Capturability drives auto-capture and must stay
    /// evidence from real scans — a hand-typed "capturable" must never make the plugin press Capture.
    /// </summary>
    [Fact]
    public void AnImportCannotTouchTheFieldsThatDriveAutoCapture()
    {
        var dir = TempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "beast-captures.json"),
                """[{"Name":"Mystery","Level":0,"RawText":"r"}]""");
            File.WriteAllText(Path.Combine(dir, "beast-captures.import.json"),
                """[{"Name":"Mystery","Level":9,"Capturable":true,"Difficulty":1}]""");

            var ledger = new BeastCaptureLedger(dir);
            var e = ledger.Find("Mystery")!;

            Assert.Equal(9, e.Level);
            Assert.Null(e.Capturable);
            Assert.Equal(BeastCaptureDifficulty.Unknown, e.Difficulty);
            Assert.False(ledger.ShouldAutoCapture("Mystery"));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    /// <summary>An import never invents a row for a beast that was never scanned — the table stays scan-backed.</summary>
    [Fact]
    public void AnImportDoesNotInventRowsForUnscannedBeasts()
    {
        var dir = TempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "beast-captures.import.json"),
                """[{"Name":"Never Scanned","Level":20}]""");

            Assert.Null(new BeastCaptureLedger(dir).Find("Never Scanned"));
        }
        finally { Directory.Delete(dir, recursive: true); }
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

    /// <summary>A beast that will outlive the debuff must not be tagged yet.</summary>
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

    [Theory]
    [InlineData(20f)]
    [InlineData(10f)]
    [InlineData(1f)]
    public void FiresOnceTheKillIsWithinTheLead(float ttk)
        => Assert.True(CaptureModule.Decide(ttk, leadSeconds: 20f, safetyMargin: 10f).Fire);

    /// <summary>
    /// The lead is clamped to what the debuff can survive, so a 300s lead cannot apply a 120s debuff
    /// five minutes early — the clamp is what makes the margin a guarantee.
    /// </summary>
    [Fact]
    public void AnAbsurdLeadCannotOutrunTheDebuffWindow()
    {
        Assert.False(CaptureModule.Decide(ttkSeconds: 119f, leadSeconds: 300f, safetyMargin: 10f).Fire);
        Assert.True(CaptureModule.Decide(ttkSeconds: 109f, leadSeconds: 300f, safetyMargin: 10f).Fire);
    }

    /// <summary>A bigger safety margin must narrow the window, not widen it.</summary>
    [Fact]
    public void ABiggerSafetyMarginAppliesEarlier()
    {
        Assert.True(CaptureModule.Decide(115f, leadSeconds: 300f, safetyMargin: 0f).Fire);
        Assert.False(CaptureModule.Decide(115f, leadSeconds: 300f, safetyMargin: 30f).Fire);
    }

    [Fact]
    public void TheCaptureWindowIsTwoMinutes()
        => Assert.Equal(120f, BSTActions.CaptureWindowSeconds);
}
