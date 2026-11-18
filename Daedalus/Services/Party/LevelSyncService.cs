using System;

namespace Daedalus.Services.Party;

/// <summary>
/// The coordination window's "FATE sync" button: every toon in the group level-syncs to the FATE it is standing
/// in, with the game's own <c>/levelsync on</c> (also what a Eureka notorious monster asks for). "on", never the
/// bare command: bare toggles, and would un-sync a toon that had already synced by hand.
/// <para>
/// The game decides whether it applies: a toon outside the FATE, or one that is not over its level, gets the
/// game's own refusal in chat and nothing else happens.
/// </para>
/// </summary>
public sealed class LevelSyncService
{
    public const string Command = "/levelsync on";

    private readonly Func<string, bool> _send;
    private readonly Action<string>? _note;
    private bool _pending;

    /// <param name="send">Runs a game text command; false = the game was not ready and nothing was sent.</param>
    public LevelSyncService(Func<string, bool> send, Action<string>? note = null)
    {
        _send = send;
        _note = note;
    }

    /// <summary>The fleet called for a level sync. Sent on the next frame.</summary>
    public void Request() => _pending = true;

    /// <summary>Framework thread, every frame.</summary>
    public void Update()
    {
        if (!_pending || !_send(Command))
            return;
        _pending = false;
        _note?.Invoke("Level sync: the group called for it, sent " + Command);
    }
}
