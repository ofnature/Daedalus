// The parser under test is DEBUG-only by requirement (Part 1 must not exist in Release), so its
// tests are gated the same way. A Release test run simply has fewer tests; a Release PLUGIN build
// has no scan-collection code at all, which is the point.
#if DEBUG
using System.Numerics;
using Daedalus.Rotation.ArtemisCore.Modules;
using Daedalus.Services.Beastmaster;
using Xunit;

namespace Daedalus.Tests.Rotation.ArtemisCore;

/// <summary>
/// The Gauge scan parser, pinned against the REAL replies collected in game on 2026-09-10.
///
/// <para>
/// Every string in <see cref="RealRepliesMeanWhatTheySay"/> is copied verbatim from a screenshot or
/// from the live ledger file. The first version of this parser guessed at wording built around
/// "catch" and "capture"; it matched none of them. These tests exist so that can't recur quietly.
/// </para>
/// </summary>
public sealed class GaugeScanParserTests
{
    private const string NoEffort = "Befriending this beast should take no effort at all.";
    private const string NotYetStrong = "You are not yet strong enough to befriend this beast...";
    private const string AlreadyBefriended = "You have already befriended this beast.";
    private const string NoPact = "No pact can be forged with this target...";

    /// <summary>Each confirmed reply, verbatim, and exactly what it means.</summary>
    [Theory]
    [InlineData(NoEffort, BeastCaptureDifficulty.Trivial, true, false)]
    [InlineData(NotYetStrong, BeastCaptureDifficulty.LevelGated, false, false)]
    [InlineData(AlreadyBefriended, BeastCaptureDifficulty.Unknown, true, true)]
    [InlineData(NoPact, BeastCaptureDifficulty.Impossible, false, false)]
    public void RealRepliesMeanWhatTheySay(
        string reply, BeastCaptureDifficulty tier, bool capturable, bool alreadyCaptured)
    {
        var r = GaugeScanParser.Parse(reply, "Beast");

        Assert.True(r.Recognised);
        Assert.Equal(tier, r.Difficulty);
        Assert.Equal(capturable, r.Capturable);
        Assert.Equal(alreadyCaptured, r.AlreadyCaptured);
    }

    /// <summary>
    /// "Not yet strong enough" must be LevelGated, never Impossible. "Not yet" means the beast opens
    /// up as the player levels; filing it as impossible would hide it from the table for good.
    /// </summary>
    [Fact]
    public void NotYetStrongEnoughIsLevelGatedNotImpossible()
        => Assert.NotEqual(BeastCaptureDifficulty.Impossible, GaugeScanParser.Parse(NotYetStrong).Difficulty);

    // ── picking the reply out of the chat window ────────────────────────────────────────

    /// <summary>Both vocabularies real replies use — "befriend" and "pact" — are recognised.</summary>
    [Theory]
    [InlineData(NoEffort)]
    [InlineData(NotYetStrong)]
    [InlineData(AlreadyBefriended)]
    [InlineData(NoPact)]
    public void EveryRealReplyIsRecognisedAsAReply(string reply)
        => Assert.True(GaugeScanParser.IsScanReply(reply));

    /// <summary>
    /// The noise actually observed landing inside the scan window, verbatim from the screenshots.
    /// The first watcher took whatever line arrived first; it only got the first four scans right
    /// because of message order.
    /// </summary>
    [Theory]
    [InlineData("You have left the sanctuary.")]
    [InlineData("To join, use the level sync function located in the duty list")]
    [InlineData("You are 6 or more levels above the recommended level for this FATE.")]
    [InlineData("You gain 606 (+250%) experience points.")]
    [InlineData("")]
    [InlineData(null)]
    public void ObservedChatNoiseIsNotAReply(string? line)
        => Assert.False(GaugeScanParser.IsScanReply(line));

    /// <summary>
    /// Why "pact" is matched as a whole word. A substring match would accept any line mentioning
    /// the action Impact — a hypothetical line, but a realistic one in combat chat.
    /// </summary>
    [Fact]
    public void PactIsMatchedAsAWholeWordNotInsideImpact()
        => Assert.False(GaugeScanParser.IsScanReply("You use Impact."));

    /// <summary>
    /// A reply in the known vocabulary but with wording not yet in the table (hypothetical here)
    /// must be CAPTURED — so it reaches the "unparsed" view to be read — but must teach nothing.
    /// That split is what lets new wording be collected without it ever driving auto-capture.
    /// </summary>
    [Fact]
    public void UnfamiliarWordingIsCapturedButNotActedOn()
    {
        const string hypothetical = "Befriending this beast will require some effort.";

        Assert.True(GaugeScanParser.IsScanReply(hypothetical));

        var r = GaugeScanParser.Parse(hypothetical, "Beast");
        Assert.False(r.Recognised);
        Assert.Equal(BeastCaptureDifficulty.Unknown, r.Difficulty);
        Assert.Null(r.Capturable);
        Assert.Null(GaugeScanParser.Derive(hypothetical));
    }

    [Fact]
    public void MatchingIgnoresCase()
        => Assert.Equal(
            BeastCaptureDifficulty.Trivial,
            GaugeScanParser.Parse(NoEffort.ToUpperInvariant()).Difficulty);

