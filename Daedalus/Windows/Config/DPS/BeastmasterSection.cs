using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Daedalus.Data;
using Daedalus.Services.Beastmaster;

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
    private readonly BattlehornReader bestiary = new();
    private string? flagError;

    // Hand-picked horn setter. Ticked from Draw, so it runs while this page is open — a Set takes a
    // couple of seconds. One request at a time; the Set buttons wait while one is running.
    private readonly BattlehornGame hornGame = new();
    private readonly BattlehornAssigner manualAssigner;
    private int[]? manualWanted;
    private readonly int[] manualPick = new int[3];

    private static readonly Vector4 Caught = new(0.55f, 0.85f, 0.55f, 1f);
    private static readonly Vector4 Dim = new(0.6f, 0.6f, 0.6f, 1f);

    public BeastmasterSection(Configuration config, Action save)
    {
        this.config = config;
        this.save = save;
        manualAssigner = new BattlehornAssigner(hornGame, oneShot: true);
    }

    public void Draw()
    {
        ConfigUIHelpers.JobHeader("Beastmaster", "Artemis", ConfigUIHelpers.BeastmasterColor);

        // Every description on this page wraps at the window edge instead of running off it.
        ImGui.PushTextWrapPos(0f);
        DrawInstinctSection();
        DrawFamiliarSection();
        DrawBattlehornSection();
        DrawCaptureSection();
        DrawBestiarySection();
        DrawLimitedJobNote();
        ImGui.PopTextWrapPos();
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

    private void DrawBattlehornSection()
    {
        if (!ConfigUIHelpers.SectionHeader("Battlehorns", "BST"))
            return;

        ConfigUIHelpers.BeginIndent();
        var cfg = config.Beastmaster;

        DrawHornSetter(cfg);
        ImGui.Spacing();

        ConfigUIHelpers.Toggle(
            "Auto-set Battlehorns",
            () => cfg.AutoSetBattlehorns,
            v => cfg.AutoSetBattlehorns = v,
            "Keeps the chosen team on your three Battlehorns. Out of combat, when the horns don't match "
            + "the team, Daedalus opens the Master's Bestiary, assigns the beasts one horn at a time and "
            + "closes it again. Changing a horn by hand while this is on gets it put back.", save);

        if (cfg.AutoSetBattlehorns)
        {
            var current = BattlehornTeams.ById(cfg.BattlehornTeam) ?? BattlehornTeams.All[0];
            ImGui.SetNextItemWidth(360);
            if (ImGui.BeginCombo("Team", current.Name))
            {
                foreach (var team in BattlehornTeams.All)
                {
                    if (ImGui.Selectable(team.Name, team == current))
                    {
                        cfg.BattlehornTeam = team.Id;
                        save();
                    }
                }
                ImGui.EndCombo();
            }
            ImGui.TextDisabled(current.UseFor);

            ImGui.Spacing();
            if (bestiary.UnlockedCount() is null)
                ImGui.TextDisabled("Your Bestiary has not loaded yet, so which beasts you own is unknown.");
            var resolved = BattlehornTeams.Resolve(current, cfg.SavedBattlehorns,
                no => bestiary.IsPetUnlocked((uint)no) == true);
            for (var horn = 0; horn < 3; horn++)
            {
                var row = resolved.Rows[horn];
                ImGui.TextUnformatted(row == 0
                    ? $"Horn {horn + 1}: (left as it is)"
                    : $"Horn {horn + 1}: {BstFamiliars.ByBestiaryNo((uint)row)?.Name ?? $"#{row}"}");
            }
            foreach (var missing in resolved.Missing)
                ConfigUIHelpers.WarningText($"Not captured yet, {missing}");

            ImGui.Spacing();
            if (ImGui.Button("Save current horns") && new BattlehornGame().ReadHorns() is { } horns)
            {
                cfg.SavedBattlehorns = horns;
                cfg.BattlehornTeam = BattlehornTeams.SavedId;
                save();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Stores the beasts on your horns right now as \"My saved horns\" and selects it.");

            ImGui.Spacing();
            ImGui.TextWrapped($"Status: {BattlehornAssigner.Status}");
            ImGui.TextDisabled(
                "Clear the Bestiary's filter and sort first: the clicks go by tile position, so a sorted "
                + "Bestiary would assign the wrong beast. Daedalus stops and says so if a horn does not "
                + "come out as expected.");

            ImGui.Spacing();
            ConfigUIHelpers.WarningText(
                "These teams are based on popular community postings (guides and forum threads). They are "
                + "starting points only, and in no way guarantee victory in the Crucible.");
        }

        ConfigUIHelpers.EndIndent();
    }

    /// <summary>
    /// What is on the three horns now, and a picker per horn: choose a caught beast, press Set, and
    /// Daedalus opens the Master's Bestiary, assigns it and closes it.
    /// </summary>
    private void DrawHornSetter(Daedalus.Config.DPS.BeastmasterConfig cfg)
    {
        manualAssigner.Tick(() => manualWanted, allowed: !InCombat());

        var horns = hornGame.ReadHorns();
        var loaded = bestiary.UnlockedCount() is not null;

        ImGui.TextUnformatted("On your Battlehorns now");
        for (var horn = 0; horn < 3; horn++)
        {
            var row = horns?[horn] ?? 0;
            var beast = BstFamiliars.ByBestiaryNo((uint)row);
            ImGui.BulletText(horns == null
                ? $"Horn {horn + 1}: unreadable"
                : row == 0
                    ? $"Horn {horn + 1}: empty"
                    : $"Horn {horn + 1}: {beast?.Name ?? $"#{row}"}"
                      + (beast != null ? $" ({beast.TrickAffinity} Trick, {beast.Classification})" : ""));
        }

        ImGui.Spacing();
        ImGui.TextUnformatted("Set a Battlehorn");
        for (var horn = 0; horn < 3; horn++)
            DrawHornPicker(cfg, horn, horns, loaded);

        if (!loaded)
            ImGui.TextDisabled("Your caught beasts haven't loaded yet, so every beast is listed.");
        if (manualWanted != null)
            ImGui.TextWrapped(manualAssigner.Message);
    }

    private void DrawHornPicker(Daedalus.Config.DPS.BeastmasterConfig cfg, int horn, int[]? horns, bool loaded)
    {
        if (manualPick[horn] == 0 && horns is { } h && h[horn] != 0)
            manualPick[horn] = h[horn];

        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted($"Horn {horn + 1}");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(220);
        var preview = BstFamiliars.ByBestiaryNo((uint)manualPick[horn]) is { } picked
            ? $"{picked.Name} ({picked.TrickAffinity})"
            : "Choose a beast";
        if (ImGui.BeginCombo($"##horn{horn}", preview))
        {
            foreach (var beast in BstFamiliars.All)
            {
                // Only beasts you own — unless the list hasn't loaded, then all of them (Set loads it).
                if (loaded && bestiary.IsPetUnlocked((uint)beast.BestiaryNo) != true)
                    continue;
                if (ImGui.Selectable($"{beast.BestiaryNo}. {beast.Name} ({beast.TrickAffinity})",
                        beast.BestiaryNo == manualPick[horn]))
                    manualPick[horn] = beast.BestiaryNo;
            }
            ImGui.EndCombo();
        }

        ImGui.SameLine();
        var onIt = horns is { } cur && cur[horn] == manualPick[horn];
        ImGui.BeginDisabled(cfg.AutoSetBattlehorns || manualPick[horn] == 0 || onIt || manualAssigner.Busy);
        if (ImGui.Button($"Set##sethorn{horn}"))
        {
            manualWanted = new int[3];
            manualWanted[horn] = manualPick[horn];
            manualAssigner.Restart();
        }
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
        {
            if (cfg.AutoSetBattlehorns)
                ImGui.SetTooltip("Turn Auto-set Battlehorns off first, or it will put the team back.");
            else if (onIt)
                ImGui.SetTooltip("Already on this horn.");
        }
    }

    private static unsafe bool InCombat()
    {
        var conditions = FFXIVClientStructs.FFXIV.Client.Game.Conditions.Instance();
        return conditions != null && conditions->InCombat;
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
                "Applies Capture automatically to beasts you haven't caught yet, timed so the kill "
                + "lands inside the 120s window. Knows which enemies count as which beast from a "
                + "community list of enemy names (and from your own Gauge scans, which win), skips "
                + "beasts already in your Bestiary and enemies above your level.", save);

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

    private void DrawBestiarySection()
    {
        if (!ConfigUIHelpers.SectionHeader("Bestiary: where to catch them", "BST"))
            return;

        ConfigUIHelpers.BeginIndent();
        ImGui.TextDisabled(bestiary.UnlockedCount() is { } count
            ? $"Captured {count}/50. Caught beasts are green."
            : "Your Bestiary has not loaded yet, so caught beasts are not marked.");
        ImGui.TextDisabled(
            "Be at or above the beast's level, bring it low, Capture it, then kill it. Flag drops the map "
            + "flag on the spot and opens the map.");
        if (flagError != null)
            ConfigUIHelpers.WarningText(flagError);

        if (ImGui.BeginTable("BstCaptureSpots", 5,
                ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY
                | ImGuiTableFlags.SizingStretchProp,
                new Vector2(0, 380)))
        {
            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableSetupColumn("Beast", ImGuiTableColumnFlags.WidthFixed, 130f);
            ImGui.TableSetupColumn("Lv", ImGuiTableColumnFlags.WidthFixed, 26f);
            ImGui.TableSetupColumn("Trick", ImGuiTableColumnFlags.WidthFixed, 64f);
            ImGui.TableSetupColumn("Where");
            ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, 40f);
            ImGui.TableHeadersRow();

            foreach (var beast in BstFamiliars.All)
            {
                var spot = BstCaptureSpots.ByBestiaryNo(beast.BestiaryNo);
                var colour = bestiary.IsPetUnlocked((uint)beast.BestiaryNo) == true
                    ? Caught
                    : ImGui.GetStyle().Colors[(int)ImGuiCol.Text];

                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.TextColored(colour, $"{beast.BestiaryNo}. {beast.Name}");
                ImGui.TableNextColumn();
                ImGui.TextColored(colour, beast.CaptureLevel.ToString());
                ImGui.TableNextColumn();
                ImGui.TextColored(Dim, beast.TrickAffinity.ToString());
                ImGui.TableNextColumn();
                ImGui.TextWrapped(spot?.Where ?? "");
                ImGui.TableNextColumn();
                if (spot is not { CanFlag: true })
                    continue;
                if (ImGui.SmallButton($"Flag##bst{beast.BestiaryNo}"))
                    flagError = CaptureSpotMap.FlagAndOpen(spot);
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip($"{spot.Zone} ({spot.MapX:0.0}, {spot.MapY:0.0})");
            }

            ImGui.EndTable();
        }

        ConfigUIHelpers.EndIndent();
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
            "Battlehorns are never pressed automatically: which familiar to summon, and when, is your choice.");
    }
}
