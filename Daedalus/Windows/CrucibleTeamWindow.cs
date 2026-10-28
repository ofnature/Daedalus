using System;
using System.Collections.Generic;
using System.Linq;
using Daedalus.Config.DPS;
using Daedalus.Data;
using Daedalus.Services.Beastmaster;
using Daedalus.Windows.Common;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace Daedalus.Windows;

/// <summary>
/// Choose the team for a Crucible board and who goes on the Battlehorns for each battle. Opens while the board layout,
/// Team Composition or horn screen is up (Beastmaster setting) — before entering too, for the board being looked at,
/// where it also loads the recommended roster. Each horn starts on Auto — the recommendation, shown beside it — and
/// any horn you set is kept for that board and battle and used by the horn setter from then on.
/// </summary>
public sealed class CrucibleTeamWindow : Window
{
    private readonly CrucibleIndex _index;
    private readonly Func<int, bool?> _isCaptured;
    private readonly Func<CrucibleHornSetter?> _setter;
    private readonly Func<CrucibleRosterLoader?> _roster;
    private readonly Func<CrucibleAutoPilot?> _autoPilot;
    private readonly Func<CrucibleLoop?> _loop;
    private readonly Func<CrucibleWalker?> _walker;
    private readonly Func<CrucibleBestiaryScanner?> _bestiary;
    private readonly Func<CrucibleRecorder?> _recorder;
    private string _recordStatus = "";
    private readonly Func<BeastmasterConfig> _config;
    private readonly Action _save;
    private bool _screensWereOpen;

    public CrucibleTeamWindow(
        CrucibleIndex index, Func<int, bool?> isCaptured, Func<CrucibleHornSetter?> setter, Func<CrucibleRosterLoader?> roster,
        Func<CrucibleAutoPilot?> autoPilot, Func<CrucibleLoop?> loop, Func<CrucibleWalker?> walker,
        Func<CrucibleBestiaryScanner?> bestiary, Func<CrucibleRecorder?> recorder, Func<BeastmasterConfig> config, Action save)
        : base("Crucible team##DaedalusCrucibleTeam", ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoFocusOnAppearing)
    {
        _index = index;
        _isCaptured = isCaptured;
        _setter = setter;
        _roster = roster;
        _autoPilot = autoPilot;
        _loop = loop;
        _walker = walker;
        _bestiary = bestiary;
        _recorder = recorder;
        _config = config;
        _save = save;
    }

    /// <summary>Open when the board screens appear and close when they go; closing it by hand lasts until they next appear.</summary>
    public void Follow(bool screensOpen)
    {
        if (screensOpen && !_screensWereOpen)
            IsOpen = true;
        else if (!screensOpen && _screensWereOpen)
            IsOpen = false;
        _screensWereOpen = screensOpen;
    }

    /// <summary>
    /// Just the run controls, for the edge dock's Crucible tab: autopilot, walk, gear, runs to loop and end-after-this-run,
    /// record, and Start run while a board's layout is open.
    /// </summary>
    public void DrawControls()
    {
        DrawAutoPilot();
        DrawGoal();
        var screen = CrucibleRosterLoader.ScreenBoard();
        if (CrucibleTracker.Current.Board == null && _index.Boards.FirstOrDefault(b => b.Index == screen) is { } board)
            DrawStart(board);
    }

    /// <summary>What the run is for (Clear / Level / Legendary), and for Legendary who tanks. In the team window and the edge tab.</summary>
    private void DrawGoal()
    {
        var config = _config();
        var goal = (int)config.CrucibleGoal;
        ImGui.TextColored(DaedalusTheme.TextSecondary, "Run for:");
        ImGui.SameLine();
        var changed = ImGui.RadioButton("Clear##crucibleGoal", ref goal, (int)CrucibleTeamGoal.Clear);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Beat the board: the core plus beasts that hit each battle's weakness; horns by weakness.");
        ImGui.SameLine();
        changed |= ImGui.RadioButton("Level##crucibleGoal", ref goal, (int)CrucibleTeamGoal.Level);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Level beasts: the core carries every battle on the horns; the rest of the roster is your\n"
                             + "lowest-rank beasts below 25. Every beast on the team earns Crucible EXP, horn or not.");
        ImGui.SameLine();
        changed |= ImGui.RadioButton("Legendary##crucibleGoal", ref goal, (int)CrucibleTeamGoal.Legendary);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Score for Legendary rank: a team of only three beasts of ONE kin (Beast Squad III + Kin Gambit,\n"
                             + "5,000 points). The autopilot never rests at a campsite and never buys; it takes no beast gear on\n"
                             + "Board 1 (Nameday Attire) and all of it from Board 2 on (Walking Armory, and it keeps the beasts alive).\n"
                             + "The walker avoids campsite sides at forks. Keep every familiar from falling (it retreats them).\n\n"
                             + "NOT a guarantee: there is RNG at work. Coffers that offer only beast gear, random spaces that turn\n"
                             + "into campsites, and every Damage Down / Vulnerability Up you take decide the bonuses, and the three\n"
                             + "beasts need ranks near the board's rank sync to survive it. On Board 1 the line is 17,500 and this\n"
                             + "plan scores about 17,300 + your HP share, so a single lost bonus can drop it to Apex.");
        if (changed)
        {
            config.CrucibleGoal = (CrucibleTeamGoal)goal;
            _save();
        }

