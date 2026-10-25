using System;
using System.Collections.Generic;
using System.Linq;

namespace Daedalus.Data;

/// <summary>
/// The Crucible's elements — XBMElement rows 0-9 in order. An enemy is weak to one of them; a familiar's attack is one
/// of them (or unaspected, <see cref="None"/>).
/// </summary>
public enum CrucibleElement : byte
{
    None = 0,
    Fire = 1,
    Wind = 2,
    Earth = 3,
    Lightning = 4,
    Ice = 5,
    Water = 6,
    Blunt = 7,
    Piercing = 8,
    Slashing = 9,
}

/// <summary>One of an enemy's signature moves, as the Crucible's own battle notes describe it.</summary>
/// <param name="Target">Who it goes for: Self, Ground, Highest Enmity, Random, Player, Allies — or "" when unstated.</param>
/// <param name="Shape">Its telegraph: Single Target, Front, Rear, Front/Rear, Lateral, Circle, Ring, Circle/Ring, Universal, Cross.</param>
/// <param name="Effect">The status it applies, or "".</param>
public sealed record CrucibleMove(uint ActionId, string Name, string Target, string Shape, string Effect);

/// <summary>One enemy of one Crucible battle (an XBMBattleDetail subrow).</summary>
/// <param name="NameId">BNpcName row — what the enemy in the world carries.</param>
/// <param name="Battle">XBMBattleDetail row: the battle it belongs to.</param>
/// <param name="IsFinalBoss">The battle is the one a board ends on.</param>
/// <param name="Weakness">
/// XBMBattleDetail's element. Read as the weakness: it matches every known case (bone knight → blunt, Pas de Seul →
/// piercing, the boss the community "Bug team" is built for).
/// </param>
/// <param name="Ratings">Five 1-5 ratings the game shows for the enemy. Their labels are not in the data.</param>
public sealed record CrucibleEnemy(
    uint NameId, string Name, int Battle, bool IsFinalBoss, CrucibleElement Weakness, byte[] Ratings,
    IReadOnlyList<CrucibleMove> Moves);

/// <summary>One Crucible board (an XBMContent row with a duty).</summary>
/// <param name="Battles">The board's battle pool (XBMContentBattle). The first is its final boss.</param>
/// <param name="Bonuses">
/// What each score bonus is worth on this board, in <see cref="CrucibleBonus"/> order — XBMContent columns 4-36, which
/// line up one for one with XBMScoreBonus (Infinity and Beyond reads 0 everywhere but the Second Master's Board, as the
/// game's own rules say). Empty when unread.
/// </param>
public sealed record CrucibleBoard(
    int Index, uint ContentFinderConditionId, uint TerritoryId, string Name, byte Level, IReadOnlyList<int> Battles,
    IReadOnlyList<int>? Bonuses = null, IReadOnlyList<CrucibleStage>? Stages = null, int RosterCap = 0, int RankCap = 0)
{
    /// <summary>
    /// The lowest recommended beast rank: the board's band is the five ranks up to its sync cap (XBMContent col 3 =
    /// 5/10/15/20/25), as the board screen shows it ("Recommended Beast Rank: 6 (Sync from 10)" on board 2). 0 when
    /// unknown.
    /// </summary>
    public int RecommendedRank => RankCap > 0 ? Math.Max(1, RankCap - 4) : 0;

    /// <summary>The board's spaces, move by move (<see cref="CrucibleRouteBuilder"/>); empty when unread.</summary>
    public IReadOnlyList<CrucibleStage> Route => Stages ?? [];

    public int FinalBattle => Battles.Count > 0 ? Battles[0] : 0;

    /// <summary>What a bonus is worth here; 0 when unknown.</summary>
    public int BonusValue(CrucibleBonus bonus) =>
        Bonuses is { } b && (int)bonus < b.Count ? b[(int)bonus] : 0;
}

