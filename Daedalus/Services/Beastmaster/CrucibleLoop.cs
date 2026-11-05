using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Daedalus.Data;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using GameObjectStruct = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;
using static Daedalus.Services.Beastmaster.CrucibleAddons;

namespace Daedalus.Services.Beastmaster;

/// <summary>
/// Goes back into the Crucible for the next run, the way the recordings show it (<see cref="CrucibleLoopRules"/>):
/// talk to Lauda, pick the Crucible and "Challenge", open the board you last ran, set the team to the goal's roster,
/// confirm, accept the warning and Commence. Counts a run each time a board is entered. It does not walk: you must be
/// standing next to Lauda, which is where a finished run leaves you. Any screen it does not expect stops it.
/// </summary>
public sealed unsafe class CrucibleLoop
{
    private static readonly TimeSpan StepEvery = TimeSpan.FromMilliseconds(800);
    private static readonly TimeSpan SettleOutside = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan DegreeStepWait = TimeSpan.FromMilliseconds(1200);

    private readonly Action<string> _log;
    private DateTime _nextStepUtc;
    private DateTime _outsideSinceUtc;
    private DateTime _lastProgressUtc;
    private bool _wasOnBoard;
    private bool _boardHighlighted;
    private bool _teamTried;
    private bool _entering;
    private int _degreeSteps;

    /// <summary>
    /// The roster asked for on this entry, fixed at the first Load. The roster is worked out from beast ranks, and the
    /// Team Composition screen updates those ranks while the team is being changed — so recomputing it afterwards could
    /// ask for a different team than the one just loaded, and the loop stopped although the load had worked (Korha
    /// 2026-10-27 23:20: "Team set: 12 beasts" and "the team could not be set" in the same second).
    /// </summary>
    private List<int>? _teamWanted;

    public CrucibleLoop(Action<string> log) => _log = log;

    /// <summary>What it did last, for the team window.</summary>
    public string Status { get; private set; } = "";

    /// <summary>The board the loop goes back into: the last one entered.</summary>
    public CrucibleBoard? LastBoard { get; private set; }

    /// <summary>
    /// Start a run of <paramref name="board"/> from its open layout: set the team, Challenge, accept the warning,
    /// Commence — the same steps the loop takes after talking to Lauda. Needs a run in hand (runs left ≥ 1).
    /// </summary>
    public void Begin(CrucibleBoard board)
    {
        LastBoard = board;
        _entering = true;
        _boardHighlighted = true;
        _teamTried = false;
        _degreeSteps = 0;
        _teamWanted = null;
        _outsideSinceUtc = default;
        _nextStepUtc = default;
        _lastProgressUtc = DateTime.UtcNow;
        Status = $"Starting {board.Name}…";
    }

    /// <summary>Framework thread, every frame.</summary>
    /// <param name="runsLeft">Runs still to start; the loop decrements it on each board entered and stops at 0.</param>
    /// <param name="rosterFor">The team to bring onto a board (the goal's roster).</param>
    /// <param name="wantedDegree">The degree to enter on (0 Standard … 3 Third), or -1 to leave the game's choice.</param>
    /// <param name="finished">Asked before each new run: a stop message when the goal is met (every beast rank 25), else null.</param>
    public void Update(
        bool enabled, ref int runsLeft, CrucibleBoard? onBoard, IPlayerCharacter? player, IObjectTable objects, bool inCombat,
        Func<CrucibleBoard, IReadOnlyList<int>> rosterFor, CrucibleRosterLoader? loader, int wantedDegree = CrucibleLoopRules.DegreeAsSet,
        Func<string?>? finished = null)
    {
        var now = DateTime.UtcNow;
        if (onBoard != null)
        {
            if (!_wasOnBoard && _entering && runsLeft > 0)
            {
                runsLeft--;
                _log($"Crucible: loop entered {onBoard.Name}; {runsLeft} run(s) left after this one.");
            }

            LastBoard = onBoard;
            _wasOnBoard = true;
            _entering = false;
            _outsideSinceUtc = default;
            return;
        }

        if (_wasOnBoard)
        {
            _wasOnBoard = false;
            _outsideSinceUtc = now;
        }

        if (!enabled || runsLeft <= 0 || LastBoard is not { } board || player == null || inCombat)
        {
            _entering = false;
            return;
        }

        if (now < _nextStepUtc || (_outsideSinceUtc != default && now - _outsideSinceUtc < SettleOutside))
            return;

        if (_entering && now - _lastProgressUtc > CrucibleLoopRules.StuckAfter)
        {
            Stop(ref runsLeft, "Loop stopped: the entry screens did not move on for 30 s.");
            return;
        }

        try
        {
            Step(now, ref runsLeft, board, player, objects, rosterFor, loader, wantedDegree, finished);
        }
        catch (Exception ex)
        {
            Stop(ref runsLeft, $"Loop failed: {ex.Message}");
        }
    }

