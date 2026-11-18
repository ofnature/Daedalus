using System;
using System.Collections.Generic;
using System.Linq;

namespace Daedalus.Data;

/// <summary>Whose documented Third Degree clear the degree teams follow (the Crucible Team window's setting).</summary>
public enum CrucibleDegreePlan
{
    /// <summary>
    /// fuurie's clears (note.com/fuurie, and BMRV's plans built on them): the Wespe opens every fight with Final Sting
    /// and comes back when its horn returns; the left fork (Arch Demon, then a campsite).
    /// </summary>
    Fuurie = 0,

    /// <summary>
    /// TPLemont's video guides (docs/crucible-research/tplemont-d3-guides.md): no Wespe; a card borrowed before every
    /// pull; a beast no later battle needs takes each tankbuster; the right fork (Piscodemon, then a treasure).
    /// </summary>
    TpLemont = 1,
}

/// <summary>
/// The teams documented Third Degree clears bring, battle by battle (docs/crucible-research/third-degree.md and
/// bmrv-setups.md: fuurie's First Board D3 clear, Piscodemon from TPL1; tplemont-d3-guides.md for the video plan).
/// Keyed by XBMBattle row, which is the client's battle numbering (First Board: 1-6, Pas de Seul = 6). Horn order is
/// the clear's order. Used on any degree above Standard, when the player has no picks of their own for the battle.
/// <para>Every lookup has an overload that names the plan; the ones that do not read <see cref="Active"/>.</para>
/// </summary>
public static class CrucibleDegreeTeams
{
    // Bestiary numbers.
    private const int OpoOpo = 5, Diremite = 8, Megalocrab = 9, Wespe = 10, Mandragora = 12, Geshunpest = 13, Puk = 14, Crab = 15,
        Mantis = 16, FlyingTrap = 20, Ziz = 21, Uragnite = 27, Spriggan = 29, Colibri = 32, Coeurl = 33, Hydra = 43, Damselfly = 44,
        IceGolem = 47, Behemoth = 50;

    /// <summary>
    /// The plan in use, written from the setting every frame (Plugin). A static, like CrucibleHints: rotations read a
    /// copy of the configuration. Tests name the plan instead of assigning this.
    /// </summary>
    public static CrucibleDegreePlan Active { get; set; } = CrucibleDegreePlan.Fuurie;

    private sealed class Plan
    {
        public Dictionary<int, int[]> ByBattle { get; init; } = [];
        public Dictionary<int, int[]> RosterByBoard { get; init; } = [];
        public Dictionary<int, int[][]> RouteByBoard { get; init; } = [];
        public Dictionary<int, int> CardBeastByBattle { get; init; } = [];
        public Dictionary<int, int> PreBorrowBeastByBattle { get; init; } = [];
        public Dictionary<int, int[]> LeaveAtOnceByBattle { get; init; } = [];
        public Dictionary<int, (int Beast, int Busters)> BusterBeastByBattle { get; init; } = [];
    }

    private static readonly Plan FuuriePlan = new()
    {
        ByBattle = new()
        {
            // First Board.
            // Bone knight + bishop, fuurie's order (user, 2026-11-15): the Wespe stings the Bishop at once and leaves whole,
            // back when its horn returns; the Mandragora and the Opo-opo, needed in no later battle, do the tanking. The
            // 2026-11-06 plan had the Wespe hold the Knight through his Forward Guard: it ended the fight at about 30% HP
            // and, being "worn", missed every battle after (Korha, both runs of 2026-11-15).
            [1] = [Wespe, Mandragora, OpoOpo],
            // The Ice Golem and the Geshunpest traded battles (user, 2026-11-15): the Banemite's Deadly Thrust leaves a
            // Poison worth more than the hit (18 s, ~515 of 1,470 HP), and the Geshunpest is the roster's one Ashkin —
            // Scouring Ash is the cleanse fuminpapa's and marurun's D3 clears are built on. See CardBeast.
            [2] = [Wespe, IceGolem, Coeurl],        // Arch Demon (the route the walker takes)
            [3] = [Crab, Damselfly, Mantis],        // Piscodemon, the other fork (TPL1)
            [4] = [Wespe, Geshunpest, Behemoth],    // Banemite: Scouring Ash for the poison, Meteor on the Mitelings
            [5] = [Wespe, Damselfly, Megalocrab],   // Ogre: Crab's Quelling Wave on the wisps
            [6] = [Wespe, Behemoth, Colibri],       // Pas de Seul: Meteor on the succubi

            // Third Board.
            // Ymir + Sahagin, Plan A (user, 2026-11-06; fuurie): the Wespe stings the shell, the Ice Golem holds the Sahagin
            // and gives Soul Crush, the Diremite binds the unshelled Ymir. ArtemisCooldownRules.YmirReleasePlan.
            [15] = [Wespe, IceGolem, Diremite],
        },

        // The board's team, most needed first: every beast of the route the walker takes, then the other fork's. The
        // First Board holds ten, and the route needs exactly ten, so the Piscodemon's Crab and Mantis come last.
        RosterByBoard = new()
        {
            [1] = [Wespe, Mandragora, OpoOpo, Geshunpest, Coeurl, IceGolem, Behemoth, Damselfly, Megalocrab, Colibri, Crab, Mantis],
        },

        // Each board's battles in the order the route meets them, one step each; a fork's two battles share a step,
        // since only one of them is ever fought (First Board: the Arch Demon or the Piscodemon).
        RouteByBoard = new()
        {
            [1] = [[1], [2, 3], [4], [5], [6]],
            [3] = [[15]],
        },

        // Battles where one beast's summon is spent on Borrow in place of its Tempered Release, and nothing is borrowed
        // before the fight. The game gives one or the other a summon (Borrow turns One with Nature into the Kinship),
        // Borrow recasts in 30 s, and the Kinship lasts 90 s from the moment its beast leaves — so the Banemite's
        // Geshunpest, out at ~13 s and gone at ~25 s, leaves Scouring Ash on the button until ~115 s: past the second
        // Deadly Thrust at ~100 s. A Borrow before the pull would still be recasting when it comes out.
        CardBeastByBattle = new() { [4] = Geshunpest },
    };

