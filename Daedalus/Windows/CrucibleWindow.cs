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
    private readonly CrucibleIndex _index;
    private readonly Func<int, bool?> _isCaptured;
    private readonly Func<string> _hornStatus;
    private int _plannedBoard;

    /// <param name="isCaptured">Whether a beast (by Bestiary number) is captured; null while the Bestiary is unread.</param>
    /// <param name="hornStatus">What the horn setter did last ("" when nothing).</param>
    /// <summary>Whether the run goal is Legendary, which changes the suggested side at a fork.</summary>
    public Func<bool>? Legendary { get; init; }

    public CrucibleWindow(CrucibleIndex index, Func<int, bool?> isCaptured, Func<string> hornStatus)
        : base("Crucible##DaedalusCrucible", ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoFocusOnAppearing)
    {
        _index = index;
        _isCaptured = isCaptured;
        _hornStatus = hornStatus;
    }

    public override void Draw()
    {
        var snap = CrucibleTracker.Current;
        if (snap.Board is not { } board)
        {
            DrawBoardPicker();
            return;
        }

        ImGui.TextColored(DaedalusTheme.AccentGold, board.Name);
        ImGui.SameLine();
        ImGui.TextColored(DaedalusTheme.TextSecondary, $"Lv.{board.Level}");
        if (_hornStatus() is { Length: > 0 } horns)
            ImGui.TextColored(DaedalusTheme.StatusGreen, horns);
        DrawPlan(board);

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

    /// <summary>Off a board: choose one to plan a team for before going in.</summary>
    private void DrawBoardPicker()
    {
        if (_index.Boards.Count == 0)
        {
            ImGui.TextColored(DaedalusTheme.TextSecondary, "No Crucible data was read from the game.");
            return;
        }

        ImGui.TextColored(DaedalusTheme.TextSecondary, "Not on a Crucible board. Plan one:");
        _plannedBoard = Math.Clamp(_plannedBoard, 0, _index.Boards.Count - 1);
        var names = _index.Boards.Select(b => $"{b.Name} (Lv.{b.Level})").ToArray();
        ImGui.SetNextItemWidth(260f);
        ImGui.Combo("##crucibleBoard", ref _plannedBoard, names, names.Length);

        var board = _index.Boards[_plannedBoard];
        var boss = _index.EnemiesInBattle(board.FinalBattle).FirstOrDefault();
        if (boss != null)
        {
            ImGui.TextColored(DaedalusTheme.TextSecondary, "Final boss");
            ImGui.SameLine();
            ImGui.Text(Capitalise(boss.Name));
            ImGui.SameLine();
            ImGui.TextColored(DaedalusTheme.TextSecondary, "weak to");
            ImGui.SameLine();
            ImGui.TextColored(DaedalusTheme.StatusGreen, CrucibleAdvice.Label(boss.Weakness));
        }

        DrawPlan(board);
    }

    /// <summary>The ascetic-run plan for a board: a one-kin team of three and the bonus points it keeps.</summary>
    private void DrawPlan(CrucibleBoard board)
    {
        var unread = _isCaptured(1) == null;
        if (unread)
        {
            ImGui.TextColored(DaedalusTheme.TextSecondary, "Open the Master's Bestiary once so your captured beasts can be read.");
            return;
        }

        var team = CrucibleRunPlan.RecommendTeam(_index, board, no => _isCaptured(no) == true);
        if (team == null)
        {
            ImGui.TextColored(DaedalusTheme.TextSecondary, "No kin with three captured beasts yet.");
            DrawRoute(board);
            return;
        }

        ImGui.TextColored(DaedalusTheme.TextSecondary, team.Kin is { } kin ? $"Team ({kin}):" : "Team (mixed):");
        ImGui.SameLine();
        ImGui.Text(string.Join(" > ", team.Beasts.Select(b => b.Name)));
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip($"{team.Source}. Battlehorn order. Since 7.57 a team of three fills the horns by itself every\n"
                             + "battle, and it earns Beast Squad III"
                             + (team.Kin is { } k ? $" and the {k} Gambit.\n" : " (mixed kin: no Gambit).\n")
                             + (team.HitsFinalBoss ? "The last horn hits the final boss's weakness." : "None of these hits the final boss's weakness.")
                             + $"\nHits the weakness of {team.Hits} of the board's {board.Battles.Count} battles.");

        DrawRoster(board);
        DrawRoute(board);

        var (sure, ifClean) = CrucibleRunPlan.BonusEstimate(board, team.Kin);
        ImGui.TextColored(DaedalusTheme.TextSecondary, "Bonus if you never shop, feed, rest or use an item:");
        ImGui.SameLine();
        ImGui.TextColored(DaedalusTheme.AccentGold, $"{sure:N0}");
        ImGui.SameLine();
        ImGui.TextColored(DaedalusTheme.TextSecondary, $"(+{ifClean:N0} if nobody falls, no Damage Down / Vulnerability Up, full HP)");
    }

    /// <summary>The roster to bring onto the board (Team Composition): the beasts the route's battles call for.</summary>
    private void DrawRoster(CrucibleBoard board)
    {
        var roster = CrucibleHornPlan.Roster(_index, board, no => _isCaptured(no) == true);
        if (roster.Count == 0)
            return;

        ImGui.TextColored(DaedalusTheme.TextSecondary, $"Roster ({roster.Count}/{board.RosterCap}):");
        ImGui.SameLine();
        ImGui.TextWrapped(string.Join(", ", roster.Select(b => b.Name)));
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Add these in Team Composition (Master's Bestiary) before entering. Between battles the horns\n"
                             + "are set from them for each fight. A team of three or fewer earns Beast Squad III instead.");
    }

    /// <summary>
    /// The board move by move: each space, what it is weak to, the three beasts to bring to each battle, and the side
    /// to take at each fork (marked with an arrow).
    /// </summary>
    private void DrawRoute(CrucibleBoard board)
    {
        if (board.Route.Count == 0)
            return;

        if (!ImGui.CollapsingHeader("Route and teams##crucibleRoute", ImGuiTreeNodeFlags.DefaultOpen))
            return;

        bool Captured(int no) => _isCaptured(no) == true;
        var flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingFixedFit;
        if (!ImGui.BeginTable("##crucibleRouteTable", 4, flags))
            return;

        ImGui.TableSetupColumn("Move");
        ImGui.TableSetupColumn("Space");
        ImGui.TableSetupColumn("Weak to");
        ImGui.TableSetupColumn("Bring");
        ImGui.TableHeadersRow();

        foreach (var move in board.Route.GroupBy(s => s.Move))
        {
            var fork = move.ToList();
            var take = CrucibleRoutePlan.SuggestedSide(_index, board, fork, Captured, Legendary?.Invoke() == true);
            foreach (var stage in fork)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                var suggested = take != 0 && stage.Side == take;
                ImGui.TextColored(suggested ? DaedalusTheme.AccentGold : DaedalusTheme.TextSecondary,
                    $"{stage.Move}{stage.SideLabel}{(suggested ? " ->" : "")}");

                ImGui.TableNextColumn();
                ImGui.Text(SpaceLabel(stage));

                ImGui.TableNextColumn();
                var lineup = stage.IsBattle ? _index.EnemiesInBattle(stage.Battle) : [];
                var weak = string.Join(" / ", lineup.Select(e => e.Weakness).Where(w => w != CrucibleElement.None).Distinct().Select(CrucibleAdvice.Label));
                ImGui.TextColored(DaedalusTheme.StatusGreen, weak);

                ImGui.TableNextColumn();
                if (stage.IsBattle)
                {
                    var team = CrucibleRoutePlan.TeamFor(_index, board, stage.Battle, Captured);
                    if (team.Count == 0)
                        ImGui.TextColored(DaedalusTheme.TextSecondary, "no captured beast hits these");
                    else
                        ImGui.Text(string.Join(" > ", team.Select(b => b.Name)));
                }
            }
        }

        ImGui.EndTable();
        ImGui.TextColored(DaedalusTheme.TextSecondary,
            "-> = suggested side: a campsite first (HP never refills), a normal enemy over an elite.");
    }

    private string SpaceLabel(CrucibleStage stage)
    {
        string Lineup(int battle)
        {
            var lineup = _index.EnemiesInBattle(battle);
            var lead = lineup.FirstOrDefault()?.Name ?? "?";
            return lineup.Count > 1 ? $"{Capitalise(lead)} +{lineup.Count - 1}" : Capitalise(lead);
        }

        return stage.Type switch
        {
            CrucibleStageType.Enemy => Lineup(stage.Battle),
            CrucibleStageType.Elite => "Elite: " + Lineup(stage.Battle),
            CrucibleStageType.Boss => "Boss: " + Lineup(stage.Battle),
            CrucibleStageType.Random => "Random: " + string.Join(" or ", stage.Options.Select(o =>
                o.Battle != 0 ? Lineup(o.Battle) : o.Type.ToString().ToLowerInvariant())),
            _ => stage.Type.ToString(),
        };
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
