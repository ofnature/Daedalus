using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Daedalus.Config;
using Daedalus.Services.Content;
using Daedalus.Services.Targeting;
using Daedalus.Windows.Config;

namespace Daedalus.Windows;

/// <summary>
/// Per-fight strategy panel. Lets the user override targeting behavior for the specific duty they're
/// in (keyed by territory), so e.g. a split-boss fight can switch off unreachable targets while the
/// rest of the game uses the global settings. Overrides are applied non-destructively onto the
/// rotation's effective config (see DutyConfigurationService) — the global config is never mutated.
/// MVP scope: targeting only.
/// </summary>
public sealed class RaidWindow : Window
{
    private static readonly string[] StrategyNames =
        ["Lowest HP", "Highest HP", "Nearest", "Tank Assist", "Current Target", "Focus Target"];

    private static readonly string[] StrategyDescriptions =
    [
        "Target the enemy with the lowest HP (finish off weak enemies).",
        "Target the enemy with the highest HP (for cleave/AoE).",
        "Target the closest enemy.",
        "Attack what the party tank is targeting.",
        "Use your current hard target if valid.",
        "Use your focus target if valid.",
    ];

    private readonly Configuration configuration;
    private readonly Action saveConfiguration;
    private static readonly Vector4 BluWeakColor = new(0.4f, 1.0f, 0.4f, 1.0f);
    private static readonly Vector4 BluImmuneColor = new(1.0f, 0.4f, 0.4f, 1.0f);

    private readonly IDutyContentService dutyContentService;
    private readonly Daedalus.Services.Blu.IDeathImmunityLedger? deathLedger;

    public RaidWindow(
        Configuration configuration, Action saveConfiguration, IDutyContentService dutyContentService,
        Daedalus.Services.Blu.IDeathImmunityLedger? deathLedger = null)
        : base("Raid", ImGuiWindowFlags.NoCollapse)
    {
        this.configuration = configuration;
        this.saveConfiguration = saveConfiguration;
        this.dutyContentService = dutyContentService;
        this.deathLedger = deathLedger;

        Size = new Vector2(360, 360);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        var territory = dutyContentService.CurrentTerritoryType;

        if (territory == 0)
        {
            ImGui.TextDisabled("Not in a duty.");
            ImGui.TextWrapped(
                "Per-fight strategies apply inside instanced duties (dungeons, trials, raids). "
                + "Enter a duty to set a strategy for it.");
        }
        else
        {
            DrawCurrentFight(territory);
            DrawBluDeathCurrentDuty(territory);
        }

        ImGui.Spacing();
        DrawSavedList();
        DrawBluDeathAllZones();
    }

    /// <summary>
    /// This duty's learned death-family verdicts (auto-recorded by BLU Missile probes): which of
    /// its enemies are WEAK to Missile/Level 5 Death/Ultravibration and which are immune.
    /// </summary>
    private void DrawBluDeathCurrentDuty(uint territory)
    {
        if (deathLedger == null) return;
        var entries = deathLedger.EntriesForTerritory((ushort)territory);
        if (entries.Count == 0) return;

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Text("Blue Mage — Death:");
        foreach (var e in entries)
        {
            if (e.Verdict == Daedalus.Services.Blu.DeathImmunityVerdict.Vulnerable)
            {
                ImGui.TextColored(BluWeakColor, "Weak");
                ImGui.SameLine();
                ImGui.Text($"— {e.Name} (Missile works, ×{e.Confirms})");
            }
            else if (e.Verdict == Daedalus.Services.Blu.DeathImmunityVerdict.Immune)
            {
                ImGui.TextColored(BluImmuneColor, "Immune");
                ImGui.SameLine();
                ImGui.TextDisabled($"— {e.Name}");
            }
        }
    }

    /// <summary>The full learned list, grouped by zone — the community list nobody published.</summary>
    private void DrawBluDeathAllZones()
    {
        if (deathLedger == null) return;
        var all = deathLedger.Entries;
        if (all.Count == 0) return;

        ImGui.Spacing();
        if (!ImGui.CollapsingHeader($"Blue Mage Death Ledger — {all.Count} enemies, all zones"))
            return;

        foreach (var zoneGroup in all
                     .GroupBy(e => e.Zone.Length == 0 ? "(unknown zone)" : e.Zone)
                     .OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            ImGui.TextDisabled(zoneGroup.Key);
            ConfigUIHelpers.BeginIndent();
            foreach (var e in zoneGroup.OrderBy(e => e.Name, StringComparer.Ordinal))
            {
                if (e.Verdict == Daedalus.Services.Blu.DeathImmunityVerdict.Vulnerable)
                {
                    ImGui.TextColored(BluWeakColor, "Weak");
                    ImGui.SameLine();
                    ImGui.Text($"— {e.Name} (×{e.Confirms})");
                }
                else
                {
                    ImGui.TextColored(BluImmuneColor, "Immune");
                    ImGui.SameLine();
                    ImGui.TextDisabled($"— {e.Name}");
                }
            }
            ConfigUIHelpers.EndIndent();
        }

        if (ImGui.SmallButton("Clear ledger"))
            deathLedger.ClearAll();
    }

