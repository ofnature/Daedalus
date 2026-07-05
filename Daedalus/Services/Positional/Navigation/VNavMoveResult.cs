namespace Daedalus.Services.Positional.Navigation;

/// <summary>Result of queueing a vnavmesh movement request.</summary>
public enum VNavMoveResult
{
    Queued,
    Busy,
    NavmeshNotReady,
    PluginUnavailable,

    /// <summary>Denied by the <see cref="MovementArbiter"/> (yielded to BossMod or rate-limited).</summary>
    Suppressed,
}
