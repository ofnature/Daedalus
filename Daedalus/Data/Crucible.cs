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
public sealed record CrucibleBoard(
    int Index, uint ContentFinderConditionId, uint TerritoryId, string Name, byte Level, IReadOnlyList<int> Battles)
{
    public int FinalBattle => Battles.Count > 0 ? Battles[0] : 0;
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
