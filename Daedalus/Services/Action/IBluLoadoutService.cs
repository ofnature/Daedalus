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

    /// <summary>
    /// Replaces the active 24-slot spell set via the game's own <c>SetBlueMageActions</c> (the
    /// same call the spellbook UI's Load button uses — no packet forgery). <paramref name="slots"/>
    /// must be exactly 24 entries, 0 = empty slot, unlearned ids rejected by the game. FAILS while
    /// Aetheric Mimicry is active — use <see cref="RequestApplyLoadout"/> for the full handshake.
    /// CALLER gates context: out of combat, outside instanced duties, player is BLU.
    /// </summary>
    bool TryApplyLoadout(uint[] slots);

    /// <summary>
    /// Queues a loadout apply. Aetheric Mimicry blocks set changes; status-off paths don't work
    /// on it, so the apply removes it via the TARGETLESS-CAST trick (casting mimicry with no
    /// target strips the buff — it cannot target self), waits for the strip, then fires (30s
    /// bound; a job swap also works as manual fallback). Progress is driven by
    /// <see cref="Update"/>; result lands in <see cref="LastApplyResult"/>. Auto-mimicry holds
    /// while <see cref="IsApplyPending"/> and recasts the buff afterwards.
    /// </summary>
    void RequestApplyLoadout(uint[] slots);

    /// <summary>A requested apply is still in flight (waiting on mimicry / retrying).</summary>
    bool IsApplyPending { get; }

    /// <summary>The pending apply is blocked on Aetheric Mimicry (auto-removal in progress).</summary>
    bool WaitingOnMimicry { get; }

    /// <summary>Human-readable outcome of the last requested apply, or null while none/pending.</summary>
    string? LastApplyResult { get; }
}
