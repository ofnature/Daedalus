using System;
using System.Collections.Generic;
using System.Linq;
using Daedalus.Data;

namespace Daedalus.Services.Beastmaster;

/// <summary>The game side of assigning a beast to a Battlehorn. Split out so the steps can be tested.</summary>
public interface IBattlehornGame
{
    /// <summary>The three horns as Bestiary numbers (0 = empty), or null when unreadable.</summary>
    int[]? ReadHorns();

    /// <summary>The Master's Bestiary window (XBMMonsterNotebook) is open.</summary>
    bool IsBestiaryOpen();

    /// <summary>Fire the Bestiary's own callback with two Int values.</summary>
    bool FireBestiary(int command, int value);

    /// <summary>The open context menu's entry texts; empty when none is open.</summary>
    IReadOnlyList<string> ContextMenuEntries();

    /// <summary>Choose context menu entry <paramref name="index"/>.</summary>
    bool PickContextMenu(int index);

    /// <summary>Open the Master's Bestiary, as its Character menu entry does.</summary>
    bool OpenBestiary();

    /// <summary>Close the Master's Bestiary.</summary>
    void CloseBestiary();
}

/// <summary>
/// Puts a team's beasts on the Battlehorns through the Master's Bestiary, one horn at a time.
///
/// <para>
/// The steps are the ones recorded in game (docs/battlehorn-assignment.md §4): turn to the beast's
/// page (<c>3, page</c>), right-click its tile (<c>8, tile</c>), pick "Assign to First / Second /
/// Third Battlehorn" from the context menu, then read the horn back about a second later — the
/// assignment round-trips the server.
/// </para>
///
/// <para>
/// Out of combat only. When the horns do not match the team, the assigner opens the Bestiary itself
/// (the Character menu's "Master's Bestiary" main command), assigns, and closes it again. It opens it
/// once per mismatch: if the player closes it part-way, or a step fails, it waits until the horns
/// have matched once, or the team changes, before opening it again. The player opening it by hand
/// always runs it.
/// </para>
///
/// <para>
/// Picking the horn a beast is already on <i>frees</i> it, so a horn that already holds the wanted
/// beast is never touched. Any step that does not come out as expected stops the assigner for that
/// team until the Bestiary is closed and opened again — the usual cause is the Bestiary's filter or
/// sort, which moves the tiles out of number order. A Bestiary that failed is left open, so the
/// player sees why.
/// </para>
/// </summary>
public sealed class BattlehornAssigner
{
    public const int PerPage = 25;
    public const double PageTurnSeconds = 0.3;
    public const double MenuTimeoutSeconds = 3;
    public const double ConfirmSeconds = 1;
    public const double OpenTimeoutSeconds = 3;

    /// <summary>With the Bestiary closed, look for a mismatch this often rather than every frame.</summary>
    public const double ClosedCheckSeconds = 1;

    /// <summary>Never more assignments than this per Bestiary opening, whatever happens.</summary>
    public const int MaxAssignmentsPerOpening = 6;

    private static readonly string[] HornNames = ["first", "second", "third"];

    /// <summary>
    /// What the assigner last did, for the settings window. Static because the rotation owns the
    /// assigner and the settings window only reads — same pattern as the other live readouts.
    /// </summary>
    public static string Status { get; private set; } = "Waiting — the horns are set out of combat.";

    /// <summary>This assigner's last message (what <see cref="Status"/> mirrors).</summary>
    public string Message { get; private set; } = Status;

    private enum Stage { Idle, TurningPage, MenuRequested, Picked }

    private readonly IBattlehornGame _game;
    private readonly bool _oneShot;
    internal Func<DateTime> UtcNow = () => DateTime.UtcNow;

    // One-shot mode: the request finished (matched or failed); nothing more until Restart().
    private bool _done;

    private Stage _stage = Stage.Idle;
    private DateTime _since;
    private int _slot = -1;
    private int _row;
    private int _assignments;
    private string? _stoppedFor;
    private bool _wasOpen;

    // Auto-open: the team we already opened the Bestiary for (cleared once the horns match), whether
    // this open was ours (so we close it), and the pending open request.
    private string? _openedFor;
    private bool _weOpened;
    private DateTime _openRequestedAt = DateTime.MinValue;
    private DateTime _nextClosedCheck = DateTime.MinValue;

