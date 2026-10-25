using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Daedalus.Data;
using Daedalus.Services.Positional.Navigation;
using Dalamud.Game.ClientState.Objects.SubKinds;

namespace Daedalus.Services.Beastmaster;

/// <summary>
/// Walks the Crucible board from space to space — the board is walked, not clicked, and a fork is just which way you
/// go. Positions come from the board's layout (<see cref="CrucibleBoardLayout"/>); the next space and the fork side
/// from <see cref="CrucibleWalkPlan"/> and <see cref="CrucibleRoutePlan.SuggestedSide"/>. It moves only on the strip,
/// out of combat, with no board screen open, and pauses after reaching a space so its screen can open. Movement goes
/// through the <see cref="IMovementArbiter"/> (vnavmesh), so it yields to the boss engine like every other move.
/// </summary>
public sealed class CrucibleWalker
{
    private static readonly TimeSpan MoveEvery = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan HoldAfterArriving = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan HoldAfterScreen = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan StuckAfter = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan RetryAfter = TimeSpan.FromSeconds(5);
    private const int MaxAttempts = 3;
    private const float ArrivedYalms = 0.5f;

    /// <summary>Every window a space or the board can open; while any is up, nobody walks.</summary>
    private static readonly string[] Screens =
    [
        "XBMStageDetailList", "XBMPetParty", "XBMContentsBooty", "XBMContentsTreasure", "XBMContentsItemShop",
        "XBMResult", "XBMBattleMonsterDetail", "SelectYesno", "SelectString", "SelectIconString",
    ];

    private readonly CrucibleIndex _index;
    private readonly IMovementArbiter _move;
    private readonly Action<string> _log;
    private readonly HashSet<int> _flipped = new();
    private CrucibleStage? _target;
    private int _lastMove = -1;
    private DateTime _holdUntilUtc;
    private DateTime _nextMoveUtc;
    private DateTime _progressUtc;
    private float _bestDistance;
    private bool _moving;
    private bool _stuck;
    private int _attempts;
    private VNavMoveResult _lastResult = VNavMoveResult.Queued;

    public CrucibleWalker(CrucibleIndex index, IMovementArbiter move, Action<string> log)
    {
        _index = index;
        _move = move;
        _log = log;
    }

    /// <summary>What it did last, for the team window.</summary>
    public string Status { get; private set; } = "";