    private void DrawCurrentFight(uint territory)
    {
        var name = string.IsNullOrEmpty(dutyContentService.CurrentDutyName)
            ? $"Territory {territory}"
            : dutyContentService.CurrentDutyName;

        ImGui.Text("Current fight:");
        ImGui.SameLine();
        ImGui.TextColored(ConfigUIHelpers.AccentBlue, name);
        ImGui.TextDisabled(dutyContentService.DutyLabel);
        ImGui.Separator();

        var existing = configuration.Raid.GetTargeting(territory);
        var enabled = existing is { Enabled: true };
        if (ImGui.Checkbox("Use a custom strategy for this fight", ref enabled))
        {
            if (enabled)
            {
                var strat = existing ?? RaidTargetingStrategy.FromGlobal(configuration.Targeting);
                strat.Enabled = true;
                strat.DisplayName = name;
                configuration.Raid.TargetingByTerritory[territory] = strat;
            }
            else if (existing != null)
            {
                existing.Enabled = false;
            }

            saveConfiguration();
        }
        ImGui.TextDisabled(
            "Overrides the global targeting settings while you're in this duty.\nYour global settings are untouched.");

        if (!enabled)
            return;

        var strategy = configuration.Raid.TargetingByTerritory[territory];

        ConfigUIHelpers.BeginIndent();

        var strategyIndex = (int)strategy.EnemyStrategy;
        ImGui.SetNextItemWidth(200);
        if (ImGui.Combo("Enemy Strategy", ref strategyIndex, StrategyNames, StrategyNames.Length))
        {
            strategy.EnemyStrategy = (EnemyTargetingStrategy)strategyIndex;
            saveConfiguration();
        }
        ImGui.TextDisabled(StrategyDescriptions[strategyIndex]);

        ConfigUIHelpers.Spacing();

        ConfigUIHelpers.Toggle(
            "Switch off unreachable targets",
            () => strategy.RetargetUnreachableTarget,
            v => strategy.RetargetUnreachableTarget = v,
            "If your followed target is alive but out of reach (e.g. a boss split into an elevated "
            + "'upper' part melee can't hit and a grounded 'lower' part) and another enemy is in range, "
            + "switch to the reachable one instead of standing idle.",
            saveConfiguration);

        ConfigUIHelpers.Toggle(
            "Strict explicit-target mode",
            () => strategy.StrictCurrentTargetStrategy,
            v => strategy.StrictCurrentTargetStrategy = v,
            "When using Current Target or Focus Target strategy, never fall back to another enemy if "
            + "yours is gone.",
            saveConfiguration);

        ConfigUIHelpers.Toggle(
            "Skip invulnerable enemies",
            () => strategy.EnableInvulnerabilityFiltering,
            v => strategy.EnableInvulnerabilityFiltering = v,
            "Auto-targeting ignores enemies with invulnerability effects (phase transitions, immune "
            + "adds). Prevents wasting actions on targets that take no damage.",
            saveConfiguration);

        ConfigUIHelpers.EndIndent();
    }

    private void DrawSavedList()
    {
        var saved = configuration.Raid.TargetingByTerritory;
        if (saved.Count == 0)
            return;

        ImGui.Separator();
        ImGui.TextDisabled("Saved fight strategies");

        uint? toRemove = null;
        foreach (var (territory, strategy) in saved.OrderBy(kvp => kvp.Value.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            var label = string.IsNullOrEmpty(strategy.DisplayName) ? $"Territory {territory}" : strategy.DisplayName;
            var status = strategy.Enabled ? "on" : "off";

            if (ImGui.SmallButton($"X##raid{territory}"))
                toRemove = territory;

            ImGui.SameLine();
            ImGui.TextUnformatted($"{label}  ({strategy.EnemyStrategy}, {status})");
        }

        if (toRemove.HasValue)
        {
            saved.Remove(toRemove.Value);
            saveConfiguration();
        }
    }
}
