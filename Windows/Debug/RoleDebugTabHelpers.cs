using System.Collections.Generic;
using System.Linq;
using Dalamud.Bindings.ImGui;
using Olympus.Localization;
using Olympus.Services.Debug;

namespace Olympus.Windows.Debug;

/// <summary>
/// Shared UI for role debug tabs (Healing, Damage, Mitigation).
/// </summary>
public static class RoleDebugTabHelpers
{
    public static void DrawSpellStatus(DebugService debugService, RoleDebugTab tab, string sectionConfigKey = "SpellStatus")
    {
        var playerLevel = debugService.GetPlayerLevel();
        var jobId = debugService.GetJobId();
        if (playerLevel == 0 || jobId == 0)
        {
            ImGui.TextColored(DebugColors.Dim, Loc.T(LocalizedStrings.Debug.NotLoggedIn, "Not logged in"));
            return;
        }

        var snapshot = debugService.GetSpellStatus(playerLevel, tab);
        if (snapshot.Spells.Count == 0)
        {
            ImGui.TextColored(DebugColors.Dim, "No abilities registered for this job/tab yet.");
            ImGui.TextDisabled("Add groups in SpellChecklistRegistry (see .cursor/rules/debug-window-role-tabs.mdc).");
            return;
        }

        ImGui.Text(Loc.T(LocalizedStrings.Debug.SpellStatus, "Spell Status"));
        ImGui.SameLine();
        ImGui.TextColored(DebugColors.Dim, Loc.TFormat(LocalizedStrings.Debug.LevelFormat, "(Lv{0})", snapshot.PlayerLevel));
        ImGui.Separator();

        foreach (var group in snapshot.Spells.GroupBy(s => s.Category).OrderBy(g => (int)g.Key))
        {
            var categoryName = GetCategoryDisplayName(group.Key);
            var readyCount = group.Count(s => s.IsReady);
            var totalCount = group.Count();

            if (ImGui.CollapsingHeader($"{categoryName} ({readyCount}/{totalCount})##{tab}_{group.Key}"))
                DrawSpellGroup(group.ToList(), $"{tab}_{group.Key}");
        }
    }

    public static void DrawSpellGroup(List<SpellStatusEntry> spells, string tableId)
    {
        if (!ImGui.BeginTable(tableId, 4,
                ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
            return;

        ImGui.TableSetupColumn("Spell", ImGuiTableColumnFlags.WidthFixed, 140);
        ImGui.TableSetupColumn("Lv", ImGuiTableColumnFlags.WidthFixed, 30);
        ImGui.TableSetupColumn("Ready", ImGuiTableColumnFlags.WidthFixed, 50);
        ImGui.TableSetupColumn("Cooldown", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableHeadersRow();

        foreach (var spell in spells.OrderBy(s => s.MinLevel).ThenBy(s => s.Name))
        {
            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            var nameColor = spell.IsLevelSynced
                ? (spell.IsReady ? DebugColors.Success : DebugColors.Dim)
                : DebugColors.Failure;
            ImGui.TextColored(nameColor, spell.Name);

            ImGui.TableNextColumn();
            ImGui.TextColored(spell.IsLevelSynced ? DebugColors.Dim : DebugColors.Failure, spell.MinLevel.ToString());

            ImGui.TableNextColumn();
            if (spell.IsReady)
                ImGui.TextColored(DebugColors.Success, "Ready");
            else if (!spell.IsLevelSynced)
                ImGui.TextColored(DebugColors.Failure, "Sync");
            else
                ImGui.TextColored(DebugColors.Warning, "CD");

            ImGui.TableNextColumn();
            if (!spell.IsGCD && spell.CooldownRemaining > 0)
                ImGui.TextColored(DebugColors.Warning, $"{spell.CooldownRemaining:F1}s");
            else if (!spell.IsReady && spell.NotReadyReason != null)
                ImGui.TextColored(DebugColors.Dim, spell.NotReadyReason);
            else
                ImGui.TextColored(DebugColors.Dim, "-");
        }

        ImGui.EndTable();
    }

    public static string GetCategoryDisplayName(SpellCategory category) => category switch
    {
        SpellCategory.GcdHealSingle => Loc.T(LocalizedStrings.Debug.GcdHealsSingle, "GCD Heals (Single)"),
        SpellCategory.GcdHealAoE => Loc.T(LocalizedStrings.Debug.GcdHealsAoE, "GCD Heals (AoE)"),
        SpellCategory.GcdHealHoT => Loc.T(LocalizedStrings.Debug.GcdHealsHoT, "GCD Heals (HoT)"),
        SpellCategory.OgcdHealSingle => Loc.T(LocalizedStrings.Debug.OgcdHealsSingle, "oGCD Heals (Single)"),
        SpellCategory.OgcdHealAoE => Loc.T(LocalizedStrings.Debug.OgcdHealsAoE, "oGCD Heals (AoE)"),
        SpellCategory.GcdDamageSingle => Loc.T(LocalizedStrings.Debug.GcdDamageSingle, "GCD Damage (Single)"),
        SpellCategory.GcdDamageAoE => Loc.T(LocalizedStrings.Debug.GcdDamageAoE, "GCD Damage (AoE)"),
        SpellCategory.GcdDoT => Loc.T(LocalizedStrings.Debug.GcdDoT, "GCD DoT"),
        SpellCategory.ComboGcd => "Combo GCDs",
        SpellCategory.ComboGcdAoE => "AoE Combo GCDs",
        SpellCategory.GaugeSpender => "Gauge Spenders",
        SpellCategory.OgcdDamage => "oGCD Damage",
        SpellCategory.Proc => "Procs / Continuation",
        SpellCategory.Buff => "Buffs / Burst",
        SpellCategory.OgcdMitigationPersonal => "Personal Mitigation",
        SpellCategory.OgcdMitigationParty => "Party Mitigation",
        SpellCategory.GcdMitigation => "GCD Mitigation",
        SpellCategory.Utility => Loc.T(LocalizedStrings.Debug.Utility, "Utility"),
        SpellCategory.RoleAction => "Role Actions",
        _ => category.ToString(),
    };
}
