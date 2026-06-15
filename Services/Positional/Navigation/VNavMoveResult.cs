namespace Olympus.Services.Positional.Navigation;

/// <summary>Result of queueing a vnavmesh movement request.</summary>
public enum VNavMoveResult
{
    Queued,
    Busy,
    NavmeshNotReady,
    PluginUnavailable,
}
