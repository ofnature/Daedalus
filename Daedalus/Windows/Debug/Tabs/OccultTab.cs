using System.Numerics;
using Dalamud.Bindings.ImGui;
using Daedalus.Data;
using Daedalus.Services.Occult;

namespace Daedalus.Windows.Debug.Tabs;

/// <summary>
/// Duty tab: duty-action layer diagnostics. Occult Crescent phantom-job detection
/// (docs/occult-phantom-plan.md) plus the Variant dungeon block
/// (docs/variant-actions-plan.md) — the field-verification surface for both layers.
/// </summary>
public static class OccultTab
{
    private static readonly Vector4 Green = new(0.49f, 0.79f, 0.49f, 1f);
    private static readonly Vector4 Yellow = new(0.88f, 0.78f, 0.42f, 1f);
    private static readonly Vector4 Dim = new(0.54f, 0.54f, 0.58f, 1f);

    public static void Draw(PhantomJobService service, Daedalus.Services.Occult.ElementalWeaknessLog? weaknessLog = null)
    {
        var snapshot = service.GetSnapshot();

        if (service.IsInVariantDungeon)
        {
            DrawVariantBlock(service, snapshot.TerritoryId);
            return;
        }

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

        ImGui.Text($"Phantom layer: {service.LayerLastEvent}");
        ImGui.TextColored(Dim, $"Last fired: {service.LayerLastDispatch}");

        if (!snapshot.InOccultCrescent)
            return;

        if (snapshot.Progression is { } prog)
        {
            if (prog.KnowledgeLevel > 0)
                ImGui.Text($"Knowledge level: {prog.KnowledgeLevel}");
            else
                ImGui.TextColored(Yellow, "Knowledge level: unavailable");
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

        DrawWeaknessBlock(weaknessLog);
    }

    /// <summary>Learned elemental weaknesses (statuses 5322-5325, revealed by Occult Libra etc.).</summary>
    private static void DrawWeaknessBlock(Daedalus.Services.Occult.ElementalWeaknessLog? weaknessLog)
    {
        if (weaknessLog is null)
            return;

        ImGui.Spacing();
        ImGui.Text("Enemies seen — kind & elemental weakness");
        ImGui.Separator();

        var entries = weaknessLog.Entries;
        if (entries.Count == 0)
        {
            ImGui.TextColored(Dim, "None yet — enemies are recorded on sight; weaknesses fill in when revealed.");
        }
        else
        {
            foreach (var e in entries)
            {
                var ice = (e.Elements & Daedalus.Services.Occult.OccultElement.Ice) != 0;
                // The CE tag shows on EVERY entry seen during an encounter, not just ones the
                // HP line promoted — that way a boss mis-sized by a bad reading is still
                // visibly a CE participant instead of silently reading as field trash.
                var kind = e.Kind switch
                {
                    Daedalus.Services.Occult.OccultEnemyKind.CriticalEncounterBoss => "CE BOSS",
                    Daedalus.Services.Occult.OccultEnemyKind.Elite => "elite",
                    _ => "trash",
                };
                if (e.SeenInCriticalEncounter && e.Kind != Daedalus.Services.Occult.OccultEnemyKind.CriticalEncounterBoss)
                    kind += " · in CE";
                if (!string.IsNullOrEmpty(e.CriticalEncounter))
                    kind += $": {e.CriticalEncounter}";
                var weakness = e.Elements == Daedalus.Services.Occult.OccultElement.None
                    ? "weakness not revealed"
                    : $"weak to {e.Elements}";
                ImGui.TextColored(ice ? Green : Dim,
                    $"{e.Name} [{kind}] — {weakness}  (zone {e.TerritoryId}, {e.MaxHp:N0} HP)");
            }
        }

        if (!string.IsNullOrEmpty(weaknessLog.FilePath))
            ImGui.TextColored(Dim, weaknessLog.FilePath!);
    }

    private static void DrawVariantBlock(PhantomJobService service, ushort territoryId)
    {
        ImGui.Text("Variant Dungeon");
        ImGui.Separator();
        ImGui.TextColored(Green, $"Territory {territoryId} — Variant/Criterion (layer active)");
        ImGui.Text($"Variant layer: {service.VariantLastEvent}");
        ImGui.TextColored(Dim, $"Last fired: {service.VariantLastDispatch}");

        ImGui.Spacing();
        ImGui.Text("Granted actions (Set statuses)");
        ImGui.Separator();

        foreach (var def in Daedalus.Data.VariantActionData.All)
        {
            if (def.Kind == Daedalus.Data.VariantAction.RaiseII && !service.PlayerHasStatus(def.SetStatusId))
                continue; // shares Raise's Set status; only shown when granted

            var selected = service.PlayerHasStatus(def.SetStatusId);
            if (selected)
                ImGui.TextColored(Green, $"{def.Name} — SELECTED (status {def.SetStatusId})");
            else
                ImGui.TextColored(Dim, $"{def.Name} — not selected");
        }

        ImGui.Spacing();
        ImGui.Text("Duty bar — slotted actions");
        ImGui.Separator();

        var slots = service.GetDutySlotIds();
        if (slots.Length == 0)
        {
            ImGui.TextColored(Yellow, "Slot read unavailable (failed closed)");
        }
        else
        {
            for (var i = 0; i < slots.Length; i++)
            {
                if (slots[i] == 0)
                    ImGui.TextColored(Dim, $"Slot {i + 1}: empty");
                else
                    ImGui.Text($"Slot {i + 1}: {service.ResolveActionName(slots[i])} ({slots[i]})");
            }
        }
    }
}