/// <summary>The Crucible's score bonuses, in XBMScoreBonus row order.</summary>
public enum CrucibleBonus
{
    WillingSacrificeI, WillingSacrificeII, WillingSacrificeIII, NoBeastLeftBehind,
    BeastSquadI, BeastSquadII, BeastSquadIII, SelfPreservation, Selfless,
    InfinityAndBeyondI, InfinityAndBeyondII, InfinityAndBeyondIII,
    Unbeastable, NamedayAttire, WalkingArmory, EconomicEthos, HabitualHoarder,
    StarveTheFever, FeedTheBold, CoinClutcher, RollingInRiches, SleeplessSurvival,
    BeastkinGambit, VilekinGambit, CloudkinGambit, SeedkinGambit, WavekinGambit, ScalekinGambit, SoulkinGambit, AshkinGambit,
    FirstDegree, SecondDegree, ThirdDegree,
}

/// <summary>
/// Every Crucible board and enemy, read from the game's XBM sheets at load (<c>CrucibleSheetReader</c>). Pure lookups.
/// </summary>
public sealed class CrucibleIndex
{
    private readonly Dictionary<uint, CrucibleBoard> _byTerritory;
    private readonly ILookup<int, CrucibleEnemy> _byBattle;

    public CrucibleIndex(IEnumerable<CrucibleBoard> boards, IEnumerable<CrucibleEnemy> enemies)
    {
        Boards = boards.ToList();
        Enemies = enemies.ToList();
        _byTerritory = Boards.Where(b => b.TerritoryId != 0).GroupBy(b => b.TerritoryId).ToDictionary(g => g.Key, g => g.First());
        _byBattle = Enemies.ToLookup(e => e.Battle);
    }

    public static readonly CrucibleIndex Empty = new([], []);

    public IReadOnlyList<CrucibleBoard> Boards { get; }

    public IReadOnlyList<CrucibleEnemy> Enemies { get; }

    /// <summary>The board this territory is, or null outside the Crucible.</summary>
    public CrucibleBoard? BoardForTerritory(uint territoryId) => _byTerritory.GetValueOrDefault(territoryId);

    /// <summary>Everyone fought together in one battle, leader first.</summary>
    public IReadOnlyList<CrucibleEnemy> EnemiesInBattle(int battle) => _byBattle[battle].ToList();

    /// <summary>
    /// The enemy a BNpcName is on this board. The same name can appear in more than one battle (a bone bishop on two
    /// boards, a golem piece twice), so the board's own battles are searched first, then any.
    /// </summary>
    public CrucibleEnemy? FindEnemy(CrucibleBoard? board, uint nameId)
    {
        if (nameId == 0)
            return null;

        if (board != null)
        {
            foreach (var battle in board.Battles)
            {
                foreach (var enemy in _byBattle[battle])
                {
                    if (enemy.NameId == nameId)
                        return enemy;
                }
            }
        }

        return Enemies.FirstOrDefault(e => e.NameId == nameId);
    }
}

/// <summary>A recommended Crucible team of three, in Battlehorn order.</summary>
/// <param name="Kin">The kin all three share (a Gambit bonus), or null for a mixed team.</param>
/// <param name="Source">Where the pick came from: a community team by name, or "matched to weaknesses".</param>
/// <param name="Hits">Of the board's battles, how many are weak to something this team's attacks deal.</param>
public sealed record CrucibleTeam(
    BeastClassification? Kin, IReadOnlyList<BstFamiliar> Beasts, bool HitsFinalBoss, int Hits, string Source);

/// <summary>
/// The plan for a board's "ascetic run" (docs/crucible-farm-plan.md): three familiars of one kin, no shop, no feed,
/// no campsite, no Crucible items. It keeps the most bonuses and needs the fewest screens.
/// </summary>
public static class CrucibleRunPlan
{
    /// <summary>The bonuses the ascetic run keeps by playing that way, whatever happens in the fights.</summary>
    public static readonly CrucibleBonus[] KeptByPolicy =
    [
        CrucibleBonus.BeastSquadIII, CrucibleBonus.NamedayAttire, CrucibleBonus.EconomicEthos,
        CrucibleBonus.StarveTheFever, CrucibleBonus.CoinClutcher, CrucibleBonus.SleeplessSurvival,
    ];

