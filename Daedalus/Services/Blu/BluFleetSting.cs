using System;
using System.Collections.Generic;
using System.Linq;

namespace Daedalus.Services.Blu;

/// <summary>
/// v3.4 fleet Final Sting planning — pure math, deterministic on every machine.
/// The stinger count comes from the validated calculator baseline (ceil(bossHp / (est × safety)),
/// over-provisioned by the safety factor); the ordered list is capability-elected with two hard
/// rules: the tank never stings (excluded at the capability level) and at least one healer-mimic
/// never stings (someone must keep Angel Whisper for the cleanup raises).
/// </summary>
public static class BluFleetStingPlanner
{
    /// <summary>Default over-provision factor (0.75 → plan ~33% more stingers than the floor).</summary>
    public const float DefaultSafetyFactor = 0.75f;

    /// <summary>Stagger spacing: each stinger fires index × this many seconds after the signal,
    /// so every sting lands, the boss HP re-checks, and later toons abort on a dead boss.</summary>
    public const double StaggerSeconds = 3.0;

    /// <summary>
    /// Build the ordered stinger list. <paramref name="estStingDamage"/> ≤ 0 (uncalibrated) plans
    /// every eligible toon — over-provisioning is the safe direction. Non-healers sting first
    /// (SenderId order), healers last; when any healer-mimic is eligible, one is always reserved.
    /// </summary>
    public static IReadOnlyList<string> Plan(
        long bossCurrentHp,
        float estStingDamage,
        float safetyFactor,
        IReadOnlyList<BluPeerCapability> bluRoster)
    {
        if (bossCurrentHp <= 0)
            return [];

        var capable = bluRoster
            .Where(p => p.SenderId.Length > 0 && p.Has(BluCapabilities.FinalSting))
            .DistinctBy(p => p.SenderId)
            .ToList();
        if (capable.Count == 0)
            return [];

        // Non-healers first, then healers — and reserve the LAST healer outright.
        var ordered = capable
            .OrderBy(p => p.Has(BluCapabilities.HealerRole) ? 1 : 0)
            .ThenBy(p => p.SenderId, StringComparer.Ordinal)
            .Select(p => p.SenderId)
            .ToList();
        if (capable.Any(p => p.Has(BluCapabilities.HealerRole)) && ordered.Count > 1)
            ordered.RemoveAt(ordered.Count - 1);

        var needed = estStingDamage <= 0f
            ? ordered.Count
            : (int)Math.Ceiling(bossCurrentHp / (double)(estStingDamage * Math.Clamp(safetyFactor, 0.1f, 1f)));

        return ordered.Take(Math.Clamp(needed, 1, ordered.Count)).ToList();
    }
}

/// <summary>
/// STATIC-BACKED bridge for an armed fleet-sting order (the config-copy lesson — rotations can't
/// see the bus). Plugin arms it from the ExecuteSting broadcast and refreshes the LOCAL toon's
/// slot every pump tick (dead stingers are dropped from the order, shifting later indexes up —
/// the deterministic re-check the design calls for, no re-negotiation). The DamageModule fires
/// Final Sting when its slot time arrives and clears the order when the boss dies first.
/// </summary>
public static class BluFleetStingCommand
{
    private const double OrderLifetimeSeconds = 90.0;

    private static ulong _targetId;
    private static string[] _originalOrder = [];
    private static DateTime _armedUtc = DateTime.MinValue;

    // Refreshed by the Plugin pump: the local toon's current slot in the alive-filtered order.
    private static int _mySlotIndex = -1;

    /// <summary>Arm a new order (replaces any previous one).</summary>
    public static void Arm(ulong targetId, string[] orderedStingers, DateTime utcNow)
    {
        _targetId = targetId;
        _originalOrder = orderedStingers;
        _armedUtc = utcNow;
        _mySlotIndex = -1; // pump recomputes next tick
    }

    public static void Clear()
    {
        _targetId = 0;
        _originalOrder = [];
        _armedUtc = DateTime.MinValue;
        _mySlotIndex = -1;
    }

    /// <summary>The armed order for the pump to re-evaluate ([] when none).</summary>
    public static IReadOnlyList<string> OriginalOrder => _originalOrder;

    public static bool IsArmed(DateTime utcNow)
        => _targetId != 0 && (utcNow - _armedUtc).TotalSeconds <= OrderLifetimeSeconds;

    /// <summary>Pump: set the local toon's slot in the alive-filtered order (-1 = not a stinger).</summary>
    public static void SetMySlot(int slotIndex) => _mySlotIndex = slotIndex;

    /// <summary>
    /// Rotation read: true when this toon holds a slot in a live order. <paramref name="fireAtUtc"/>
    /// is the staggered execution time (signal + slot × 3s).
    /// </summary>
    public static bool TryGetMyOrder(DateTime utcNow, out ulong targetId, out DateTime fireAtUtc, out int slot)
    {
        targetId = _targetId;
        slot = _mySlotIndex;
        if (!IsArmed(utcNow) || _mySlotIndex < 0)
        {
            fireAtUtc = DateTime.MaxValue; // disarmed: MinValue+negative would underflow
            return false;
        }
        fireAtUtc = _armedUtc.AddSeconds(_mySlotIndex * BluFleetStingPlanner.StaggerSeconds);
        return true;
    }
}
