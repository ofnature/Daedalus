using System.Numerics;
using Dalamud.Bindings.ImGui;
using Daedalus.Rotation.ProteusCore.Context;

namespace Daedalus.Windows.Debug.Tabs;

/// <summary>
/// Blue Mage tab: the role dropdown vs the ACTIVE mimicry buff (matching = no recast), mimicry
/// scan/retry state, stance and module states.
/// </summary>
public static class ProteusTab
{
    private static readonly Vector4 Green = new(0.3f, 0.9f, 0.3f, 1f);
    private static readonly Vector4 Red = new(1f, 0.4f, 0.4f, 1f);
    private static readonly Vector4 Yellow = new(1f, 0.85f, 0.3f, 1f);
    private static readonly Vector4 Dim = new(0.62f, 0.62f, 0.62f, 1f);

    public static void Draw(ProteusDebugState? state, Configuration config)
    {
        if (state == null)
        {
            ImGui.TextColored(Red, "Blue Mage rotation not active.");
            ImGui.TextDisabled("Switch to Blue Mage to see debug info.");
            return;
        }

        // ── Role / Mimicry ──
        ImGui.TextColored(Dim, "Role & Mimicry");
        ImGui.Separator();

        var roleMatchesBuff = state.ActiveMimicry.Length > 0 &&
                              string.Equals(state.ActiveMimicry, state.Role,
                                  System.StringComparison.OrdinalIgnoreCase);

        ImGui.Text($"Configured Role: {state.Role}");
        ImGui.Text("Active Mimicry:");
        ImGui.SameLine();
        if (state.ActiveMimicry.Length == 0)
            ImGui.TextColored(Red, "none");
        else
            ImGui.TextColored(roleMatchesBuff ? Green : Yellow, state.ActiveMimicry);

        ImGui.Text("Match:");
        ImGui.SameLine();
        if (roleMatchesBuff)
            ImGui.TextColored(Green, "role == buff — no recast");
        else if (state.ActiveMimicry.Length > 0)
            ImGui.TextColored(Yellow, "buff differs from role — will recast from a matching character");
        else
            ImGui.TextColored(Red, "no mimicry — scanning");

        if (state.MimicryState.Length > 0)
            ImGui.TextColored(Dim, $"Scan: {state.MimicryState}");
        if (state.MimicryBlacklist.Length > 0)
            ImGui.TextColored(Yellow, $"Retry: {state.MimicryBlacklist} — cast didn't land, trying others");

        ImGui.Spacing();

        // ── Stance / survival ──
        ImGui.TextColored(Dim, "Stance & Survival");
        ImGui.Separator();
        ImGui.Text("Mighty Guard:");
        ImGui.SameLine();
        ImGui.TextColored(state.HasMightyGuard ? Green : Dim, state.HasMightyGuard ? "Active" : "Off");
        if (state.MitigationState.Length > 0)
            ImGui.TextColored(Dim, $"Mitigation: {state.MitigationState}");
        if (state.HealingState.Length > 0)
            ImGui.TextColored(Dim, $"Healing: {state.HealingState}");

        ImGui.Spacing();

        // ── Module states ──
        ImGui.TextColored(Dim, "Modules");
        ImGui.Separator();
        ImGui.Text($"Damage: {state.DamageState}");
        ImGui.Text($"Buff: {state.BuffState}");
        if (state.PlannedAction.Length > 0)
            ImGui.Text($"Planned: {state.PlannedAction}");
        if (state.AoeRangeEnemies > 0)
            ImGui.TextColored(Dim, $"Enemies in AoE range: {state.AoeRangeEnemies}");
    }
}
