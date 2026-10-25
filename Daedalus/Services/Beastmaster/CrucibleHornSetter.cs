using System;
using System.Collections.Generic;
using System.Linq;
using Daedalus.Data;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace Daedalus.Services.Beastmaster;

/// <summary>
/// Sets the three Battlehorns on the Crucible's horn screen before each battle (XBMPetParty, mode 2) — the game does
/// not keep them between battles, and only fills them for you when the whole team is three or fewer.
/// <para>
/// Reads the upcoming battle from the stage list (its battle-detail entry), the team and its HP from the pet-party
/// agent, picks with <see cref="CrucibleHornPlan"/>, and clicks one team member at a time, checking the game's own
/// selection between clicks. It never starts the battle — that stays the player's button.
/// </para>
/// </summary>
public sealed unsafe class CrucibleHornSetter
{
    private static readonly TimeSpan ClickEvery = TimeSpan.FromMilliseconds(350);
    private const int MaxClicksPerScreen = 12;

    private readonly CrucibleIndex _index;
    private readonly Action<string> _log;
    private readonly Func<CrucibleBoard, int, IReadOnlyList<int>?> _chosenFor;
    private readonly Func<int, int> _rankOf;
    private DateTime _nextClickUtc;
    private int _clicks;
    private bool _screenOpen;
    private string _doneFor = "";

    /// <param name="chosenFor">The player's picks for a battle (Bestiary numbers, 0 = Auto), or null for all Auto.</param>
    /// <param name="rankOf">A beast's rank as last read (0 when unknown), so the strongest fill the horns.</param>
    public CrucibleHornSetter(
        CrucibleIndex index, Action<string> log, Func<CrucibleBoard, int, IReadOnlyList<int>?> chosenFor, Func<int, int> rankOf)
    {
        _rankOf = rankOf;
        _index = index;
        _log = log;
        _chosenFor = chosenFor;
    }

    /// <summary>What it did last, for the Crucible window.</summary>
    public string Status { get; private set; } = "";

    /// <summary>The board's team as the horn screen last listed it, with who is standing; empty until it has been seen.</summary>
    public IReadOnlyList<CrucibleTeamMember> LastTeam { get; private set; } = [];

    /// <summary>Which board <see cref="LastTeam"/> belongs to (its index), 0 when none.</summary>
    public int LastTeamBoard { get; private set; }

    /// <summary>The battle the horns are set for on the open horn screen, 0 while they are not (yet) right.</summary>
    public int ReadyBattle { get; private set; }

    /// <summary>When <see cref="ReadyBattle"/> last became set.</summary>
    public DateTime ReadySinceUtc { get; private set; }

    /// <summary>Framework thread, every frame.</summary>
    public void Update(bool enabled, CrucibleBoard? board)
    {
        try
        {
            var party = ReadParty();
            if (party == null || board == null)
            {
                if (_screenOpen)
                    _clicks = 0;
                _screenOpen = false;
                _doneFor = "";
                ReadyBattle = 0;
                return;
            }

            _screenOpen = true;

            var (agent, team, selected) = party.Value;
            if (team.Count > 0)
            {
                LastTeam = team;
                LastTeamBoard = board.Index;
            }

            if (!enabled || DateTime.UtcNow < _nextClickUtc)
                return;

            var battle = UpcomingBattles().FirstOrDefault();
            if (battle == 0)
                return;

            var wanted = CrucibleHornPlan.Pick(_index, board, battle, team, _chosenFor(board, battle));
            var toggles = CrucibleHornPlan.Toggles(selected, wanted);
            var key = $"{battle}:{string.Join(",", wanted)}";
            if (toggles.Count == 0)
            {
                if (ReadyBattle != battle)
                {
                    ReadyBattle = battle;
                    ReadySinceUtc = DateTime.UtcNow;
                }

                if (_doneFor != key)
                {
                    _doneFor = key;
                    Status = $"Horns set for {Lineup(battle)}: {Names(team, wanted)}.";
                    _log("Crucible: " + Status);
                }
                return;
            }

            ReadyBattle = 0;
            if (_clicks >= MaxClicksPerScreen)
            {
                Status = $"Gave up setting the horns after {_clicks} clicks — set them by hand.";
                return;
            }

            var addon = RaptureAtkUnitManager.Instance()->GetAddonByName("XBMPetParty");
            if (addon == null || !addon->IsVisible)
                return;

            // One click a frame-interval, then re-read: the next toggle is computed from what the game now has.
            Fire(addon, 1, toggles[0]);
            _clicks++;
            _nextClickUtc = DateTime.UtcNow + ClickEvery;
            Status = $"Setting horns for {Lineup(battle)}…";
        }
        catch (Exception ex)
        {
            Status = $"Horn setter failed: {ex.Message}";
        }
    }