    /// <param name="game">The game side.</param>
    /// <param name="oneShot">
    /// For the hand-picked horn setter: carry out one request, then stop until <see cref="Restart"/> —
    /// never re-apply it later when the horns change. It also keeps its messages to itself rather
    /// than writing the auto-set <see cref="Status"/>.
    /// </param>
    public BattlehornAssigner(IBattlehornGame game, bool oneShot = false)
    {
        _game = game;
        _oneShot = oneShot;
    }

    /// <summary>A one-shot request is being carried out.</summary>
    public bool Busy => _oneShot && !_done;

    /// <summary>Start a fresh request: forget earlier failures and what was already opened.</summary>
    public void Restart()
    {
        _done = false;
        _stoppedFor = null;
        _openedFor = null;
        _assignments = 0;
        _openRequestedAt = DateTime.MinValue;
        _nextClosedCheck = DateTime.MinValue;
    }

    /// <summary>
    /// The next horn to change: the first one whose wanted beast (non-zero) is not already on it.
    /// Null when every wanted horn matches.
    /// </summary>
    public static (int Slot, int Row)? NextStep(IReadOnlyList<int> wanted, IReadOnlyList<int> horns)
    {
        for (var slot = 0; slot < 3 && slot < wanted.Count && slot < horns.Count; slot++)
            if (wanted[slot] != 0 && horns[slot] != wanted[slot])
                return (slot, wanted[slot]);
        return null;
    }

    /// <summary>
    /// Run one frame. <paramref name="allowed"/> is false in combat or with the feature off.
    /// <paramref name="wantedSource"/> gives the Bestiary number per horn (0 = leave alone), or null
    /// while the captured-beast list has not loaded — opening the Bestiary loads it. It is asked at
    /// most once a second while the Bestiary is closed.
    /// </summary>
    public void Tick(Func<int[]?> wantedSource, bool allowed)
    {
        if (_oneShot && _done)
            return;

        var open = _game.IsBestiaryOpen();
        if (!allowed)
        {
            if (_stage != Stage.Idle)
                Stop("Stopped: combat started or auto-set was turned off.");
            _wasOpen = open;
            _weOpened &= open;
            _openRequestedAt = DateTime.MinValue;
            return;
        }

        if (!open)
        {
            var inFlight = _stage != Stage.Idle;
            if (inFlight)
                Stop("Stopped: the Bestiary was closed.");
            if (_oneShot && inFlight)
            {
                // Closed part-way through a hand-picked Set: that request is over.
                _done = true;
                _wasOpen = false;
                _weOpened = false;
                return;
            }
            if (_wasOpen)
            {
                // Closing the Bestiary is the retry: whatever stopped us gets a fresh go next time.
                _stoppedFor = null;
                _assignments = 0;
                _weOpened = false;

                // Closed with the horns still off the team — by the player, part-way: don't pop it
                // back open for this team. (One-shot requests start clean from Restart instead.)
                if (!_oneShot && wantedSource() is { } w && _game.ReadHorns() is { } h && NextStep(w, h) != null)
                    _openedFor = string.Join(",", w);
            }
            _wasOpen = false;
            TryOpen(wantedSource);
            return;
        }
        _wasOpen = true;
        _openRequestedAt = DateTime.MinValue;

        var wanted = wantedSource();
        if (wanted == null)
            return; // the captured list is still loading
        var key = string.Join(",", wanted);
        if (_stoppedFor == key)
            return;
        if (_stoppedFor != null)
        {
            // A different team was chosen since we stopped.
            _stoppedFor = null;
            _assignments = 0;
        }

        var elapsed = (UtcNow() - _since).TotalSeconds;
        switch (_stage)
        {
            case Stage.Idle:
                StartNext(wanted, key);
                break;

            case Stage.TurningPage when elapsed >= PageTurnSeconds:
                if (!_game.FireBestiary(8, (_row - 1) % PerPage))
                {
                    Fail(key, "The Bestiary did not take the right-click.");
                    return;
                }
                Enter(Stage.MenuRequested);
                break;

            case Stage.MenuRequested:
                var menu = _game.ContextMenuEntries();
                if (menu.Count > _slot && menu[_slot].Contains("Battlehorn", StringComparison.OrdinalIgnoreCase))
                {
                    _game.PickContextMenu(_slot);
                    Enter(Stage.Picked);
                }
                else if (menu.Count > 0)
                    Fail(key, $"The menu for {Name(_row)} did not offer the {HornNames[_slot]} battlehorn "
                              + $"([{string.Join(" | ", menu)}]). Is {Name(_row)} captured?");
                else if (elapsed >= MenuTimeoutSeconds)
                    Fail(key, $"No menu opened for {Name(_row)} — not captured yet?");
                break;

            case Stage.Picked when elapsed >= ConfirmSeconds:
                var horns = _game.ReadHorns();
                if (horns != null && horns[_slot] == _row)
                {
                    _assignments++;
                    Say($"{Name(_row)} is on the {HornNames[_slot]} battlehorn.");
                    _stage = Stage.Idle;
                }
                else
                {
                    var got = horns == null ? "unreadable" : horns[_slot] == 0 ? "empty" : Name(horns[_slot]);
                    Fail(key, $"{Name(_row)} did not land on the {HornNames[_slot]} battlehorn (it holds {got}). "
                              + "Clear the Bestiary's filter and sort, then reopen it.");
                }
                break;
        }
    }

