using Daedalus.Rotation.Common;

namespace Daedalus.Rotation.HephaestusCore.Context;

/// <summary>
/// Debug state for Gunbreaker (Hephaestus) rotation.
/// Tracks rotation decisions and state for debug display.
/// </summary>
public sealed class HephaestusDebugState : IEnemyPackDebug
{
    // Module states
    public string DamageState { get; set; } = "";
    public string MitigationState { get; set; } = "";
    public string BuffState { get; set; } = "";
    public string EnmityState { get; set; } = "";

    // Current action planning
    public string PlannedAction { get; set; } = "";
    public string PlanningState { get; set; } = "";

    /// <summary>Non-empty when the whole rotation is globally paused this frame; explains why it's idle.</summary>
    public string PauseReason { get; set; } = "";

    // Combo tracking
    public int ComboStep { get; set; }
    public string LastComboAction { get; set; } = "";
    public float ComboTimeRemaining { get; set; }

    // Cartridge resource
    public int Cartridges { get; set; }

    // Gnashing Fang combo state
    public int GnashingFangStep { get; set; }
    public bool IsInGnashingFangCombo { get; set; }

    // Reign of Beasts combo state
    public int ReignComboStep { get; set; }
    public bool IsInReignCombo { get; set; }

    // Continuation ready states
    public bool IsReadyToRip { get; set; }
    public bool IsReadyToTear { get; set; }
    public bool IsReadyToGouge { get; set; }
    public bool IsReadyToBlast { get; set; }
    public bool IsReadyToBrand { get; set; }
    public bool IsReadyToReign { get; set; }

    // Tank stance
    public bool HasRoyalGuard { get; set; }

    // Buff tracking
    public bool HasNoMercy { get; set; }
    public float NoMercyRemaining { get; set; }

    /// <summary>Live Bloodfest readiness diagnostic (ready / cooldown / action-status) — to debug the
    /// repeated-dispatch ("rejected") behaviour where its 60s cooldown isn't being respected.</summary>
    public string BloodfestDiag { get; set; } = "";

    // Defensive tracking
    public bool HasActiveMitigation { get; set; }
    public string ActiveMitigations { get; set; } = "";
    public bool HasSuperbolide { get; set; }
    public bool HasNebula { get; set; }
    public bool HasHeartOfCorundum { get; set; }
    public bool HasCamouflage { get; set; }
    public bool HasAurora { get; set; }

    // DoT tracking
    public bool HasSonicBreakDot { get; set; }
    public bool HasBowShockDot { get; set; }

    // Enmity tracking
    public bool IsMainTank { get; set; }
    public string CurrentTarget { get; set; } = "";

    // Targeting
    public int EngagedEnemies { get; set; }
    public int AoeRangeEnemies { get; set; }
    public int NearbyEnemies
    {
        get => AoeRangeEnemies;
        set => AoeRangeEnemies = value;
    }
}
