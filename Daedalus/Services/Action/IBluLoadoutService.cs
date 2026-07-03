using System.Collections.Generic;

namespace Daedalus.Services.Action;

/// <summary>
/// Reads the Blue Mage ACTIVE spell set (the 24 slots). BLU availability is learned AND
/// slotted — a learned spell outside the active set cannot be cast. Fail-open: when slot
/// data is unavailable (not BLU, read failure), <see cref="IsSlotted"/> returns true so
/// the rotation degrades to the learned-only gating it had before.
/// </summary>
public interface IBluLoadoutService
{
    /// <summary>True when the active set was read successfully (player is BLU).</summary>
    bool HasSlotData { get; }

    /// <summary>Action ids currently slotted (empty when <see cref="HasSlotData"/> is false).</summary>
    IReadOnlyCollection<uint> SlottedActionIds { get; }

    /// <summary>Number of filled slots (of 24).</summary>
    int SlottedCount { get; }

    /// <summary>Is this action in the active set? Fail-open true when slot data is unavailable.</summary>
    bool IsSlotted(uint actionId);

    /// <summary>Per-frame update (internally throttled). Only reads while the player is BLU.</summary>
    void Update();
}
