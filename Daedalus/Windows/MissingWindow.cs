using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Windowing;
using Daedalus.Data;
using Daedalus.Services.Action;
using Daedalus.Services.Debug;
using Daedalus.Windows.Common;

namespace Daedalus.Windows;

/// <summary>
/// "Missing" window — scans the current job's expected abilities and flags any that are level-met but
/// not actually usable (uncompleted job quests). Auto-updates with whatever job you're on. Catches the
/// case where the rotation silently can't cast something (e.g. Reaper Enshroud before the Lv80 quest).
/// On BLU it doubles as the farm planner and the role-loadout checklist (Blue Academy reference sets).
/// </summary>
public sealed class MissingWindow : Window
{
    private static readonly Vector4 _red = new(1.0f, 0.4f, 0.4f, 1.0f);
    private static readonly Vector4 _green = new(0.4f, 1.0f, 0.4f, 1.0f);
    private static readonly Vector4 _yellow = new(1.0f, 0.85f, 0.3f, 1.0f);
    private static readonly Vector4 _dim = new(0.6f, 0.6f, 0.6f, 1.0f);

    private readonly DebugService debugService;
    private readonly IBluLoadoutService? bluLoadoutService;

    /// <summary>Feedback line for the last manual loadout apply ("" = none yet).</summary>
    private string applyFeedback = "";

    public MissingWindow(DebugService debugService, IBluLoadoutService? bluLoadoutService = null)
        : base("Missing", ImGuiWindowFlags.NoCollapse)
    {
        this.debugService = debugService;
        this.bluLoadoutService = bluLoadoutService;
        Size = new Vector2(360, 420);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        var jobId = debugService.GetJobId();
        var level = debugService.GetPlayerLevel();

        if (jobId == 0)
        {
            ImGui.TextColored(_dim, "Not logged in.");
            return;
        }

        var jobName = JobRegistry.GetJobName(jobId);
        ImGui.Text($"Job: {jobName} (Lv.{level})");
        ImGui.Separator();

        var statuses = debugService.GetAbilityUnlockStatus();
        if (statuses.Count == 0)
        {
            ImGui.TextColored(_dim, "No ability list for this job.");
            return;
        }

        var missing = statuses.Where(s => !s.Learned).ToList();

        // BLU: spells are learned from enemies, not quests — show the farm source per entry so
        // this window doubles as a spell-hunting planner (sources from the game's Aoz sheets).
        var isBlu = jobId == JobRegistry.BlueMage;
        var bluSources = isBlu
            ? BLUSpellbook.All.ToDictionary(e => e.ActionId, e => e.SourceName)
            : null;

        // Missing first — the actionable part.
        if (missing.Count == 0)
        {
            ImGui.TextColored(_green, $"All {statuses.Count} abilities unlocked for Lv.{level}.");
        }
        else
        {
            ImGui.TextColored(_red, isBlu
                ? $"{missing.Count} spell(s) not yet learned:"
                : $"{missing.Count} ability(ies) level-met but NOT unlocked:");
            ImGui.TextWrapped(isBlu
                ? "Blue Mage spells are learned from enemies (synced duty clears guarantee the learn). The source is shown next to each spell."
                : "These are almost always uncompleted job quests — finish the job quest to enable them.");
            ImGui.Spacing();
            foreach (var s in missing.OrderBy(s => s.MinLevel))
            {
                DaedalusTheme.StatusIcon(FontAwesomeIcon.Times, _red);
                ImGui.SameLine();
                if (isBlu && bluSources!.TryGetValue(s.ActionId, out var source))
                {
                    ImGui.Text(s.Name);
                    ImGui.SameLine();
                    ImGui.TextColored(_dim, $"— {source}");
                }
                else
                {
                    ImGui.Text($"{s.Name}  (Lv.{s.MinLevel})");
                }
            }
        }

        // BLU role-loadout checklist (Blue Academy reference sets): ✔ learned+slotted,
        // ● learned but NOT slotted, ✗ not learned (+ farm source). Core = the role does
        // not function without it; Flex = content-dependent alternates.
        if (isBlu)
        {
            ImGui.Spacing();
            ImGui.Separator();

            var learnedById = statuses.ToDictionary(s => s.ActionId, s => s.Learned);
            var spellbook = BLUSpellbook.All.ToDictionary(e => e.ActionId, e => e);
            var hasSlots = bluLoadoutService is { HasSlotData: true };

            ImGui.Text("Role loadouts");
            ImGui.SameLine();
            ImGui.TextColored(_dim, hasSlots
                ? $"— active set {bluLoadoutService!.SlottedCount}/{BluLoadoutService.SlotCount}"
                : "— slot data unavailable");

            foreach (var loadout in BLULoadouts.All)
            {
                DrawLoadoutHeader(loadout, learnedById, spellbook, hasSlots);
            }
        }

        ImGui.Spacing();
        ImGui.Separator();

        // Full list (collapsed by default) for reference.
        if (ImGui.CollapsingHeader($"All expected abilities ({statuses.Count})"))
        {
            foreach (var s in statuses.OrderBy(s => s.MinLevel))
            {
                if (s.Learned)
                {
                    DaedalusTheme.StatusIcon(FontAwesomeIcon.Check, _green);
                    ImGui.SameLine();
                    ImGui.TextColored(_dim, $"{s.Name}  (Lv.{s.MinLevel})");
                }
                else
                {
                    DaedalusTheme.StatusIcon(FontAwesomeIcon.Times, _red);
                    ImGui.SameLine();
                    ImGui.Text($"{s.Name}  (Lv.{s.MinLevel})");
                }
            }
        }
    }