    /// <summary>The bonuses it keeps if the fights go cleanly: nobody incapacitated, no Damage Down / Vulnerability Up, full HP.</summary>
    public static readonly CrucibleBonus[] KeptIfClean =
    [
        CrucibleBonus.NoBeastLeftBehind, CrucibleBonus.Unbeastable, CrucibleBonus.SelfPreservation, CrucibleBonus.Selfless,
    ];

    /// <summary>The Gambit bonus for a one-kin team.</summary>
    public static CrucibleBonus GambitFor(BeastClassification kin) => kin switch
    {
        BeastClassification.Beastkin => CrucibleBonus.BeastkinGambit,
        BeastClassification.Vilekin => CrucibleBonus.VilekinGambit,
        BeastClassification.Cloudkin => CrucibleBonus.CloudkinGambit,
        BeastClassification.Seedkin => CrucibleBonus.SeedkinGambit,
        BeastClassification.Wavekin => CrucibleBonus.WavekinGambit,
        BeastClassification.Scalekin => CrucibleBonus.ScalekinGambit,
        BeastClassification.Soulkin => CrucibleBonus.SoulkinGambit,
        _ => CrucibleBonus.AshkinGambit,
    };

    /// <summary>(sure, if clean): the bonus points the plan keeps on this board; a one-kin team's Gambit counts as sure.</summary>
    public static (int Sure, int IfClean) BonusEstimate(CrucibleBoard board, BeastClassification? kin) =>
        (KeptByPolicy.Sum(board.BonusValue) + (kin is { } k ? board.BonusValue(GambitFor(k)) : 0),
         KeptIfClean.Sum(board.BonusValue));

    /// <summary>
    /// The community Crucible teams (<see cref="BattlehornTeams"/>), tried before anything is worked out from attack
    /// types: the Bug team for a piercing-weak boss (Wespe's Final Sting last), its Dullahan ending for a slashing-weak
    /// one. They are built on what the beasts DO — Mantis's +10% vulnerability amplifies the finisher, Damselfly opens
    /// with an AoE Trick and poison — which attack type alone cannot see (it once picked Antling and Diremite over
    /// Damselfly and Mantis because all three dealt piercing damage).
    /// </summary>
    public static readonly string[] CuratedTeamIds = ["single-target", "crucible-slashing"];

    /// <summary>
    /// A curated team from the captured beasts. Each horn takes the first captured beast of its preference list,
    /// preferring the kin the other horns already share (a Gambit bonus), and never a beast twice. Null when a horn
    /// cannot be filled.
    /// </summary>
    public static (IReadOnlyList<BstFamiliar> Beasts, BeastClassification? Kin)? FromCurated(BattlehornTeam team, Func<int, bool> isCaptured)
    {
        if (team.Horns.Length != 3)
            return null;

        // Horns with a single choice set the kin the others should match.
        var fixedKins = team.Horns.Where(h => h.Length == 1)
            .Select(h => BstFamiliars.ByBestiaryNo((uint)h[0])?.Classification)
            .Where(k => k != null).Select(k => k!.Value).ToList();
        BeastClassification? wanted = fixedKins.Count > 0 && fixedKins.All(k => k == fixedKins[0]) ? fixedKins[0] : null;

        var picked = new List<BstFamiliar>();
        foreach (var horn in team.Horns)
        {
            var options = horn
                .Where(n => isCaptured(n) && picked.All(p => p.BestiaryNo != n))
                .Select(n => BstFamiliars.ByBestiaryNo((uint)n))
                .Where(b => b != null).Select(b => b!)
                .ToList();
            var choice = options.FirstOrDefault(b => wanted != null && b.Classification == wanted) ?? options.FirstOrDefault();
            if (choice == null)
                return null;
            picked.Add(choice);
        }

        var kin = picked.All(b => b.Classification == picked[0].Classification) ? picked[0].Classification : (BeastClassification?)null;
        return (picked, kin);
    }

