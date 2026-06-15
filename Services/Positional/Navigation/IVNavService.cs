using System.Numerics;

namespace Olympus.Services.Positional.Navigation;

/// <summary>
/// vnavmesh IPC adapter. Executor only — no mechanic or job awareness.
/// </summary>
public interface IVNavService
{
    /// <summary>vnavmesh plugin is installed and loaded.</summary>
    bool IsAvailable { get; }

    /// <summary><c>vnavmesh.Nav.IsReady</c> — navmesh loaded for current zone.</summary>
    bool IsNavReady { get; }

    /// <summary><c>vnavmesh.Path.IsRunning</c>.</summary>
    bool IsPathRunning { get; }

    /// <summary><c>vnavmesh.SimpleMove.PathfindInProgress</c>.</summary>
    bool IsPathfindInProgress { get; }

    /// <summary><c>vnavmesh.SimpleMove.PathfindAndMoveTo</c>.</summary>
    VNavMoveResult PathfindAndMoveTo(Vector3 destination, bool fly = false);

    /// <summary><c>vnavmesh.SimpleMove.PathfindAndMoveCloseTo</c>.</summary>
    VNavMoveResult PathfindAndMoveCloseTo(Vector3 destination, float toleranceYalms, bool fly = false);

    /// <summary><c>vnavmesh.Path.Stop</c>.</summary>
    void Stop();

    /// <summary><c>vnavmesh.Query.Mesh.PointOnFloor</c> when available; otherwise <paramref name="position"/>.</summary>
    Vector3 SnapToFloor(Vector3 position);
}
