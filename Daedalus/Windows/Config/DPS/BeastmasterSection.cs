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
