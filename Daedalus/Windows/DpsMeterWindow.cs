using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Daedalus.Localization;
using Daedalus.Services.Analytics;
using Daedalus.Windows.Common;

namespace Daedalus.Windows;

/// <summary>
/// The Parser window — full-party DPS meter with ACT-style bars. Two draw modes share the
/// same window and data: normal (title bar, header/footer chrome) and borderless (compact
/// semi-transparent overlay, optional click-through). Mockup: docs/ui-mockups/parser.html.
/// </summary>
public sealed class DpsMeterWindow : Window
{
    private const ImGuiWindowFlags NormalFlags = ImGuiWindowFlags.NoCollapse;
    private const ImGuiWindowFlags BorderlessFlags =
        ImGuiWindowFlags.NoTitleBar
        | ImGuiWindowFlags.NoResize
        | ImGuiWindowFlags.NoScrollbar
        | ImGuiWindowFlags.NoScrollWithMouse
        | ImGuiWindowFlags.NoCollapse
        | ImGuiWindowFlags.AlwaysAutoResize
        | ImGuiWindowFlags.NoFocusOnAppearing
        | ImGuiWindowFlags.NoNav;

    // Mythological aliases for the scramble toggle — assigned first-seen, stable per session.
    private static readonly string[] AliasPool =
    [
        "Eos", "Iris", "Dike", "Selene", "Helios", "Nyx", "Rhea", "Metis",
        "Leto", "Eris", "Gaia", "Thea", "Crius", "Ceto", "Pallas", "Doris",
        "Maia", "Clio", "Erato", "Thalia", "Urania", "Calliope", "Astraea", "Hemera",
    ];

    /// <summary>
    /// Healing accent — verdigris rather than the suite gold, so the two meters are never
    /// confused at a glance in a fight.
    /// </summary>
    private static readonly Vector4 HealAccent = new(0.31f, 0.75f, 0.60f, 1.00f);

    private readonly Configuration configuration;
    private readonly Action saveConfiguration;
    private readonly IDpsMeterService meter;
    private readonly Dictionary<uint, string> aliases = new();

    private int selectedIndex;

