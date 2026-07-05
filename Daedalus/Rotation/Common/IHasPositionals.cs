using Daedalus.Services.Positional;

namespace Daedalus.Rotation.Common;

/// <summary>
/// Snapshot of the player's current positional state relative to their target.
/// Updated once per frame by the rotation.
/// </summary>
public readonly struct PositionalSnapshot
{
    /// <summary>Player is at the target's rear (135–225°).</summary>
    public bool IsAtRear { get; init; }

    /// <summary>Player is at the target's flank (45–135° or 225–315°).</summary>
    public bool IsAtFlank { get; init; }

    /// <summary>Player is in front of the target.</summary>
    public bool IsAtFront => !IsAtRear && !IsAtFlank;

    /// <summary>Target has positional immunity — no positional bonuses apply.</summary>
    public bool TargetHasImmunity { get; init; }

    /// <summary>Player has an active target within melee range.</summary>
    public bool HasTarget { get; init; }

    /// <summary>
    /// The positional required by the next planned melee action (Rear, Flank, or null if no requirement).
    /// Set by the rotation when it knows what action comes next.
    /// </summary>
    public PositionalType? RequiredPositional { get; init; }

    /// <summary>
    /// Boundary-camping positional movement is live for this job (arc rollout enabled + positional
    /// movement on + auto-movement allowed + bias &gt; 0). When true, Auto-Manage BMR AI pushes
    /// DesiredPositional=Any so BMR keeps range while Daedalus owns the standing angle.
    /// </summary>
    public bool BoundaryCampingActive { get; init; }
}

/// <summary>
/// Implemented by rotations that track player positionals (melee DPS jobs).
/// Allows MainWindow and other UI to display the current positional without
/// depending on job-specific context types.
/// </summary>
public interface IHasPositionals
{
    /// <summary>
    /// Gets the current per-frame positional snapshot.
    /// </summary>
    PositionalSnapshot Positionals { get; }
}
