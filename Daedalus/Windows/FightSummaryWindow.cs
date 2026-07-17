using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using Daedalus.Data;
using Daedalus.Localization;
using Daedalus.Models;
using Daedalus.Services.Analytics;

namespace Daedalus.Windows;

/// <summary>
/// Post-combat summary popup that auto-shows after a fight ends.
/// Displays GCD uptime, estimated DPS, grade, and coaching callouts.
/// </summary>
public sealed class FightSummaryWindow : Window, IDisposable
{
    private readonly IFightSummaryService _service;
    private readonly IFramework _framework;
    private readonly Configuration _config;
    private readonly Action _openAnalytics;

    private FightSummaryRecord? _record;
    private float _delayRemaining;
    private bool _waitingForDelay;

    // Theme sweep: status/accent from the shared identity (EstDps keeps a distinct data-viz
    // hue — it is a metric, not a status, and must not collide with the uptime green).
    private static readonly Vector4 GcdUptimeColor = Common.DaedalusTheme.StatusGreen;
    private static readonly Vector4 PercentileColor = Common.DaedalusTheme.AccentGold;
    private static readonly Vector4 EstDpsColor = new(0.38f, 0.65f, 0.98f, 1f);

    // Grade colors: S = the gold accent (the identity's one flourish), A→D = status ramp.
    private static readonly Vector4 GradeS = Common.DaedalusTheme.AccentGold;
    private static readonly Vector4 GradeA = Common.DaedalusTheme.StatusGreen;
    private static readonly Vector4 GradeB = Common.DaedalusTheme.StatusYellow;
    private static readonly Vector4 GradeC = Common.DaedalusTheme.AccentDim;
    private static readonly Vector4 GradeD = Common.DaedalusTheme.StatusRed;

    // Callout severity colors
    private static readonly Vector4 CriticalColor = Common.DaedalusTheme.StatusRed;
    private static readonly Vector4 WarningColor = Common.DaedalusTheme.StatusYellow;
    private static readonly Vector4 GoodColor = Common.DaedalusTheme.StatusGreen;

    public FightSummaryWindow(
        IFightSummaryService service,
        IFramework framework,
        Configuration config,
        Action openAnalytics)
        : base(
            Loc.T(LocalizedStrings.FightSummary.WindowTitle, "Fight Complete"),
            ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoCollapse)
    {
        _service = service;
        _framework = framework;
        _config = config;
        _openAnalytics = openAnalytics;

        IsOpen = false;

        _service.OnSummaryReady += OnSummaryReady;
    }

    private void OnSummaryReady(FightSummaryRecord record)
    {
        _record = record;

        if (!_config.Analytics.ShowSummaryOnCombatEnd)
            return;

        _delayRemaining = _config.Analytics.SummaryPopupDelaySeconds;

        if (_delayRemaining <= 0f)
        {
            IsOpen = true;
            return;
        }

        _waitingForDelay = true;
        _framework.Update += OnFrameworkUpdate;
    }

    private void OnFrameworkUpdate(IFramework fw)
    {
        // Use a fixed frame delta approximation — Dalamud framework.Update
        // doesn't expose delta time directly. 16ms matches the game's ~60fps tick.
        _delayRemaining -= 0.016f;

        if (_delayRemaining > 0f)
            return;

        IsOpen = true;
        _waitingForDelay = false;
        _framework.Update -= OnFrameworkUpdate;
    }

    public override void Draw()
    {
        if (_record == null)
            return;

        // Enforce minimum width (AlwaysAutoResize ignores Size)
        ImGui.Dummy(new Vector2(420, 0));

        // Title area: job name + zone + duration
        var jobName = JobRegistry.GetJobName(_record.JobId);
        ImGui.TextDisabled($"{jobName} · {_record.ZoneName} · {_record.Duration.Minutes}m{_record.Duration.Seconds:D2}s");

        ImGui.Spacing();

        // Stats strip
        DrawStatsStrip();

        ImGui.Spacing();
        ImGui.Spacing();

        // "Improve Next Pull" callouts
        DrawCallouts();

        // Footer
        DrawFooter();
    }

