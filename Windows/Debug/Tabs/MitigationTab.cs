using Dalamud.Bindings.ImGui;
using Olympus.Services.Debug;
using Olympus.Windows.Debug;

namespace Olympus.Windows.Debug.Tabs;

/// <summary>
/// Mitigation tab: defensive CDs and role actions by level/category.
/// </summary>
public static class MitigationTab
{
    public static void Draw(DebugSnapshot snapshot, Configuration config, DebugService debugService)
    {
        if (IsSectionVisible(config, "SpellStatus"))
        {
            RoleDebugTabHelpers.DrawSpellStatus(debugService, RoleDebugTab.Mitigation);
            ImGui.Spacing();
        }

        if (IsSectionVisible(config, "MitigationState"))
            DrawMitigationState(snapshot);
    }

    private static void DrawMitigationState(DebugSnapshot snapshot)
    {
        ImGui.Text("Mitigation State");
        ImGui.Separator();

        var rot = snapshot.Rotation;
        if (ImGui.BeginTable("MitigationStateTable", 2, ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("Field", ImGuiTableColumnFlags.WidthFixed, 140);
            ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch);

            DrawRow("Defensive", rot.DefensiveState);
            DrawRow("Temperance", rot.TemperanceState);
            DrawRow("Surecast", rot.SurecastState);

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
