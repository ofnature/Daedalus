using System.Linq;
using Daedalus.Data;
using Daedalus.Windows.Common;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace Daedalus.Windows;

/// <summary>
/// The points a Crucible run scored, kept on screen after the game's result screen closes (the autopilot closes it within
/// seconds). Opens after Clear and Legendary runs; for Legendary it also says how far the run was from the line and which
/// of the board's bonuses it missed.
/// </summary>
public sealed class CrucibleResultWindow : Window
{
    private CrucibleRunResult? _result;
    private CrucibleBoard? _board;
    private bool _legendary;

    public CrucibleResultWindow()
        : base("Crucible result##DaedalusCrucibleResult", ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoFocusOnAppearing)
    {
    }

    /// <summary>Show a run's result. <paramref name="board"/> gives the Legendary line and the bonuses on offer.</summary>
    public void Show(CrucibleRunResult result, CrucibleBoard? board, bool legendary)
    {
        _result = result;
        _board = board;
        _legendary = legendary;
        IsOpen = true;
    }

    public override void Draw()
    {
        if (_result is not { } r)
        {
            ImGui.TextColored(DaedalusTheme.TextSecondary, "No run finished yet.");
            return;
        }

        ImGui.TextColored(DaedalusTheme.AccentGold, r.Board);
        ImGui.SameLine();
        ImGui.TextColored(DaedalusTheme.TextSecondary, r.Mode);
        ImGui.Text($"{r.Rank}  ·  {r.Total:N0} points");

        var line = _board != null ? CrucibleResultRules.LegendaryLine(_board.Index) : 0;
        if (_legendary && line > 0)
        {
            if (r.Total >= line)
                ImGui.TextColored(DaedalusTheme.StatusGreen, $"Legendary — {r.Total - line:N0} over the {line:N0} line.");
            else
                ImGui.TextColored(DaedalusTheme.StatusRed, $"{line - r.Total:N0} short of Legendary ({line:N0}).");
        }

        ImGui.Separator();
        if (ImGui.BeginTable("##crucibleResultParts", 3, ImGuiTableFlags.SizingFixedFit))
        {
            Row("Completion", r.CompletionPct, r.CompletionPoints);
            Row("Enemies", r.Enemies, r.EnemyPoints);
            Row("Elites", r.Elites, r.ElitePoints);
            Row("Boss", r.Bosses, r.BossPoints);
            Row("Performance", "", r.Performance, total: true);
            ImGui.EndTable();
        }

        ImGui.Spacing();
        ImGui.TextColored(DaedalusTheme.TextSecondary, $"Bonuses ({r.Bonuses.Count}) — {r.BonusTotal:N0}");
        if (r.Bonuses.Count == 0)
            ImGui.TextColored(DaedalusTheme.TextSecondary, "  none");
        else if (ImGui.BeginTable("##crucibleResultBonuses", 2, ImGuiTableFlags.SizingFixedFit))
        {
            foreach (var (name, points) in r.Bonuses)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.TextColored(DaedalusTheme.StatusGreen, name);
                ImGui.TableNextColumn();
                ImGui.Text($"{points:N0}");
            }

            ImGui.EndTable();
        }

        if (_legendary && _board != null)
        {
            var missed = CrucibleResultRules.Missed(_board, r.Bonuses.Select(b => b.Name));
            if (missed.Count > 0)
            {
                ImGui.Spacing();
                ImGui.TextColored(DaedalusTheme.TextSecondary, "Missed on this board");
                if (ImGui.BeginTable("##crucibleResultMissed", 2, ImGuiTableFlags.SizingFixedFit))
                {
                    foreach (var (bonus, points) in missed)
                    {
                        ImGui.TableNextRow();
                        ImGui.TableNextColumn();
                        ImGui.TextColored(DaedalusTheme.StatusRed, CrucibleResultRules.Label(bonus));
                        ImGui.TableNextColumn();
                        ImGui.Text($"{points:N0}");
                    }

                    ImGui.EndTable();
                }
            }
        }

        ImGui.Separator();
        if (r.Remnants > 0)
            ImGui.Text($"{r.Remnants} {r.RemnantName}");
        if (ImGui.Button("Close##crucibleResultClose"))
            IsOpen = false;
    }

    private static void Row(string label, string detail, int points, bool total = false)
    {
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        if (total)
            ImGui.TextColored(DaedalusTheme.AccentGold, label);
        else
            ImGui.Text(label);
        ImGui.TableNextColumn();
        ImGui.TextColored(DaedalusTheme.TextSecondary, detail);
        ImGui.TableNextColumn();
        ImGui.Text($"{points:N0}");
    }
}
