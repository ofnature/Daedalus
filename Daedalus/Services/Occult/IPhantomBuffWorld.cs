using Daedalus.Data;

namespace Daedalus.Services.Occult;

/// <summary>
/// Everything the buff cycle needs from the game, behind one seam so the state machine itself is
/// testable without Dalamud. Mirrors the pattern used for the rotation scheduler's world access.
/// </summary>
public interface IPhantomBuffWorld
{
    /// <summary>Phantom job currently equipped (<see cref="PhantomJob.None"/> when unreadable).</summary>
    PhantomJob ActiveJob { get; }

    /// <summary>Per-job phantom levels; 0 or missing means locked.</summary>
    System.Collections.Generic.IReadOnlyDictionary<PhantomJob, byte> JobLevels { get; }

    /// <summary>In an Occult zone at all.</summary>
    bool InOccultZone { get; }

    /// <summary>In combat — the cycle refuses to start and aborts if it begins.</summary>
    bool InCombat { get; }

    /// <summary>
    /// A Knowledge Crystal is within interaction range. NOT required to collect a buff for
    /// yourself; it is what makes the cast broadcast to the party across the zone.
    /// </summary>
    bool NearKnowledgeCrystal { get; }

    /// <summary>
    /// Switch support job. Returns the game's own success flag — <c>ChangeSupportJob</c> reports
    /// failure rather than failing silently, so a false here is real and worth surfacing.
    /// </summary>
    bool ChangeSupportJob(PhantomJob job);

    /// <summary>
    /// The game's verdict on whether this action can be used right now
    /// (<c>GetActionStatus == 0</c>) — covers level, learned, cooldown AND duty-bar slotting in
    /// one call, so the cycle does not reimplement any of them.
    /// </summary>
    bool CanCast(uint actionId);

    /// <summary>Fire the action at self. Returns whether the game accepted the submit.</summary>
    bool Cast(uint actionId, string actionName);

    /// <summary>Seconds left on a status on the player; 0 when absent.</summary>
    float StatusRemaining(uint statusId);
}
