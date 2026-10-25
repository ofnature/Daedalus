using System;
using Daedalus.Data;
using static Daedalus.Services.Beastmaster.CrucibleAddons;

namespace Daedalus.Services.Beastmaster;

/// <summary>
/// Reads true beast ranks from the Master's Bestiary detail panel (<see cref="CrucibleBestiaryDetail"/>): whenever the
/// panel shows a beast, and — on request — by clicking through both Bestiary pages beast by beast. Team screens only
/// show ranks synced to their board; this is the source that can also correct a rank downwards.
/// </summary>
public sealed unsafe class CrucibleBestiaryScanner
{
    private static readonly TimeSpan StepEvery = TimeSpan.FromMilliseconds(350);
    private static readonly TimeSpan GiveUpAfter = TimeSpan.FromSeconds(10);

    private readonly Action<string> _log;
    private readonly Action<int, int> _rankRead;
    private (int PetId, int Rank, int Exp)? _lastRead;
    private bool _scanning;
    private bool _openedBestiary;
    private int _page;
    private int _slot;
    private bool _pageShown;
    private int _read;
    private DateTime _nextStepUtc;
    private DateTime _startedUtc;

    /// <param name="rankRead">Called with (Bestiary number, true rank) for each beast the panel shows.</param>
    public CrucibleBestiaryScanner(Action<string> log, Action<int, int> rankRead)
    {
        _log = log;
        _rankRead = rankRead;
    }

    /// <summary>What it did last, for the team window.</summary>
    public string Status { get; private set; } = "";

    public bool Scanning => _scanning;

    /// <summary>Click through every beast in the Master's Bestiary (opening it from Team Composition if needed).</summary>
    public void StartScan()
    {
        _scanning = true;
        _openedBestiary = false;
        _page = 0;
        _slot = 0;
        _pageShown = false;
        _read = 0;
        _startedUtc = DateTime.UtcNow;
        _nextStepUtc = DateTime.UtcNow;
        Status = "Reading ranks…";
    }

    /// <summary>Framework thread, every frame.</summary>
    public void Update()
    {
        try
        {
            ReadPanel();
            if (_scanning)
                Scan();
        }
        catch (Exception ex)
        {
            _scanning = false;
            Status = $"Reading ranks failed: {ex.Message}";
        }
    }

    private void ReadPanel()
    {
        var panel = Visible(CrucibleBestiaryDetail.Window);
        if (panel == null)
            return;

        var read = CrucibleBestiaryDetail.Parse(
            NodeText(panel, CrucibleBestiaryDetail.NumberNode),
            NodeText(panel, CrucibleBestiaryDetail.RankNode),
            NodeText(panel, CrucibleBestiaryDetail.ExpNode));
        if (read is not { } beast || beast == _lastRead)
            return;

        _lastRead = beast;
        _read++;
        _rankRead(beast.PetId, beast.Rank);
        _log($"Crucible: Bestiary — {BstFamiliars.ByBestiaryNo((uint)beast.PetId)?.Name ?? $"#{beast.PetId}"} rank {beast.Rank} ({beast.Exp}/100 EXP)");
    }

    private void Scan()
    {
        var now = DateTime.UtcNow;
        if (now < _nextStepUtc)
            return;

        var notebook = Visible(CrucibleBestiaryDetail.Notebook);
        if (notebook == null)
        {
            // Open it from Team Composition (its "Master's Bestiary" button), once.
            var party = Visible("XBMPetParty");
            if (!_openedBestiary && party != null)
            {
                _openedBestiary = true;
                Fire(party, 5);
                _nextStepUtc = now + TimeSpan.FromSeconds(1);
                return;
            }

            if (now - _startedUtc > GiveUpAfter)
                Finish("Open Team Composition (or the Master's Bestiary) first.");
            return;
        }

        if (!_pageShown)
        {
            Fire(notebook, 3, _page);
            _pageShown = true;
            _nextStepUtc = now + StepEvery * 2;
            return;
        }

        Fire(notebook, 5, _slot);
        _nextStepUtc = now + StepEvery;
        Status = $"Reading ranks… page {_page + 1}, beast {_slot + 1}";
        if (++_slot < CrucibleBestiaryDetail.PageSize)
            return;

        _slot = 0;
        _pageShown = false;
        if (++_page >= 2)
        {
            _nextStepUtc = now + StepEvery; // let the last panel be read
            Finish($"Read the Bestiary: {_read} beasts.");
        }
    }

    private void Finish(string status)
    {
        _scanning = false;
        Status = status;
        _log("Crucible: " + status);
    }
}
