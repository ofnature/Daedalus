using System;
using System.Collections.Generic;
using System.Linq;
using Daedalus.Data;

namespace Daedalus.Services.Blu;

/// <summary>The per-frame result of the multi-BLU owner election — what the rotation acts on.</summary>
public readonly record struct BluCoordinationSnapshot(
    bool CoordinationActive,
    bool IsBleedOwner,
    bool IsMortalFlameOwner,
    bool IsBreathOfMagicOwner,
    bool IsGobskinOwner,
    bool IsCactguardOwner,
    char StaggerGroup,
    int MoonFluteStaggerDelaySeconds,
    string Summary,
    bool IsFreezeLead = true,
    bool IsShatterOwner = true)
{
    /// <summary>Solo / no-LAN / single-BLU fallback: self owns everything (v1 behavior unchanged).</summary>
    public static readonly BluCoordinationSnapshot SelfOwnsEverything = new(
        CoordinationActive: false,
        IsBleedOwner: true,
        IsMortalFlameOwner: true,
        IsBreathOfMagicOwner: true,
        IsGobskinOwner: true,
        IsCactguardOwner: true,
        StaggerGroup: 'A',
        MoonFluteStaggerDelaySeconds: 0,
        Summary: "solo (owns all)",
        IsFreezeLead: true,
        IsShatterOwner: true);
}

/// <summary>
/// Pure election → snapshot computation, one call per pump tick. Deterministic on every machine
/// (capability filter + SenderId sort), so all boxes agree on owners with no negotiation round.
/// </summary>
public static class BluCoordinationCalculator
{
    /// <summary>Group B's Moon Flute delay after a burst signal (T13 Gigaflare stagger).</summary>
    public const int StaggerDelaySeconds = 30;

    /// <summary>
    /// <paramref name="bluRoster"/> = every fresh BLU toon on the bus INCLUDING self.
    /// Fewer than two → the solo fallback (self owns everything).
    /// </summary>
    public static BluCoordinationSnapshot Compute(
        string localSenderId,
        IReadOnlyList<BluPeerCapability> bluRoster,
        uint territoryId)
    {
        if (localSenderId.Length == 0 || bluRoster.Count(p => p.SenderId.Length > 0) < 2)
            return BluCoordinationSnapshot.SelfOwnsEverything;

        var bleed = BluPartyElection.ElectOwner(bluRoster, BluCapabilities.SongOfTorment);
        var mortalFlame = BluPartyElection.ElectOwner(bluRoster, BluCapabilities.MortalFlame);
        var breath = BluPartyElection.ElectOwner(bluRoster, BluCapabilities.BreathOfMagic);
        var gobskin = BluPartyElection.ElectGobskinOwner(bluRoster);
        var cactguard = BluPartyElection.ElectOwner(bluRoster, BluCapabilities.Cactguard);

        // v3.6 co-op freeze→shatter: ONE toon freezes (simultaneous Ram's Voices are wasted GCDs
        // and Deep Freeze re-application builds resistance), ONE shatters — possibly different
        // toons. No capable ShatterOwner → NOBODY freezes (don't burn the GCD for nothing).
        // The operator's "Shatter" pick (LAN window) rides the heartbeat as a preference bit and
        // outranks the SenderId sort for BOTH roles.
        var shatterOwner = BluPartyElection.ElectPreferredOwner(
            bluRoster, BluCapabilities.Ultravibration, BluCapabilities.PreferredFreezeShatter);
        var freezeLead = shatterOwner == null
            ? null
            : BluPartyElection.ElectPreferredOwner(
                bluRoster, BluCapabilities.RamsVoice, BluCapabilities.PreferredFreezeShatter);

        var group = BluPartyElection.StaggerGroupFor(bluRoster, localSenderId);
        var delay = BluDutyAssignments.UsesMoonFluteStagger(territoryId) && group == 'B'
            ? StaggerDelaySeconds
            : 0;

        static string Who(string? owner, string self)
            => owner == null ? "—" : owner == self ? "me" : owner.Split('@')[0];

        return new BluCoordinationSnapshot(
            CoordinationActive: true,
            IsBleedOwner: bleed == localSenderId,
            IsMortalFlameOwner: mortalFlame == localSenderId,
            IsBreathOfMagicOwner: breath == localSenderId,
            IsGobskinOwner: gobskin == localSenderId,
            IsCactguardOwner: cactguard == localSenderId,
            StaggerGroup: group,
            MoonFluteStaggerDelaySeconds: delay,
            Summary: $"{bluRoster.Count}×BLU bleed:{Who(bleed, localSenderId)} "
                     + $"MF:{Who(mortalFlame, localSenderId)} BoM:{Who(breath, localSenderId)} "
                     + $"gob:{Who(gobskin, localSenderId)} cact:{Who(cactguard, localSenderId)} "
                     + $"frz:{Who(freezeLead, localSenderId)}→{Who(shatterOwner, localSenderId)} "
                     + $"grp:{group}{(delay > 0 ? $"+{delay}s" : "")}",
            IsFreezeLead: freezeLead == localSenderId,
            IsShatterOwner: shatterOwner == localSenderId);
    }
}