    private void Step(
        DateTime now, ref int runsLeft, CrucibleBoard board, IPlayerCharacter player, IObjectTable objects,
        Func<CrucibleBoard, IReadOnlyList<int>> rosterFor, CrucibleRosterLoader? loader, int wantedDegree, Func<string?>? finished)
    {
        var yesNo = Visible("SelectYesno");
        if (yesNo != null)
        {
            var prompt = Text(yesNo, 0);
            if (_entering && CrucibleLoopRules.IsEntryWarning(prompt))
            {
                Fire(yesNo, 0);
                Progress(now, "Accepting the board warning.");
            }

            return; // anything else is the player's
        }

        var commence = Visible("ContentsFinderConfirm");
        if (commence != null)
        {
            if (_entering)
            {
                Fire(commence, 8);
                Progress(now, "Commencing.");
            }

            return;
        }

        // The board's layout and Team Composition: set the team, then confirm.
        var layout = Visible("XBMStageDetailList");
        if (layout != null && _entering)
        {
            // The degree first, then the team.
            if (SelectedDegree() is { } degree)
            {
                switch (CrucibleLoopRules.NextDegreeAction(degree, wantedDegree, _degreeSteps))
                {
                    case CrucibleLoopRules.DegreeAction.StepUp or CrucibleLoopRules.DegreeAction.StepDown:
                        _degreeSteps++;
                        Fire(layout, degree < wantedDegree ? CrucibleLoopRules.DegreeStepEvent : CrucibleLoopRules.DegreeStepDownEvent);
                        Progress(now, $"Setting {CrucibleLoopRules.DegreeName(wantedDegree)} (on {CrucibleLoopRules.DegreeName(degree)}).");
                        _nextStepUtc = now + DegreeStepWait;
                        return;
                    case CrucibleLoopRules.DegreeAction.GiveUp:
                        Stop(ref runsLeft, $"Loop stopped: could not set {CrucibleLoopRules.DegreeName(wantedDegree)} — still on "
                                           + $"{CrucibleLoopRules.DegreeName(degree)}. Set it with the arrows beside Crucible Mode "
                                           + "(a degree above Standard needs the Second Master's Board and Blazing Trails).");
                        return;
                }
            }

            var wanted = (_teamWanted ?? rosterFor(board)).Where(p => loader?.Refused.Contains(p) != true).ToList();
            var changes = loader == null ? [] : CrucibleLevelPlan.Changes(loader.Team, wanted);
            if (loader is { Loading: true })
                return;

            if (changes.Count > 0 && loader is { CompositionOpen: true })
            {
                if (_teamTried)
                {
                    Stop(ref runsLeft, "Loop stopped: the team could not be set — check Team Composition. "
                                       + $"(wanted {string.Join(",", wanted)}; team {string.Join(",", loader.Team)})");
                    return;
                }

                _teamTried = true;
                _teamWanted = wanted;
                loader.Load(wanted);
                Progress(now, "Setting the team…");
                return;
            }

            Fire(layout, 8);
            Progress(now, "Confirming the team.");
            return;
        }

        var boards = Visible("XBMStageList");
        if (boards != null && _entering)
        {
            if (!_boardHighlighted)
            {
                Fire(boards, 2, board.Index);
                _boardHighlighted = true;
            }
            else
            {
                Fire(boards, 1, board.Index);
            }

            Progress(now, $"Opening {board.Name}.");
            return;
        }

        var menu = Visible("SelectString");
        if (menu != null)
        {
            if (_entering && Text(menu, 7) == CrucibleLoopRules.ChallengeOption)
            {
                Fire(menu, 0);
                Progress(now, "Challenge.");
            }

            return;
        }

        var list = Visible("SelectIconString");
        if (list != null)
        {
            if (!_entering)
                return;

            var count = (int)Number(list, 5);
            var options = Enumerable.Range(0, Math.Clamp(count, 0, 10)).Select(i => Text(list, 7 + (3 * i))).ToList();
            var pick = CrucibleLoopRules.MenuPick(options);
            if (pick < 0)
            {
                Stop(ref runsLeft, $"Loop stopped: Lauda's list has no \"{CrucibleLoopRules.MenuOption}\".");
                return;
            }

            Fire(list, pick);
            Progress(now, "Lauda: the Crucible.");
            return;
        }

        if (_entering)
            return; // waiting for the next screen; the stuck timer covers a dead end

        // The goal is met: stop here, before the next run, whatever the run count.
        if (finished?.Invoke() is { } done)
        {
            Stop(ref runsLeft, done);
            return;
        }

        // Nothing open: talk to Lauda.
        var lauda = objects
            // Three Laudas stand on that spot (recordings 2026-10-24/25); only one is targetable — the one to talk to.
            .Where(o => o.ObjectKind == ObjectKind.EventNpc && o.IsTargetable && o.Name.TextValue == CrucibleLoopRules.EntryNpc)
            .OrderBy(o => Vector3.Distance(o.Position, player.Position))
            .FirstOrDefault();
        if (lauda == null || Vector3.Distance(lauda.Position, player.Position) > CrucibleLoopRules.TalkRange)
        {
            Status = $"Stand next to {CrucibleLoopRules.EntryNpc} to start the next run.";
            _nextStepUtc = now + TimeSpan.FromSeconds(2);
            return;
        }

        _entering = true;
        _boardHighlighted = false;
        _teamTried = false;
        _degreeSteps = 0;
        _teamWanted = null;
        TargetSystem.Instance()->InteractWithObject((GameObjectStruct*)lauda.Address, false);
        Progress(now, $"Talking to {CrucibleLoopRules.EntryNpc}.");
    }

    /// <summary>The degree the board layout is set to (0 Standard … 3 Third), or null when the agent is not there.</summary>
    public static int? SelectedDegree()
    {
        var module = AgentModule.Instance();
        var stage = module == null ? null : (AgentXBMStageDetailList*)module->GetAgentByInternalId(AgentId.XBMStageDetailList);
        return stage == null ? null : (int)stage->SelectedRank;
    }

    private void Progress(DateTime now, string status)
    {
        Status = status;
        _lastProgressUtc = now;
        _nextStepUtc = now + StepEvery;
    }

    private void Stop(ref int runsLeft, string status)
    {
        Status = status;
        _log("Crucible: " + status);
        runsLeft = 0;
        _entering = false;
    }
}