    private void DrawStatsStrip()
    {
        if (!ImGui.BeginTable("##FightSummaryStats", 4, ImGuiTableFlags.BordersInnerV))
            return;

        ImGui.TableNextColumn();
        DrawStatCell(
            Loc.T(LocalizedStrings.FightSummary.GcdUptime, "GCD Uptime"),
            $"{_record!.GcdUptimePercent:F0}%",
            GcdUptimeColor);

        ImGui.TableNextColumn();
        DrawStatCell(
            Loc.T(LocalizedStrings.FightSummary.Percentile, "Percentile"),
            _record.FflogsPercentile?.ToString() ?? "\u2014",
            PercentileColor);

        ImGui.TableNextColumn();
        DrawStatCell(
            Loc.T(LocalizedStrings.FightSummary.EstDps, "Est. DPS"),
            $"~{_record.EstimatedDps:F0}",
            EstDpsColor);

        ImGui.TableNextColumn();
        DrawStatCell(
            Loc.T(LocalizedStrings.FightSummary.Grade, "Grade"),
            _record.Grade,
            GetGradeColor(_record.Grade));

        ImGui.EndTable();
    }

    private static void DrawStatCell(string label, string value, Vector4 color)
    {
        // Center-aligned big value
        var valueSize = ImGui.CalcTextSize(value);
        var columnWidth = ImGui.GetColumnWidth();
        var valueOffset = (columnWidth - valueSize.X) * 0.5f;
        if (valueOffset > 0f)
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + valueOffset);

        ImGui.PushStyleColor(ImGuiCol.Text, color);
        ImGui.Text(value);
        ImGui.PopStyleColor();

        // Center-aligned small label below
        var labelSize = ImGui.CalcTextSize(label);
        var labelOffset = (columnWidth - labelSize.X) * 0.5f;
        if (labelOffset > 0f)
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + labelOffset);

        ImGui.TextDisabled(label);
    }

    private void DrawCallouts()
    {
        if (_record == null || _record.Callouts.Count == 0)
            return;

        ImGui.TextDisabled(
            Loc.T(LocalizedStrings.FightSummary.ImproveNextPull, "Improve Next Pull")
                .ToUpperInvariant());
        ImGui.Spacing();

        foreach (var callout in _record.Callouts)
        {
            var severityColor = GetSeverityColor(callout.Severity);

            // Colored title
            ImGui.PushStyleColor(ImGuiCol.Text, severityColor);
            ImGui.Text(callout.Title);
            ImGui.PopStyleColor();

            // Description below
            ImGui.TextDisabled(callout.Description);
            ImGui.Spacing();
        }
    }

    private void DrawFooter()
    {
        ImGui.Separator();

        var pullCount = _service.RecentSummaries.Count;
        ImGui.TextDisabled(
            $"{Loc.T(LocalizedStrings.FightSummary.SavedToHistory, "Saved to history")} \u00b7 pull {pullCount}");

        ImGui.SameLine();

        if (ImGui.SmallButton(Loc.T(LocalizedStrings.FightSummary.ViewInAnalytics, "View in Analytics \u2192")))
        {
            _openAnalytics();
        }
    }

    private static Vector4 GetGradeColor(string grade)
    {
        if (grade.StartsWith("S"))
            return GradeS;
        if (grade.StartsWith("A"))
            return GradeA;
        if (grade.StartsWith("B"))
            return GradeB;
        if (grade.StartsWith("C"))
            return GradeC;
        return GradeD;
    }

    private static Vector4 GetSeverityColor(CalloutSeverity severity) => severity switch
    {
        CalloutSeverity.Critical => CriticalColor,
        CalloutSeverity.Warning => WarningColor,
        CalloutSeverity.Good => GoodColor,
        _ => WarningColor,
    };

    public void Dispose()
    {
        _service.OnSummaryReady -= OnSummaryReady;

        if (_waitingForDelay)
            _framework.Update -= OnFrameworkUpdate;
    }
}
