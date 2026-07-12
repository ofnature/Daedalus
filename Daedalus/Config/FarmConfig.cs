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

    // ---- v4: mounted travel + wide acquisition (docs/farm-mode.md §v4) ----

    /// <summary>Mount Roulette (default) or a specific unlocked mount.</summary>
    public FarmMountMode MountMode { get; set; } = FarmMountMode.Roulette;

    /// <summary>Mount sheet row id when <see cref="MountMode"/> is Specific. 0 = none picked.</summary>
    public uint SpecificMountId { get; set; }

    /// <summary>Travel legs longer than this mount up first; shorter legs just walk (yalms).</summary>
    public float MountDistanceThresholdYalms { get; set; } = 40f;

    /// <summary>Edge distance from the target mob at which to land/dismount before engaging (yalms).</summary>
    public float DismountRangeYalms { get; set; } = 15f;

    /// <summary>
    /// Mob acquisition scan radius around the player (yalms). The object table reliably covers
    /// ~100y in the open world — beyond that entries pop in and out, so 100 is the hard cap.
    /// </summary>
    public float ScanRadiusYalms { get; set; } = 50f;

    /// <summary>Fly between spots/mobs when mounted and the zone allows it.</summary>
    public bool FlyWhenPossible { get; set; } = true;

    /// <summary>
    /// Saved farm profiles — persistence is wired for later; v1 has no UI for these on purpose
    /// (the user wants the working list temp-only for now).
    /// </summary>
    public List<SavedFarmProfile> SavedProfiles { get; set; } = new();
}

/// <summary>How farm mode picks a mount for spot-to-spot travel.</summary>
public enum FarmMountMode
{
    /// <summary>Mount Roulette (General Action 9) — always available once any mount is owned.</summary>
    Roulette = 0,

    /// <summary>A specific mount by id; falls back to Roulette when it is not unlocked.</summary>
    Specific = 1,
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
