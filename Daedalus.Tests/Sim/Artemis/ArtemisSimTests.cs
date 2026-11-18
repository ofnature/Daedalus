using System.Text;
using Daedalus.Data;
using Xunit;
using Xunit.Abstractions;

namespace Daedalus.Tests.Sim.Artemis;

/// <summary>
/// The Beastmaster rotation run against the game model (<see cref="ArtemisSim"/>). Set <c>BST_SIM_REPORT</c> to a folder
/// to have every scenario's press-by-press timeline written there.
/// <para>
/// The numbers these tests hold the simulator to come from the recordings (Minerva replays, 3,400 Beastmaster fights;
/// <c>burn-reference/bst-sandbox</c>): each board's damage per potency, the first Trick at about 30 s, the axe that
/// answers it one GCD later.
/// </para>
/// </summary>
public class ArtemisSimTests(ITestOutputHelper output)
{
    private static BstFamiliar Beast(string name) => BstFamiliars.ByName(name)
        ?? throw new InvalidOperationException($"no familiar named {name}");

    /// <summary>Level, and damage per point of potency, of each board (calibration of 2026-11-15).</summary>
    private static SimFight Board(int board, params string[] roster) => new()
    {
        Name = $"Board {board} dummy" + (roster.Length == 0 ? ", no familiar" : ", " + string.Join(" + ", roster)),
        Level = (byte)(board switch { 1 => 30, 2 => 40, _ => 50 }),
        DamagePerPotency = board switch { 1 => 0.63, 2 => 0.79, 3 => 1.62, 4 => 1.80, _ => 1.95 },
        Roster = roster.Select(Beast).ToList(),
    };

