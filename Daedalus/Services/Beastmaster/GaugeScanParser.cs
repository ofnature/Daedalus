#if DEBUG
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Daedalus.Services.Beastmaster;

/// <summary>What one Gauge scan told us. Any field may be absent.</summary>
/// <param name="Name">Beast name, resolved from the target — the reply only ever says "this beast" / "this target".</param>
/// <param name="Difficulty">Tier, or Unknown when no confirmed phrase matched.</param>
/// <param name="Capturable">True/false when a confirmed phrase decided it; null otherwise.</param>
/// <param name="AlreadyCaptured">True/false when a confirmed phrase decided it; null otherwise.</param>
public readonly record struct GaugeScanResult(
    string? Name,
    BeastCaptureDifficulty Difficulty,
    bool? Capturable,
    bool? AlreadyCaptured)
{
    /// <summary>Whether a confirmed phrase matched — i.e. this scan taught us something.</summary>
    public bool Recognised => Capturable.HasValue || AlreadyCaptured.HasValue;
}

/// <summary>One confirmed scan reply and what it means.</summary>
public readonly record struct ScanPhrase(
    string Text,
    BeastCaptureDifficulty Tier,
    bool? Capturable,
    bool? AlreadyCaptured);

/// <summary>
/// Parses the Beastmaster Gauge action's reply.
///
/// <para>
/// <b>DEBUG-only.</b> Every phrase in <see cref="ConfirmedPhrases"/> is copied from a real reply
/// seen in game — nothing here is a guess. The first version of this table <i>was</i> guesses, built
/// around "catch" and "capture", and it matched none of the real replies. Rows logged against it were
/// not lost: the ledger kept their raw text, and <see cref="BeastCaptureLedger.Reparse"/> re-derives
/// them against this table on load.
/// </para>
///
/// <para>
/// A reply this table does not know stays <see cref="BeastCaptureDifficulty.Unknown"/> with every
/// flag null, and the shipped auto-capture rule refuses to act on that — so an unfamiliar reply can
/// never make the plugin press Capture. It lands in the debug tab's "unparsed" view instead, which is
/// the list of wording still to be added here.
/// </para>
/// </summary>
public static class GaugeScanParser
{
    /// <summary>
    /// What makes a chat line a Gauge reply rather than noise. Two vocabularies have been seen:
    /// "befriend" (every difficulty reply, and "already befriended") and "pact" ("No pact can be
    /// forged with this target..."). The noise actually observed inside the scan window — "You have
    /// left the sanctuary.", FATE level-sync prompts, experience gains — carries neither.
    /// <para>
    /// Whole words only. A bare substring match on "pact" would accept any damage line mentioning
    /// <i>Impact</i>; the word boundary is what keeps that out. "befriend" is matched as a word
    /// prefix so "befriended" and "Befriending" both count.
    /// </para>
    /// </summary>
    private static readonly Regex ReplyMarker =
        new(@"\b(?:befriend\w*|pact)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// Confirmed replies, verbatim fragments. Add new wording here as samples arrive. Matching is
    /// longest-phrase-first, so a specific phrase beats a substring of itself whatever the order.
    /// </summary>
    public static readonly IReadOnlyList<ScanPhrase> ConfirmedPhrases =
    [
        // "Befriending this beast should take no effort at all." — Black Eft, Central Shroud.
        new("should take no effort at all", BeastCaptureDifficulty.Trivial, Capturable: true, AlreadyCaptured: false),

        // "You are not yet strong enough to befriend this beast..." — Geshunpest, Central Shroud.
        // Level-gated, not impossible: "not yet" means it opens up as the player levels.
        new("not yet strong enough to befriend", BeastCaptureDifficulty.LevelGated, Capturable: false, AlreadyCaptured: false),

        // "You have already befriended this beast." — Ground Squirrel, Treant Sapling.
        // It WAS capturable (it was captured); the reply carries no difficulty.
        new("already befriended this beast", BeastCaptureDifficulty.Unknown, Capturable: true, AlreadyCaptured: true),

        // "No pact can be forged with this target..." — a flat no. Not a beast that can be tamed at
        // all, as opposed to the level-gated "not yet" reply above.
        new("no pact can be forged", BeastCaptureDifficulty.Impossible, Capturable: false, AlreadyCaptured: false),
    ];

    /// <summary>Whether a chat line is a Gauge reply at all, as opposed to unrelated noise in the window.</summary>
    public static bool IsScanReply(string? text) =>
        !string.IsNullOrWhiteSpace(text) && ReplyMarker.IsMatch(text!);

    /// <summary>
    /// Parse one reply. <paramref name="knownName"/> is the name resolved from the actual target, and
    /// it is the only name there is: the reply never names the beast.
    /// </summary>
    public static GaugeScanResult Parse(string? text, string? knownName = null)
        => Parse(text, knownName, ConfirmedPhrases);

    /// <summary>Test seam: parse against an arbitrary table.</summary>
    internal static GaugeScanResult Parse(string? text, string? knownName, IReadOnlyList<ScanPhrase> table)
    {
        var name = string.IsNullOrWhiteSpace(knownName) ? null : knownName;
        if (string.IsNullOrWhiteSpace(text))
            return new GaugeScanResult(name, BeastCaptureDifficulty.Unknown, null, null);

        ScanPhrase? best = null;
        foreach (var phrase in table)
        {
            if ((best is null || phrase.Text.Length > best.Value.Text.Length)
                && text!.Contains(phrase.Text, StringComparison.OrdinalIgnoreCase))
            {
                best = phrase;
            }
        }

        return best is { } p
            ? new GaugeScanResult(name, p.Tier, p.Capturable, p.AlreadyCaptured)
            : new GaugeScanResult(name, BeastCaptureDifficulty.Unknown, null, null);
    }

    /// <summary>Adapter for <see cref="BeastCaptureLedger.Reparse"/>: null when nothing matched.</summary>
    public static (BeastCaptureDifficulty Tier, bool? Capturable, bool? AlreadyCaptured)? Derive(string sample)
    {
        var r = Parse(sample);
        return r.Recognised ? (r.Difficulty, r.Capturable, r.AlreadyCaptured) : null;
    }
}
#endif