    /// <summary>The horn screen's agent when it is open in mode 2: the team (with life) and the selected positions.</summary>
    private (nint Agent, IReadOnlyList<CrucibleTeamMember> Team, IReadOnlyList<int> Selected)? ReadParty()
    {
        var module = AgentModule.Instance();
        var agent = module == null ? null : (AgentXBMPetParty*)module->GetAgentByInternalId(AgentId.XBMPetParty);
        if (agent == null || !((AgentInterface*)agent)->IsAgentActive() || agent->Mode != 2)
            return null;

        var pets = agent->SelectedPets.AsSpan();
        var team = new List<CrucibleTeamMember>(pets.Length);
        for (var i = 0; i < pets.Length; i++)
        {
            var hp = i < 15 ? agent->PetCurrentHealth[i] : 1u;
            var max = i < 15 ? agent->PetMaxHealth[i] : 0u;
            var petId = (int)pets[i].PetId;
            team.Add(new CrucibleTeamMember(petId, hp > 0, _rankOf(petId), max > 0 ? (float)hp / max : 1f));
        }

        var selected = agent->SelectedPetIds.AsSpan().ToArray().Select(p => (int)p.PetId).ToList();
        return ((nint)agent, team, selected);
    }

    /// <summary>
    /// The battles the stage list offers next: its battle-detail entries (type 3) — one, or both sides of a fork while
    /// the board layout is up. At horn time it is the battle about to start.
    /// </summary>
    public static IReadOnlyList<int> UpcomingBattles()
    {
        var module = AgentModule.Instance();
        var stage = module == null ? null : (AgentXBMStageDetailList*)module->GetAgentByInternalId(AgentId.XBMStageDetailList);
        if (stage == null)
            return [];

        var battles = new List<int>(2);
        for (var i = 0; i < 40; i++)
        {
            var entry = stage->Entries[i];
            if (entry.EntryType == 3 && entry.BattleDetailId != 0 && !battles.Contains((int)entry.BattleDetailId))
                battles.Add((int)entry.BattleDetailId);
        }

        return battles;
    }

    /// <summary>The board layout (XBMStageDetailList) or the horn screen (XBMPetParty) is on screen.</summary>
    public static bool BoardScreensOpen()
    {
        var manager = RaptureAtkUnitManager.Instance();
        if (manager == null)
            return false;

        var layout = manager->GetAddonByName("XBMStageDetailList");
        var horns = manager->GetAddonByName("XBMPetParty");
        return (layout != null && layout->IsVisible) || (horns != null && horns->IsVisible);
    }

    private string Lineup(int battle) =>
        _index.EnemiesInBattle(battle).FirstOrDefault()?.Name ?? $"battle {battle}";

    private static string Names(IReadOnlyList<CrucibleTeamMember> team, IReadOnlyList<int> positions) =>
        string.Join(" > ", positions.Select(p => BstFamiliars.ByBestiaryNo((uint)team[p].PetId)?.Name ?? $"#{team[p].PetId}"));

    private static void Fire(AtkUnitBase* unit, params int[] ints)
    {
        var values = stackalloc AtkValue[ints.Length];
        for (var i = 0; i < ints.Length; i++)
        {
            values[i] = default;
            values[i].Type = AtkValueType.Int;
            values[i].Int = ints[i];
        }

        unit->FireCallback((uint)ints.Length, values, true);
    }
}
