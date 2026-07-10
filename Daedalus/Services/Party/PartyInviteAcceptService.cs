using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Dalamud.Memory;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace Daedalus.Services.Party;

/// <summary>
/// Auto-accepts party invites from OUR OWN toons (the LAN roster) by answering the SelectYesno
/// dialog — the receive half of one-click grouping: press the invite button on one box, every
/// target box joins without a human clicking Yes. Mechanism mirrors FrenRider's PartyService:
/// match the "Join {name}'s party?" prompt, whitelist-check the inviter, fire the Yes callback
/// (paced retries; the dialog can eat the first callback while animating). Opt-in via
/// <c>PartyCoordination.AutoAcceptRosterInvites</c>; only ever accepts names the LAN roster knows,
/// and only while solo — it can never yank a toon out of an existing party. EN-client prompt text.
/// </summary>
public sealed class PartyInviteAcceptService
{
    private const int MaxCallbackAttempts = 8;
    private const int CallbackRetryMs = 250;

    private static readonly Regex InvitePromptRegex =
        new("Join (?<name>.+?)'?s party\\?", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly IGameGui _gameGui;
    private readonly IPluginLog _log;

    private string? _lastPromptInviter;
    private long _lastAttemptAt;
    private int _attempts;

    public PartyInviteAcceptService(IGameGui gameGui, IPluginLog log)
    {
        _gameGui = gameGui;
        _log = log;
    }

    /// <summary>
    /// Per-frame poll. <paramref name="rosterNames"/> is the whitelist (LAN roster character names);
    /// <paramref name="inParty"/> short-circuits the addon probe for the common case.
    /// </summary>
    public unsafe void Update(bool enabled, bool inParty, IReadOnlyCollection<string> rosterNames)
    {
        if (!enabled || inParty || rosterNames.Count == 0)
        {
            _lastPromptInviter = null;
            _attempts = 0;
            return;
        }

        try
        {
            var addonPtr = _gameGui.GetAddonByName("SelectYesno", 1);
            if (addonPtr.Address == nint.Zero)
            {
                _lastPromptInviter = null;
                _attempts = 0;
                return;
            }

            var addon = (AddonSelectYesno*)addonPtr.Address;
            if (!addon->AtkUnitBase.IsVisible)
                return;

            var promptNode = addon->PromptText;
            if (promptNode == null)
                return;

            var textPtr = promptNode->NodeText.StringPtr;
            if (!textPtr.HasValue)
                return;

            var prompt = MemoryHelper.ReadSeStringNullTerminated(new IntPtr(textPtr.Value)).TextValue;
            var inviter = MatchInviter(prompt);
            if (inviter == null || !IsWhitelisted(inviter, rosterNames))
                return;

            var now = Environment.TickCount64;
            if (_lastPromptInviter != inviter)
            {
                _lastPromptInviter = inviter;
                _attempts = 0;
                _lastAttemptAt = 0;
                _log.Info($"Party invite from rostered toon '{inviter}' — auto-accepting");
            }

            if (_attempts >= MaxCallbackAttempts || now - _lastAttemptAt < CallbackRetryMs)
                return;

            _lastAttemptAt = now;
            _attempts++;

            // Yes = option 0. Two Int(0) AtkValues, same shape the button itself submits.
            var values = stackalloc AtkValue[2];
            values[0] = default;
            values[1] = default;
            values[0].Type = AtkValueType.Int;
            values[0].Int = 0;
            values[1].Type = AtkValueType.Int;
            values[1].Int = 0;
            addon->AtkUnitBase.FireCallback(2, values);
        }
        catch (Exception ex)
        {
            _log.Debug(ex, "PartyInviteAcceptService.Update failed — will retry next frame");
        }
    }

    /// <summary>Extracts the inviter name from the SelectYesno prompt, or null when it isn't a party invite.</summary>
    internal static string? MatchInviter(string? prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
            return null;

        var match = InvitePromptRegex.Match(prompt.Trim());
        return match.Success ? match.Groups["name"].Value.Trim() : null;
    }

    /// <summary>Case-insensitive exact-name match against the roster whitelist.</summary>
    internal static bool IsWhitelisted(string inviter, IReadOnlyCollection<string> rosterNames)
    {
        foreach (var name in rosterNames)
        {
            if (name.Length > 0 && string.Equals(name, inviter, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
