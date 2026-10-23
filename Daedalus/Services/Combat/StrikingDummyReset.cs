using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using Daedalus.Ipc;

namespace Daedalus.Services.Combat;

/// <summary>
/// Ends a fight on a striking dummy. A dummy never dies, so combat with one only ends when the game is told to forget
/// you: the target's subcommand menu has "Reset Striking Dummy Enmity" for exactly that. This sends the same command.
/// <para>
/// The command is ExecuteCommand 319 with the dummy's entity id (named <c>ResetStrikingDummy</c> in OmenTools and Daily
/// Routines, which both call it the same way). It clears the dummy's enmity toward THIS character only — on a
/// multibox, each box resets its own.
/// </para>
/// <para>
/// One reset is not enough (field log, 2026-10-22). Three things pull the dummy straight back in after it: the rotation
/// (the combat flag lingers and it picks the dummy up again), the player's own damage-over-time ticking on the dummy,
/// and a pet or chocobo still swinging at it — a Beastmaster familiar kept hitting through a Heel for the full ten
/// seconds. So it is a short campaign, not one command: the rotation is <see cref="Holding"/>, the reset and the
/// companion orders are re-sent while combat goes on, a pet that will not stop is sent Away, and the hold only ends
/// once the character has been out of combat for <see cref="QuietNeeded"/> — not on the first frame the flag drops.
/// </para>
/// </summary>
public sealed class StrikingDummyReset
{
    /// <summary>The game's command for the subcommand-menu entry.</summary>
    public const int ResetStrikingDummyCommand = 319;

    /// <summary>How far away a dummy may be and still be reset. Covers a whole dummy yard.</summary>
    public const float Range = 50f;

    /// <summary>Pet command "Heel" (PetAction 2): has the pet stop and follow you. It did not stop a Beastmaster familiar.</summary>
    public const uint PetHeel = 2;

    /// <summary>Pet command "Away" (PetAction 1): sends the pet off the battlefield. The fallback when Heel is not obeyed.</summary>
    public const uint PetAway = 1;

    /// <summary>Chocobo command "Withdraw" (BuddyAction 2): takes the companion out of the fight; it stays summoned.</summary>
    public const uint ChocoboWithdraw = 2;

    /// <summary>The hold ends once the character has been out of combat this long without a break.</summary>
    public static readonly TimeSpan QuietNeeded = TimeSpan.FromSeconds(2);

    /// <summary>While combat goes on, the reset and the companion orders are sent again this often.</summary>
    public static readonly TimeSpan ResendEvery = TimeSpan.FromSeconds(1.5);

    /// <summary>A pet still out this long into the hold is sent Away.</summary>
    public static readonly TimeSpan AwayAfter = TimeSpan.FromSeconds(4);

    /// <summary>The hold never lasts longer than this, so a fight that will not end cannot stall the rotation for good.</summary>
    public static readonly TimeSpan MaxHold = TimeSpan.FromSeconds(30);

    private readonly IObjectTable _objects;
    private readonly ITargetManager _targets;
    private readonly Action<string> _log;
    private DateTime? _holdStartUtc;
    private DateTime _lastSentUtc;
    private DateTime? _quietSinceUtc;
    private bool _awaySent;
    private int _rounds;

    /// <summary>Sends one game command. Swappable for tests.</summary>
    internal Func<int, int, bool> ExecuteCommand { get; set; } =
        (command, param1) => FFXIVClientStructs.FFXIV.Client.Game.GameMain.ExecuteCommand(command, param1);

    /// <summary>Uses one pet or chocobo command. Swappable for tests.</summary>
    internal Func<FFXIVClientStructs.FFXIV.Client.Game.ActionType, uint, bool> UseCommand { get; set; } = UseCommandInGame;

    private static unsafe bool UseCommandInGame(FFXIVClientStructs.FFXIV.Client.Game.ActionType type, uint id)
    {
        var am = FFXIVClientStructs.FFXIV.Client.Game.ActionManager.Instance();
        return am != null && am->UseAction(type, id);
    }

    public StrikingDummyReset(IObjectTable objects, ITargetManager targets, Action<string> log)
    {
        _objects = objects;
        _targets = targets;
        _log = log;
    }

    /// <summary>A dummy, as the selection sees it.</summary>
    public readonly record struct Candidate(uint EntityId, uint NameId, float Distance);

    /// <summary>An object as the companion check sees it: who owns it and what kind of battle NPC it is.</summary>
    public readonly record struct Companion(uint OwnerId, byte Kind);

    /// <summary>What one frame of the hold does.</summary>
    public readonly record struct HoldStep(bool Release, bool Resend, bool SendAway);

    /// <summary>True from the button until combat has ended: the rotation does nothing meanwhile.</summary>
    public bool Holding => _holdStartUtc.HasValue;

    /// <summary>The dummies to reset: every striking dummy within <see cref="Range"/>, nearest first.</summary>
    public static IReadOnlyList<uint> Select(IEnumerable<Candidate> nearby) =>
        nearby.Where(c => RsrCompatIpc.IsTrainingDummy(c.NameId) && c.Distance <= Range && c.EntityId != 0)
              .OrderBy(c => c.Distance)
              .Select(c => c.EntityId)
              .ToList();

    /// <summary>Whether the player has a pet (battle NPC kind 2) and a chocobo (kind 3, Buddy) of their own out.</summary>
    public static (bool Pet, bool Chocobo) Companions(IEnumerable<Companion> objects, uint playerId)
    {
        var mine = objects.Where(o => o.OwnerId == playerId && playerId != 0).ToList();
        return (mine.Any(o => o.Kind == (byte)Dalamud.Game.ClientState.Objects.Enums.BattleNpcSubKind.Pet),
                mine.Any(o => o.Kind == (byte)Dalamud.Game.ClientState.Objects.Enums.BattleNpcSubKind.Buddy));
    }