        if (config.CrucibleGoal != CrucibleTeamGoal.Legendary)
            return;

        ImGui.SameLine();
        var tanks = config.CrucibleLegendaryBeastmasterTanks;
        if (ImGui.Checkbox("You tank (Challenge)##crucibleLegendaryTank", ref tanks))
        {
            config.CrucibleLegendaryBeastmasterTanks = tanks;
            _save();
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Challenge whenever an enemy is not on you, so no familiar takes a hit: Selfless (+1,000) needs every\n"
                             + "familiar at 100% HP at the end. Snarl then only covers you at 30% HP or less.\n"
                             + "Off: the familiar tanks (Snarl on cooldown), which is what keeps you alive at low ranks.\n"
                             + "Turn it on when your beastmaster's gear and the team's ranks can take the hits.");
    }

    public override void Draw()
    {
        var onBoard = CrucibleTracker.Current.Board;
        var screen = CrucibleRosterLoader.ScreenBoard();
        if ((onBoard ?? _index.Boards.FirstOrDefault(b => b.Index == screen)) is not { } board)
        {
            ImGui.TextColored(DaedalusTheme.TextSecondary, "Open a board's layout (or Team Composition) to plan its team.");
            return;
        }

        var (pool, source) = Pool(board);
        ImGui.TextColored(DaedalusTheme.AccentGold, board.Name);
        ImGui.SameLine();
        ImGui.TextColored(DaedalusTheme.TextSecondary, onBoard == null ? $"Lv.{board.Level} · planning" : $"Lv.{board.Level}");
        ImGui.TextColored(DaedalusTheme.TextSecondary, source);
        if (pool.Count == 0)
        {
            ImGui.TextColored(DaedalusTheme.TextSecondary, "Open the Master's Bestiary once so your captured beasts can be read.");
            return;
        }

        DrawAutoPilot();
        if (onBoard == null)
        {
            DrawRoster(board);
            DrawStart(board);
        }

        var upcoming = onBoard == null
            ? []
            : CrucibleHornSetter.UpcomingBattles().Where(b => _index.EnemiesInBattle(b).Count > 0).ToList();
        ImGui.Separator();
        if (onBoard != null && upcoming.Count == 0)
            ImGui.TextColored(DaedalusTheme.TextSecondary, "Next battle not shown yet.");
        foreach (var battle in upcoming)
            DrawBattle(board, battle, pool, next: true);

        var rest = board.Route
            .SelectMany(s => s.IsBattle ? [s.Battle] : s.Options.Where(o => o.Battle != 0).Select(o => o.Battle))
            .Distinct()
            .Where(b => !upcoming.Contains(b))
            .ToList();
        var header = onBoard == null ? "Horns for each battle##crucibleTeamRest" : "Other battles on this board##crucibleTeamRest";
        if (rest.Count > 0 && ImGui.CollapsingHeader(header, onBoard == null ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None))
        {
            foreach (var battle in rest)
                DrawBattle(board, battle, pool, next: false);
        }

        ImGui.Spacing();
        ImGui.TextColored(DaedalusTheme.TextSecondary,
            "Auto = the recommendation. Your picks go on the horns first; Auto fills the rest. A fallen pick is skipped.");
    }

    /// <summary>
    /// The beasts to choose from: the board's team as the horn screen last listed it (with who has fallen), else as
    /// Team Composition last listed it, else every captured beast.
    /// </summary>
    private (IReadOnlyList<CrucibleTeamMember> Pool, string Source) Pool(CrucibleBoard board)
    {
        if (_setter() is { LastTeamBoard: var seen, LastTeam: { Count: > 0 } team } && seen == board.Index)
            return (team, $"Your team: {team.Count} beasts, {team.Count(m => !m.Alive)} fallen (as of the last horn screen).");

        int Rank(int no) => _config().CrucibleRanks.TryGetValue(no, out var rank) ? rank : 0;
        if (_roster() is { TeamBoard: var listed, Team: { Count: > 3 } composition } && listed == board.Index)
            return (composition.Select(no => new CrucibleTeamMember(no, true, Rank(no))).ToList(),
                $"Your team: {composition.Count} beasts (from Team Composition).");

        var captured = BstFamiliars.All
            .Where(b => _isCaptured(b.BestiaryNo) == true)
            .Select(b => new CrucibleTeamMember(b.BestiaryNo, true, Rank(b.BestiaryNo)))
            .ToList();
        return (captured, "Choosing from all your captured beasts until your team is read.");
    }

    /// <summary>
    /// Before entering: what the run is for (clear or level), the core team, the roster that goal calls for against
    /// the team composition, and a button that sets the team to it.
    /// </summary>
    private void DrawRoster(CrucibleBoard board)
    {
        var config = _config();
        var captured = BstFamiliars.All.Where(b => _isCaptured(b.BestiaryNo) == true).Select(b => b.BestiaryNo).ToList();
        if (captured.Count == 0)
            return;

        int RankOf(int no) => config.CrucibleRanks.GetValueOrDefault(no, 1);
        string Ranked(int no) => $"{Name(no)} {(config.CrucibleRanks.TryGetValue(no, out var r) ? r.ToString() : "?")}";

        ImGui.Separator();
        DrawGoal();

        // The core: three picks, Auto = the highest rank not already chosen. For Legendary the picks lead the team when
        // they are all one kin; otherwise the kin whose best three rank highest is used.
        var core = CrucibleLevelPlan.Core(config.CrucibleCoreTeam, captured, RankOf);
        if (config.CrucibleGoal == CrucibleTeamGoal.Legendary)
        {

            var legendary = CrucibleLevelPlan.LegendaryTeam(config.CrucibleCoreTeam, captured, RankOf, board.RankCap);
            var kin = legendary.Count > 0 ? BstFamiliars.ByBestiaryNo((uint)legendary[0])?.Classification.ToString() : null;
            ImGui.TextColored(DaedalusTheme.TextSecondary, $"Legendary team{(kin != null ? $" ({kin} Gambit)" : "")}:");
            ImGui.SameLine();
            ImGui.Text(legendary.Count == 0 ? "no captured beasts" : string.Join(" > ", legendary.Select(Ranked)));
            if (legendary.Count < CrucibleLevelPlan.LegendaryTeamSize)
                ImGui.TextColored(DaedalusTheme.StatusRed, "No kin has three captured beasts: the Gambit still counts, but a beast short.");
        }

        ImGui.TextColored(DaedalusTheme.TextSecondary, "Core:");
        for (var slot = 0; slot < 3; slot++)
        {
            ImGui.SameLine();
            ImGui.SetNextItemWidth(140f);
            var chosen = config.CrucibleCoreTeam.ElementAtOrDefault(slot);
            var preview = chosen == 0 ? $"Auto ({(slot < core.Count ? Ranked(core[slot]) : "—")})" : Ranked(chosen);
            if (!ImGui.BeginCombo($"##crucibleCore{slot}", preview))
                continue;

            int? pick = null;
            if (ImGui.Selectable("Auto (highest rank)", chosen == 0))
                pick = 0;
            foreach (var no in captured.OrderByDescending(RankOf).ThenBy(Name))
            {
                if (ImGui.Selectable($"{Ranked(no)}  {CrucibleAdvice.Label(BstFamiliarAttacks.Of(no))}##core{no}", chosen == no))
                    pick = no;
            }

            ImGui.EndCombo();
            if (pick is { } p)
            {
                var team = config.CrucibleCoreTeam.Length == 3 ? config.CrucibleCoreTeam.ToArray() : [0, 0, 0];
                team[slot] = p;
                config.CrucibleCoreTeam = team;
                _save();
            }
        }

        var cap = board.RosterCap > 0 ? board.RosterCap : 10;
        var roster = config.CrucibleGoal switch
        {
            CrucibleTeamGoal.Legendary => CrucibleLevelPlan.LegendaryTeam(config.CrucibleCoreTeam, captured, RankOf, board.RankCap),
            CrucibleTeamGoal.Level => CrucibleLevelPlan.LevelRoster(core, captured, RankOf, cap),
            _ => CrucibleLevelPlan.ClearRoster(_index, board, core, no => _isCaptured(no) == true),
        };

        var loader = _roster();
        var current = loader is { TeamBoard: var listed } && listed == board.Index ? loader.Team : [];
        ImGui.TextColored(DaedalusTheme.TextSecondary, $"Roster ({roster.Count}/{cap}):");
        foreach (var no in roster)
        {
            var on = current.Contains(no);
            var label = (on ? "+" : "") + Ranked(no);
            ImGui.SameLine();
            if (ImGui.GetContentRegionAvail().X < ImGui.CalcTextSize(label).X + 24f)
                ImGui.NewLine();
            ImGui.TextColored(on ? DaedalusTheme.StatusGreen : DaedalusTheme.TextPrimary, label);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip($"{(on ? "On your team" : "Not on your team yet")} · hits {CrucibleAdvice.Label(BstFamiliarAttacks.Of(no))}"
                                 + (core.Contains(no) ? " · core" : ""));
        }

        if (_bestiary() is { } bestiary)
        {
            // Read once: clicking starts the scan mid-frame, and re-reading Scanning then ended a disabled block that
            // was never begun (ImGui assert, Korha 2026-10-27).
            var scanning = bestiary.Scanning;
            if (scanning)
                ImGui.BeginDisabled();
            if (ImGui.SmallButton("Read ranks##crucibleReadRanks"))
                bestiary.StartScan();
            if (scanning)
                ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                ImGui.SetTooltip("Opens the Master's Bestiary and reads every beast's true rank (team screens show ranks\n"
                                 + "synced to the board — Board 1 shows everyone at 5 or under). Clicking a beast there reads it too.");
            ImGui.SameLine();
            ImGui.TextColored(DaedalusTheme.TextSecondary, bestiary.Status.Length > 0
                ? bestiary.Status
                : "? = rank not seen yet (counted as 1).");
        }

        var changes = CrucibleLevelPlan.Changes(current, roster);
        if (loader is not { CompositionOpen: true })
        {
            ImGui.TextColored(DaedalusTheme.TextSecondary, "Open Team Composition to set it.");
        }
        else if (changes.Count == 0)
        {
            ImGui.TextColored(DaedalusTheme.StatusGreen, "Your team matches.");
        }
        else
        {
            var busy = loader.Loading;
            if (busy)
                ImGui.BeginDisabled();
            var adds = changes.Count(c => c.Add);
            if (ImGui.Button($"Set team (+{adds} / -{changes.Count - adds})##crucibleLoadRoster"))
                loader.Load(roster);
            if (busy)
                ImGui.EndDisabled();
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Swaps Team Composition to this roster one beast at a time: beasts not on it leave first,\n"
                                 + "then the missing ones join. Stops at the first change the game does not take.");
        }

        if (loader is { Status.Length: > 0 })
            ImGui.TextColored(DaedalusTheme.StatusGreen, loader.Status);
    }

    private void DrawBattle(CrucibleBoard board, int battle, IReadOnlyList<CrucibleTeamMember> pool, bool next)
    {
        var lineup = _index.EnemiesInBattle(battle);
        var lead = lineup.FirstOrDefault();
        var weak = lineup.Select(e => e.Weakness).Where(w => w != CrucibleElement.None).Distinct().ToList();

        ImGui.TextColored(next ? DaedalusTheme.AccentGold : DaedalusTheme.TextPrimary,
            (next ? "Next: " : "") + Capitalise(lead?.Name ?? $"battle {battle}") + (lineup.Count > 1 ? $" +{lineup.Count - 1}" : ""));
        if (lead?.IsFinalBoss == true)
        {
            ImGui.SameLine();
            ImGui.TextColored(DaedalusTheme.StatusRed, "final boss");
        }

        ImGui.SameLine();
        ImGui.TextColored(DaedalusTheme.TextSecondary, "weak to");
        ImGui.SameLine();
        ImGui.TextColored(DaedalusTheme.StatusGreen, weak.Count == 0 ? "—" : string.Join(" / ", weak.Select(CrucibleAdvice.Label)));

        var config = _config();
        var key = CrucibleHornPlan.PickKey(board, battle);
        var chosen = config.CrucibleHornPicks.TryGetValue(key, out var saved) && saved.Length == 3 ? saved.ToArray() : [0, 0, 0];
        var recommended = CrucibleHornPlan.Recommend(_index, board, battle, pool).Select(p => pool[p].PetId).ToList();

        var changed = false;
        for (var horn = 0; horn < 3; horn++)
        {
            if (horn > 0)
                ImGui.SameLine();
            ImGui.SetNextItemWidth(150f);
            var preview = chosen[horn] == 0 ? $"Auto ({Name(recommended.ElementAtOrDefault(horn))})" : Name(chosen[horn]);
            if (!ImGui.BeginCombo($"##horn{battle}_{horn}", preview))
                continue;

            if (ImGui.Selectable("Auto", chosen[horn] == 0))
            {
                chosen[horn] = 0;
                changed = true;
            }

            // Beasts that hit this battle's weakness first.
            foreach (var member in pool.OrderByDescending(m => weak.Contains(BstFamiliarAttacks.Of(m.PetId))).ThenBy(m => Name(m.PetId)))
            {
                var attack = BstFamiliarAttacks.Of(member.PetId);
                var label = $"{Name(member.PetId)}  {CrucibleAdvice.Label(attack)}{(weak.Contains(attack) ? " *" : "")}{(member.Alive ? "" : "  (fallen)")}";
                if (ImGui.Selectable($"{label}##{member.PetId}", chosen[horn] == member.PetId))
                {
                    chosen[horn] = member.PetId;
                    changed = true;
                }
            }

            ImGui.EndCombo();
        }

        ImGui.SameLine();
        if (ImGui.SmallButton($"Recommended##reset{battle}") && chosen.Any(c => c != 0))
        {
            chosen = [0, 0, 0];
            changed = true;
        }

        if (changed)
        {
            if (chosen.All(c => c == 0))
                config.CrucibleHornPicks.Remove(key);
            else
                config.CrucibleHornPicks[key] = chosen;
            _save();
        }

        var carry = config.CrucibleGoal == CrucibleTeamGoal.Legendary
            ? CrucibleLevelPlan.LegendaryTeam(config.CrucibleCoreTeam,
                BstFamiliars.All.Where(b => _isCaptured(b.BestiaryNo) == true).Select(b => b.BestiaryNo),
                no => config.CrucibleRanks.GetValueOrDefault(no, 1), board.RankCap)
            : CoreTeam();
        var horns = CrucibleHornPlan.Pick(_index, board, battle, pool, CrucibleLevelPlan.HornChoice(config.CrucibleGoal, chosen, carry));
        ImGui.TextColored(DaedalusTheme.TextSecondary, "Horns:");
        ImGui.SameLine();
        ImGui.Text(horns.Count == 0 ? "nobody left standing" : string.Join(" > ", horns.Select(p => Name(pool[p].PetId))));
        ImGui.Spacing();
    }

    /// <summary>
    /// Off a board, with its layout open: start the run — set the team, Challenge, accept the warning, Commence. The
    /// autopilot and walker take it from there; "runs to loop" above 0 carries on after it.
    /// </summary>
    private void DrawStart(CrucibleBoard board)
    {
        var config = _config();
        if (_loop() is not { } loop)
            return;

        var ready = config.CrucibleAutoPilot && config.AutoCrucibleHorns;
        if (!ready)
            ImGui.BeginDisabled();
        if (ImGui.Button($"Start run##crucibleStart"))
        {
            if (config.CrucibleLoopRuns < 1)
            {
                config.CrucibleLoopRuns = 1;
                _save();
            }

            loop.Begin(board);
        }

        if (!ready)
            ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip(ready
                ? "Sets the team, presses Challenge This Board, accepts the warning and commences. The autopilot and\n"
                  + "walker take over on the board; with runs to loop above 1 it goes again from Lauda afterwards."
                : "Turn on Autopilot and \"Set the Battlehorns before each battle\" first.");

        if (config.CrucibleLoopRuns > 0 && loop.Status.Length > 0)
        {
            ImGui.SameLine();
            ImGui.TextColored(DaedalusTheme.StatusGreen, loop.Status);
        }
    }

    /// <summary>Start / stop a Crucible recording (the same as /dae crucible record).</summary>
    private void DrawRecord()
    {
        if (_recorder() is not { } recorder)
            return;

        var recording = recorder.Recording;
        if (recording)
            ImGui.PushStyleColor(ImGuiCol.Button, DaedalusTheme.StatusRed);
        if (ImGui.Button(recording ? "Stop recording##crucibleRecord" : "Record##crucibleRecord"))
            _recordStatus = recorder.Toggle();
        if (recording)
            ImGui.PopStyleColor();
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(recording
                ? "Recording the Crucible. Stop it after the run (or where something went wrong) and send it over.\n" + _recordStatus
                : "Records the screens, clicks, your position and HP and your familiars' HP for a run — what is needed\n"
                  + "to see what went wrong. Same as /dae crucible record. Files go to pluginConfigs\\Daedalus\\crucible-recordings.");
    }

    /// <summary>On a board: the autopilot switch and what it did last.</summary>
    private void DrawAutoPilot()
    {
        DrawRecord();
        ImGui.SameLine();
        var config = _config();
        var on = config.CrucibleAutoPilot;
        if (ImGui.Checkbox("Autopilot##crucibleAuto", ref on))
        {
            config.CrucibleAutoPilot = on;
            _save();
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Steps through the board: starts each battle once the horns are set, takes the spoils' tokens,\n"
                             + "the first treasure, leaves shops without buying, rests the most hurt beast at a campsite and\n"
                             + "closes the result. Walking the board (and so the forks) and any prompt it did not raise stay yours.\n"
                             + "Needs \"Set the Battlehorns before each battle\" on.");

        ImGui.SameLine();
        var items = config.CrucibleLootTakeItems;
        if (ImGui.Checkbox("Take beast gear##crucibleItems", ref items))
        {
            config.CrucibleLootTakeItems = items;
            _save();
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Also take beast gear from the spoils (the first gear offered; potions and other items are left).\n"
                             + "It helps you survive; it costs the Nameday Attire bonus.");

        ImGui.SameLine();
        var walk = config.CrucibleWalkBoard;
        if (ImGui.Checkbox("Walk the board##crucibleWalk", ref walk))
        {
            config.CrucibleWalkBoard = walk;
            _save();
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("With the autopilot on, walk from space to space (vnavmesh), taking the suggested side at each\n"
                             + "fork (the arrows in the Crucible window's route). It waits while a screen is open or you are fighting.");

        ImGui.SameLine();
        var runs = config.CrucibleLoopRuns;
        ImGui.SetNextItemWidth(90f);
        if (ImGui.InputInt("runs to loop##crucibleLoop", ref runs))
        {
            config.CrucibleLoopRuns = Math.Clamp(runs, 0, 999);
            _save();
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("With the autopilot on: after a run, talk to Lauda (stand next to her), open the same board, set the\n"
                             + "team for your goal (Level re-picks the lowest ranks each time) and go in again. One run is taken\n"
                             + "off per board entered, so on a board this is the runs still to come after this one; 0 = no loop.");

        if (config.CrucibleLoopRuns > 0)
        {
            ImGui.SameLine();
            var onBoard = CrucibleTracker.Current.OnBoard;
            if (ImGui.SmallButton(onBoard ? "End after this run##crucibleEndLoop" : "Stop loop##crucibleEndLoop"))
            {
                config.CrucibleLoopRuns = 0;
                _save();
            }

            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(onBoard
                    ? "Finish this board (the autopilot and walker carry on) and do not go in again."
                    : "Do not start another run.");
        }

        if (on && !config.AutoCrucibleHorns)
            ImGui.TextColored(DaedalusTheme.StatusRed, "Turn on \"Set the Battlehorns before each battle\" — the autopilot waits for it.");
        else if (on && _autoPilot() is { Status.Length: > 0 } pilot)
            ImGui.TextColored(DaedalusTheme.StatusGreen, pilot.Status);
        if (on && walk && _walker() is { Status.Length: > 0 } walker)
            ImGui.TextColored(DaedalusTheme.StatusGreen, "Walk: " + walker.Status);
        if (on && config.CrucibleLoopRuns > 0 && _loop() is { Status.Length: > 0 } loop)
            ImGui.TextColored(DaedalusTheme.StatusGreen, "Loop: " + loop.Status);
    }

    /// <summary>The core team as configured, Auto slots filled by rank.</summary>
    private IReadOnlyList<int> CoreTeam()
    {
        var config = _config();
        var captured = BstFamiliars.All.Where(b => _isCaptured(b.BestiaryNo) == true).Select(b => b.BestiaryNo);
        return CrucibleLevelPlan.Core(config.CrucibleCoreTeam, captured, no => config.CrucibleRanks.GetValueOrDefault(no, 1));
    }

    private static string Name(int petId) =>
        petId == 0 ? "—" : BstFamiliars.ByBestiaryNo((uint)petId)?.Name ?? $"#{petId}";

    private static string Capitalise(string name) =>
        name.Length == 0 ? name : char.ToUpperInvariant(name[0]) + name[1..];
}
