using System.Collections.Generic;
using System.Linq;
using Olympus.Data;
using Olympus.Models.Action;
using Olympus.Services.Action;

namespace Olympus.Services.Debug;

/// <summary>
/// Spell category for grouping in role debug tabs.
/// </summary>
public enum SpellCategory
{
    GcdHealSingle,
    GcdHealAoE,
    GcdHealHoT,
    OgcdHealSingle,
    OgcdHealAoE,
    GcdDamageSingle,
    GcdDamageAoE,
    GcdDoT,
    ComboGcd,
    ComboGcdAoE,
    GaugeSpender,
    OgcdDamage,
    Proc,
    Buff,
    OgcdMitigationPersonal,
    OgcdMitigationParty,
    GcdMitigation,
    Utility,
    RoleAction,
}

/// <summary>
/// Real-time status of a single spell.
/// </summary>
public sealed class SpellStatusEntry
{
    public uint ActionId { get; init; }
    public string Name { get; init; } = "";
    public byte MinLevel { get; init; }
    public SpellCategory Category { get; init; }
    public bool IsGCD { get; init; }
    public float CooldownTotal { get; init; }

    public bool IsLevelSynced { get; set; }
    public bool IsReady { get; set; }
    public float CooldownRemaining { get; set; }
    public string? NotReadyReason { get; set; }
}

/// <summary>
/// Complete spell status snapshot for debug display.
/// </summary>
public sealed class SpellStatusSnapshot
{
    public byte PlayerLevel { get; init; }
    public uint JobId { get; init; }
    public RoleDebugTab Tab { get; init; }
    public List<SpellStatusEntry> Spells { get; init; } = new();
}

/// <summary>
/// Real-time spell readiness for role debug tabs (Healing/Damage/Mitigation).
/// </summary>
public sealed class SpellStatusService
{
    private readonly ActionService _actionService;

    public SpellStatusService(ActionService actionService)
    {
        _actionService = actionService;
    }

    public SpellStatusSnapshot GetSnapshot(uint jobId, byte playerLevel, RoleDebugTab tab)
    {
        var spells = new List<SpellStatusEntry>();
        var seen = new HashSet<uint>();

        foreach (var (category, action) in SpellStatusRegistry.GetActions(jobId, playerLevel, tab))
        {
            if (!seen.Add(action.ActionId))
                continue;

            var cooldownRemaining = _actionService.GetCooldownRemaining(action.ActionId);
            var isLevelSynced = playerLevel >= action.MinLevel;

            var entry = new SpellStatusEntry
            {
                ActionId = action.ActionId,
                Name = action.Name,
                MinLevel = action.MinLevel,
                Category = category,
                IsGCD = action.IsGCD,
                CooldownTotal = action.RecastTime,
                IsLevelSynced = isLevelSynced,
                CooldownRemaining = cooldownRemaining,
            };

            if (!isLevelSynced)
                entry.NotReadyReason = $"Lv{action.MinLevel}";
            else if (cooldownRemaining > 0)
                entry.NotReadyReason = $"{cooldownRemaining:F1}s";
            else
            {
                entry.IsReady = _actionService.IsActionReady(action.ActionId);
                if (!entry.IsReady)
                    entry.NotReadyReason = "Not Ready";
            }

            spells.Add(entry);
        }

        return new SpellStatusSnapshot
        {
            JobId = jobId,
            PlayerLevel = playerLevel,
            Tab = tab,
            Spells = spells,
        };
    }
}
