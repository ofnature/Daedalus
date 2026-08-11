using System;
using System.Collections.Generic;
using System.Linq;
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

    /// <summary>
    /// Chest ledger: what has been collected, and the button that writes it out for baking into
    /// a shipped seed. Debug-only, same as the collection itself.
    /// </summary>
    private static void DrawChestLedgerBlock(Daedalus.Services.Occult.ChestLedger? ledger)
    {
        if (ledger is null)
            return;

        ImGui.Text("Chest ledger");
        ImGui.Separator();

        var zoneCount = ledger.EntriesForCurrentZone();
        var total = ledger.TotalEntries;
        ImGui.TextColored(total > 0 ? Green : Dim,
            $"{zoneCount} spot(s) recorded here, {total} across all zones ({ledger.TotalOpened} opened)");

        if (ImGui.Button("Export seed##chestledger"))
            _lastSeedExport = ledger.ExportSeed() ?? "nothing to export";

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Writes occult-chests.json to the plugin config folder, for baking into a shipped seed.");

        if (!string.IsNullOrEmpty(_lastSeedExport))
            ImGui.TextColored(Dim, _lastSeedExport);

        ImGui.Spacing();
    }

    /// <summary>Path of the last seed export, so the button can say where it went.</summary>
    private static string? _lastSeedExport;

    public static void Draw(
        PhantomJobService service,
        Daedalus.Services.Occult.ElementalWeaknessLog? weaknessLog = null,
        Daedalus.Services.Occult.ChestLedger? chestLedger = null)
    {
        var snapshot = service.GetSnapshot();

        if (service.IsInVariantDungeon)
        {
            DrawVariantBlock(service, snapshot.TerritoryId);
            return;
        }

        DrawChestLedgerBlock(chestLedger);

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
        ImGui.TextColored(
            service.RaiseState.StartsWith("raising", StringComparison.OrdinalIgnoreCase) ? Green : Dim,
            $"Phantom raise: {service.RaiseState}");
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
#if !DEBUG
        ImGui.TextColored(Yellow, "Collection is Debug-only — this is the table gathered previously.");
#endif

        var entries = weaknessLog.Entries;
        if (entries.Count == 0)
        {
            ImGui.TextColored(Dim, "None yet — enemies are recorded on sight; weaknesses fill in when revealed.");
        }
        else
        {
            // Zone → Critical Encounters / Regular mobs, each collapsible. Any zone id that
            // shows up is listed, so a future Occult zone needs no code change here.
            foreach (var zone in entries.Select(e => e.TerritoryId).Distinct().OrderBy(z => z))
            {
                var inZone = entries.Where(e => e.TerritoryId == zone).ToList();
                var zoneName = Daedalus.Data.PhantomJobData.GetZoneName(zone);

                if (!ImGui.CollapsingHeader($"{zoneName} ({inZone.Count})###occult_zone_{zone}"))
                    continue;

                ImGui.Indent();
                // Three kinds of content, in descending "is this a fight worth planning for"
                // order. CE membership needs the HP heuristic; FATE membership is stamped on
                // the object by the game, so it is exact.
                DrawEnemyGroup($"Critical Encounters###occult_ce_{zone}",
                    inZone.Where(e => e.BelongsToCriticalEncounter).ToList(), groupByEncounter: true);
                DrawEnemyGroup($"FATEs###occult_fate_{zone}",
                    inZone.Where(e => !e.BelongsToCriticalEncounter && e.SeenInFate).ToList(),
                    groupByEncounter: true, fateNames: true);
                DrawEnemyGroup($"Regular mobs###occult_mobs_{zone}",
                    inZone.Where(e => !e.BelongsToCriticalEncounter && !e.SeenInFate).ToList(), groupByEncounter: false);
                ImGui.Unindent();
            }
        }

        if (!string.IsNullOrEmpty(weaknessLog.FilePath))
            ImGui.TextColored(Dim, weaknessLog.FilePath!);
    }

    /// <summary>One collapsible group of enemies (bosses first, then by HP).</summary>
    private static void DrawEnemyGroup(
        string label, List<Daedalus.Services.Occult.OccultWeaknessEntry> group, bool groupByEncounter,
        bool fateNames = false)
    {
        // Grouped views count ENCOUNTERS as well as enemies — "(36)" on the CE header read as
        // 36 Critical Encounters when it was 36 mobs across a handful of them.
        string counts;
        if (groupByEncounter)
        {
            var encounters = group
                .Select(e => fateNames ? e.Fate : e.CriticalEncounter)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
            counts = $" ({encounters} {(fateNames ? "FATEs" : "CEs")}, {group.Count} enemies)";
        }
        else
        {
            counts = $" ({group.Count})";
        }

        var title = label.Contains("###")
            ? label.Insert(label.IndexOf("###", StringComparison.Ordinal), counts)
            : $"{label}{counts}";

        if (!ImGui.CollapsingHeader(title))
            return;

        ImGui.Indent();
        if (group.Count == 0)
        {
            ImGui.TextColored(Dim, "none recorded");
        }
        else
        {
            string EncounterOf(Daedalus.Services.Occult.OccultWeaknessEntry e)
                => fateNames ? e.Fate : e.CriticalEncounter;

            var ordered = groupByEncounter
                ? group.OrderBy(EncounterOf, StringComparer.OrdinalIgnoreCase)
                       .ThenByDescending(e => e.MaxHp).ToList()
                // Non-CE groups (FATEs, Regular mobs): notable first, then ALPHABETICAL. These
                // lists are dozens of similarly-sized "Crescent …" field mobs, so name order
                // is what you can actually scan; HP order just looked arbitrary.
                : group.OrderByDescending(e => e.Kind)
                       .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();

            var lastEncounter = string.Empty;
            for (var i = 0; i < ordered.Count; i++)
            {
                var e = ordered[i];

                // Inside the CE group, break the list by encounter so it reads as a per-fight
                // roster rather than one long list.
                if (groupByEncounter && !string.Equals(EncounterOf(e), lastEncounter, StringComparison.Ordinal))
                {
                    lastEncounter = EncounterOf(e);
                    ImGui.TextColored(Yellow, string.IsNullOrEmpty(lastEncounter)
                        ? (fateNames ? "(FATE unnamed — seen before names were read)" : "(encounter unnamed)")
                        : lastEncounter);
                }

                // Fold runs of rows that would print the same line — see RendersSameAs. The
                // ordering above already puts them adjacent, and never merge across an encounter
                // heading, which would move a mob out from under the fight it belongs to.
                var copies = 1;
                while (i + 1 < ordered.Count
                       && RendersSameAs(ordered[i + 1], e)
                       && (!groupByEncounter
                           || string.Equals(EncounterOf(ordered[i + 1]), lastEncounter, StringComparison.Ordinal)))
                {
                    copies++;
                    i++;
                }

                DrawEnemyLine(e, indented: groupByEncounter, copies);
            }
        }

        ImGui.Unindent();
    }

    /// <summary>
    /// Whether two entries would draw the identical line.
    ///
    /// <para>
    /// Two NameIds can share a name, kind, weakness AND max HP — spawn variants of one field mob
    /// (Animated Doll 13893/13894, Crescent Void Viper 13896/13907). The table has to keep both
    /// rows because <c>KnownWeakness</c> looks up by NameId, but printing the same line twice
    /// reads as a bug in the table.
    /// </para>
    ///
    /// <para>
    /// Deliberately strict: it compares everything the line shows, so same-named enemies that are
    /// genuinely different still get their own row — the 24.7M Crescent Garula weak to Fire and
    /// the 634k one with nothing revealed are two different fights and must not be folded
    /// together.
    /// </para>
    /// </summary>
    private static bool RendersSameAs(
        Daedalus.Services.Occult.OccultWeaknessEntry a, Daedalus.Services.Occult.OccultWeaknessEntry b)
        => a.MaxHp == b.MaxHp
           && a.Elements == b.Elements
           && a.Kind == b.Kind
           && string.Equals(a.Name, b.Name, StringComparison.Ordinal);

    private static void DrawEnemyLine(
        Daedalus.Services.Occult.OccultWeaknessEntry e, bool indented, int copies = 1)
    {
        var ice = (e.Elements & Daedalus.Services.Occult.OccultElement.Ice) != 0;
        // The zone's own two words for a named target, and they are not interchangeable:
        // critical encounters have BOSSES, FATEs have ELITES.
        var kind = e.Kind switch
        {
            Daedalus.Services.Occult.OccultEnemyKind.CriticalEncounterBoss => "CE BOSS",
            Daedalus.Services.Occult.OccultEnemyKind.FateElite => "FATE ELITE",
            Daedalus.Services.Occult.OccultEnemyKind.FieldNotorious => "notorious",
            Daedalus.Services.Occult.OccultEnemyKind.MechanicObject => "untargetable",
            _ => "trash",
        };
        var weakness = e.Elements == Daedalus.Services.Occult.OccultElement.None
            ? "weakness not revealed"
            : $"weak to {e.Elements}";

        // "×2" rather than silently dropping the row: the second one is a real, separately
        // tracked enemy, and a reader counting the roster of a fight should still see it.
        var multiple = copies > 1 ? $" ×{copies}" : string.Empty;

        if (indented)
            ImGui.Indent();
        ImGui.TextColored(ice ? Green : Dim, $"{e.Name}{multiple} [{kind}] — {weakness}  ({e.MaxHp:N0} HP)");
        if (indented)
            ImGui.Unindent();
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
