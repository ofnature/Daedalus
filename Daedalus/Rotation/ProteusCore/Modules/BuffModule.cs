using Daedalus.Config.DPS;
using Daedalus.Rotation.Common.Helpers;
using Daedalus.Rotation.Common.Scheduling;
using Daedalus.Rotation.ProteusCore.Abilities;
using Daedalus.Rotation.ProteusCore.Context;
using Daedalus.Rotation.ProteusCore.Helpers;

namespace Daedalus.Rotation.ProteusCore.Modules;

/// <summary>
/// Role upkeep: Aetheric Mimicry (matching the role dropdown) and Mighty Guard stance for the tank
/// role. Both are GCDs and both apply OUT of combat too (duty pop / between pulls — same rule as
/// the tank stance-on-pop work): the GCD dispatcher is not combat-gated, so candidates pushed here
/// fire while idle in a duty.
/// </summary>
public sealed class BuffModule : IProteusModule
{
    public int Priority => 20;
    public string Name => "Buff";

    public bool TryExecute(IProteusContext context, bool isMoving) => false;
    public void UpdateDebugState(IProteusContext context) { }

    public void CollectCandidates(IProteusContext context, RotationScheduler scheduler, bool isMoving)
    {
        if (context.HasDiamondback) return; // locked in the shell — nothing is castable
        if (context.HasWaningNocturne) return; // Moon Flute hangover — nothing is castable

        // Mimicry applies out of combat OUTSIDE duties only — in an all-BLU party the tank/healer
        // archetype can only be sourced from a real player nearby (grabbed in town before queuing;
        // the buff survives zoning), and once inside an instance jobs are locked so there is nothing
        // to fix. Mimicry Helper parity.
        TryPushMimicry(context, scheduler, isMoving);

        // Self buffs in combat or inside duties — no overworld auto-cast surprises. EXCEPT the
        // SOLO role: picking it IS the explicit opt-in, so Basic Instinct + Mighty Guard (+ Toad
        // Oil) go up immediately in the overworld instead of eating the first pull's GCDs.
        if (context.InCombat || PlayerSafetyHelper.IsInInstancedDuty()
            || context.Role == Daedalus.Config.DPS.BluRole.Solo)
        {
            TryPushMightyGuard(context, scheduler, isMoving);
            TryPushBasicInstinct(context, scheduler, isMoving);
            TryPushToadOil(context, scheduler, isMoving);
        }
        else
        {
            context.Debug.BuffState = "Not in combat";
        }
    }

    // Self-buff status-latency latch: a 2.0s cast's status appears ~0.2-0.5s AFTER the cast ends
    // (server round trip), so the very next GCD decision still reads "buff missing" and re-casts
    // (field: Toad Oil ×2 with no movement, twice). One dispatch per buff per window; a genuinely
    // interrupted cast retries after the window (status still absent then).
    private readonly System.Collections.Generic.Dictionary<uint, System.DateTime> _selfBuffCastUtc = new();
    private const float SelfBuffRelatchSeconds = 5f;

    private bool SelfBuffRecentlyCast(uint actionId)
        => _selfBuffCastUtc.TryGetValue(actionId, out var at)
           && (System.DateTime.UtcNow - at).TotalSeconds < SelfBuffRelatchSeconds;

    private void MarkSelfBuffCast(uint actionId)
        => _selfBuffCastUtc[actionId] = System.DateTime.UtcNow;

    // Retry state: if a chosen mimicry target doesn't yield the buff within the grace window
    // (cast blocked — LoS, untargetable, cutscene-adjacent...), blacklist them briefly and scan
    // for a DIFFERENT character instead of hammering the same one forever.
    private ulong _mimicryTargetId;
    private System.DateTime _mimicryTargetSince;
    private readonly System.Collections.Generic.Dictionary<ulong, System.DateTime> _mimicryBlacklist = new();
    private const float MimicryGraceSeconds = 4f;
    private const float MimicryBlacklistSeconds = 60f;

