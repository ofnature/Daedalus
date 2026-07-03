using System;
using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Client.Game;
using Daedalus.Data;

namespace Daedalus.Services.Action;

/// <summary>
/// Reads the BLU active spell set via <c>ActionManager.GetActiveBlueMageActionInSlot</c>
/// (returns normal Action sheet ids; 0 = empty slot — same read RSR uses). Refreshes at
/// most once per second; the set only changes out of combat via the spellbook UI.
/// </summary>
public sealed unsafe class BluLoadoutService : IBluLoadoutService
{
    /// <summary>The active BLU spell set is always 24 slots.</summary>
    public const int SlotCount = 24;

    private const double RefreshIntervalSeconds = 1.0;

    private readonly Func<uint> jobIdProvider;
    private readonly HashSet<uint> slotted = new();
    private DateTime lastRefreshUtc = DateTime.MinValue;

    public BluLoadoutService(Func<uint> jobIdProvider)
    {
        this.jobIdProvider = jobIdProvider;
    }

    public bool HasSlotData { get; private set; }

    public IReadOnlyCollection<uint> SlottedActionIds => slotted;

    public int SlottedCount => slotted.Count;

    public bool IsSlotted(uint actionId)
        => !HasSlotData || slotted.Contains(actionId);

    public void Update()
    {
        var now = DateTime.UtcNow;
        if ((now - lastRefreshUtc).TotalSeconds < RefreshIntervalSeconds)
            return;
        lastRefreshUtc = now;

        if (jobIdProvider() != JobRegistry.BlueMage)
        {
            HasSlotData = false;
            slotted.Clear();
            return;
        }

        try
        {
            var actionManager = ActionManager.Instance();
            if (actionManager == null)
            {
                HasSlotData = false;
                return;
            }

            slotted.Clear();
            for (var i = 0; i < SlotCount; i++)
            {
                var actionId = actionManager->GetActiveBlueMageActionInSlot(i);
                if (actionId != 0)
                    slotted.Add(actionId);
            }

            HasSlotData = true;
        }
        catch
        {
            // Fail-open — rotation falls back to learned-only gating.
            HasSlotData = false;
            slotted.Clear();
        }
    }
}