    private void StartNext(int[] wanted, string key)
    {
        var horns = _game.ReadHorns();
        if (horns == null)
            return;

        if (NextStep(wanted, horns) is not { } step)
        {
            Say(wanted.All(w => w == 0)
                ? "None of this team's beasts are captured yet."
                : "Battlehorns match the team.");
            _openedFor = null;
            _done = true;
            if (_weOpened)
            {
                _weOpened = false;
                _game.CloseBestiary();
            }
            return;
        }

        if (_assignments >= MaxAssignmentsPerOpening)
        {
            Fail(key, "Gave up after several assignments without the horns matching. Reopen the Bestiary to retry.");
            return;
        }

        _slot = step.Slot;
        _row = step.Row;
        if (!_game.FireBestiary(3, (_row - 1) / PerPage))
        {
            Fail(key, "The Bestiary did not take the page turn.");
            return;
        }
        Say($"Assigning {Name(_row)} to the {HornNames[_slot]} battlehorn…");
        Enter(Stage.TurningPage);
    }

    /// <summary>With the Bestiary closed: open it once when the horns do not match the team.</summary>
    private void TryOpen(Func<int[]?> wantedSource)
    {
        var now = UtcNow();
        if (_openRequestedAt != DateTime.MinValue)
        {
            if ((now - _openRequestedAt).TotalSeconds >= OpenTimeoutSeconds)
            {
                _openRequestedAt = DateTime.MinValue;
                _weOpened = false;
                _done = true;
                Say("Could not open the Master's Bestiary. Open it from the Character menu and the horns are set.");
            }
            return;
        }

        if (now < _nextClosedCheck)
            return;
        _nextClosedCheck = now.AddSeconds(ClosedCheckSeconds);

        // Unknown captures (list not loaded) also count as "open it": opening is what loads the list.
        var wanted = wantedSource();
        var key = wanted == null ? "?" : string.Join(",", wanted);
        if (key == _openedFor)
        {
            // We opened it for this request and it was closed before any assignment started.
            if (_oneShot)
            {
                _done = true;
                Say("Stopped: the Bestiary was closed.");
            }
            return;
        }
        if (wanted != null)
        {
            if (_game.ReadHorns() is not { } horns)
                return;
            if (NextStep(wanted, horns) == null)
            {
                if (_oneShot)
                {
                    _done = true;
                    Say("Already on that horn.");
                }
                return;
            }
        }

        _openedFor = key;
        if (!_game.OpenBestiary())
        {
            _done = true;
            Say("Could not open the Master's Bestiary. Open it from the Character menu and the horns are set.");
            return;
        }
        _weOpened = true;
        _openRequestedAt = now;
        Say("Opening the Master's Bestiary to set the horns…");
    }

    private void Enter(Stage stage)
    {
        _stage = stage;
        _since = UtcNow();
    }

    private void Fail(string key, string why)
    {
        _stoppedFor = key;
        _done = true;
        Stop(why);
    }

    private void Stop(string why)
    {
        Say(why);
        _stage = Stage.Idle;
        _slot = -1;
    }

    private void Say(string message)
    {
        Message = message;
        if (!_oneShot)
            Status = message;
    }

    private static string Name(int row) => BstFamiliars.ByBestiaryNo((uint)row)?.Name ?? $"beast #{row}";
}
