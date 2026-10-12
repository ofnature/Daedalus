using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Dalamud.Plugin.Services;

namespace Daedalus.Services.Occult;

/// <summary>One line of an Occult Treasuresight answer: "27 bronze".</summary>
public readonly record struct CofferCount(int Count, string Kind);

/// <summary>
/// Reads the game's answer to Occult Treasuresight out of chat, so the count can sit in the Occult window
/// instead of scrolling away. The line, as the game prints it (field 2026-10-11):
/// "You sense the presence of 4 silver coffers and 27 bronze coffers in the area!"
/// </summary>
public sealed class TreasuresightLog : IDisposable
{
    private static readonly Regex CountPattern =
        new(@"(\d+)\s+([A-Za-z]+)\s+coffers?", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly IChatGui? _chatGui;
    private readonly IClientState? _clientState;
    private readonly IPluginLog? _log;
    private bool _subscribed;

    /// <summary>The last answer, in the order the game listed the kinds. Empty until one is seen.</summary>
    public IReadOnlyList<CofferCount> Counts { get; private set; } = [];

    public DateTime TakenUtc { get; private set; } = DateTime.MinValue;

    /// <summary>Zone the answer was for. A count from the other Horn says nothing about this one.</summary>
    public ushort TerritoryId { get; private set; }

    public TreasuresightLog(IChatGui? chatGui, IClientState? clientState, IPluginLog? log = null)
    {
        _chatGui = chatGui;
        _clientState = clientState;
        _log = log;

        if (_chatGui is null)
            return;

        try
        {
            _chatGui.ChatMessage += OnChatMessage;
            _subscribed = true;
        }
        catch (Exception ex)
        {
            _log?.Warning(ex, "TreasuresightLog: could not subscribe to chat");
        }
    }

    /// <summary>
    /// The counts in a Treasuresight line, or false for any other chat line. Kinds are taken as the game names
    /// them rather than from a fixed list, so a coffer type this has never seen still shows up.
    /// </summary>
    public static bool TryParse(string? text, out IReadOnlyList<CofferCount> counts)
    {
        counts = [];
        if (string.IsNullOrEmpty(text)
            || !text.Contains("sense the presence", StringComparison.OrdinalIgnoreCase))
            return false;

        var found = new List<CofferCount>();
        foreach (Match match in CountPattern.Matches(text))
        {
            if (int.TryParse(match.Groups[1].Value, out var count))
                found.Add(new CofferCount(count, match.Groups[2].Value.ToLowerInvariant()));
        }

        counts = found;
        return found.Count > 0;
    }

    /// <summary>Feed one chat line. Returns whether it was a Treasuresight answer.</summary>
    internal bool Observe(string? text, ushort territoryId, DateTime nowUtc)
    {
        if (!TryParse(text, out var counts))
            return false;

        Counts = counts;
        TakenUtc = nowUtc;
        TerritoryId = territoryId;
        return true;
    }

    /// <summary>"4 silver · 27 bronze", or empty when there is no answer for this zone.</summary>
    public string Describe(ushort territoryId)
    {
        if (Counts.Count == 0 || territoryId != TerritoryId)
            return string.Empty;

        var parts = new List<string>(Counts.Count);
        foreach (var c in Counts)
            parts.Add($"{c.Count} {c.Kind}");
        return string.Join(" · ", parts);
    }

    /// <summary>"just now", "3m ago", "1h 5m ago" — a coffer count goes stale, so it always says how old it is.</summary>
    public static string DescribeAge(TimeSpan age)
    {
        if (age.TotalSeconds < 60)
            return "just now";
        if (age.TotalMinutes < 60)
            return $"{(int)age.TotalMinutes}m ago";
        return $"{(int)age.TotalHours}h {age.Minutes}m ago";
    }

    private void OnChatMessage(Dalamud.Game.Chat.IHandleableChatMessage message)
    {
        try
        {
            Observe(message.Message.TextValue, (ushort)(_clientState?.TerritoryType ?? 0), DateTime.UtcNow);
        }
        catch (Exception ex)
        {
            _log?.Warning(ex, "TreasuresightLog: chat handling failed");
        }
    }

    public void Dispose()
    {
        if (!_subscribed || _chatGui is null)
            return;

        try { _chatGui.ChatMessage -= OnChatMessage; }
        catch { /* shutting down */ }

        _subscribed = false;
    }
}
