using System;
using System.Collections.Generic;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using Daedalus.Data;

namespace Daedalus.Services.Action;

/// <summary>
/// Reads the BLU active spell set via <c>ActionManager.GetActiveBlueMageActionInSlot</c>
/// (returns normal Action sheet ids; 0 = empty slot — same read RSR uses). Refreshes at
/// most once per second; the set only changes out of combat via the spellbook UI.
/// Also owns the loadout APPLY state machine: the game refuses <c>SetBlueMageActions</c> while
/// Aetheric Mimicry is active. Status-off paths don't work on it — the ONLY removal is the
/// targetless-cast trick (user-discovered 2026-07-12: casting Aetheric Mimicry with no target
/// strips the buff, since it cannot target self). A requested apply fires that removal, waits
/// for the strip, then applies; the rotation's auto-mimicry recasts afterwards.
/// </summary>
public sealed unsafe class BluLoadoutService : IBluLoadoutService
{
    /// <summary>The active BLU spell set is always 24 slots.</summary>
    public const int SlotCount = 24;

    private const double RefreshIntervalSeconds = 1.0;
    private const double ApplyTimeoutSeconds = 5.0;

    /// <summary>How long a pending apply waits for the user to drop Aetheric Mimicry.</summary>
    private const double MimicryWaitTimeoutSeconds = 30.0;

    private readonly Func<uint> jobIdProvider;
    private readonly Func<IReadOnlyCollection<uint>>? activeMimicryProvider;
    private readonly Func<bool>? mimicryRemovalInvoker;
    private readonly IPluginLog? log;
    private readonly HashSet<uint> slotted = new();
    private DateTime lastRefreshUtc = DateTime.MinValue;

    private uint[]? pendingApply;
    private DateTime pendingSinceUtc;
    private DateTime lastRemovalAttemptUtc = DateTime.MinValue;
    private const double RemovalRetrySeconds = 1.5; // the removal is a 1.0s cast — give it room

    public BluLoadoutService(
        Func<uint> jobIdProvider,
        Func<IReadOnlyCollection<uint>>? activeMimicryProvider = null,
        IPluginLog? log = null,
        Func<bool>? mimicryRemovalInvoker = null)
    {
        this.jobIdProvider = jobIdProvider;
        this.activeMimicryProvider = activeMimicryProvider;
        this.log = log;
        this.mimicryRemovalInvoker = mimicryRemovalInvoker;
    }

    public bool HasSlotData { get; private set; }

    public IReadOnlyCollection<uint> SlottedActionIds => slotted;

    public int SlottedCount => slotted.Count;

    public bool IsSlotted(uint actionId)
        => !HasSlotData || slotted.Contains(actionId);

    public bool IsApplyPending => pendingApply != null;

    public bool WaitingOnMimicry { get; private set; }

    public string? LastApplyResult { get; private set; }

    /// <inheritdoc />
    public void RequestApplyLoadout(uint[] slots)
    {
        if (slots.Length != SlotCount)
        {
            LastApplyResult = "Apply failed — bad slot array";
            return;
        }

        pendingApply = slots;
        pendingSinceUtc = DateTime.UtcNow;
        WaitingOnMimicry = false;
        LastApplyResult = null;
        log?.Information("[BLU] Loadout apply requested");
    }

    public void Update()
    {
        ProcessPendingApply(); // every frame — the apply handshake can't wait for the 1s throttle

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

    /// <summary>
    /// Drives a requested apply to completion. FIELD-VERIFIED 2026-07-11: Aetheric Mimicry CANNOT
    /// be cancelled programmatically — <c>StatusManager.ExecuteStatusOff</c> returns false and the
    /// game's own <c>/statusoff</c> silently no-ops on it (despite the Status sheet's
    /// CanStatusOff=true), and the game refuses <c>SetBlueMageActions</c> while it's active. Only
    /// a job change drops the buff. So: while mimicry is up we WAIT (30s window, clearly reported)
    /// for the user to drop it, then apply immediately; the rotation re-buffs afterwards.
    /// </summary>
    private void ProcessPendingApply()
    {
        if (pendingApply == null) return;

        var now = DateTime.UtcNow;
        var onBlu = jobIdProvider() == JobRegistry.BlueMage;

        if (!onBlu && !WaitingOnMimicry)
        {
            LastApplyResult = "Apply failed — not on BLU";
            pendingApply = null;
            return;
        }

        var mimicry = onBlu ? activeMimicryProvider?.Invoke() : null;
        if (!onBlu || mimicry is { Count: > 0 })
        {
            // Blocked by mimicry (or mid-job-swap). Remove it ourselves via the targetless-cast
            // trick (user-discovered: Aetheric Mimicry cast with NO target strips the buff since
            // it cannot target self), retrying until it strips; the 30s wait bounds everything.
            if (!WaitingOnMimicry)
            {
                WaitingOnMimicry = true;
                log?.Information("[BLU] Apply blocked by Aetheric Mimicry — removing it (targetless recast)");
            }

            if (onBlu && mimicryRemovalInvoker != null
                && (now - lastRemovalAttemptUtc).TotalSeconds >= RemovalRetrySeconds)
            {
                lastRemovalAttemptUtc = now;
                try { mimicryRemovalInvoker(); }
                catch (Exception ex) { log?.Warning(ex, "[BLU] Mimicry removal invoker threw"); }
            }

            if ((now - pendingSinceUtc).TotalSeconds > MimicryWaitTimeoutSeconds)
            {
                LastApplyResult = "Blocked by Aetheric Mimicry — the auto-removal never landed "
                                  + "(a quick job swap also drops the buff)";
                log?.Warning("[BLU] Loadout apply abandoned — mimicry never stripped");
                pendingApply = null;
                WaitingOnMimicry = false;
            }
            return;
        }

        if (WaitingOnMimicry)
        {
            // Mimicry just dropped and we're back on BLU — restart the apply window from here.
            WaitingOnMimicry = false;
            pendingSinceUtc = now;
        }

        if ((now - pendingSinceUtc).TotalSeconds > ApplyTimeoutSeconds)
        {
            LastApplyResult = "Apply timed out — set unchanged";
            log?.Warning("[BLU] Loadout apply timed out (the game kept refusing SetBlueMageActions)");
            pendingApply = null;
            return;
        }

        if (TryApplyLoadout(pendingApply))
        {
            var filled = 0;
            foreach (var s in pendingApply) if (s != 0) filled++;
            LastApplyResult = $"Applied {filled}/24 learned spells";
            log?.Information($"[BLU] Loadout applied ({filled}/24 slots); auto-mimicry will recast if enabled");
            pendingApply = null;
        }
        // else: retry until the timeout (transient refusals right after a job swap settle fast).
    }

    /// <inheritdoc />
    public bool TryApplyLoadout(uint[] slots)
    {
        if (slots.Length != SlotCount) return false;
        if (jobIdProvider() != JobRegistry.BlueMage) return false;

        try
        {
            var actionManager = ActionManager.Instance();
            if (actionManager == null) return false;

            bool applied;
            fixed (uint* ptr = slots)
            {
                applied = actionManager->SetBlueMageActions(ptr);
            }

            if (applied)
                lastRefreshUtc = DateTime.MinValue; // re-read the set on the next Update

            return applied;
        }
        catch
        {
            return false;
        }
    }
}
