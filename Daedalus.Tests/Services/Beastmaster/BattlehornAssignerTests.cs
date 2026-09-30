using System;
using System.Collections.Generic;
using System.Linq;
using Daedalus.Data;
using Daedalus.Services.Beastmaster;
using Xunit;

namespace Daedalus.Tests.Services.Beastmaster;

/// <summary>
/// A pretend Bestiary: page turns and right-clicks are recorded, the context menu opens on the tile
/// clicked, and picking a horn puts the clicked beast on it (or, with <see cref="Sorted"/>, a
/// different one — what a filtered or sorted Bestiary does).
/// </summary>
internal sealed class FakeBattlehornGame : IBattlehornGame
{
    public int[] Horns = [0, 0, 0];
    public bool Open = true;
    public bool Sorted;
    public HashSet<int> Captured = Enumerable.Range(1, 50).ToHashSet();
    public readonly List<(int, int)> Fired = [];
    public readonly List<int> Picks = [];
    public int Opens;
    public int Closes;
    public bool CanOpen = true;
    private int _page;
    private int? _menuFor;

    public int[]? ReadHorns() => (int[])Horns.Clone();
    public bool IsBestiaryOpen() => Open;

    public bool FireBestiary(int command, int value)
    {
        Fired.Add((command, value));
        if (command == 3)
            _page = value;
        if (command == 8)
        {
            var row = _page * BattlehornAssigner.PerPage + value + 1;
            _menuFor = Captured.Contains(row) ? row : null;
        }
        return Open;
    }

    public IReadOnlyList<string> ContextMenuEntries() => _menuFor is null
        ? []
        : ["Assign to First Battlehorn", "Assign to Second Battlehorn", "Assign to Third Battlehorn", "Edit Appearance"];

    public bool OpenBestiary()
    {
        Opens++;
        if (CanOpen)
            Open = true;
        return CanOpen;
    }

    public void CloseBestiary()
    {
        Closes++;
        Open = false;
    }

    public bool PickContextMenu(int index)
    {
        Picks.Add(index);
        if (_menuFor is { } row)
        {
            // A beast sits on one horn only: assigning it elsewhere moves it.
            for (var i = 0; i < 3; i++)
                if (Horns[i] == row)
                    Horns[i] = 0;
            Horns[index] = Sorted ? (row % 50) + 1 : row;
        }
        _menuFor = null;
        return true;
    }
}

public class BattlehornAssignerTests
{
    private DateTime _now = new(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc);
    private readonly FakeBattlehornGame _game = new();
    private readonly BattlehornAssigner _assigner;

    public BattlehornAssignerTests() => _assigner = new BattlehornAssigner(_game) { UtcNow = () => _now };

    /// <summary>Tick at 0.1 s steps, like frames.</summary>
    private void Run(int[]? wanted, double seconds, bool allowed = true)
    {
        for (var t = 0.0; t < seconds; t += 0.1)
        {
            _assigner.Tick(() => wanted, allowed);
            _now = _now.AddSeconds(0.1);
        }
    }

    [Fact]
    public void FillsAllThreeHornsFromEmpty()
    {
        Run([44, 16, 10], 10);
        Assert.Equal(new[] { 44, 16, 10 }, _game.Horns);
        Assert.Equal("Battlehorns match the team.", _assigner.Message);
    }

    /// <summary>The page is 0-based and the tile counts from the page's start (doc §4).</summary>
    [Fact]
    public void TurnsToThePageThenClicksTheTile()
    {
        Run([30, 0, 0], 3);
        Assert.Equal(new[] { (3, 1), (8, 4) }, _game.Fired);
    }

    /// <summary>Picking the horn a beast is already on frees it — so a matching horn is never clicked.</summary>
    [Fact]
    public void NeverTouchesAHornThatAlreadyMatches()
    {
        _game.Horns = [44, 16, 10];
        Run([44, 16, 10], 5);
        Assert.Empty(_game.Fired);
        Assert.Empty(_game.Picks);
    }

    [Fact]
    public void ZeroMeansLeaveTheHornAlone()
    {
        _game.Horns = [1, 2, 5];
        Run([0, 16, 0], 5);
        Assert.Equal(new[] { 1, 16, 5 }, _game.Horns);
    }