    private void TryPushMimicry(IProteusContext context, RotationScheduler scheduler, bool isMoving)
    {
        // Manual role request from the Mimicry window: bypasses the auto toggle AND the config
        // role — a fresh cast on a different-role target OVERWRITES the current buff, which is
        // the only supported way to change it (the buff cannot be cancelled).
        var manualRole = BluMimicryCommand.GetPending();
        if (!context.Configuration.BlueMage.EnableMimicry && manualRole == null) return;

        // A manual removal just happened — hold the auto recast so it sticks (manual requests
        // still pass; picking a role button ends the suppression naturally).
        if (manualRole == null && BluMimicryCommand.AutoSuppressed)
        {
            context.Debug.MimicryState = "Auto-mimicry paused (removed manually)";
            return;
        }

        // A loadout apply is in flight: it needs mimicry GONE (the game refuses set changes
        // while it's up) — recasting now would deadlock the handshake. Hold; the normal path
        // recasts the buff the moment the apply completes.
        if (context.LoadoutService is { IsApplyPending: true })
        {
            context.Debug.MimicryState = "Holding (loadout apply in progress)";
            return;
        }

        // Not learned / not slotted: without this gate the cast never lands, the 4s grace window
        // expires, and the scan BLACKLISTS the (innocent) target — cycling through every valid ally.
        if (!context.IsSpellUsable(Daedalus.Data.BLUActions.AethericMimicry.ActionId))
        {
            context.Debug.MimicryState = "Mimicry not slotted";
            return;
        }

        var desiredRole = manualRole ?? context.Role;
        var desiredStatus = ProteusStatusHelper.MimicryStatusFor(desiredRole);

        // Desired buff already active -> nothing to do (never recast a matching mimicry).
        if (Daedalus.Rotation.Common.Helpers.BaseStatusHelper.HasStatus(context.Player, desiredStatus))
        {
            if (manualRole != null) BluMimicryCommand.Clear();
            context.Debug.MimicryState = $"{desiredRole} (active)";
            _mimicryTargetId = 0;
            if (_mimicryBlacklist.Count > 0) _mimicryBlacklist.Clear();
            context.Debug.MimicryBlacklist = "";
            return;
        }

        // AUTO casts never happen inside a dungeon/trial/raid (all-BLU comps have nothing to
        // copy; grab it before queuing). A MANUAL button press is user judgment — in a mixed
        // real-player party there can be a valid source mid-duty, so let it through.
        if (manualRole == null && PlayerSafetyHelper.IsInInstancedDuty())
        {
            context.Debug.MimicryState =
                $"MISSING {context.Role} — grab mimicry BEFORE queuing (locked in-duty)";
            return;
        }

        // 1.0s cast — hold while moving rather than slide-walking a cancel.
        if (isMoving) return;

        var now = System.DateTime.UtcNow;

        // Expire old blacklist entries; give up on the current target if the grace window passed
        // without the buff appearing.
        var expired = new System.Collections.Generic.List<ulong>();
        foreach (var (id, until) in _mimicryBlacklist)
            if (now >= until) expired.Add(id);
        foreach (var id in expired)
            _mimicryBlacklist.Remove(id);

        if (_mimicryTargetId != 0 && (now - _mimicryTargetSince).TotalSeconds > MimicryGraceSeconds)
        {
            _mimicryBlacklist[_mimicryTargetId] = now.AddSeconds(MimicryBlacklistSeconds);
            _mimicryTargetId = 0;
        }
        context.Debug.MimicryBlacklist = _mimicryBlacklist.Count == 0 ? "" : $"{_mimicryBlacklist.Count} blocked target(s)";

        var exclude = _mimicryBlacklist.Count > 0
            ? new System.Collections.Generic.HashSet<ulong>(_mimicryBlacklist.Keys)
            : null;
        var ally = MimicryScanHelper.FindArchetypeAlly(
            context.PartyHelper, context.PartyList, context.ObjectTable, context.Player, desiredRole, exclude);
        if (ally == null)
        {
            // Fail-open: no (non-blacklisted) archetype in range — play on, surface the miss.
            context.Debug.MimicryState = _mimicryBlacklist.Count > 0
                ? $"MISSING — no other {desiredRole} in range ({_mimicryBlacklist.Count} blocked)"
                : $"MISSING — no {desiredRole} in range";
            return;
        }

        if (ally.GameObjectId != _mimicryTargetId)
        {
            _mimicryTargetId = ally.GameObjectId;
            _mimicryTargetSince = now;
        }

        var capturedAlly = ally;
        var capturedRole = desiredRole;
        scheduler.PushGcd(ProteusAbilities.AethericMimicry, ally.GameObjectId, priority: 4,
            onDispatched: _ =>
            {
                context.Debug.PlannedAction = Daedalus.Data.BLUActions.AethericMimicry.Name;
                context.Debug.MimicryState = $"{capturedRole} (from {capturedAlly.Name?.TextValue ?? "ally"})";
            });
    }

