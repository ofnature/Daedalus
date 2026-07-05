using System.Numerics;
using System.Text;
using Dalamud.Bindings.ImGui;
using Daedalus.Services.Debug;

namespace Daedalus.Windows.Debug.Tabs;

/// <summary>
/// Debug Log tab: a curated, low-noise feed of meaningful diagnostic events (refused casts, failed
/// BossMod config pushes) — coalesced so a stall reads as one line with a count, not a flood. Mirrors to
/// <c>daedalus-debug.log</c> when file logging is on.
/// </summary>
public static class DebugLogTab
{
    private static bool _showAction = true;
    private static bool _showNav = true;
    private static bool _showTargeting = true;
    private static bool _showGeneral = true;
    private static bool _autoScroll = true;

    private static readonly Vector4 ColError = new(1f, 0.35f, 0.35f, 1f);
    private static readonly Vector4 ColWarning = new(1f, 0.8f, 0.25f, 1f);
    private static readonly Vector4 ColInfo = new(0.7f, 0.7f, 0.7f, 1f);
    private static readonly Vector4 ColDim = new(0.55f, 0.55f, 0.55f, 1f);
    private static readonly Vector4 ColCount = new(0.4f, 0.85f, 1f, 1f);

    public static void Draw(DebugLogService service, Configuration config)
    {
        var entries = service.GetSnapshot();

        // ── Controls ──────────────────────────────────────────────────────────────────────────────────
        var writeFile = config.Debug.EnableDebugLogFile;
        if (ImGui.Checkbox("Write to file", ref writeFile))
        {
            config.Debug.EnableDebugLogFile = writeFile;
        }
        if (ImGui.IsItemHovered() && !string.IsNullOrEmpty(service.FilePath))
            ImGui.SetTooltip(service.FilePath);

        ImGui.SameLine();
        ImGui.Checkbox("Auto-scroll", ref _autoScroll);

        ImGui.SameLine();
        var dumpPackets = config.Debug.DumpRawCombatPackets;
        if (ImGui.Checkbox("Raw packets", ref dumpPackets))
        {
            config.Debug.DumpRawCombatPackets = dumpPackets;
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Dump raw [ActorControl] and [ScreenLog] combat packets here and to the Dalamud log.\nDiagnostic firehose for re-deriving packet layouts after a game patch — noisy, keep off in normal play.");

        ImGui.SameLine(ImGui.GetContentRegionAvail().X - 130);
        if (ImGui.Button("Copy"))
        {
            var sb = new StringBuilder();
            // Copy in chronological order (snapshot is newest-first).
            for (var i = entries.Count - 1; i >= 0; i--)
            {
                var e = entries[i];
                if (!IsVisible(e)) continue;
                sb.Append(e.LastTimestamp.ToLocalTime().ToString("HH:mm:ss.fff"))
                  .Append(" [").Append(e.Severity).Append("] [").Append(e.Category).Append("] ")
                  .Append(e.Message);
                if (e.Count > 1) sb.Append(" ×").Append(e.Count);
                sb.AppendLine();
            }
            ImGui.SetClipboardText(sb.ToString());
        }
        ImGui.SameLine();
        if (ImGui.Button("Clear"))
            service.Clear();

        // ── Filters ───────────────────────────────────────────────────────────────────────────────────
        ImGui.TextColored(ColDim, "Show:");
        ImGui.SameLine();
        ImGui.Checkbox("Action", ref _showAction);
        ImGui.SameLine();
        ImGui.Checkbox("Nav", ref _showNav);
        ImGui.SameLine();
        ImGui.Checkbox("Targeting", ref _showTargeting);
        ImGui.SameLine();
        ImGui.Checkbox("General", ref _showGeneral);

        ImGui.Separator();

        // ── Log ───────────────────────────────────────────────────────────────────────────────────────
        if (ImGui.BeginChild("DebugLogList", new Vector2(0, -1), true, ImGuiWindowFlags.HorizontalScrollbar))
        {
            var any = false;
            foreach (var e in entries)
            {
                if (!IsVisible(e))
                    continue;
                any = true;
                DrawEntry(e);
            }

            if (!any)
                ImGui.TextColored(ColDim, "No diagnostic events yet. Refused casts and failed BossMod pushes will appear here.");

            if (_autoScroll && ImGui.GetScrollY() >= ImGui.GetScrollMaxY() - 1f)
                ImGui.SetScrollHereY(1f);
        }
        ImGui.EndChild();
    }

    private static void DrawEntry(DebugLogEntry e)
    {
        ImGui.TextColored(ColDim, e.LastTimestamp.ToLocalTime().ToString("HH:mm:ss"));
        ImGui.SameLine();
        ImGui.TextColored(ColDim, $"[{e.Category}]");
        ImGui.SameLine();
        ImGui.TextColored(SeverityColor(e.Severity), e.Message);
        if (e.Count > 1)
        {
            ImGui.SameLine();
            ImGui.TextColored(ColCount, $"×{e.Count}");

            // Span between first and last occurrence = how long this condition has persisted (e.g. a stall).
            var span = e.LastTimestamp - e.FirstTimestamp;
            if (span.TotalSeconds >= 1.0)
            {
                ImGui.SameLine();
                ImGui.TextColored(ColDim, $"over {span.TotalSeconds:F1}s");
            }
        }
    }

    private static Vector4 SeverityColor(DebugLogSeverity severity) => severity switch
    {
        DebugLogSeverity.Error => ColError,
        DebugLogSeverity.Warning => ColWarning,
        _ => ColInfo,
    };

    private static bool IsVisible(DebugLogEntry e) => e.Category switch
    {
        DebugLogCategory.Action => _showAction,
        DebugLogCategory.Nav => _showNav,
        DebugLogCategory.Targeting => _showTargeting,
        _ => _showGeneral,
    };
}
