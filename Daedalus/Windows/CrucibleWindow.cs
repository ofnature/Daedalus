using System;
using System.Linq;
using System.Numerics;
using Daedalus.Data;
using Daedalus.Services.Beastmaster;
using Daedalus.Windows.Common;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace Daedalus.Windows;

/// <summary>
/// The Crucible of the Unbroken, read off the game's own battle notes: which board you are on, the enemy you are
/// fighting, what it is weak to, what its signature moves do and who they go for — and which of your familiars hit
/// that weakness. Opens on entering a board (Beastmaster setting), closes on leaving; also an edge-dock tab.
/// </summary>
public sealed class CrucibleWindow : Window
{
    private readonly Func<int, bool?> _isCaptured;

    /// <param name="isCaptured">Whether a beast (by Bestiary number) is captured; null while the Bestiary is unread.</param>
    public CrucibleWindow(Func<int, bool?> isCaptured)
        : base("Crucible##DaedalusCrucible", ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoFocusOnAppearing)
    {
        _isCaptured = isCaptured;
    }

    public override void Draw()
    {
        var snap = CrucibleTracker.Current;
        if (snap.Board is not { } board)
        {
            ImGui.TextColored(DaedalusTheme.TextSecondary, "Not on a Crucible board.");
            return;
        }

        ImGui.TextColored(DaedalusTheme.AccentGold, board.Name);
        ImGui.SameLine();
        ImGui.TextColored(DaedalusTheme.TextSecondary, $"Lv.{board.Level}");

        if (snap.Enemy is not { } enemy)
        {
            ImGui.TextColored(DaedalusTheme.TextSecondary, "No enemy in sight. Target one to see its notes.");
            return;
        }

        ImGui.Separator();
        DrawEnemy(enemy, headline: true);

        foreach (var other in snap.Lineup.Where(e => e != enemy))
        {
            ImGui.Spacing();
            DrawEnemy(other, headline: false);
        }

        ImGui.Separator();
        DrawFamiliars(enemy.Weakness);
    }

    private static void DrawEnemy(CrucibleEnemy enemy, bool headline)
    {
        var name = Capitalise(enemy.Name);
        if (headline)
            ImGui.Text(name);
        else
            ImGui.TextColored(DaedalusTheme.TextSecondary, "with " + name);

        if (enemy.IsFinalBoss)
        {
            ImGui.SameLine();
            ImGui.TextColored(DaedalusTheme.StatusRed, "final boss");
        }

        ImGui.SameLine();
        ImGui.TextColored(DaedalusTheme.TextSecondary, "weak to");
        ImGui.SameLine();
        ImGui.TextColored(DaedalusTheme.StatusGreen, CrucibleAdvice.Label(enemy.Weakness));

        foreach (var move in enemy.Moves)
        {
            var how = string.Join(" · ", new[] { move.Shape, move.Target, move.Effect }.Where(s => !string.IsNullOrEmpty(s)));
            ImGui.BulletText(move.Name);
            if (how.Length > 0)
            {
                ImGui.SameLine();
                ImGui.TextColored(DaedalusTheme.TextSecondary, how);
            }
        }
    }

    private void DrawFamiliars(CrucibleElement weakness)
    {
        if (weakness == CrucibleElement.None)
        {
            ImGui.TextColored(DaedalusTheme.TextSecondary, "No weakness listed.");
            return;
        }

        var beasts = CrucibleAdvice.FamiliarsFor(weakness, no => _isCaptured(no) == true);
        ImGui.TextColored(DaedalusTheme.TextSecondary, $"{CrucibleAdvice.Label(weakness)} familiars:");
        if (beasts.Count == 0)
        {
            ImGui.SameLine();
            ImGui.Text("none");
            return;
        }

        var unread = _isCaptured(1) == null;
        foreach (var (beast, captured) in beasts)
        {
            ImGui.SameLine();
            if (ImGui.GetContentRegionAvail().X < ImGui.CalcTextSize(beast.Name).X + 8f)
                ImGui.NewLine();
            var colour = unread || captured ? DaedalusTheme.TextPrimary : DaedalusTheme.TextSecondary;
            ImGui.TextColored(colour, beast.Name);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(unread ? "Bestiary not read yet" : captured ? "Captured" : "Not captured yet");
        }
    }

    private static string Capitalise(string name) =>
        name.Length == 0 ? name : char.ToUpperInvariant(name[0]) + name[1..];
}
