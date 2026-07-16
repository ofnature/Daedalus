namespace Daedalus.Rotation.ProteusCore.Context;

/// <summary>
/// Debug state for the Blue Mage (Proteus) rotation.
/// </summary>
public sealed class ProteusDebugState
{
    public string PlanningState { get; set; } = "";
    public string PlannedAction { get; set; } = "";
    public string DamageState { get; set; } = "";
    public string BuffState { get; set; } = "";
    public string MitigationState { get; set; } = "";
    public string HealingState { get; set; } = "";

    /// <summary>e.g. "Tank (from Thancred's Avatar)" / "MISSING — no tank in range".</summary>
    public string MimicryState { get; set; } = "";

    /// <summary>Which mimicry buff is ACTIVE right now: "Tank"/"DPS"/"Healer"/"" — compared against
    /// <see cref="Role"/> in the debug tab; matching means no recast will be attempted.</summary>
    public string ActiveMimicry { get; set; } = "";

    /// <summary>Blocked mimicry targets currently on retry cooldown (cast failed to land).</summary>
    public string MimicryBlacklist { get; set; } = "";

    public string Role { get; set; } = "";

    /// <summary>Active spell-set summary: "22/24 slotted" or "no slot data".</summary>
    public string Loadout { get; set; } = "";

    /// <summary>Multi-BLU owner election summary ("" when single-BLU/solo — self owns everything).</summary>
    public string Coordination { get; set; } = "";

    public bool HasMightyGuard { get; set; }
    public int EngagedEnemies { get; set; }
    public int AoeRangeEnemies { get; set; }
    public string CurrentTarget { get; set; } = "";
}