    /// <summary>
    /// TPLemont's First Board Third Degree guide (video vwGyMzQsL7g; tplemont-d3-guides.md "First Board D3"). User,
    /// 2026-11-16: "mock up a version using his setup ... I just want to have an option". Unverified in a fight.
    /// </summary>
    private static readonly Plan TpLemontPlan = new()
    {
        ByBattle = new()
        {
            // Bone pair: Soul Crush borrowed from the Spriggan before the pull; the Mantis releases and leaves at once
            // (it and the Spriggan end near full), the Opo-opo tanks, the Spriggan comes for the Knight.
            [1] = [Mantis, OpoOpo, Spriggan],
            // Piscodemon: Quelling Wave from the Megalocrab for Clear Mind; it and the Damselfly (release: poison)
            // leave at once for the Ogre; the Mantis, needed no more, tanks.
            [3] = [Megalocrab, Damselfly, Mantis],
            // Banemite: Quelling Wave from the Uragnite, which leaves at once; the Flying Trap takes the first Deadly
            // Thrust, the Ziz (release: petrify on the Mitelings) the second; the Uragnite's horn is back for the end.
            [4] = [Uragnite, FlyingTrap, Ziz],
            // Ogre: Quelling Wave from the Uragnite for the wisps; none of the three is needed again.
            [5] = [Damselfly, Megalocrab, Uragnite],
            // Pas de Seul: the Puk's Scaleskin before the pull, Soul Crush from the Spriggan for the Mage.
            [6] = [Puk, Spriggan, Hydra],
        },

        RosterByBoard = new()
        {
            [1] = [Mantis, OpoOpo, Spriggan, Megalocrab, Damselfly, Uragnite, FlyingTrap, Ziz, Puk, Hydra],
        },

        // The right fork. The Arch Demon shares the step so that, met after all (a fork walked the wrong way round),
        // the later battles' beasts still count as needed; it has no team here, so the usual picks fight it.
        RouteByBoard = new()
        {
            [1] = [[1], [3, 2], [4], [5], [6]],
        },

        // The beast whose card is borrowed before the pull. The Megalocrab, the Banemite's Uragnite and the Puk are the
        // first horn: borrowed from while out, and not called again (a second summon of the same beast wipes its Kinship).
        PreBorrowBeastByBattle = new() { [1] = Spriggan, [3] = Megalocrab, [4] = Uragnite, [5] = Uragnite, [6] = Puk },

        // Kept for a later battle and of no more use in this one: Parting Blow as soon as their release is spent (the
        // opener borrowed from has none), once a fight.
        LeaveAtOnceByBattle = new() { [1] = [Mantis], [3] = [Megalocrab, Damselfly], [4] = [Uragnite] },

        // The Ziz stays out, low or not, until the Banemite's second Deadly Thrust has been cast: the beastmaster
        // Challenges when it gets low, and it Snarls into the Thrust (the video: "keep it alive until the next
        // tankbuster, then Ziz Snarls into it").
        BusterBeastByBattle = new() { [4] = (Ziz, 2) },
    };

    private static Plan Of(CrucibleDegreePlan plan) => plan == CrucibleDegreePlan.TpLemont ? TpLemontPlan : FuuriePlan;

    /// <summary>The beast whose card is borrowed in place of its release in this battle, or null.</summary>
    public static int? CardBeast(int degree, int battle) => CardBeast(degree, battle, Active);

    /// <inheritdoc cref="CardBeast(int, int)"/>
    public static int? CardBeast(int degree, int battle, CrucibleDegreePlan plan)
        => degree > 0 && Of(plan).CardBeastByBattle.TryGetValue(battle, out var no) ? no : null;