    [Fact]
    public void MovesABeastThatSitsOnTheWrongHorn()
    {
        _game.Horns = [16, 44, 10];
        Run([44, 16, 10], 10);
        Assert.Equal(new[] { 44, 16, 10 }, _game.Horns);
    }

    /// <summary>Closed Bestiary and horns off the team: open it, set them, close it again.</summary>
    [Fact]
    public void OpensTheBestiarySetsTheHornsAndClosesIt()
    {
        _game.Open = false;
        Run([44, 16, 10], 10);
        Assert.Equal(new[] { 44, 16, 10 }, _game.Horns);
        Assert.Equal(1, _game.Opens);
        Assert.Equal(1, _game.Closes);
        Assert.False(_game.Open);
    }

    [Fact]
    public void DoesNotOpenTheBestiaryWhenTheHornsMatch()
    {
        _game.Open = false;
        _game.Horns = [44, 16, 10];
        Run([44, 16, 10], 5);
        Assert.Equal(0, _game.Opens);
    }

    /// <summary>A Bestiary the player opened stays open when the horns are done.</summary>
    [Fact]
    public void LeavesThePlayersOwnBestiaryOpen()
    {
        Run([44, 16, 10], 10);
        Assert.Equal(0, _game.Closes);
        Assert.True(_game.Open);
    }

    /// <summary>Closed part-way by the player: not reopened for the same team.</summary>
    [Fact]
    public void DoesNotReopenWhatThePlayerClosed()
    {
        _game.Open = false;
        Run([44, 16, 10], 0.5);
        _game.Open = false;
        Run([44, 16, 10], 10);
        Assert.Equal(1, _game.Opens);
    }

    /// <summary>The horns emptied again after matching (the Crucible clears them): open again.</summary>
    [Fact]
    public void ReopensWhenMatchedHornsLaterChange()
    {
        _game.Open = false;
        Run([44, 16, 10], 10);
        _game.Horns = [0, 0, 0];
        Run([44, 16, 10], 10);
        Assert.Equal(2, _game.Opens);
        Assert.Equal(new[] { 44, 16, 10 }, _game.Horns);
    }

    /// <summary>A failed step leaves the Bestiary open so the player sees why.</summary>
    [Fact]
    public void AFailureLeavesTheBestiaryOpen()
    {
        _game.Open = false;
        _game.Sorted = true;
        Run([44, 16, 10], 10);
        Assert.Equal(0, _game.Closes);
        Assert.True(_game.Open);
    }

    [Fact]
    public void SaysSoWhenTheBestiaryWillNotOpen()
    {
        _game.Open = false;
        _game.CanOpen = false;
        Run([44, 16, 10], 5);
        Assert.Equal(1, _game.Opens);
        Assert.Contains("Could not open", _assigner.Message);
    }

    /// <summary>Captures not loaded yet: opening the Bestiary is what loads them.</summary>
    [Fact]
    public void OpensToLoadAnUnknownCaptureList()
    {
        _game.Open = false;
        Run(null, 2);
        Assert.Equal(1, _game.Opens);
        Assert.Empty(_game.Fired);
    }

    [Fact]
    public void NeverOpensWhenNotAllowed()
    {
        _game.Open = false;
        Run([44, 16, 10], 5, allowed: false);
        Assert.Equal(0, _game.Opens);
    }

    [Fact]
    public void DoesNothingWhenNotAllowed()
    {
        Run([44, 16, 10], 5, allowed: false);
        Assert.Empty(_game.Fired);
    }

    /// <summary>A sorted Bestiary puts the wrong beast on the horn: stop, don't keep clicking.</summary>
    [Fact]
    public void StopsWhenTheWrongBeastLands()
    {
        _game.Sorted = true;
        Run([44, 16, 10], 10);
        Assert.Single(_game.Picks);
        Assert.Contains("did not land", _assigner.Message);
    }

    [Fact]
    public void StopsWhenNoMenuOpens()
    {
        _game.Captured.Remove(44);
        Run([44, 16, 10], 10);
        Assert.Empty(_game.Picks);
        Assert.Contains("No menu opened", _assigner.Message);
    }

