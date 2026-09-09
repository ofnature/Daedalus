using Daedalus.Rotation.Common;

namespace Daedalus.Rotation.ArtemisCore.Context;

/// <summary>
/// Debug state for the Beastmaster (Artemis) rotation.
/// <para>
/// There is no TP readout here on purpose: the Beastmaster gauge is not in ClientStructs, and the
/// only route to the number is scraping the JobHudXBM0 nodes. The rotation gates on action status
/// codes instead, so <see cref="InstinctState"/> reports what the game allowed rather than a TP
/// value we would have had to guess at.
/// </para>
/// </summary>
public sealed class ArtemisDebugState : IEnemyPackDebug
{
    public string DamageState { get; set; } = "";
    public string PlannedAction { get; set; } = "";
    public string PlanningState { get; set; } = "";

    /// <summary>What the instinct picker decided, and why.</summary>
    public string InstinctState { get; set; } = "";

    /// <summary>The combo clock readout — last affinity, wanted affinity, seconds left.</summary>
    public string InstinctChain { get; set; } = "";

    public int ComboStep { get; set; }
    public string CurrentTarget { get; set; } = "None";

    public int EngagedEnemies { get; set; }
    public int AoeRangeEnemies { get; set; }
}
