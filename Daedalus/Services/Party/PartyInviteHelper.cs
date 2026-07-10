using System;
using System.Text;
using FFXIVClientStructs.FFXIV.Client.UI.Info;

namespace Daedalus.Services.Party;

/// <summary>
/// Sends an in-game party invite through the native call the game's own UI uses
/// (<c>InfoProxyPartyInvite.InviteToParty</c> — same approach as Automaton's Auto Inviter). Unlike
/// the "/invite" text command this addresses the target by content id + name + home world, so it
/// works for multi-word names and cross-world (same data center) targets alike. The LAN heartbeat
/// carries each toon's content id and world id precisely for this. Fail-open: returns false with a
/// reason when the proxy is unavailable, never throws.
/// </summary>
public static unsafe class PartyInviteHelper
{
    public static bool TryInvite(string characterName, ulong contentId, ushort homeWorldId, out string detail)
    {
        characterName = characterName?.Trim() ?? "";
        if (characterName.Length == 0)
        {
            detail = "empty name";
            return false;
        }

        if (homeWorldId == 0)
        {
            detail = "no world id yet (waiting on heartbeat)";
            return false;
        }

        try
        {
            var proxy = InfoProxyPartyInvite.Instance();
            if (proxy == null)
            {
                detail = "invite proxy unavailable";
                return false;
            }

            var nameBytes = new byte[Encoding.UTF8.GetByteCount(characterName) + 1];
            Encoding.UTF8.GetBytes(characterName, 0, characterName.Length, nameBytes, 0);

            fixed (byte* namePtr = nameBytes)
            {
                proxy->InviteToParty(contentId, namePtr, homeWorldId);
            }

            detail = $"invite sent to {characterName}";
            return true;
        }
        catch (Exception ex)
        {
            detail = $"threw {ex.GetType().Name}";
            return false;
        }
    }
}
