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

        // Mimicry applies out of combat ANYWHERE — in an all-BLU party the tank/healer archetype
        // can only be sourced from a real player nearby (typically grabbed in town before queuing;
        // the buff survives zoning). Mimicry Helper parity.
        TryPushMimicry(context, scheduler, isMoving);

        // Mighty Guard upkeep only in combat or inside duties — no -40%-damage overworld surprise.
        if (context.InCombat || PlayerSafetyHelper.IsInInstancedDuty())
            TryPushMightyGuard(context, scheduler, isMoving);
        else
            context.Debug.BuffState = "Not in combat";
    }

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
        if (!context.Configuration.BlueMage.EnableMimicry) return;

        // Dropdown role == active buff -> nothing to do (never recast a matching mimicry).
        if (context.HasCorrectMimicry)
        {
            context.Debug.MimicryState = $"{context.Role} (active)";
            _mimicryTargetId = 0;
            if (_mimicryBlacklist.Count > 0) _mimicryBlacklist.Clear();
            context.Debug.MimicryBlacklist = "";
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
            context.PartyHelper, context.PartyList, context.ObjectTable, context.Player, context.Role, exclude);
        if (ally == null)
        {
            // Fail-open: no (non-blacklisted) archetype in range — play on, surface the miss.
            context.Debug.MimicryState = _mimicryBlacklist.Count > 0
                ? $"MISSING — no other {context.Role} in range ({_mimicryBlacklist.Count} blocked)"
                : $"MISSING — no {context.Role} in range";
            return;
        }

        if (ally.GameObjectId != _mimicryTargetId)
        {
            _mimicryTargetId = ally.GameObjectId;
            _mimicryTargetSince = now;
        }

        var capturedAlly = ally;
        scheduler.PushGcd(ProteusAbilities.AethericMimicry, ally.GameObjectId, priority: 4,
            onDispatched: _ =>
            {
                context.Debug.PlannedAction = Daedalus.Data.BLUActions.AethericMimicry.Name;
                context.Debug.MimicryState = $"{context.Role} (from {capturedAlly.Name?.TextValue ?? "ally"})";
            });
    }

    private void TryPushMightyGuard(IProteusContext context, RotationScheduler scheduler, bool isMoving)
    {
        if (!context.Configuration.BlueMage.EnableMightyGuard) return;
        // Mighty Guard is a toggle: maintain it in tank role, drop it when the role changes
        // (its -40% damage dealt is pure loss outside tank duty).
        var wantStance = context.Role == BluRole.Tank;
        if (context.HasMightyGuard == wantStance) return;
        if (isMoving) return; // 2.0s cast

        scheduler.PushGcd(ProteusAbilities.MightyGuard, context.Player.GameObjectId, priority: 3,
            onDispatched: _ =>
            {
                context.Debug.PlannedAction = Daedalus.Data.BLUActions.MightyGuard.Name;
                context.Debug.BuffState = wantStance ? "Enabling Mighty Guard" : "Dropping Mighty Guard (role change)";
            });
    }
}
