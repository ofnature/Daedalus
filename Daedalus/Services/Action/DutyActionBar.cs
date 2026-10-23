using System;

namespace Daedalus.Services.Action;

/// <summary>
/// The duty action bar — the five slots a duty fills with its own actions (Occult Crescent's phantom actions, the
/// Crucible's Challenge and Snarl). An action is only usable while the duty has put it there.
/// </summary>
public static class DutyActionBar
{
    public const int SlotCount = 5;

    /// <summary>Whether the action sits in a duty slot right now. Fails closed on a read error.</summary>
    public static unsafe bool Contains(uint actionId)
    {
        if (actionId == 0)
            return false;

        try
        {
            for (ushort i = 0; i < SlotCount; i++)
            {
                if (FFXIVClientStructs.FFXIV.Client.Game.DutyActionManager.GetDutyActionId(i) == actionId)
                    return true;
            }
        }
        catch (Exception)
        {
            // Not in a duty that has a bar: nothing is on it.
        }

        return false;
    }
}