    /// <summary>
    /// One frame of the hold. Release after <see cref="QuietNeeded"/> out of combat, or at <see cref="MaxHold"/>; while
    /// in combat, resend every <see cref="ResendEvery"/>, and send a pet that is still out Away once, at
    /// <see cref="AwayAfter"/>.
    /// </summary>
    /// <param name="sinceStart">Time since the button.</param>
    /// <param name="quietFor">How long the character has been out of combat without a break; null while in combat.</param>
    /// <param name="sinceSent">Time since the reset and orders were last sent.</param>
    public static HoldStep Decide(TimeSpan sinceStart, TimeSpan? quietFor, TimeSpan sinceSent, bool petOut, bool awaySent)
    {
        if (quietFor >= QuietNeeded || sinceStart >= MaxHold)
            return new HoldStep(Release: true, Resend: false, SendAway: false);

        var inCombat = quietFor == null;
        return new HoldStep(
            Release: false,
            Resend: inCombat && sinceSent >= ResendEvery,
            SendAway: inCombat && petOut && !awaySent && sinceStart >= AwayAfter);
    }

    /// <summary>True while the hard target is a striking dummy.</summary>
    public bool DummyTargeted() => _targets.Target is IBattleNpc npc && RsrCompatIpc.IsTrainingDummy(npc.NameId);

    /// <summary>
    /// Whether the main window shows the button: a dummy targeted, or in combat with one in range (the target may
    /// have been dropped or switched mid-fight), or a hold under way.
    /// </summary>
    public bool ShouldShowButton(bool inCombat) => Holding || DummyTargeted() || (inCombat && DummiesInRange().Count > 0);

    /// <summary>
    /// The button: hold the rotation, reset every nearby dummy, call off the pet and chocobo. Returns how many dummies
    /// were reset. Framework thread only.
    /// </summary>
    public int Reset()
    {
        if (_objects.LocalPlayer == null)
            return 0;

        var ids = DummiesInRange();
        if (ids.Count == 0)
        {
            _log("Striking dummy: none within range to reset.");
            return 0;
        }

        var now = DateTime.UtcNow;
        _holdStartUtc = now;
        _quietSinceUtc = null;
        _awaySent = false;
        _rounds = 0;

        var (reset, note) = SendRound(ids, now);
        _log($"Striking dummy: reset enmity on {reset} of {ids.Count} dummy(s) in range{note}; rotation held until combat ends.");
        return reset;
    }

    /// <summary>Every frame: keep at it while combat goes on, and end the hold once it has properly ended.</summary>
    public void Update(bool inCombat)
    {
        if (_holdStartUtc is not { } start)
            return;

        var now = DateTime.UtcNow;
        if (inCombat)
            _quietSinceUtc = null;
        else
            _quietSinceUtc ??= now;

        var petOut = OwnCompanions().Pet;
        var step = Decide(now - start, _quietSinceUtc is { } q ? now - q : null, now - _lastSentUtc, petOut, _awaySent);

        if (step.Release)
        {
            _holdStartUtc = null;
            _log(_quietSinceUtc != null
                ? $"Striking dummy: out of combat - rotation released after {(now - start).TotalSeconds:0.0}s and {_rounds} round(s)."
                : $"Striking dummy: still in combat after {MaxHold.TotalSeconds:0}s and {_rounds} round(s) - rotation released anyway.");
            return;
        }

        if (step.SendAway)
        {
            _awaySent = true;
            var sent = UseCommand(FFXIVClientStructs.FFXIV.Client.Game.ActionType.PetAction, PetAway);
            _log(sent
                ? "Striking dummy: pet still out and fighting - sent Away."
                : "Striking dummy: pet still out and fighting - Away was refused.");
        }

        if (step.Resend)
        {
            var (reset, note) = SendRound(DummiesInRange(), now);
            _log($"Striking dummy: still in combat - round {_rounds}: reset {reset}{note}.");
        }
    }

    /// <summary>Reset the dummies, clear a dummy target, heel the pet, withdraw the chocobo.</summary>
    private (int Reset, string Note) SendRound(IReadOnlyList<uint> ids, DateTime now)
    {
        _lastSentUtc = now;
        _rounds++;

        var reset = 0;
        foreach (var id in ids)
        {
            if (ExecuteCommand(ResetStrikingDummyCommand, unchecked((int)id)))
                reset++;
        }

        if (DummyTargeted())
            _targets.Target = null;

        var (hasPet, hasChocobo) = OwnCompanions();
        var note = "";
        if (hasPet)
            note += UseCommand(FFXIVClientStructs.FFXIV.Client.Game.ActionType.PetAction, PetHeel) ? ", pet told to heel" : ", pet heel refused";
        if (hasChocobo)
            note += UseCommand(FFXIVClientStructs.FFXIV.Client.Game.ActionType.BuddyAction, ChocoboWithdraw) ? ", chocobo withdrawn" : ", chocobo withdraw refused";
        return (reset, note);
    }

    private (bool Pet, bool Chocobo) OwnCompanions()
    {
        var player = _objects.LocalPlayer;
        return player == null
            ? (false, false)
            : Companions(_objects.OfType<IBattleNpc>().Select(o => new Companion(o.OwnerId, (byte)o.BattleNpcKind)), player.EntityId);
    }

    private IReadOnlyList<uint> DummiesInRange()
    {
        var player = _objects.LocalPlayer;
        if (player == null)
            return [];

        return Select(_objects
            .OfType<IBattleNpc>()
            .Select(o => new Candidate(o.EntityId, o.NameId, System.Numerics.Vector3.Distance(o.Position, player.Position))));
    }
}