/// <summary>
/// STATIC-BACKED bridge from the coordination bus to the Proteus rotation (the config-copy
/// lesson: rotations read a copied Configuration, and the bus lives outside the rotation DI
/// container — transient coordination state must ride a static, like BluMimicryCommand /
/// ExternalCombatOverride). Plugin refreshes it every pump tick on the framework thread;
/// modules only ever read. Defaults = self owns everything, so a box with LAN disabled (or
/// tests that never touch this) keep the field-validated single-BLU behavior.
/// </summary>
public static class BluCoordinationState
{
    private static BluCoordinationSnapshot _snapshot = BluCoordinationSnapshot.SelfOwnsEverything;

    /// <summary>True only when ≥2 fresh BLU toons share the bus — every ownership gate keys on this.</summary>
    public static bool CoordinationActive => _snapshot.CoordinationActive;

    public static bool IsBleedOwner => _snapshot.IsBleedOwner;
    public static bool IsMortalFlameOwner => _snapshot.IsMortalFlameOwner;
    public static bool IsBreathOfMagicOwner => _snapshot.IsBreathOfMagicOwner;
    public static bool IsGobskinOwner => _snapshot.IsGobskinOwner;
    public static bool IsCactguardOwner => _snapshot.IsCactguardOwner;

    /// <summary>v3.6: the ONLY toon that starts the Ram's Voice freeze (false when no capable
    /// ShatterOwner exists — a freeze nobody can shatter is a wasted GCD).</summary>
    public static bool IsFreezeLead => _snapshot.IsFreezeLead;

    /// <summary>v3.6: the ONLY toon that casts Ultravibration; everyone else holds damage while
    /// a nearby enemy carries Deep Freeze (local status read — no LAN message needed).</summary>
    public static bool IsShatterOwner => _snapshot.IsShatterOwner;

    /// <summary>Moon Flute stagger group ('A'/'B'); B delays its Flute in stagger duties.</summary>
    public static char StaggerGroup => _snapshot.StaggerGroup;

    /// <summary>Seconds group B waits after the burst signal before Fluting (0 outside T13).</summary>
    public static int MoonFluteStaggerDelaySeconds => _snapshot.MoonFluteStaggerDelaySeconds;

    /// <summary>Owner/stagger one-liner for the Debug window.</summary>
    public static string Summary => _snapshot.Summary;

    /// <summary>BMR tankbuster forecast (seconds; MaxValue = none/unavailable) — Cactguard's trigger.</summary>
    public static float NextTankbusterInSeconds { get; set; } = float.MaxValue;

    /// <summary>Wired by Plugin to <c>CoordinationBus.BroadcastBurstReady</c> (null without LAN).</summary>
    public static System.Action? SignalBurstReady { get; set; }

    private static DateTime _lastBurstFireUtc = DateTime.MinValue;

    /// <summary>Plugin calls this from the bus's OnBurstFire — the synced Moon Flute start signal.</summary>
    public static void NotifyBurstFire(DateTime utcNow) => _lastBurstFireUtc = utcNow;

    /// <summary>Seconds since the last coordinated burst signal (MaxValue when none seen).</summary>
    public static double SecondsSinceBurstFire(DateTime utcNow)
        => _lastBurstFireUtc == DateTime.MinValue
            ? double.MaxValue
            : (utcNow - _lastBurstFireUtc).TotalSeconds;

    public static void Apply(in BluCoordinationSnapshot snapshot) => _snapshot = snapshot;

    /// <summary>Back to the solo defaults (logout / leaving BLU / tests).</summary>
    public static void Reset()
    {
        _snapshot = BluCoordinationSnapshot.SelfOwnsEverything;
        NextTankbusterInSeconds = float.MaxValue;
        _lastBurstFireUtc = DateTime.MinValue;
    }
}