    /// <summary>Closing and reopening the Bestiary is the retry.</summary>
    [Fact]
    public void ReopeningTheBestiaryRetries()
    {
        _game.Sorted = true;
        Run([44, 16, 10], 5);
        _game.Sorted = false;
        Run([44, 16, 10], 5);
        Assert.Single(_game.Picks);

        _game.Open = false;
        Run([44, 16, 10], 0.2);
        _game.Open = true;
        Run([44, 16, 10], 10);
        Assert.Equal(new[] { 44, 16, 10 }, _game.Horns);
    }

    [Fact]
    public void ClosingMidAssignmentStops()
    {
        Run([44, 16, 10], 0.2);
        _game.Open = false;
        Run([44, 16, 10], 0.2);
        Assert.Contains("closed", _assigner.Message);
    }

    [Theory]
    [InlineData(new[] { 44, 16, 10 }, new[] { 44, 16, 10 }, -1, 0)]
    [InlineData(new[] { 44, 16, 10 }, new[] { 44, 0, 10 }, 1, 16)]
    [InlineData(new[] { 0, 0, 7 }, new[] { 1, 2, 3 }, 2, 7)]
    public void NextStep(int[] wanted, int[] horns, int slot, int row)
    {
        var step = BattlehornAssigner.NextStep(wanted, horns);
        if (slot < 0)
            Assert.Null(step);
        else
            Assert.Equal((slot, row), step);
    }
}

/// <summary>The hand-picked horn setter: one request, carried out once.</summary>
public class BattlehornOneShotTests
{
    private DateTime _now = new(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc);
    private readonly FakeBattlehornGame _game = new() { Open = false };
    private readonly BattlehornAssigner _setter;

    public BattlehornOneShotTests() => _setter = new BattlehornAssigner(_game, oneShot: true) { UtcNow = () => _now };

    private void Run(int[]? wanted, double seconds)
    {
        for (var t = 0.0; t < seconds; t += 0.1)
        {
            _setter.Tick(() => wanted, allowed: true);
            _now = _now.AddSeconds(0.1);
        }
    }

    [Fact]
    public void SetsTheHornThenStops()
    {
        _setter.Restart();
        Run([16, 0, 0], 5);
        Assert.Equal(16, _game.Horns[0]);
        Assert.False(_setter.Busy);
        Assert.Equal(1, _game.Opens);
        Assert.False(_game.Open);
    }

    /// <summary>Unlike auto-set, a hand-picked horn is not put back after you change it.</summary>
    [Fact]
    public void NeverReappliesOnItsOwn()
    {
        _setter.Restart();
        Run([16, 0, 0], 5);
        _game.Horns[0] = 10;
        Run([16, 0, 0], 5);
        Assert.Equal(10, _game.Horns[0]);
        Assert.Equal(1, _game.Opens);
    }

    [Fact]
    public void AlreadyThereFinishesWithoutOpening()
    {
        _game.Horns = [16, 0, 0];
        _setter.Restart();
        Run([16, 0, 0], 3);
        Assert.False(_setter.Busy);
        Assert.Equal(0, _game.Opens);
        Assert.Equal("Already on that horn.", _setter.Message);
    }

    [Fact]
    public void RestartRunsItAgainAfterAFailure()
    {
        _game.Captured.Remove(16);
        _setter.Restart();
        Run([16, 0, 0], 6);
        Assert.False(_setter.Busy);
        Assert.Contains("No menu opened", _setter.Message);

        _game.Captured.Add(16);
        _game.Open = false;
        _setter.Restart();
        Run([16, 0, 0], 5);
        Assert.Equal(16, _game.Horns[0]);
    }

    /// <summary>Closing the Bestiary part-way ends the request; it is not reopened.</summary>
    [Fact]
    public void ClosingPartWayEndsTheRequest()
    {
        _setter.Restart();
        Run([16, 0, 0], 0.5);
        _game.Open = false;
        Run([16, 0, 0], 5);
        Assert.False(_setter.Busy);
        Assert.Equal(1, _game.Opens);
        Assert.NotEqual(16, _game.Horns[0]);
    }

    /// <summary>The setter's messages stay out of the auto-set status line.</summary>
    [Fact]
    public void DoesNotWriteTheAutoSetStatus()
    {
        var before = BattlehornAssigner.Status;
        _setter.Restart();
        Run([16, 0, 0], 5);
        Assert.Equal(before, BattlehornAssigner.Status);
    }
}

