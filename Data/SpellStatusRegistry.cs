using System;
using System.Collections.Generic;
using System.Linq;
using Olympus.Models.Action;
using Olympus.Services.Debug;

namespace Olympus.Data;

/// <summary>
/// Maps <see cref="SpellChecklistRegistry"/> groups to <see cref="SpellCategory"/> and role tabs.
/// Single source of truth for Healing/Damage/Mitigation spell status panels.
/// </summary>
public static class SpellStatusRegistry
{
    private static readonly Dictionary<string, SpellCategory> GroupCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        ["GCD Single Heals"] = SpellCategory.GcdHealSingle,
        ["oGCD Single Heals"] = SpellCategory.OgcdHealSingle,
        ["GCD AoE Heals"] = SpellCategory.GcdHealAoE,
        ["oGCD AoE Heals"] = SpellCategory.OgcdHealAoE,
        ["GCD DoT"] = SpellCategory.GcdDoT,
        ["GCD DoTs"] = SpellCategory.GcdDoT,
        ["GCD Damage"] = SpellCategory.GcdDamageSingle,
        ["Combo GCDs"] = SpellCategory.ComboGcd,
        ["AoE Combo GCDs"] = SpellCategory.ComboGcdAoE,
        ["Gauge Spenders"] = SpellCategory.GaugeSpender,
        ["Delirium Combo"] = SpellCategory.ComboGcd,
        ["Edge / Flood"] = SpellCategory.OgcdDamage,
        ["Continuation"] = SpellCategory.Proc,
        ["oGCD Damage"] = SpellCategory.OgcdDamage,
        ["AoE GCDs"] = SpellCategory.GcdDamageAoE,
        ["GCD AoE Damage"] = SpellCategory.GcdDamageAoE,
        ["Buffs"] = SpellCategory.Buff,
        ["Defensive"] = SpellCategory.OgcdMitigationPersonal,
        ["Utility"] = SpellCategory.Utility,
        ["Role Actions"] = SpellCategory.RoleAction,
        ["Cards"] = SpellCategory.Buff,
        ["Minor Arcana"] = SpellCategory.OgcdDamage,
        ["Fairy"] = SpellCategory.Buff,
        ["Kardia"] = SpellCategory.Buff,
        ["Eukrasia"] = SpellCategory.Buff,
    };

    private static readonly HashSet<SpellCategory> HealingCategories =
    [
        SpellCategory.GcdHealSingle,
        SpellCategory.GcdHealAoE,
        SpellCategory.GcdHealHoT,
        SpellCategory.OgcdHealSingle,
        SpellCategory.OgcdHealAoE,
    ];

    private static readonly HashSet<SpellCategory> DamageCategories =
    [
        SpellCategory.GcdDamageSingle,
        SpellCategory.GcdDamageAoE,
        SpellCategory.GcdDoT,
        SpellCategory.ComboGcd,
        SpellCategory.ComboGcdAoE,
        SpellCategory.GaugeSpender,
        SpellCategory.OgcdDamage,
        SpellCategory.Proc,
        SpellCategory.Buff,
    ];

    private static readonly HashSet<SpellCategory> MitigationCategories =
    [
        SpellCategory.OgcdMitigationPersonal,
        SpellCategory.OgcdMitigationParty,
        SpellCategory.GcdMitigation,
        SpellCategory.Utility,
        SpellCategory.RoleAction,
    ];

    /// <summary>
    /// Expands checklist groups into leveled actions for the given job and role tab.
    /// </summary>
    public static IEnumerable<(SpellCategory Category, ActionDefinition Action)> GetActions(
        uint jobId,
        byte playerLevel,
        RoleDebugTab tab)
    {
        jobId = SpellChecklistRegistry.NormalizeJobId(jobId);

        var allowed = tab switch
        {
            RoleDebugTab.Healing => HealingCategories,
            RoleDebugTab.Damage => DamageCategories,
            RoleDebugTab.Mitigation => MitigationCategories,
            _ => throw new ArgumentOutOfRangeException(nameof(tab)),
        };

        foreach (var group in SpellChecklistRegistry.GetChecklist(jobId))
        {
            if (!GroupCategories.TryGetValue(group.Name, out var category))
                continue;
            if (!allowed.Contains(category))
                continue;

            foreach (var action in group.GetActions(playerLevel))
                yield return (category, action);
        }
    }

    public static bool HasActionsForTab(uint jobId, RoleDebugTab tab)
    {
        jobId = SpellChecklistRegistry.NormalizeJobId(jobId);
        return GetActions(jobId, 100, tab).Any();
    }
}