    private void DrawLoadoutHeader(
        BluLoadout loadout,
        Dictionary<uint, bool> learnedById,
        Dictionary<uint, BLUSpellEntry> spellbook,
        bool hasSlots)
    {
        var coreSlotted = 0;
        var coreNotLearned = 0;
        foreach (var id in loadout.Core)
        {
            if (!learnedById.GetValueOrDefault(id))
                coreNotLearned++;
            else if (!hasSlots || bluLoadoutService!.SlottedActionIds.Contains(id))
                coreSlotted++;
        }

        var summary = hasSlots
            ? $"{loadout.Name} — core {coreSlotted}/{loadout.Core.Length} slotted, {coreNotLearned} not learned"
            : $"{loadout.Name} — core {loadout.Core.Length - coreNotLearned}/{loadout.Core.Length} learned";

        if (!ImGui.CollapsingHeader($"{summary}###BluLoadout{loadout.Name}"))
            return;

        // Manual apply: replace the active set with this role's learned spells via the game's
        // own SetBlueMageActions (what the spellbook Load button calls). Blocked in duties/combat.
        if (hasSlots)
        {
            var blocked = Daedalus.Rotation.Common.Helpers.PlayerSafetyHelper.IsInInstancedDuty()
                          || (Daedalus.Rotation.Base.RotationServices.Condition?[
                              Dalamud.Game.ClientState.Conditions.ConditionFlag.InCombat] ?? true);
            var pending = bluLoadoutService!.IsApplyPending;
            if (blocked || pending) ImGui.BeginDisabled();
            if (ImGui.SmallButton($"Apply learned spells###ApplyBlu{loadout.Name}"))
            {
                var slots = BluLoadoutComposer.Compose(loadout, id => learnedById.GetValueOrDefault(id));
                bluLoadoutService.RequestApplyLoadout(slots);
                applyFeedback = $"Applying {loadout.Name}…";
            }
            if (blocked || pending) ImGui.EndDisabled();
            ImGui.SameLine();
            ImGui.TextColored(_dim, blocked
                ? "(unavailable in combat / in a duty)"
                : "replaces the ACTIVE set with this role's learned spells");
            if (pending && bluLoadoutService.WaitingOnMimicry)
                ImGui.TextColored(_yellow,
                    "Waiting: drop Aetheric Mimicry (swap jobs briefly — the buff can't be cancelled). "
                    + "The set applies the moment it's gone.");
            else if (pending)
                ImGui.TextColored(_yellow, "Applying…");
            else if (bluLoadoutService.LastApplyResult is { Length: > 0 } result)
                ImGui.TextColored(_yellow, result);
            else if (applyFeedback.Length > 0)
                ImGui.TextColored(_yellow, applyFeedback);
        }

        foreach (var id in loadout.Core)
            DrawLoadoutSpell(id, learnedById, spellbook, hasSlots);

        if (loadout.Flex.Length > 0)
        {
            ImGui.TextColored(_dim, "Flex (content-dependent):");
            foreach (var id in loadout.Flex)
                DrawLoadoutSpell(id, learnedById, spellbook, hasSlots);
        }
    }

    private void DrawLoadoutSpell(
        uint actionId,
        Dictionary<uint, bool> learnedById,
        Dictionary<uint, BLUSpellEntry> spellbook,
        bool hasSlots)
    {
        var name = spellbook.TryGetValue(actionId, out var entry) ? entry.Name : $"Action {actionId}";
        var learned = learnedById.GetValueOrDefault(actionId);

        if (!learned)
        {
            DaedalusTheme.StatusIcon(FontAwesomeIcon.Times, _red);
            ImGui.SameLine();
            ImGui.Text(name);
            if (entry != null)
            {
                ImGui.SameLine();
                ImGui.TextColored(_dim, $"— {entry.SourceName}");
            }
            return;
        }

        if (hasSlots && !bluLoadoutService!.SlottedActionIds.Contains(actionId))
        {
            DaedalusTheme.StatusIcon(FontAwesomeIcon.Circle, _yellow);
            ImGui.SameLine();
            ImGui.Text(name);
            ImGui.SameLine();
            // Learned/slotted detection verified in-game 2026-07-08 (GetActionStatus distinguishes
            // the two for BLU); the label carries both halves so the state is unambiguous.
            ImGui.TextColored(_yellow, "learned, not slotted");
            return;
        }

        DaedalusTheme.StatusIcon(FontAwesomeIcon.Check, _green);
        ImGui.SameLine();
        ImGui.TextColored(_dim, name);
    }
}
