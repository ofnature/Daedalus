#if DEBUG
using System;
using System.Text.RegularExpressions;

namespace Daedalus.Services.Beastmaster;

/// <summary>What one Gauge scan told us. Any field may be absent.</summary>
/// <param name="Name">Beast name, or null when the line did not name one.</param>
/// <param name="Level">Reported level, or 0.</param>
/// <param name="Difficulty">Tier, or Unknown when no phrase matched.</param>
/// <param name="Capturable">True/false when the text was explicit; null when it said nothing.</param>
public readonly record struct GaugeScanResult(
    string? Name,
    int Level,
    BeastCaptureDifficulty Difficulty,
    bool? Capturable)
{
    /// <summary>Whether this carries anything worth storing beyond the raw text.</summary>
    public bool HasAnything =>
        !string.IsNullOrWhiteSpace(Name)
        || Level > 0
        || Difficulty != BeastCaptureDifficulty.Unknown
        || Capturable.HasValue;
}

/// <summary>
/// Parses the Beastmaster Gauge action's scan text.
///
/// <para>
/// <b>DEBUG-only, and deliberately provisional.</b> Beastmaster is one patch old and the real
/// wording of this message has not been confirmed. Every phrase in <see cref="DifficultyPhrases"/>
/// is a <i>guess</i>, and the whole design assumes the guesses are wrong:
/// </para>
///
/// <list type="bullet">
/// <item>The caller stores the <b>raw text</b> regardless of what this returns, so a corrected
/// table can re-derive every field from samples already collected.</item>
/// <item>A scan that matches nothing returns <see cref="BeastCaptureDifficulty.Unknown"/> rather
/// than defaulting to a tier — and <c>IsKnownCapturable</c> refuses to act on Unknown, so a bad
/// parse cannot make the shipped auto-capture rule press anything.</item>
/// <item>Correcting it is editing one table, not rewriting a parser.</item>
/// </list>
///
/// <para>
/// Phrases are matched longest-first, so a specific phrase wins over a substring of itself
/// ("very difficult" must not be swallowed by "difficult").
/// </para>
/// </summary>
public static class GaugeScanParser
{
    /// <summary>
    /// Phrase → tier. <b>UNVERIFIED — replace with real samples.</b> Ordered longest-first at
    /// match time, not here, so entries can be added in any order.
    /// </summary>
    public static readonly (string Phrase, BeastCaptureDifficulty Tier)[] DifficultyPhrases =
    [
        ("impossible to catch", BeastCaptureDifficulty.Impossible),
        ("cannot be caught", BeastCaptureDifficulty.Impossible),
        ("cannot be captured", BeastCaptureDifficulty.Impossible),

        ("an extremely difficult target to catch", BeastCaptureDifficulty.Extreme),
        ("extremely difficult", BeastCaptureDifficulty.Extreme),
        ("a very difficult target to catch", BeastCaptureDifficulty.Extreme),
        ("very difficult", BeastCaptureDifficulty.Extreme),

        ("a difficult target to catch", BeastCaptureDifficulty.Hard),
        ("difficult", BeastCaptureDifficulty.Hard),

        ("a moderate target to catch", BeastCaptureDifficulty.Moderate),
        ("moderate", BeastCaptureDifficulty.Moderate),

        ("a very easy target to catch", BeastCaptureDifficulty.Trivial),
        ("very easy", BeastCaptureDifficulty.Trivial),

        ("an easy target to catch", BeastCaptureDifficulty.Easy),
        ("easy", BeastCaptureDifficulty.Easy),
    ];

    // "Level 42", "Lv. 42", "Lv42" — all three shapes, so a wording change in one does not lose
    // the level entirely.
    private static readonly Regex LevelPattern =
        new(@"\b(?:level|lv\.?)\s*(\d{1,3})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Parse one scan line. <paramref name="knownName"/> is the name the caller already resolved
    /// from the actual target — always more trustworthy than anything scraped out of prose, so it
    /// wins when supplied.
    /// </summary>
    public static GaugeScanResult Parse(string? text, string? knownName = null)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new GaugeScanResult(knownName, 0, BeastCaptureDifficulty.Unknown, null);

        var lower = text!.ToLowerInvariant();

        var tier = BeastCaptureDifficulty.Unknown;
        var bestLength = 0;
        foreach (var (phrase, candidate) in DifficultyPhrases)
        {
            if (phrase.Length > bestLength && lower.Contains(phrase, StringComparison.Ordinal))
            {
                tier = candidate;
                bestLength = phrase.Length;
            }
        }

        var level = 0;
        var m = LevelPattern.Match(text);
        if (m.Success && int.TryParse(m.Groups[1].Value, out var parsed))
            level = parsed;

        // Capturable is only asserted where the tier is decisive. An unmatched scan leaves it null
        // rather than guessing "yes" — see the class remarks.
        bool? capturable = tier switch
        {
            BeastCaptureDifficulty.Impossible => false,
            BeastCaptureDifficulty.Unknown => null,
            _ => true,
        };

        return new GaugeScanResult(
            string.IsNullOrWhiteSpace(knownName) ? null : knownName,
            level,
            tier,
            capturable);
    }
}
#endif