    /// <summary>
    /// A specific phrase beats a substring of itself regardless of table order. Pinned with a
    /// synthetic table, since no two confirmed phrases overlap yet — they will once the other tiers
    /// arrive.
    /// </summary>
    [Fact]
    public void TheLongestMatchingPhraseWins()
    {
        ScanPhrase[] table =
        [
            new("effort", BeastCaptureDifficulty.Easy, true, false),
            new("no effort at all", BeastCaptureDifficulty.Trivial, true, false),
        ];

        Assert.Equal(BeastCaptureDifficulty.Trivial, GaugeScanParser.Parse(NoEffort, "B", table).Difficulty);
    }

    /// <summary>The reply never names the beast; the name is the target's, or there is none.</summary>
    [Fact]
    public void TheNameComesFromTheTargetOnly()
    {
        Assert.Equal("Black Eft", GaugeScanParser.Parse(NoEffort, "Black Eft").Name);
        Assert.Null(GaugeScanParser.Parse(NoEffort, knownName: null).Name);
    }

    // ── re-deriving the rows that were logged against the wrong table ───────────────────

    /// <summary>
    /// The exact four rows the live ledger held on 2026-09-10, logged while the table still guessed
    /// "catch" — every one Unknown, capturable unknown, owned unknown. Re-parsing their stored text
    /// must correct them in place, without a rescan. This is the whole reason raw text is kept.
    /// </summary>
    [Fact]
    public void RowsLoggedAgainstTheWrongTableAreCorrectedFromTheirRawText()
    {
        var ledger = new BeastCaptureLedger(configDirectory: null);
        foreach (var (name, reply) in new[]
                 {
                     ("Black Eft", NoEffort),
                     ("Geshunpest", NotYetStrong),
                     ("Ground Squirrel", AlreadyBefriended),
                     ("Treant Sapling", AlreadyBefriended),
                 })
        {
            // As the old parser logged them: nothing understood.
            ledger.Record(name, 0, BeastCaptureDifficulty.Unknown, null, 148, "Central Shroud",
                Vector3.Zero, null, reply);
        }

        Assert.Equal(4, ledger.Reparse(GaugeScanParser.Derive));

        var eft = ledger.Find("Black Eft")!;
        Assert.Equal(BeastCaptureDifficulty.Trivial, eft.Difficulty);
        Assert.True(eft.Capturable);
        Assert.False(eft.AlreadyCaptured);

        Assert.Equal(BeastCaptureDifficulty.LevelGated, ledger.Find("Geshunpest")!.Difficulty);
        Assert.True(ledger.Find("Ground Squirrel")!.AlreadyCaptured);
        Assert.True(ledger.Find("Treant Sapling")!.AlreadyCaptured);

        // And what auto-capture would now do with each of them.
        Assert.True(ledger.ShouldAutoCapture("Black Eft"));
        Assert.False(ledger.ShouldAutoCapture("Geshunpest"));      // too strong yet
        Assert.False(ledger.ShouldAutoCapture("Ground Squirrel")); // already owned
        Assert.False(ledger.ShouldAutoCapture("Treant Sapling"));  // already owned
    }

    /// <summary>A second re-parse finds nothing left to fix — it is idempotent.</summary>
    [Fact]
    public void ReparseIsIdempotent()
    {
        var ledger = new BeastCaptureLedger(configDirectory: null);
        ledger.Record("Black Eft", 0, BeastCaptureDifficulty.Unknown, null, 1, "Z", Vector3.Zero, null, NoEffort);

        Assert.Equal(1, ledger.Reparse(GaugeScanParser.Derive));
        Assert.Equal(0, ledger.Reparse(GaugeScanParser.Derive));
    }

    /// <summary>The skip reason for each real case, as the capture debug readout shows it.</summary>
    [Fact]
    public void AutoCaptureExplainsWhyItSkipsEachRealCase()
    {
        var ledger = new BeastCaptureLedger(configDirectory: null);
        ledger.Record("Geshunpest", 0, BeastCaptureDifficulty.Unknown, null, 1, "Z", Vector3.Zero, null, NotYetStrong);
        ledger.Record("Ground Squirrel", 0, BeastCaptureDifficulty.Unknown, null, 1, "Z", Vector3.Zero, null, AlreadyBefriended);
        ledger.Record("Some Humanoid", 0, BeastCaptureDifficulty.Unknown, null, 1, "Z", Vector3.Zero, null, NoPact);
        ledger.Reparse(GaugeScanParser.Derive);

        Assert.Contains("too strong", CaptureModule.DescribeSkip("Geshunpest", ledger.Find("Geshunpest")));
        Assert.Contains("already in your Bestiary", CaptureModule.DescribeSkip("Ground Squirrel", ledger.Find("Ground Squirrel")));
        Assert.Contains("no pact", CaptureModule.DescribeSkip("Some Humanoid", ledger.Find("Some Humanoid")));
        Assert.Contains("not scanned yet", CaptureModule.DescribeSkip("Unseen", ledger.Find("Unseen")));
    }
}
#endif
