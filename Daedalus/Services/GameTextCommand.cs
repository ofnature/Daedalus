using System;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;

namespace Daedalus.Services;

/// <summary>
/// A game text command (not a plugin one), run as if typed in chat. Game commands such as /magiaauto and
/// /levelsync cannot go through Dalamud's command manager. Framework thread only.
/// </summary>
public static unsafe class GameTextCommand
{
    /// <returns>False when the game's UI module is not there yet; nothing was sent.</returns>
    public static bool Send(string text)
    {
        var ui = UIModule.Instance();
        if (ui == null)
            return false;
        var str = Utf8String.FromString(text);
        ui->ProcessChatBoxEntry(str, IntPtr.Zero, false);
        str->Dtor(true);
        return true;
    }
}
