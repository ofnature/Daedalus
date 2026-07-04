using System.Collections.Generic;
using System.Numerics;

namespace Daedalus.Config;

/// <summary>
/// Farm mode settings. The WORKING farm profile is deliberately session-only (runtime state in
/// <c>FarmModeService</c>, lost on reload/logout); only the button visibility, tuning defaults,
/// and the future saved-profile store persist here. See docs/farm-mode.md for the full design.
/// </summary>
public sealed class FarmConfig
{
    /// <summary>Show the Farm button on the main window (Settings → General → Window Behavior).</summary>
    public bool ShowFarmButton { get; set; } = false;

    /// <summary>How far from the active spot mobs may be pulled (yalms).</summary>
    public float DefaultLeashRadiusYalms { get; set; } = 60f;

    /// <summary>Seconds to wait at a spot with nothing to kill before roaming to the next spot.</summary>
    public int RespawnWaitSeconds { get; set; } = 12;

    /// <summary>
    /// Saved farm profiles — persistence is wired for later; v1 has no UI for these on purpose
    /// (the user wants the working list temp-only for now).
    /// </summary>
    public List<SavedFarmProfile> SavedProfiles { get; set; } = new();
}

/// <summary>Serializable snapshot of a farm profile for the future save/load UI.</summary>
public sealed class SavedFarmProfile
{
    public string Name { get; set; } = "";
    public uint ItemId { get; set; }
    public string ItemName { get; set; } = "";
    public int TargetCount { get; set; }
    public List<uint> MobNameIds { get; set; } = new();
    public List<string> MobNames { get; set; } = new();
    public ushort TerritoryId { get; set; }
    public List<Vector3> Spots { get; set; } = new();
    public float LeashRadiusYalms { get; set; } = 60f;
}
