using System.Collections.Generic;
using Dalamud.Bindings.ImGui;
using Daedalus.Windows.Common;

namespace Daedalus.Windows.Config;

/// <summary>
/// Shared header for job config sections: job name in gold, Greek codename in disabled grey,
/// and a validation chip right-aligned (docs/ui-mockups/config-window.html). The ledger is
/// hand-maintained — update it when a job passes (or loses) its in-game validation status;
/// the memory file daedalus-trust-validation-status is the source of truth.
/// </summary>
public static class JobSectionHeader
{
    public enum JobValidation { Validated, Pending, Untested }

    private static readonly Dictionary<ConfigSection, (string Job, string Codename, JobValidation State)> Ledger = new()
    {
        [ConfigSection.Paladin]     = ("Paladin", "Themis", JobValidation.Validated),
        [ConfigSection.Warrior]     = ("Warrior", "Ares", JobValidation.Validated),
        [ConfigSection.DarkKnight]  = ("Dark Knight", "Nyx", JobValidation.Validated),
        [ConfigSection.Gunbreaker]  = ("Gunbreaker", "Hephaestus", JobValidation.Validated),
        [ConfigSection.WhiteMage]   = ("White Mage", "Apollo", JobValidation.Validated),
        [ConfigSection.Scholar]     = ("Scholar", "Athena", JobValidation.Validated),      // full pass 2026-07-04
        [ConfigSection.Astrologian] = ("Astrologian", "Astraea", JobValidation.Validated),
        [ConfigSection.Sage]        = ("Sage", "Asclepius", JobValidation.Pending),        // Kardia rework v0.1.12 awaits live pass
        [ConfigSection.Monk]        = ("Monk", "Kratos", JobValidation.Validated),
        [ConfigSection.Dragoon]     = ("Dragoon", "Zeus", JobValidation.Validated),        // Lv100 pass 2026-07-05
        [ConfigSection.Ninja]       = ("Ninja", "Hermes", JobValidation.Validated),
        [ConfigSection.Samurai]     = ("Samurai", "Nike", JobValidation.Validated),
        [ConfigSection.Reaper]      = ("Reaper", "Thanatos", JobValidation.Validated),
        [ConfigSection.Viper]       = ("Viper", "Echidna", JobValidation.Validated),
        [ConfigSection.Bard]        = ("Bard", "Calliope", JobValidation.Pending),         // mid-level pass 2026-07-04; 52/56/72+ milestones remain
        [ConfigSection.Machinist]   = ("Machinist", "Prometheus", JobValidation.Validated),
        [ConfigSection.Dancer]      = ("Dancer", "Terpsichore", JobValidation.Validated),
        [ConfigSection.BlackMage]   = ("Black Mage", "Hecate", JobValidation.Validated),   // sign-off 2026-07-05, all milestones closed
        [ConfigSection.Summoner]    = ("Summoner", "Persephone", JobValidation.Validated), // full pass 2026-07-06 incl. Lv100 Solar Bahamut
        [ConfigSection.RedMage]     = ("Red Mage", "Circe", JobValidation.Validated),
        [ConfigSection.Pictomancer] = ("Pictomancer", "Iris", JobValidation.Validated),
        [ConfigSection.BlueMage]    = ("Blue Mage", "Proteus", JobValidation.Untested),
    };

    /// <summary>Draws the header when the section is a job section; no-op otherwise.</summary>
    public static void Draw(ConfigSection section)
    {
        if (!Ledger.TryGetValue(section, out var entry))
            return;

        ImGui.TextColored(DaedalusTheme.AccentGold, entry.Job);
        ImGui.SameLine();
        ImGui.TextColored(DaedalusTheme.TextDisabled, entry.Codename);

        var (chipText, chipColor) = entry.State switch
        {
            JobValidation.Validated => ("● validated", DaedalusTheme.StatusGreen),
            JobValidation.Pending   => ("● pending", DaedalusTheme.StatusYellow),
            _                       => ("● untested", DaedalusTheme.StatusGrey),
        };
        // Right-align inside the CONTENT region — GetWindowWidth includes the scrollbar and
        // ran the chip underneath it.
        ImGui.SameLine();
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize(chipText).X);
        ImGui.TextColored(chipColor, chipText);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("In-game validation status: has this job had a full\nTrust/AutoDuty pass since its last behavior change?");

        ImGui.Spacing();
    }
}
