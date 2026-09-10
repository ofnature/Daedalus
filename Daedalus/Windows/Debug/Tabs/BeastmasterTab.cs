#if DEBUG
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Daedalus.Services.Beastmaster;

namespace Daedalus.Windows.Debug.Tabs;

/// <summary>
/// Beastmaster capture-research tab: everything the Gauge scans have taught us so far.
///
/// <para>
/// <b>DEBUG BUILD ONLY</b>, like the collection tooling it displays. The auto-capture rule that
/// reads the same ledger ships to everyone; this window does not.
/// </para>
/// </summary>
public static class BeastmasterTab
{
    private static readonly Vector4 Green = new(0.49f, 0.79f, 0.49f, 1f);
    private static readonly Vector4 Yellow = new(0.88f, 0.78f, 0.42f, 1f);
    private static readonly Vector4 Red = new(0.85f, 0.45f, 0.45f, 1f);
    private static readonly Vector4 Dim = new(0.54f, 0.54f, 0.58f, 1f);

    private static string _filter = "";
    private static bool _onlyCapturable;
    private static bool _onlyUnparsed;

    public static void Draw(BeastCaptureLedger? ledger, GaugeScanWatcher? watcher, string battlehorns)
    {
        if (ledger is null)
        {
            ImGui.TextColored(Red, "No capture ledger — plugin still starting?");
            return;
        }

        ImGui.TextColored(Dim, battlehorns);
        ImGui.Separator();

        ImGui.Text($"Beasts recorded: {ledger.Count}");
        if (watcher is not null)
        {
            ImGui.SameLine();
            ImGui.TextColored(Dim, $"   scans this session: {watcher.ScansThisSession}");
        }

        if (ImGui.Button("Copy export"))
            ImGui.SetClipboardText(ledger.Export());
        ImGui.SameLine();
        if (ImGui.Button("Save now"))
            ledger.Save();

        if (watcher is not null && !string.IsNullOrWhiteSpace(watcher.LastRawText))
        {
            ImGui.Spacing();
            ImGui.TextColored(Dim, "Last raw scan text:");
            ImGui.TextWrapped(watcher.LastRawText);
        }

        ImGui.Spacing();
        ImGui.SetNextItemWidth(200);
        ImGui.InputText("Filter", ref _filter, 64);
        ImGui.SameLine();
        ImGui.Checkbox("Capturable only", ref _onlyCapturable);
        ImGui.SameLine();
        ImGui.Checkbox("Unparsed only", ref _onlyUnparsed);

        // "Unparsed only" is the working view while the scan wording is still unknown: these are
        // the rows whose raw text the phrase table failed on, i.e. exactly what needs reading.
        var rows = Filter(ledger.Entries, _filter, _onlyCapturable, _onlyUnparsed);

        ImGui.Spacing();
        if (rows.Count == 0)
        {
            ImGui.TextColored(Dim, ledger.Count == 0
                ? "Nothing scanned yet. Use Gauge on a beast with scan logging enabled."
                : "No rows match the filter.");
            return;
        }

        if (!ImGui.BeginTable("bst_captures", 7,
                ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY
                | ImGuiTableFlags.Resizable, new Vector2(0, 320)))
            return;

        ImGui.TableSetupColumn("Beast", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Lv", ImGuiTableColumnFlags.WidthFixed, 32);
        ImGui.TableSetupColumn("Difficulty", ImGuiTableColumnFlags.WidthFixed, 80);
        ImGui.TableSetupColumn("Cap?", ImGuiTableColumnFlags.WidthFixed, 42);
        ImGui.TableSetupColumn("Got?", ImGuiTableColumnFlags.WidthFixed, 42);
        ImGui.TableSetupColumn("Zone", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Pos (player)", ImGuiTableColumnFlags.WidthFixed, 140);
        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableHeadersRow();

        foreach (var e in rows)
        {
            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(e.Name);
            if (ImGui.IsItemHovered() && !string.IsNullOrWhiteSpace(e.RawText))
                ImGui.SetTooltip(e.RawText);

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(e.Level > 0 ? e.Level.ToString() : "?");

            ImGui.TableNextColumn();
            var tierColour = e.Difficulty switch
            {
                BeastCaptureDifficulty.Unknown => Dim,
                BeastCaptureDifficulty.Impossible => Red,
                BeastCaptureDifficulty.Hard or BeastCaptureDifficulty.Extreme => Yellow,
                _ => Green,
            };
            ImGui.TextColored(tierColour, e.Difficulty.ToString());

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(e.Capturable is null ? "?" : e.Capturable.Value ? "yes" : "no");

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(e.AlreadyCaptured is null ? "?" : e.AlreadyCaptured.Value ? "yes" : "no");

            ImGui.TableNextColumn();
            ImGui.TextUnformatted($"{e.TerritoryName} ({e.TerritoryId})");

            ImGui.TableNextColumn();
            ImGui.TextUnformatted($"{e.PlayerX:0.0}, {e.PlayerY:0.0}, {e.PlayerZ:0.0}");
        }

        ImGui.EndTable();
        ImGui.TextColored(Dim, "Position is where YOU stood when scanning, not the beast's spawn point.");
    }

    /// <summary>Pure so the filter behaviour is testable without ImGui.</summary>
    internal static List<BeastCaptureEntry> Filter(
        IReadOnlyList<BeastCaptureEntry> entries, string filter, bool onlyCapturable, bool onlyUnparsed)
    {
        IEnumerable<BeastCaptureEntry> q = entries;

        if (!string.IsNullOrWhiteSpace(filter))
        {
            q = q.Where(e =>
                e.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || e.TerritoryName.Contains(filter, StringComparison.OrdinalIgnoreCase));
        }

        if (onlyCapturable)
            q = q.Where(e => e.Capturable == true);

        if (onlyUnparsed)
            q = q.Where(e => e.Difficulty == BeastCaptureDifficulty.Unknown);

        return q.ToList();
    }
}
#endif