    /// <summary>
    /// Basic Instinct: +100% damage while SOLO (no other party members) — permanent until someone
    /// joins. The game itself refuses the cast when a party is present, so the party gate here is
    /// about not wasting candidate pushes, not safety.
    /// </summary>
    private void TryPushBasicInstinct(IProteusContext context, RotationScheduler scheduler, bool isMoving)
    {
        if (!context.Configuration.BlueMage.EnableBasicInstinct) return;
        if (context.HasBasicInstinctBuff) return;
        if (!context.IsSpellUsable(Daedalus.Data.BLUActions.BasicInstinct.ActionId)) return;

        // FIELD-VERIFIED 2026-07-12: Basic Instinct is DUTY-ONLY — the game refuses it in the
        // open world (RSR's IsInDuty gate was right). Solo overworld runs WITHOUT it (and thus
        // without Mighty Guard, which waits for it); unsynced dungeon farming is where it shines.
        if (!PlayerSafetyHelper.IsInInstancedDuty())
        {
            if (context.Role == Daedalus.Config.DPS.BluRole.Solo)
                context.Debug.BuffState = "Basic Instinct: duty only — overworld runs without it";
            return;
        }

        if (context.PartyList.Length > 0)
        {
            // Deliberate: the game refuses Basic Instinct with party members. Say so — the first
            // field run left the user wondering why it never fired (boxes were partied).
            context.Debug.BuffState = "Basic Instinct/Toad Oil: held (party present)";
            return;
        }
        if (isMoving) return; // 2.0s cast
        if (SelfBuffRecentlyCast(Daedalus.Data.BLUActions.BasicInstinct.ActionId)) return; // status latency

        scheduler.PushGcd(ProteusAbilities.BasicInstinct, context.Player.GameObjectId, priority: 5,
            onDispatched: _ =>
            {
                MarkSelfBuffCast(Daedalus.Data.BLUActions.BasicInstinct.ActionId);
                context.Debug.PlannedAction = Daedalus.Data.BLUActions.BasicInstinct.Name;
                context.Debug.BuffState = "Basic Instinct (solo +100%)";
            });
    }

    /// <summary>Toad Oil (+20% evasion, 180s): survivability upkeep for tank role and solo play.
    /// COMBAT/DUTY only — pre-buffing it in the open world reads as a wasted cast (user call).</summary>
    private void TryPushToadOil(IProteusContext context, RotationScheduler scheduler, bool isMoving)
    {
        if (!context.Configuration.BlueMage.EnableToadOil) return;
        if (!context.InCombat && !PlayerSafetyHelper.IsInInstancedDuty()) return;
        if (context.HasToadOil) return;
        if (!context.IsSpellUsable(Daedalus.Data.BLUActions.ToadOil.ActionId)) return;
        var solo = context.PartyList.Length == 0;
        if (context.Role != Daedalus.Config.DPS.BluRole.Tank
            && context.Role != Daedalus.Config.DPS.BluRole.Solo
            && !solo) return;
        if (isMoving) return; // 2.0s cast
        if (SelfBuffRecentlyCast(Daedalus.Data.BLUActions.ToadOil.ActionId)) return; // status latency

        scheduler.PushGcd(ProteusAbilities.ToadOil, context.Player.GameObjectId, priority: 6,
            onDispatched: _ =>
            {
                MarkSelfBuffCast(Daedalus.Data.BLUActions.ToadOil.ActionId);
                context.Debug.PlannedAction = Daedalus.Data.BLUActions.ToadOil.Name;
                context.Debug.BuffState = "Toad Oil (evasion)";
            });
    }

    private void TryPushMightyGuard(IProteusContext context, RotationScheduler scheduler, bool isMoving)
    {
        if (!context.Configuration.BlueMage.EnableMightyGuard) return;
        if (!context.IsSpellUsable(Daedalus.Data.BLUActions.MightyGuard.ActionId))
        {
            if (context.Role == BluRole.Tank)
                context.Debug.BuffState = "Mighty Guard not slotted";
            return;
        }
        // Mighty Guard is a toggle: maintain it in tank role, drop it when the role changes
        // (its -40% damage dealt is pure loss outside tank duty). SOLO role wants it too — but
        // only AFTER Basic Instinct is up (BI's +100% cancels the penalty; MG before BI would
        // gut damage for nothing). BI (priority 5) casts first, then MG (priority 3) follows.
        var wantStance = context.Role == BluRole.Tank
                         || (context.Role == BluRole.Solo && context.HasBasicInstinctBuff);
        if (context.HasMightyGuard == wantStance) return;
        if (isMoving) return; // 2.0s cast
        if (SelfBuffRecentlyCast(Daedalus.Data.BLUActions.MightyGuard.ActionId)) return; // status latency

        scheduler.PushGcd(ProteusAbilities.MightyGuard, context.Player.GameObjectId, priority: 3,
            onDispatched: _ =>
            {
                MarkSelfBuffCast(Daedalus.Data.BLUActions.MightyGuard.ActionId);
                context.Debug.PlannedAction = Daedalus.Data.BLUActions.MightyGuard.Name;
                context.Debug.BuffState = wantStance ? "Enabling Mighty Guard" : "Dropping Mighty Guard (role change)";
            });
    }
}