    /// <summary>
    /// The team to bring. A community team (<see cref="CuratedTeamIds"/>) whose finisher hits the final boss's weakness
    /// comes first. Otherwise the best one-kin team of three from the captured familiars: the most of them hitting the
    /// final boss's weakness, then the most of the board's battles, then the most beasts available, with the final
    /// boss's weakness on the last horn. Null when nothing fits.
    /// </summary>
    public static CrucibleTeam? RecommendTeam(CrucibleIndex index, CrucibleBoard board, Func<int, bool> isCaptured)
    {
        var final = index.EnemiesInBattle(board.FinalBattle).FirstOrDefault()?.Weakness ?? CrucibleElement.None;
        var poolWeak = board.Battles
            .Select(b => index.EnemiesInBattle(b).FirstOrDefault()?.Weakness ?? CrucibleElement.None)
            .ToList();

        int HitsOf(IEnumerable<BstFamiliar> beasts)
        {
            var attacks = beasts.Select(b => BstFamiliarAttacks.Of(b.BestiaryNo)).ToHashSet();
            return poolWeak.Count(w => w != CrucibleElement.None && attacks.Contains(w));
        }

        // A community team whose finisher hits the final boss's weakness, when its beasts are captured.
        foreach (var id in CuratedTeamIds)
        {
            if (BattlehornTeams.ById(id) is not { } curated || FromCurated(curated, isCaptured) is not { } resolved)
                continue;
            if (final == CrucibleElement.None || BstFamiliarAttacks.Of(resolved.Beasts[^1].BestiaryNo) != final)
                continue;
            return new CrucibleTeam(resolved.Kin, resolved.Beasts, HitsFinalBoss: true, HitsOf(resolved.Beasts), curated.Name);
        }

        CrucibleTeam? best = null;
        var bestScore = (-1, -1, -1);
        foreach (var kin in Enum.GetValues<BeastClassification>())
        {
            var mine = BstFamiliars.All.Where(b => b.Classification == kin && isCaptured(b.BestiaryNo)).ToList();
            if (mine.Count < 3)
                continue;

            var picked = mine
                .OrderByDescending(b => final != CrucibleElement.None && BstFamiliarAttacks.Of(b.BestiaryNo) == final)
                .ThenByDescending(b => poolWeak.Count(w => w != CrucibleElement.None && w == BstFamiliarAttacks.Of(b.BestiaryNo)))
                .ThenByDescending(b => b.CaptureLevel)
                .Take(3)
                .ToList();
            var attacks = picked.Select(b => BstFamiliarAttacks.Of(b.BestiaryNo)).ToHashSet();
            var hitsFinal = final != CrucibleElement.None && attacks.Contains(final);
            var hits = poolWeak.Count(w => w != CrucibleElement.None && attacks.Contains(w));
            var score = (picked.Count(b => final != CrucibleElement.None && BstFamiliarAttacks.Of(b.BestiaryNo) == final), hits, mine.Count);
            if (score.CompareTo(bestScore) > 0)
            {
                bestScore = score;
                var order = picked
                    .OrderBy(b => final != CrucibleElement.None && BstFamiliarAttacks.Of(b.BestiaryNo) == final)
                    .ToList();
                best = new CrucibleTeam(kin, order, hitsFinal, hits, "matched to weaknesses");
            }
        }

        return best;
    }
}

/// <summary>Advice the Crucible window gives. Pure.</summary>
public static class CrucibleAdvice
{
    /// <summary>"Blunt", "Fire", … ; "—" for none.</summary>
    public static string Label(CrucibleElement element) => element == CrucibleElement.None ? "—" : element.ToString();

    /// <summary>
    /// The familiars whose attack hits <paramref name="weakness"/>, captured ones first, by Bestiary number.
    /// Empty when the enemy has no weakness listed.
    /// </summary>
    public static IReadOnlyList<(BstFamiliar Beast, bool Captured)> FamiliarsFor(CrucibleElement weakness, Func<int, bool> isCaptured)
    {
        if (weakness == CrucibleElement.None)
            return [];

        return BstFamiliars.All
            .Where(b => BstFamiliarAttacks.Of(b.BestiaryNo) == weakness)
            .Select(b => (Beast: b, Captured: isCaptured(b.BestiaryNo)))
            .OrderByDescending(x => x.Captured)
            .ThenBy(x => x.Beast.BestiaryNo)
            .ToList();
    }
}

