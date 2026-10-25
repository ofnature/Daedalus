using System;
using System.Collections.Generic;
using System.Linq;
using Daedalus.Data;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;

namespace Daedalus.Services.Beastmaster;

/// <summary>
/// Reads the Crucible's Team Composition (the pet-party agent outside the horn screen) and, on request, turns it into
/// a wanted roster with the agent's own <c>TogglePet</c> — leavers first, then joiners, one beast at a time, checking
/// after each that the game took it and stopping at the first one it did not. Also reports beast ranks as the screen
/// lists them.
/// </summary>
public sealed unsafe class CrucibleRosterLoader
{
    private static readonly TimeSpan StepEvery = TimeSpan.FromMilliseconds(400);

    private readonly Action<string> _log;
    private readonly Action<int, int> _rankSeen;
    private IReadOnlyList<int>? _wanted;
    private (int PetId, bool Add)? _lastToggle;
    private DateTime _nextStepUtc;
    private string _lastRanks = "";

    /// <param name="rankSeen">Called with (Bestiary number, rank) for every beast the screen lists a rank for.</param>
    public CrucibleRosterLoader(Action<string> log, Action<int, int> rankSeen)
    {
        _log = log;
        _rankSeen = rankSeen;
    }

    /// <summary>What it did last, for the team window.</summary>
    public string Status { get; private set; } = "";

    /// <summary>The team composition as last read (Bestiary numbers); kept after the screen closes.</summary>
    public IReadOnlyList<int> Team { get; private set; } = [];

    /// <summary>The board (XBMContent row) <see cref="Team"/> was read for; 0 when none.</summary>
    public int TeamBoard { get; private set; }

    /// <summary>Team Composition is open now.</summary>
    public bool CompositionOpen { get; private set; }

    public bool Loading => _wanted != null;

    /// <summary>Make the team exactly <paramref name="wanted"/>: beasts not in it leave, missing ones join in order.</summary>
    public void Load(IReadOnlyList<int> wanted)
    {
        _wanted = wanted;
        _lastToggle = null;
        _nextStepUtc = DateTime.UtcNow;
        Status = "Setting the team…";
    }

    /// <summary>The board the Team Composition or board layout is showing (XBMContent row), 0 when neither is up.</summary>
    public static int ScreenBoard()
    {
        var module = AgentModule.Instance();
        if (module == null)
            return 0;

        var party = (AgentXBMPetParty*)module->GetAgentByInternalId(AgentId.XBMPetParty);
        if (party != null && ((AgentInterface*)party)->IsAgentActive() && party->ContentId != 0)
            return (int)party->ContentId;

        var stage = (AgentXBMStageDetailList*)module->GetAgentByInternalId(AgentId.XBMStageDetailList);
        if (stage != null && ((AgentInterface*)stage)->IsAgentActive() && stage->ContentId != 0)
            return (int)stage->ContentId;

        return 0;
    }

    /// <summary>Framework thread, every frame.</summary>
    public void Update()
    {
        try
        {
            var module = AgentModule.Instance();
            var agent = module == null ? null : (AgentXBMPetParty*)module->GetAgentByInternalId(AgentId.XBMPetParty);
            if (agent == null || !((AgentInterface*)agent)->IsAgentActive())
            {
                Closed();
                return;
            }

            var team = agent->SelectedPets.AsSpan().ToArray().Select(p => (int)p.PetId).Where(id => id != 0).ToList();
            ReadRanks();
            if (agent->Mode == 2)
            {
                Closed(); // the horn screen: the team is fixed for the run
                return;
            }

            CompositionOpen = true;
            Team = team;
            TeamBoard = (int)agent->ContentId;

            if (_wanted == null || DateTime.UtcNow < _nextStepUtc)
                return;

            if (_lastToggle is { } last)
            {
                if (team.Contains(last.PetId) != last.Add)
                {
                    Status = $"The game did not take {(last.Add ? "adding" : "removing")} {Name(last.PetId)} — finish by hand in Master's Bestiary.";
                    _log($"Crucible: team change stopped — TogglePet({last.PetId}) to {(last.Add ? "add" : "remove")} left the team at {team.Count} "
                         + $"(mode {agent->Mode}, sub {agent->SubMode}, list source {agent->PetListSource}).");
                    _wanted = null;
                    return;
                }

                _lastToggle = null;
            }

            var changes = CrucibleLevelPlan.Changes(team, _wanted);
            if (changes.Count == 0)
            {
                Status = $"Team set: {team.Count} beasts.";
                _log("Crucible: " + Status);
                _wanted = null;
                return;
            }

            var next = changes[0];
            agent->TogglePet((uint)next.PetId);
            _lastToggle = next;
            _nextStepUtc = DateTime.UtcNow + StepEvery;
            Status = $"{(next.Add ? "Adding" : "Removing")} {Name(next.PetId)}…";
        }
        catch (Exception ex)
        {
            _wanted = null;
            Status = $"Team change failed: {ex.Message}";
        }
    }

    private void Closed()
    {
        CompositionOpen = false;
        if (_wanted != null)
        {
            _wanted = null;
            Status = "Stopped: Team Composition was closed.";
        }
    }

    /// <summary>
    /// Each listed beast's rank, straight off the team screen's values (<see cref="CrucibleTeamScreen"/>): the rank text
    /// and the beast's icon, which names it. Works on Team Composition and the horn screen alike. (The agent's grow
    /// values were tried first; they never lined up with the team.)
    /// </summary>
    private void ReadRanks()
    {
        var screen = CrucibleAddons.Visible("XBMPetParty");
        if (screen == null)
            return;

        var count = Math.Clamp((int)CrucibleAddons.Number(screen, CrucibleTeamScreen.CountIndex), 0, 15);
        var seen = new List<(int PetId, int Rank)>(count);
        for (var i = 0; i < count; i++)
        {
            if (CrucibleTeamScreen.Member(
                    CrucibleAddons.Text(screen, CrucibleTeamScreen.RankIndex(i)),
                    CrucibleAddons.Number(screen, CrucibleTeamScreen.IconIndex(i))) is { } member)
                seen.Add(member);
        }

        foreach (var (petId, rank) in seen)
            _rankSeen(petId, rank);

        var line = string.Join(", ", seen.Select(m => $"{Name(m.PetId)} {m.Rank}"));
        if (seen.Count > 0 && line != _lastRanks)
        {
            _lastRanks = line;
            _log($"Crucible: ranks: {line}");
        }
    }

    private static string Name(int petId) => BstFamiliars.ByBestiaryNo((uint)petId)?.Name ?? $"#{petId}";
}