    public DpsMeterWindow(Configuration configuration, Action saveConfiguration, IDpsMeterService meter)
        : base("Parser##DaedalusParser", NormalFlags)
    {
        this.configuration = configuration;
        this.saveConfiguration = saveConfiguration;
        this.meter = meter;

        Size = new Vector2(360, 320);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(280, 140),
            MaximumSize = new Vector2(700, 900),
        };
    }

    public override void PreDraw()
    {
        var cfg = configuration.Parser;
        if (cfg.BorderlessMode)
        {
            Flags = BorderlessFlags | (cfg.ClickThrough ? ImGuiWindowFlags.NoInputs : ImGuiWindowFlags.None);
            BgAlpha = 0.88f;
        }
        else
        {
            Flags = NormalFlags;
            BgAlpha = null;
        }
    }

    public override bool DrawConditions()
        => !configuration.Parser.HideOutOfCombat || meter.Current != null;

    public override void Draw()
    {
        var entries = BuildEntryList();
        var borderless = configuration.Parser.BorderlessMode;

        if (entries.Count == 0)
        {
            ImGui.TextDisabled(Loc.T(LocalizedStrings.Parser.NoData, "No fights recorded yet"));
            return;
        }

        // Live fights always pin the selection back to the top entry.
        if (meter.Current != null)
            selectedIndex = 0;
        selectedIndex = Math.Clamp(selectedIndex, 0, entries.Count - 1);
        var encounter = entries[selectedIndex];

        // Borderless keeps whatever tab you left selected but drops the tab bar itself — the
        // whole point of that mode is no chrome.
        if (!borderless)
            DrawTabs();

        var healing = configuration.Parser.ShowHealingTab;

        DrawHeader(encounter, borderless, healing);
        DrawRows(encounter, borderless, healing);

        if (healing && !borderless)
            DrawShieldNotice();

        if (!borderless)
            DrawFooter(entries);
    }

    private void DrawTabs()
    {
        if (!ImGui.BeginTabBar("##ParserTabs", ImGuiTabBarFlags.None))
            return;

        try
        {
            if (ImGui.BeginTabItem(Loc.T(LocalizedStrings.Parser.DamageTab, "Damage")))
            {
                if (configuration.Parser.ShowHealingTab)
                {
                    configuration.Parser.ShowHealingTab = false;
                    saveConfiguration();
                }

                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem(Loc.T(LocalizedStrings.Parser.HealingTab, "Healing")))
            {
                if (!configuration.Parser.ShowHealingTab)
                {
                    configuration.Parser.ShowHealingTab = true;
                    saveConfiguration();
                }

                ImGui.EndTabItem();
            }
        }
        finally
        {
            ImGui.EndTabBar();
        }
    }

    /// <summary>
    /// Effective vs raw HPS. Both numbers are real; which one headlines is the judgement call.
    /// Effective is the default because raw rewards pouring casts into full health bars.
    /// </summary>
    private void DrawHealingModeToggle()
    {
        var raw = configuration.Parser.ShowRawHealing;

        if (ImGui.SmallButton(raw
                ? Loc.T(LocalizedStrings.Parser.RawHealing, "Raw")
                : Loc.T(LocalizedStrings.Parser.EffectiveHealing, "Effective")))
        {
            configuration.Parser.ShowRawHealing = !raw;
            saveConfiguration();
        }

        if (ImGui.IsItemHovered() && !configuration.Parser.ClickThrough)
        {
            ImGui.SetTooltip(raw
                ? "HPS includes overheal. Click for effective healing only."
                : "HPS excludes overheal. Click to include it.");
        }

        ImGui.SameLine();
        ImGui.TextColored(DaedalusTheme.TextDisabled,
            raw ? "HPS · overheal included" : "HPS · overheal excluded");
    }

    /// <summary>
    /// Barrier healing (Adloquium, Succor, Kerachole, Panhaima) lands as a STATUS, not a heal
    /// effect, so none of it reaches this meter. Scholars and Sages therefore read low. Saying
    /// so out loud beats a number that is confidently wrong for the two jobs it matters most to.
    /// </summary>
    private void DrawShieldNotice()
    {
        ImGui.TextColored(DaedalusTheme.TextDisabled,
            Loc.T(LocalizedStrings.Parser.ShieldsNotCounted, "Shields not counted — SCH/SGE read low"));

        if (ImGui.IsItemHovered() && !configuration.Parser.ClickThrough)
        {
            ImGui.SetTooltip(
                "Barrier healing is applied as a status effect rather than a heal, so the game\n"
                + "never reports it here. Adloquium, Eukrasian Prognosis, Succor, Kerachole and\n"
                + "Panhaima are all missing from these numbers.");
        }
    }

    private List<DpsEncounter> BuildEntryList()
    {
        var list = new List<DpsEncounter>();
        if (meter.Current is { } live)
            list.Add(live);
        list.AddRange(meter.History);
        return list;
    }

    private void DrawHeader(DpsEncounter encounter, bool borderless, bool healing)
    {
        var stateColor = encounter.IsActive ? DaedalusTheme.StatusGreen : DaedalusTheme.StatusGrey;
        ImGui.TextColored(stateColor, "●");
        ImGui.SameLine();

        var title = encounter.TargetName.Length > 0
            ? encounter.TargetName
            : Loc.T(LocalizedStrings.Parser.CurrentFight, "Current fight");
        if (borderless)
            ImGui.TextColored(DaedalusTheme.TextDisabled, title);
        else
            ImGui.TextColored(DaedalusTheme.TextSecondary, title);

        ImGui.SameLine();
        var duration = TimeSpan.FromSeconds(encounter.DurationSeconds);
        var timeLabel = $"{(int)duration.TotalMinutes}:{duration.Seconds:00}";

        if (borderless)
        {
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize(timeLabel).X);
            ImGui.TextDisabled(timeLabel);
        }
        else
        {
            ImGui.TextDisabled(timeLabel);

            var partyRate = FormatNumber(healing ? encounter.GetPartyHps() : encounter.GetPartyDps());
            var partyLabel = Loc.T(LocalizedStrings.Parser.PartyLabel, "Party");
            var width = ImGui.CalcTextSize(partyLabel).X + ImGui.CalcTextSize(partyRate).X + ImGui.GetStyle().ItemSpacing.X;
            ImGui.SameLine();
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X - width);
            ImGui.TextColored(DaedalusTheme.TextSecondary, partyLabel);
            ImGui.SameLine();
            ImGui.TextColored(healing ? HealAccent : DaedalusTheme.AccentGold, partyRate);

            ImGui.Separator();

            if (healing)
                DrawHealingModeToggle();
        }
    }

    private void DrawRows(DpsEncounter encounter, bool borderless, bool healing)
    {
        var ranked = healing ? encounter.GetRankedByHealing() : encounter.GetRanked();
        if (ranked.Count == 0)
        {
            ImGui.TextDisabled(healing
                ? Loc.T(LocalizedStrings.Parser.NoHealing, "No healing this fight")
                : Loc.T(LocalizedStrings.Parser.NoData, "No fights recorded yet"));
            return;
        }

        var top = Math.Max(1L, healing ? ranked[0].EffectiveHealing : ranked[0].EffectiveDamage);
        var dl = ImGui.GetWindowDrawList();
        var rowHeight = ImGui.GetTextLineHeight() + 5f;
        var scramble = configuration.Parser.ScrambleNames;
        var rawHealing = configuration.Parser.ShowRawHealing;

        // Teal bars on the healing tab so a glance never mistakes one meter for the other.
        var selfFill = ImGui.ColorConvertFloat4ToU32(new Vector4(0.85f, 0.65f, 0.20f, 0.22f));
        var otherFill = healing
            ? ImGui.ColorConvertFloat4ToU32(new Vector4(0.20f, 0.55f, 0.43f, 0.34f))
            : ImGui.ColorConvertFloat4ToU32(new Vector4(0.55f, 0.42f, 0.13f, 0.32f));
        var goldBar = ImGui.ColorConvertFloat4ToU32(DaedalusTheme.AccentGold);

        for (var i = 0; i < ranked.Count; i++)
        {
            var stats = ranked[i];
            var isSelf = stats.Kind == CombatantKind.Self;

            var rowMin = ImGui.GetCursorScreenPos();
            var avail = ImGui.GetContentRegionAvail().X;
            var rowMax = rowMin + new Vector2(avail, rowHeight);

            // Bars always scale on EFFECTIVE output, even when the raw toggle is on — otherwise
            // the longest bar could belong to whoever wasted the most.
            var fraction = (float)(healing ? stats.EffectiveHealing : stats.EffectiveDamage) / top;
            dl.AddRectFilled(rowMin, new Vector2(rowMin.X + avail * fraction, rowMax.Y), isSelf ? selfFill : otherFill, 2f);
            if (isSelf)
                dl.AddRectFilled(rowMin, new Vector2(rowMin.X + 2f, rowMax.Y), goldBar);

            var textY = rowMin.Y + 2.5f;
            var x = rowMin.X + 6f;

            if (!borderless)
            {
                dl.AddText(new Vector2(x, textY), ImGui.ColorConvertFloat4ToU32(DaedalusTheme.TextDisabled), $"{i + 1}");
                x += 16f;

                // Gold = you (exact), green = Daedalus toon self-reporting over IPC/LAN (exact),
                // grey = observed locally.
                var dotColor = isSelf ? DaedalusTheme.AccentGold
                             : stats.IsSelfReported ? DaedalusTheme.StatusGreen
                             : DaedalusTheme.StatusGrey;
                dl.AddText(new Vector2(x, textY), ImGui.ColorConvertFloat4ToU32(dotColor), "●");
                x += 16f;
            }

            dl.AddText(new Vector2(x, textY), ImGui.ColorConvertFloat4ToU32(DaedalusTheme.StatusGrey), stats.JobAbbrev);
            x += 34f;

            var name = DisplayName(stats, scramble, borderless);
            var nameColor = isSelf ? DaedalusTheme.AccentGold
                          : stats.Kind == CombatantKind.Support ? DaedalusTheme.TextSecondary
                          : DaedalusTheme.TextPrimary;
            dl.AddText(new Vector2(x, textY), ImGui.ColorConvertFloat4ToU32(nameColor), name);
            x += ImGui.CalcTextSize(name).X + 6f;

            // Self-reporting Daedalus toons lose the HUMAN tag — the green dot says it all.
            if (!borderless && stats.Kind != CombatantKind.Self
                && !(stats.Kind == CombatantKind.Player && stats.IsSelfReported))
            {
                var tag = stats.Kind == CombatantKind.Support
                    ? Loc.T(LocalizedStrings.Parser.TrustTag, "TRUST")
                    : Loc.T(LocalizedStrings.Parser.HumanTag, "HUMAN");
                dl.AddText(new Vector2(x, textY), ImGui.ColorConvertFloat4ToU32(DaedalusTheme.TextDisabled), tag);
            }

            // Right side. On healing, overheal% takes the slot damage gives to share: when you
            // are healing, waste is the number worth watching.
            var rate = healing
                ? FormatNumber(rawHealing ? encounter.GetRawHps(stats) : encounter.GetHps(stats))
                : FormatNumber(encounter.GetDps(stats));
            var trailing = healing
                ? $"{stats.OverhealPercent * 100f:F0}%"
                : $"{encounter.GetDamageShare(stats) * 100f:F1}%";
            var trailingWidth = 42f;
            var rateWidth = ImGui.CalcTextSize(rate).X;
            dl.AddText(new Vector2(rowMax.X - trailingWidth, textY), ImGui.ColorConvertFloat4ToU32(DaedalusTheme.TextSecondary), trailing);
            dl.AddText(new Vector2(rowMax.X - trailingWidth - 8f - rateWidth, textY), ImGui.ColorConvertFloat4ToU32(DaedalusTheme.TextPrimary), rate);

            ImGui.Dummy(new Vector2(avail, rowHeight));

            if (ImGui.IsMouseHoveringRect(rowMin, rowMax) && !configuration.Parser.ClickThrough)
            {
                // Composed manually (not the localized 4-arg format) so the optional DoT/HoT
                // segment can't garble translated format strings, and so "are my ticks tracked?"
                // is answerable per row.
                string tooltip;
                if (healing)
                {
                    tooltip = $"{name} — Effective {FormatNumber(stats.EffectiveHealing)}";
                    var direct = stats.TotalHealing - stats.HotHealing;
                    if (direct > 0)
                        tooltip += $" · direct {FormatNumber(direct)}";
                    if (stats.HotHealing > 0)
                        tooltip += $" · HoT {FormatNumber(stats.HotHealing)}";
                    tooltip += $" · overheal {FormatNumber(stats.Overheal)} ({stats.OverhealPercent * 100f:F0}%)";
                    if (stats.HealCount > 0)
                        tooltip += $" · crit {stats.HealCritPercent:F1}%";
                }
                else
                {
                    tooltip = $"{name} — Total {FormatNumber(stats.EffectiveDamage)}";
                    if (stats.DotDamage > 0)
                        tooltip += $" · DoT {FormatNumber(stats.DotDamage)}";
                    tooltip += $" · Crit {stats.CritPercent:F1}% · DH {stats.DirectHitPercent:F1}%";
                }

                ImGui.SetTooltip(tooltip);
            }
        }
    }

    private void DrawFooter(List<DpsEncounter> entries)
    {
        ImGui.Separator();

        ImGui.SetNextItemWidth(150f);
        var label = EntryLabel(entries[selectedIndex]);
        if (ImGui.BeginCombo("##ParserFightSelect", label))
        {
            for (var i = 0; i < entries.Count; i++)
            {
                if (ImGui.Selectable(EntryLabel(entries[i]) + $"##fight{i}", i == selectedIndex))
                    selectedIndex = i;
            }
            ImGui.EndCombo();
        }

        ImGui.SameLine();
        if (ImGui.SmallButton(Loc.T(LocalizedStrings.Parser.Reset, "Reset")))
        {
            meter.Reset();
            selectedIndex = 0;
        }

        // Undercount visibility: aggregated multi-source DoT ticks (typical with Trust casters
        // DoTing the same enemy) can't be attributed to a row — show the missing total so a
        // low-looking DoT job is explainable at a glance.
        var unattributed = entries[selectedIndex].UnattributedDotDamage;
        if (unattributed > 0)
        {
            ImGui.SameLine();
            ImGui.TextDisabled($"+{FormatNumber(unattributed)} DoT?");
            if (ImGui.IsItemHovered() && !configuration.Parser.ClickThrough)
                ImGui.SetTooltip("DoT tick damage that couldn't be attributed to anyone — the game merges all DoTs on a target into one tick, and with several casters DoTing (Trust allies) the source is ambiguous. Every DoT user's row is missing a share of this.");
        }

        ImGui.SameLine();
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize("(?)").X);
        DaedalusTheme.HelpMarker(Loc.T(
            LocalizedStrings.Parser.LegendTooltip,
            "Source dots:\n● gold — you (exact)\n● green — Daedalus toon, self-reported over IPC/LAN (exact)\n● grey — observed locally (Trusts, other players)\n\nDoT ticks are attributed to whoever applied the effect; ambiguous merged ticks are split by DoT potency (\"Estimate shared DoT ticks\" setting) or shown as \"+N DoT?\".")
            + $"\n\nThis fight: {entries[selectedIndex].DotTicksProcessed} DoT/HoT tick packet(s) received."
            + $"\n{meter.DescribeTickPipeline()}");
    }

    private string EntryLabel(DpsEncounter encounter)
    {
        if (encounter.IsActive)
            return Loc.T(LocalizedStrings.Parser.CurrentFight, "Current fight");

        var duration = TimeSpan.FromSeconds(encounter.DurationSeconds);
        var name = encounter.TargetName.Length > 0 ? encounter.TargetName : Loc.T(LocalizedStrings.Parser.Ended, "Ended");
        return $"{name} ({(int)duration.TotalMinutes}:{duration.Seconds:00})";
    }

    private string DisplayName(CombatantStats stats, bool scramble, bool borderless)
    {
        var name = stats.Name;
        if (scramble && stats.Kind != CombatantKind.Support)
        {
            if (!aliases.TryGetValue(stats.EntityId, out var alias))
            {
                alias = AliasPool[aliases.Count % AliasPool.Length];
                aliases[stats.EntityId] = alias;
            }
            name = alias;
        }

        if (borderless)
        {
            var space = name.IndexOf(' ');
            if (space > 0)
                name = name[..space];
        }

        return name;
    }

    internal static string FormatNumber(double value)
        => value >= 1_000_000 ? $"{value / 1_000_000:F1}M"
         : value >= 1_000 ? $"{value / 1_000:F1}k"
         : $"{value:F0}";
}