    private SimResult Run(SimFight fight)
    {
        var result = ArtemisSim.Run(fight);
        var text = Describe(result);
        output.WriteLine(text);
        if (Environment.GetEnvironmentVariable("BST_SIM_REPORT") is { Length: > 0 } folder)
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, fight.Name.Replace(' ', '-').Replace(",", "").Replace('|', '_') + ".txt"), text);
        }

        return result;
    }

    internal static string Describe(SimResult r)
    {
        var sb = new StringBuilder();
        var f = r.Fight;
        sb.AppendLine($"{f.Name}: level {f.Level}, {f.Duration:0}s");
        sb.AppendLine($"total {r.TotalDamage:N0}  dps {r.Dps:0.0}  chain presses {r.ChainPresses}  idle in range {r.IdleInRange:0.0}s  TP overcapped {r.TpOvercapped}");
        foreach (var g in r.Events.GroupBy(e => (e.Who, e.Name)).OrderByDescending(g => g.Sum(e => e.Damage)))
            sb.AppendLine($"  {g.Key.Who,-3} {g.Key.Name,-34} x{g.Count(),-3} {g.Sum(e => e.Damage),9:N0}  {100 * g.Sum(e => e.Damage) / Math.Max(1, r.TotalDamage),5:0.0}%");
        sb.AppendLine("-- timeline (no auto-attacks)");
        foreach (var e in r.Events.Where(e => e.Name is not ("Auto-attack" or "attack")))
            sb.AppendLine($"  {e.T,7:0.00}  {e.Who,-3} {e.Name,-34} {e.Damage,7:0}  {e.Note}");
        sb.AppendLine("-- what the instinct picker said");
        foreach (var (t, state) in r.InstinctStates)
            sb.AppendLine($"  {t,7:0.00}  {state}");
        return sb.ToString();
    }

    private static bool IsChain(SimEvent e) => e.Who == "me" && e.Name is "Smash Axe" or "Axeblade Bite" or "Shieldsplitter";

    [Fact]
    public void TheChainNeverIdlesOnADummy()
    {
        var r = Run(Board(1));

        Assert.InRange(r.ChainPresses, 46, 48);   // 120 s of 2.55 s GCDs
        Assert.InRange(r.IdleInRange, 0, 1.5);
    }

    /// <summary>100 potency below level 38, 160 from it: the First Board is 30, the Second 40.</summary>
    [Theory]
    [InlineData(1, 100 * 0.63)]
    [InlineData(2, 160 * 0.79)]
    [InlineData(3, 160 * 1.62)]
    public void SmashAxeHitsForItsLevelsPotency(int board, double damage)
        => Assert.All(Run(Board(board)).Presses("Smash Axe"), e => Assert.Equal(damage, e.Damage, 1));

    /// <summary>TP comes only from the chain's combo hits, 28 a loop: four loops make the 112 the first axe spends.</summary>
    [Fact]
    public void TheFirstAxeSpendsWhatFourLoopsOfTheChainMade()
    {
        var r = Run(Board(1, "Ice Golem"));

        var axe = r.Events.First(e => e.Name is "Spinning Axe" or "Avalanche Axe" or "Mistral Axe" or "Gale Axe");
        Assert.Equal("112 TP", axe.Note);
        Assert.Equal(4 * 112 * 0.63, axe.Damage, 1);
    }

    /// <summary>
    /// The Ice Golem's Trick lights Durant, Spinning Axe (Eldritch, clockwise of it) answers, and the intentional combo's
    /// follow-up lands 2.1 s later for one and a half times the axe. First Trick at about 30 s in the recordings
    /// (median 30.1 s), the answer one GCD after it (median 2.4 s).
    /// </summary>
    [Fact]
    public void TrickThenTheClockwiseAxeIsAnIntentionalCombo()
    {
        var r = Run(Board(1, "Ice Golem"));

        var trick = r.Presses("Trick").First();
        var axe = r.Presses("Spinning Axe").First();
        var combo = r.Presses("Intentional Combo: Sunstrider").First();
        Assert.InRange(trick.T, 27, 33);
        Assert.InRange(axe.T - trick.T, 0.7, 3.5);
        Assert.Equal(2.1, combo.T - axe.T, 1);
        Assert.InRange(combo.Damage, axe.Damage * 1.5 - 0.2, axe.Damage * 1.5 + 0.2); // both are logged to one decimal
    }

    /// <summary>
    /// One off-GCD press fits a GCD, in a slot half a second wide, and an action offered outside it used to be marked
    /// as tried and not offered again for two seconds — by which time the slot had usually passed. In the recordings
    /// Rally landed a median 4.2 s after the axe before it (189 uses) and the next axe made the 7 s combo window one
    /// time in ten. Offered only on a frame that can press, Rally goes out in the very next slot and the axe it pays
    /// for lands inside the window as one more combo.
    /// </summary>
    [Fact]
    public void RallyGoesOutInTheNextSlotAndItsAxeMakesTheWindow()
    {
        var r = Run(Board(1, "Ice Golem"));

        var rally = r.Presses("Rally").First();
        var before = r.Events.Last(e => IsAxe(e) && e.T < rally.T);
        var after = r.Events.First(e => IsAxe(e) && e.T > rally.T);
        Assert.InRange(rally.T - before.T, 0, 3.2);     // one GCD (2.55 s) and the slot's half second
        Assert.InRange(after.T - before.T, 5, 7);       // the axes' shared 5 s recast, inside the Heart's 7 s
        Assert.Contains(r.Events, e => e.Name.StartsWith("Instinctual Combo") && e.T > after.T && e.T < after.T + 2);
    }

    /// <summary>The same slot at the pull: Tempered Release goes out behind the first GCD, not seconds later.</summary>
    [Fact]
    public void TemperedReleaseGoesOutBehindTheFirstGcd()
        => Assert.InRange(Run(Board(1, "Ice Golem")).Presses("Tempered Release").First().T, 0, 1.5);

    /// <summary>Out of melee nothing of the chain goes out, and it is back the moment the boss is in reach again.</summary>
    [Fact]
    public void TheChainStopsOutOfReachAndResumes()
    {
        var r = Run(Board(1, "Ice Golem") with { Name = "Board 1 out of reach 40-55 s", OutOfRange = [(40, 55)] });

        Assert.DoesNotContain(r.Events, e => IsChain(e) && e.T > 40.5 && e.T < 55);
        Assert.Contains(r.Events, e => IsChain(e) && e.T is >= 55 and < 56);
    }

    private static bool IsAxe(SimEvent e) => e.Who == "me" && e.Name is "Avalanche Axe" or "Mistral Axe" or "Spinning Axe"
        or "Gale Axe" or "Brutal Rage" or "Hawkish Talons" or "Risen Fall" or "Calamity";

    /// <summary>Seconds from the axe before each Rally to that Rally.</summary>
    private static List<double> RallyDelays(SimResult r)
        => r.Presses("Rally")
            .Select(rally => (Rally: rally, Axe: r.Events.LastOrDefault(e => IsAxe(e) && e.T < rally.T)))
            .Where(p => p.Axe.Name != null)
            .Select(p => p.Rally.T - p.Axe.T).ToList();

    /// <summary>
    /// One beast of each Trick colour, three boards, four lengths, three familiar attack speeds, three ways of being out
    /// of reach: 432 fights.
    /// </summary>
    private static IEnumerable<SimFight> SweepFights()
    {
        (string Name, (double, double)[] Windows)[] reach =
        [
            ("in reach throughout", []),
            ("out 40-52 s", [(40, 52)]),
            ("out 5 s in every 30", Enumerable.Range(0, 8).Select(k => (20.0 + 30 * k, 25.0 + 30 * k)).ToArray()),
        ];
        foreach (var board in new[] { 1, 2, 3 })
        foreach (var beast in new[] { "Ice Golem", "Coeurl", "Buffalo", "Colibri" })
        foreach (var attack in new[] { 2.9, 3.03, 3.2 })
        foreach (var duration in new[] { 90, 120, 180, 240 })
        foreach (var (name, windows) in reach)
        {
            yield return Board(board, beast) with
            {
                Name = $"{board}|{beast}|{attack}|{duration}|{name}",
                Duration = duration, FamiliarAutoSeconds = attack, OutOfRange = windows,
            };
        }
    }

    /// <summary>
    /// The sweep, as a table. Minutes long, so only when asked: <c>BST_SIM_SWEEP=1</c> with <c>BST_SIM_REPORT</c> set
    /// to the folder <c>sweep.csv</c> goes to. Run it on the rotation before and after a change and compare.
    /// </summary>
    [Fact]
    public void SweepWritesItsTableWhenAsked()
    {
        if (Environment.GetEnvironmentVariable("BST_SIM_SWEEP") != "1"
            || Environment.GetEnvironmentVariable("BST_SIM_REPORT") is not { Length: > 0 } folder)
            return;

        var rows = new System.Collections.Concurrent.ConcurrentBag<string>();
        Parallel.ForEach(SweepFights(), new ParallelOptions { MaxDegreeOfParallelism = 8 }, fight =>
        {
            var r = ArtemisSim.Run(fight);
            var delays = RallyDelays(r);
            rows.Add(string.Join(",", fight.Name.Replace('|', ','), r.Dps.ToString("0.0"),
                r.Events.Count(IsAxe), r.Events.Count(e => e.Name.StartsWith("Intentional Combo")),
                r.Events.Count(e => e.Name.StartsWith("Instinctual Combo")), r.Count("Infinitive Combo: Universality"),
                r.Count("Rally"), delays.Count == 0 ? "" : delays.Average().ToString("0.00"),
                r.Count("Trick"), r.TpOvercapped, r.IdleInRange.ToString("0.0")));
        });

        Directory.CreateDirectory(folder);
        File.WriteAllLines(Path.Combine(folder, "sweep.csv"),
            rows.OrderBy(x => x).Prepend("board,beast,attack,duration,reach,dps,axes,intentional,instinctual,universality,rally,rallyDelay,trick,tpOvercapped,idleInRange"));
        Assert.Equal(432, rows.Count);
    }

    /// <summary>One fight of the sweep, timeline and all: <c>BST_SIM_ONE</c> set to its name, e.g. <c>3|Ice Golem|3.03|240|out 5 s in every 30</c>.</summary>
    [Fact]
    public void OneSweepFightWhenAsked()
    {
        if (Environment.GetEnvironmentVariable("BST_SIM_ONE") is not { Length: > 0 } name)
            return;

        Run(SweepFights().Single(f => f.Name == name));
    }

    /// <summary>Every board runs to the end with a full team, and does more a second than the board below it.</summary>
    [Fact]
    public void EveryBoardRuns()
    {
        var dps = Enumerable.Range(1, 5).Select(b => Run(Board(b, "Ice Golem", "Damselfly", "Colibri")).Dps).ToList();

        for (var b = 1; b < dps.Count; b++)
            Assert.True(dps[b] > dps[b - 1], $"board {b + 1} ({dps[b]:0}) should out-damage board {b} ({dps[b - 1]:0})");
    }
}