    /// <summary>The beast the plan borrows from before this battle's pull, or null (then the kin rule picks: a Beastkin, else a Vilekin).</summary>
    public static int? PreBorrowBeast(int degree, int battle) => PreBorrowBeast(degree, battle, Active);

    /// <inheritdoc cref="PreBorrowBeast(int, int)"/>
    public static int? PreBorrowBeast(int degree, int battle, CrucibleDegreePlan plan)
        => degree > 0 && Of(plan).PreBorrowBeastByBattle.TryGetValue(battle, out var no) ? no : null;

    /// <summary>This beast leaves (Parting Blow) as soon as its release is spent in this battle: it is kept for a later one.</summary>
    public static bool LeavesAtOnce(int degree, int battle, int bestiaryNo) => LeavesAtOnce(degree, battle, bestiaryNo, Active);

    /// <inheritdoc cref="LeavesAtOnce(int, int, int)"/>
    public static bool LeavesAtOnce(int degree, int battle, int bestiaryNo, CrucibleDegreePlan plan)
        => degree > 0 && Of(plan).LeaveAtOnceByBattle.TryGetValue(battle, out var beasts) && beasts.Contains(bestiaryNo);

    /// <summary>
    /// The beast that stays out for this battle's tankbusters, and how many of them must have been cast before it may
    /// retreat; null when the battle has none.
    /// </summary>
    public static (int Beast, int Busters)? BusterBeast(int degree, int battle) => BusterBeast(degree, battle, Active);

    /// <inheritdoc cref="BusterBeast(int, int)"/>
    public static (int Beast, int Busters)? BusterBeast(int degree, int battle, CrucibleDegreePlan plan)
        => degree > 0 && Of(plan).BusterBeastByBattle.TryGetValue(battle, out var entry) ? entry : null;

    /// <summary>
    /// At a fork between battles: the one battle the plan has a team for, when the others have none (the video plan's
    /// Piscodemon); null when the plan has a team for several or for none — then the walker's own ranking decides.
    /// </summary>
    public static int? PlannedBattle(int degree, IReadOnlyList<int> forkBattles) => PlannedBattle(degree, forkBattles, Active);

    /// <inheritdoc cref="PlannedBattle(int, IReadOnlyList{int})"/>
    public static int? PlannedBattle(int degree, IReadOnlyList<int> forkBattles, CrucibleDegreePlan plan)
    {
        if (degree <= 0 || forkBattles.Count < 2)
            return null;

        var planned = forkBattles.Where(b => Of(plan).ByBattle.ContainsKey(b)).ToList();
        return planned.Count == 1 ? planned[0] : null;
    }

    /// <summary>
    /// The beasts a battle after <paramref name="battle"/> on this board plans to use (Bestiary numbers): the teams of
    /// every later step of the route. A beast not in it can be spent — it tanks and takes the tankbusters.
    /// <para>The first version counted every battle not yet fought, the untaken side of the fork included, so at the
    /// Ogre the Damselfly (on the Piscodemon's team) was kept "for later": it did not tank and did not cover a
    /// critical beastmaster, who died 28 s in (Korha, 2026-11-15 16:24).</para>
    /// </summary>
    public static IReadOnlyCollection<int> NeededLater(int degree, int boardIndex, int battle)
        => NeededLater(degree, boardIndex, battle, Active);

    /// <inheritdoc cref="NeededLater(int, int, int)"/>
    public static IReadOnlyCollection<int> NeededLater(int degree, int boardIndex, int battle, CrucibleDegreePlan plan)
    {
        var data = Of(plan);
        if (degree <= 0 || !data.RouteByBoard.TryGetValue(boardIndex, out var route))
            return [];

        var step = Array.FindIndex(route, s => s.Contains(battle));
        return step < 0
            ? []
            : route.Skip(step + 1).SelectMany(s => s).SelectMany(b => data.ByBattle.TryGetValue(b, out var team) ? team : []).Distinct().ToList();
    }

    /// <summary>The battle's team in horn order on a degree above Standard, or null (Standard, or no documented team).</summary>
    public static IReadOnlyList<int>? For(int degree, int battle) => For(degree, battle, Active);

    /// <inheritdoc cref="For(int, int)"/>
    public static IReadOnlyList<int>? For(int degree, int battle, CrucibleDegreePlan plan)
        => degree > 0 && Of(plan).ByBattle.TryGetValue(battle, out var team) ? team : null;

    /// <summary>The captured beasts of the board's documented teams, most needed first; empty on Standard.</summary>
    public static IReadOnlyList<int> Roster(int degree, int boardIndex, Func<int, bool> isCaptured)
        => Roster(degree, boardIndex, isCaptured, Active);

    /// <inheritdoc cref="Roster(int, int, Func{int, bool})"/>
    public static IReadOnlyList<int> Roster(int degree, int boardIndex, Func<int, bool> isCaptured, CrucibleDegreePlan plan)
        => degree > 0 && Of(plan).RosterByBoard.TryGetValue(boardIndex, out var roster) ? roster.Where(isCaptured).ToList() : [];
}
