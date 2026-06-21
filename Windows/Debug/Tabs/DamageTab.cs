using Dalamud.Bindings.ImGui;
using Olympus.Services.Debug;
using Olympus.Windows.Debug;

namespace Olympus.Windows.Debug.Tabs;

/// <summary>
/// Damage tab: spell toolkit for DPS rotation and healer filler (by level/category).
/// </summary>
public static class DamageTab
{
    public static void Draw(DebugSnapshot snapshot, Configuration config, DebugService debugService)
    {
        if (IsSectionVisible(config, "SpellStatus"))
        {
            RoleDebugTabHelpers.DrawSpellStatus(debugService, RoleDebugTab.Damage);
            ImGui.Spacing();
        }

        if (IsSectionVisible(config, "DpsRotationState"))
            DrawRotationState(snapshot);
    }

    private static void DrawRotationState(DebugSnapshot snapshot)
    {
        ImGui.Text("Rotation State");
        ImGui.Separator();

        var rot = snapshot.Rotation;
        if (ImGui.BeginTable("DpsRotationTable", 2, ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("Field", ImGuiTableColumnFlags.WidthFixed, 140);
            ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch);

            DrawRow("Planning", rot.PlanningState);
            DrawRow("Planned Action", rot.PlannedAction);
            DrawRow("DPS State", rot.DpsState);
            DrawRow("AoE State", rot.AoEDpsState);
            DrawRow("AoE Targets", rot.AoEDpsEnemyCount.ToString());
            DrawRow("Target", rot.TargetInfo);

            ImGui.EndTable();
        }
    }

    private static void DrawRow(string label, string value)
    {
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.Text(label);
        ImGui.TableNextColumn();
        ImGui.Text(value);
    }

    private static bool IsSectionVisible(Configuration config, string sectionKey) =>
        !config.Debug.DebugSectionVisibility.TryGetValue(sectionKey, out var visible) || visible;
}
