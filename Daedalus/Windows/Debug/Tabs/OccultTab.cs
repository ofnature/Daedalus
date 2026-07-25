using System.Numerics;
using Dalamud.Bindings.ImGui;
using Daedalus.Data;
using Daedalus.Services.Occult;

namespace Daedalus.Windows.Debug.Tabs;

/// <summary>
/// Occult tab: Occult Crescent phantom-job detection readout (Phase 1 of
/// docs/occult-phantom-plan.md). Shows territory gate, active phantom job + level
/// (with the raw status the level was read from), duty-bar slots, and consumable
/// counts — the field-verification surface before any phantom action ever fires.
/// </summary>
public static class OccultTab
{
    private static readonly Vector4 Green = new(0.49f, 0.79f, 0.49f, 1f);
    private static readonly Vector4 Yellow = new(0.88f, 0.78f, 0.42f, 1f);
    private static readonly Vector4 Dim = new(0.54f, 0.54f, 0.58f, 1f);

    public static void Draw(PhantomJobService service)
    {
        var snapshot = service.GetSnapshot();

        ImGui.Text("Detection");
        ImGui.Separator();

        if (snapshot.InOccultCrescent)
            ImGui.TextColored(Green, $"Territory {snapshot.TerritoryId} — Occult Crescent (layer active)");
        else
            ImGui.TextColored(Dim, $"Territory {snapshot.TerritoryId} — not in Occult Crescent");

        if (snapshot.ActiveJob == PhantomJob.None)
        {
            ImGui.TextColored(Dim, "Active phantom job: none detected");
        }
        else
        {
            ImGui.Text($"Active phantom job: {snapshot.ActiveJob} — Lv.{snapshot.Level}");
            ImGui.SameLine();
            ImGui.TextColored(Dim, $"(status {snapshot.LevelStatusId}, stacks = {snapshot.Level})");
        }

        if (!snapshot.InOccultCrescent)
            return;

        if (snapshot.Progression is { } prog)
        {
            ImGui.Text($"Knowledge level: {prog.KnowledgeLevel}");
            ImGui.SameLine();
            ImGui.TextColored(Dim, $"({prog.KnowledgeExp:N0} / {prog.KnowledgeExpNeeded:N0} exp)");
            ImGui.TextColored(Dim, $"Silver: {prog.Silver}   Gold: {prog.Gold}");
        }
        else
        {
            ImGui.TextColored(Yellow, "Knowledge level: unavailable (state read failed)");
        }

        ImGui.Spacing();
        ImGui.Text("Duty bar — slotted actions");
        ImGui.Separator();

        if (snapshot.DutySlots.Count == 0)
        {
            ImGui.TextColored(Yellow, "Slot read unavailable (failed closed — no phantom action is considered usable)");
        }
        else
        {
            for (var i = 0; i < snapshot.DutySlots.Count; i++)
            {
                var slot = snapshot.DutySlots[i];
                if (slot.ActionId == 0)
                    ImGui.TextColored(Dim, $"Slot {i + 1}: empty");
                else
                    ImGui.Text($"Slot {i + 1}: {slot.Name} ({slot.ActionId})");
            }
        }

        ImGui.Spacing();
        ImGui.Text("Consumables");
        ImGui.Separator();

        foreach (var item in snapshot.Items)
        {
            if (item.Count == 0)
                ImGui.TextColored(Dim, $"{item.Name} ({item.ItemId}): × 0");
            else
                ImGui.Text($"{item.Name} ({item.ItemId}): × {item.Count}");
        }

        ImGui.TextColored(Dim, "Occult Potion feeds BOTH Chemist restores (Occult Potion + Occult Ether actions).");
    }
}