/// <summary>
/// What each familiar's attack is, from its XBMPet attack text in the 2026.10.16 client ("Delivers a slashing physical
/// attack.", "Deals fire-aspected damage.", "Deals unaspected damage…"). Kept as data rather than parsed from text at
/// runtime, which would only work in English.
/// </summary>
public static class BstFamiliarAttacks
{
    private static readonly CrucibleElement[] ByBestiaryNo =
    [
        CrucibleElement.None,      // 0 — unused
        CrucibleElement.Slashing,  // 1 Cu Sith
        CrucibleElement.Fire,      // 2 Squirrel
        CrucibleElement.Blunt,     // 3 Lamb
        CrucibleElement.Water,     // 4 Pugil
        CrucibleElement.Blunt,     // 5 Opo-opo
        CrucibleElement.Earth,     // 6 Dodo
        CrucibleElement.Lightning, // 7 Coblyn
        CrucibleElement.Piercing,  // 8 Diremite
        CrucibleElement.Water,     // 9 Megalocrab
        CrucibleElement.Piercing,  // 10 Wespe
        CrucibleElement.Wind,      // 11 Vulture
        CrucibleElement.Blunt,     // 12 Mandragora
        CrucibleElement.Lightning, // 13 Geshunpest
        CrucibleElement.Fire,      // 14 Puk
        CrucibleElement.Water,     // 15 Crab
        CrucibleElement.Slashing,  // 16 Mantis
        CrucibleElement.None,      // 17 Slime (unaspected)
        CrucibleElement.Slashing,  // 18 Dullahan
        CrucibleElement.None,      // 19 Bat (unaspected)
        CrucibleElement.Wind,      // 20 Flying Trap
        CrucibleElement.Ice,       // 21 Ziz
        CrucibleElement.Piercing,  // 22 Sabotender
        CrucibleElement.Earth,     // 23 Golem
        CrucibleElement.Water,     // 24 Apkallu
        CrucibleElement.Lightning, // 25 Adamantoise
        CrucibleElement.Blunt,     // 26 Buffalo
        CrucibleElement.Ice,       // 27 Uragnite
        CrucibleElement.Earth,     // 28 Worm
        CrucibleElement.Fire,      // 29 Spriggan
        CrucibleElement.Blunt,     // 30 Goobbue
        CrucibleElement.Ice,       // 31 Gigantoad
        CrucibleElement.Piercing,  // 32 Colibri
        CrucibleElement.Lightning, // 33 Coeurl
        CrucibleElement.Ice,       // 34 Raptor
        CrucibleElement.Fire,      // 35 Drake
        CrucibleElement.Earth,     // 36 Treant
        CrucibleElement.Piercing,  // 37 Antling
        CrucibleElement.Fire,      // 38 Chimera
        CrucibleElement.Piercing,  // 39 Morbol
        CrucibleElement.Wind,      // 40 Ghost
        CrucibleElement.Water,     // 41 Salamander
        CrucibleElement.Piercing,  // 42 Cobra
        CrucibleElement.Piercing,  // 43 Hydra
        CrucibleElement.Wind,      // 44 Damselfly
        CrucibleElement.Earth,     // 45 Rotting Goobbue
        CrucibleElement.Blunt,     // 46 Zu
        CrucibleElement.Ice,       // 47 Ice Golem
        CrucibleElement.Piercing,  // 48 Karlabos
        CrucibleElement.Wind,      // 49 Rafflesia
        CrucibleElement.Lightning, // 50 Behemoth
    ];

    /// <summary>The familiar's attack, by Bestiary number; <see cref="CrucibleElement.None"/> for unaspected or unknown.</summary>
    public static CrucibleElement Of(int bestiaryNo) =>
        bestiaryNo is >= 1 and <= 50 ? ByBestiaryNo[bestiaryNo] : CrucibleElement.None;
}
