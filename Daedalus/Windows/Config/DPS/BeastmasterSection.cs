using System;
using Dalamud.Bindings.ImGui;
using Daedalus.Data;

namespace Daedalus.Windows.Config.DPS;

/// <summary>
/// Renders the Beastmaster (Artemis) settings section.
/// <para>
/// Deliberately short. Beastmaster is a limited job — Lv50 cap, no role actions at all — so there is
/// no Role Actions group here, unlike every other job section. Its absence is a fact about the job,
/// not an omission.
/// </para>
/// </summary>
public sealed class BeastmasterSection
{
    private readonly Configuration config;
    private readonly Action save;

    public BeastmasterSection(Configuration config, Action save)
    {
        this.config = config;
        this.save = save;
    }

    public void Draw()
    {
        ConfigUIHelpers.JobHeader("Beastmaster", "Artemis", ConfigUIHelpers.BeastmasterColor);

        DrawInstinctSection();
        DrawFamiliarSection();
        DrawCaptureSection();
        DrawLimitedJobNote();
    }

    private void DrawInstinctSection()
    {
        if (ConfigUIHelpers.SectionHeader("Instinctual Skills", "BST"))
        {
            ConfigUIHelpers.BeginIndent();

            ConfigUIHelpers.Toggle(
                "Enable instinctual skills",
                () => config.Beastmaster.EnableInstinctualSkills,
                v => config.Beastmaster.EnableInstinctualSkills = v,
                "Avalanche, Mistral, Spinning and Gale Axe. Their recast is independent of the GCD, "
                + "so they fire between combo hits rather than replacing them.", save);

            ConfigUIHelpers.Toggle(
                "Prefer intentional combos",
                () => config.Beastmaster.PreferIntentionalCombos,
                v => config.Beastmaster.PreferIntentionalCombos = v,
                "Pick the affinity that runs clockwise on the Inner Compass to complete a Sunstrider "
                + "or Moonstalker combo. When none is ready, an off-order skill is still used — the "
                + "chain length itself raises combo potency, so keeping the chain alive wins.", save);

            ConfigUIHelpers.EndIndent();
        }
    }

    private void DrawFamiliarSection()
    {
        if (ConfigUIHelpers.SectionHeader("Familiar", "BST"))
        {
            ConfigUIHelpers.BeginIndent();

            ConfigUIHelpers.Toggle(
                "Order Trick",
                () => config.Beastmaster.EnableTrick,
                v => config.Beastmaster.EnableTrick = v,
                "Orders your familiar's instinctual skill. Costs familiar TP, and counts toward the "
                + "instinct chain the same as your own skills.", save);

            ConfigUIHelpers.Toggle(
                "Use Parting Blow",
                () => config.Beastmaster.EnablePartingBlow,
                v => config.Beastmaster.EnablePartingBlow = v,
                "1,000 potency to the target and everything within 8y — but your familiar RETREATS "
                + "afterwards, so Trick stops until you summon again. Off by default.", save);

            ConfigUIHelpers.EndIndent();
        }
    }

    private void DrawCaptureSection()
    {
        if (ConfigUIHelpers.SectionHeader("Capture", "BST"))
        {
            ConfigUIHelpers.BeginIndent();

            ConfigUIHelpers.Toggle(
                "Auto-capture known beasts",
                () => config.Beastmaster.EnableAutoCapture,
                v => config.Beastmaster.EnableAutoCapture = v,
                "Applies Capture automatically to beasts already recorded as capturable, timed so "
                + "the kill lands inside the 120s window. Does nothing against a beast that has not "
                + "been scanned — it never guesses.", save);

            ImGui.BeginDisabled(!config.Beastmaster.EnableAutoCapture);

            var lead = config.Beastmaster.CaptureLeadSeconds;
            ImGui.SetNextItemWidth(160);
            if (ImGui.SliderFloat("Apply this close to death (s)", ref lead, 5f, 60f, "%.0f"))
            {
                config.Beastmaster.CaptureLeadSeconds = lead;
                save();
            }
            ImGui.TextDisabled(
                "Lower wastes less of the 120s window; higher is safer if the kill takes longer "
                + "than estimated.");

            var margin = config.Beastmaster.CaptureSafetyMarginSeconds;
            ImGui.SetNextItemWidth(160);
            if (ImGui.SliderFloat("Safety margin (s)", ref margin, 0f, 30f, "%.0f"))
            {
                config.Beastmaster.CaptureSafetyMarginSeconds = margin;
                save();
            }
            ImGui.TextDisabled(
                "Slack kept against a time-to-kill estimate that runs low. Better slightly early "
                + "than a missed window.");

            ImGui.EndDisabled();

            ImGui.Spacing();
            ImGui.TextDisabled(
                "If time-to-kill cannot be estimated confidently, auto-capture stands down and "
                + "leaves the timing to you.");

            ConfigUIHelpers.EndIndent();
        }
    }

    private void DrawLimitedJobNote()
    {
        ImGui.Spacing();
        ImGui.TextWrapped(
            "Beastmaster is a limited job: level 1-50, no role actions, and barred from roulettes, "
            + "Eureka/Bozja/Occult Crescent, Variant and Criterion, Ultimates, Deep Dungeons and PvP. "
            + "Duty automation, content overrides and party coordination are suppressed for it, and it "
            + "does not auto-move for positionals or max-melee range keeping.");
        ImGui.Spacing();
        ImGui.TextDisabled(
            "Battlehorns are never pressed automatically — which familiar to summon is your choice.");
    }
}