    /// <summary>Framework thread, every frame.</summary>
    /// <param name="busy">In combat, casting, between areas or in an event — never walk then.</param>
    public void Update(bool enabled, CrucibleBoard? board, IPlayerCharacter? player, bool busy, Func<int, bool> isCaptured)
    {
        if (!enabled || board == null || player == null)
        {
            Halt();
            if (board == null)
                _lastMove = -1;
            return;
        }

        var now = DateTime.UtcNow;
        if (_moving && now >= _nextMoveUtc && !_move.IsPathRunning && !_move.IsPathfindInProgress)
            _moving = false; // our path finished on its own

        var screenOpen = Screens.Any(IsOpen);
        CheckForkSides(board, screenOpen);
        if (busy || screenOpen)
        {
            Halt();
            if (screenOpen)
                _holdUntilUtc = now + HoldAfterScreen;
            return;
        }

        var route = board.Route;
        var position = player.Position;
        if (!CrucibleWalkPlan.OnStrip(route, position))
        {
            Halt();
            return;
        }

        // Reaching a space: give its screen time to open before moving on.
        var move = CrucibleWalkPlan.CurrentMove(route, position);
        if (move != _lastMove)
        {
            _lastMove = move;
            _holdUntilUtc = now + HoldAfterArriving;
            Halt();
        }

        if (now < _holdUntilUtc)
            return;

        var next = CrucibleWalkPlan.Next(route, position, fork => CrucibleRoutePlan.SuggestedSide(_index, board, fork, isCaptured));
        if (next == null)
        {
            Halt();
            Status = route.Any(s => s.At != null) ? "Board walked." : "This board's spaces could not be placed — walk it yourself.";
            return;
        }

        var at = TargetPosition(board, route, next);
        var distance = Vector2.Distance(new Vector2(position.X, position.Z), new Vector2(at.X, at.Z));
        if (next != _target)
        {
            _target = next;
            _bestDistance = distance;
            _progressUtc = now;
            _stuck = false;
            _attempts = 0;
        }
        else if (distance < _bestDistance - 0.3f || _lastResult != VNavMoveResult.Queued)
        {
            // Closer, or not actually trying (navmesh building, held for the boss engine): not stuck.
            _bestDistance = Math.Min(_bestDistance, distance);
            _progressUtc = now;
        }

        if (distance <= ArrivedYalms || _stuck)
            return;

        if (now - _progressUtc > StuckAfter)
        {
            _attempts++;
            Halt();
            var snapshot = _move.Snapshot;
            _log($"Crucible: no progress towards move {next.Move}{next.SideLabel} for {StuckAfter.TotalSeconds:0} s "
                 + $"(try {_attempts}/{MaxAttempts}; {distance:F1}y left; last path request {_lastResult}; path running {_move.IsPathRunning}; "
                 + $"movement owner {snapshot.Owner}, held: {snapshot.Suppression}).");
            if (_attempts >= MaxAttempts)
            {
                _stuck = true;
                Status = $"Stuck on the way to move {next.Move}{next.SideLabel} — walk it yourself.";
                return;
            }

            _progressUtc = now;
            _nextMoveUtc = now + RetryAfter;
            Status = $"No progress towards move {next.Move}{next.SideLabel} — trying again…";
            return;
        }

        if (_move.IsPathRunning || _move.IsPathfindInProgress || now < _nextMoveUtc)
            return;

        _nextMoveUtc = now + MoveEvery;
        // Positional intent: like a flank hop, walking the board is let through while the boss engine merely steers
        // (its AI holds position on the board, which as a MaxMelee move refused every step — field 2026-10-25, the
        // player walked the whole board by hand) but never through real zone or damage danger. BossMod defers to a
        // running vnav path.
        var result = _move.PathfindAndMoveCloseTo(at, 0.3f, MovementIntent.PositionalArc);
        if (result != _lastResult)
        {
            var snapshot = _move.Snapshot;
            _log($"Crucible: walk to move {next.Move}{next.SideLabel} — {result}"
                 + (result == VNavMoveResult.Queued ? "" : $" (owner {snapshot.Owner}, held: {snapshot.Suppression})"));
        }

        _lastResult = result;
        _moving = result == VNavMoveResult.Queued;
        Status = result switch
        {
            VNavMoveResult.Queued => $"Walking to move {next.Move}{next.SideLabel}: {Describe(next)}.",
            VNavMoveResult.NavmeshNotReady => "Waiting for vnavmesh to build this board…",
            VNavMoveResult.PluginUnavailable => "vnavmesh is not installed — walk the board yourself.",
            VNavMoveResult.Suppressed => "Movement held for the boss engine…",
            _ => Status,
        };
    }

    /// <summary>
    /// After walking to one side of a battle fork, the battle that opened should be that side's. If it is the other
    /// side's, this board's left and right were read the wrong way round: swap them for the rest of the session.
    /// </summary>
    private void CheckForkSides(CrucibleBoard board, bool screenOpen)
    {
        if (!screenOpen || _target is not { IsBattle: true, Side: > 0 } walkedTo || _flipped.Contains(board.Index))
            return;
        if (CrucibleHornSetter.UpcomingBattles() is not [var shown] || shown == walkedTo.Battle)
            return;
        if (CrucibleWalkPlan.Sibling(board.Route, walkedTo) is { IsBattle: true } other && other.Battle == shown)
        {
            _flipped.Add(board.Index);
            _log($"Crucible: {board.Name} — walked to move {walkedTo.Move}{walkedTo.SideLabel} but {other.SideLabel}'s battle opened; "
                 + "swapping left and right for this board.");
        }
    }

    private Vector3 TargetPosition(CrucibleBoard board, IReadOnlyList<CrucibleStage> route, CrucibleStage stage)
    {
        if (_flipped.Contains(board.Index) && CrucibleWalkPlan.Sibling(route, stage) is { At: { } swapped })
            return swapped;
        return stage.At!.Value;
    }

    private string Describe(CrucibleStage stage) => stage.Type switch
    {
        CrucibleStageType.Enemy or CrucibleStageType.Elite or CrucibleStageType.Boss =>
            $"{stage.Type.ToString().ToLowerInvariant()} {_index.EnemiesInBattle(stage.Battle).FirstOrDefault()?.Name ?? ""}".Trim(),
        _ => stage.Type.ToString().ToLowerInvariant(),
    };

    private void Halt()
    {
        if (!_moving)
            return;
        _move.Stop();
        _moving = false;
    }

    private static unsafe bool IsOpen(string name) => CrucibleAddons.Visible(name) != null;
}