public class BattlehornTeamsTests
{
    [Fact]
    public void EveryTeamHasThreeHornsOfRealBeasts()
    {
        foreach (var team in BattlehornTeams.All.Where(t => t.Id != BattlehornTeams.SavedId))
        {
            Assert.Equal(3, team.Horns.Length);
            Assert.All(team.Horns.SelectMany(h => h), no => Assert.NotNull(BstFamiliars.ByBestiaryNo((uint)no)));
        }
    }

    [Fact]
    public void IdsAreUnique() =>
        Assert.Equal(BattlehornTeams.All.Count, BattlehornTeams.All.Select(t => t.Id).Distinct().Count());

    /// <summary>The Bug team as the guides give it: Damselfly, Mantis, Wespe.</summary>
    [Fact]
    public void BugTeamWithEverythingCaught()
    {
        var r = BattlehornTeams.Resolve(BattlehornTeams.ById("single-target")!, null, _ => true);
        Assert.Equal(new[] { 44, 16, 10 }, r.Rows);
        Assert.Empty(r.Missing);
    }

    /// <summary>Before Damselfly (Lv50) the opener falls back to Dullahan.</summary>
    [Fact]
    public void FallsBackToTheNextBeastInTheSlot()
    {
        var r = BattlehornTeams.Resolve(BattlehornTeams.ById("single-target")!, null, no => no != 44);
        Assert.Equal(new[] { 18, 16, 10 }, r.Rows);
    }

    [Fact]
    public void AnUncaughtSlotIsLeftAloneAndReported()
    {
        var r = BattlehornTeams.Resolve(BattlehornTeams.ById("single-target")!, null, no => no != 16);
        Assert.Equal(0, r.Rows[1]);
        Assert.Contains(r.Missing, m => m.Contains("Mantis"));
    }

    /// <summary>One beast, one horn.</summary>
    [Fact]
    public void ABeastIsNeverChosenTwice()
    {
        var team = new BattlehornTeam("t", "t", "t", [[16], [16, 10], [10]]);
        var r = BattlehornTeams.Resolve(team, null, _ => true);
        Assert.Equal(new[] { 16, 10, 0 }, r.Rows);
    }

    [Fact]
    public void SavedTeamComesFromTheConfig()
    {
        var saved = BattlehornTeams.ById(BattlehornTeams.SavedId)!;
        Assert.Equal(new[] { 38, 26, 36 }, BattlehornTeams.Resolve(saved, [38, 26, 36], _ => true).Rows);
        Assert.Equal(new[] { 0, 0, 0 }, BattlehornTeams.Resolve(saved, [0, 0, 0], _ => true).Rows);
        Assert.Empty(BattlehornTeams.Resolve(saved, null, _ => true).Missing);
    }
}

public class BstCaptureSpotsTests
{
    [Fact]
    public void FiftySpotsInBestiaryOrder() =>
        Assert.Equal(Enumerable.Range(1, 50), BstCaptureSpots.All.Select(s => s.BestiaryNo));

    [Theory]
    [InlineData(2, "Central Shroud", 22.9f, 15.9f)]
    [InlineData(16, "Western La Noscea", 21.9f, 23.4f)]
    [InlineData(26, "Middle La Noscea", 18.5f, 17.2f)]
    [InlineData(36, "Central Shroud", 27.9f, 15.3f)]
    public void FlaggableSpots(int no, string zone, float x, float y)
    {
        var spot = BstCaptureSpots.ByBestiaryNo(no)!;
        Assert.True(spot.CanFlag);
        Assert.Equal(zone, spot.Zone);
        Assert.Equal(x, spot.MapX);
        Assert.Equal(y, spot.MapY);
    }

    /// <summary>Duty-only beasts carry the text but no flag.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(38)]
    [InlineData(44)]
    public void DutyOnlyBeastsHaveNoFlag(int no) => Assert.False(BstCaptureSpots.ByBestiaryNo(no)!.CanFlag);

    [Fact]
    public void CoordinatesAreOnTheMap() =>
        Assert.All(BstCaptureSpots.All.Where(s => s.CanFlag), s =>
        {
            Assert.InRange(s.MapX, 1f, 42f);
            Assert.InRange(s.MapY, 1f, 42f);
        });
}
