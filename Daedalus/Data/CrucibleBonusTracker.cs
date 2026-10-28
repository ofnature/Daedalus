using System;
using System.Collections.Generic;
using System.Linq;

namespace Daedalus.Data;

/// <summary>Where one score bonus stands during a Crucible run.</summary>
public enum CrucibleBonusState
{
    /// <summary>Paid if the board is finished, and an event can still lose it (a KO, a rest, an item…).</summary>
    Intact,

    /// <summary>Not paid. Only Willing Sacrifice's tiers can come back, as the KO count changes.</summary>
    Lost,

    /// <summary>Paid if the board is finished, whatever happens now (decided at the start, or by an event).</summary>
    Earned,

    /// <summary>Decided at the end and would NOT be paid if the board ended now, but can still be made good.</summary>
    AtRisk,

    /// <summary>Decided at the end and would be paid if the board ended now.</summary>
    DecidedAtEnd,

    /// <summary>Paid on this board but not followed (Infinity and Beyond, the Degrees).</summary>
    NotTracked,
}

/// <summary>Why a bonus is where it is, for the window to word ("lost: Potion used at 21:39").</summary>
public enum CrucibleBonusReason
{
    /// <summary>On track: nothing against it.</summary>
    None,

    /// <summary>Beast Squad: the roster's size is another tier's.</summary>
    RosterSize,

    /// <summary>Gambit: the roster is not all one (known) kin.</summary>
    MixedKin,

    /// <summary>Gambit: the roster is one kin, another one.</summary>
    OtherKin,

    FamiliarKnockedOut,

    /// <summary>Willing Sacrifice: no familiar has been knocked out (No Beast Left Behind is the one paid).</summary>
    NoKnockOut,

    /// <summary>Willing Sacrifice: the KO count is another tier's.</summary>
    OtherTier,

    /// <summary>Unbeastable: the beastmaster got Damage Down or Vulnerability Up.</summary>
    DebuffTaken,

    Rested,
    Fed,

    /// <summary>Feed the Bold: not fed enough yet.</summary>
    NotFed,

    GearAcquired,
    ItemUsed,
    TokensSpent,

    /// <summary>Selfless / Self-preservation: someone is below full HP.</summary>
    HpNotFull,

    /// <summary>Walking Armory / Habitual Hoarder / Rolling in Riches: holding fewer than needed.</summary>
    NotEnoughHeld,

    /// <summary>The run ended in death: no bonus is paid.</summary>
    BeastmasterDied,

    /// <summary>Infinity and Beyond / the Degrees: not followed.</summary>
    OutOfScope,
}

/// <summary>One bonus the board pays, as the run stands.</summary>
/// <param name="Points">What it is worth on this board (<see cref="CrucibleBoard.BonusValue"/>).</param>
public sealed record CrucibleBonusLine(CrucibleBonus Bonus, CrucibleBonusState State, int Points, CrucibleBonusReason Reason)
{
    /// <summary>Still in play: paid, or payable by the end.</summary>
    public bool StillPossible => State is CrucibleBonusState.Earned or CrucibleBonusState.Intact
        or CrucibleBonusState.DecidedAtEnd or CrucibleBonusState.AtRisk;
}

/// <summary>
/// The run's bonuses and its projected score against the Legendary line. Performance leaves out the share of the
/// beastmaster's remaining HP, which is not understood (<see cref="CrucibleBonusRules.CompletionPoints"/>), so the
/// real score is that much higher.
/// </summary>
/// <param name="Lines">Every bonus the board pays (value above 0), in <see cref="CrucibleBonus"/> order.</param>
/// <param name="Sure">Bonus points paid if the board is finished, whatever happens from here.</param>
/// <param name="IfKept">
/// <see cref="Sure"/> plus everything still possible (Intact, DecidedAtEnd, AtRisk), one per exclusive group (the most
/// valuable). At-risk bonuses count: they can still be made good (heal up, pick up more).
/// </param>
/// <param name="PerformanceSoFar">Spaces cleared, plus completion once finished. No HP share.</param>
/// <param name="PerformanceIfFinished">
/// <see cref="PerformanceSoFar"/> plus completion plus the least the rest of the route must pay (the poorer side of
/// each fork, a random space at its poorest). Without a route (unread) only completion is added.
/// </param>
/// <param name="LegendaryLine">The Legendary score on Standard difficulty, or 0 when unknown for this board.</param>
public sealed record CrucibleBonusSnapshot(
    IReadOnlyList<CrucibleBonusLine> Lines, int Sure, int IfKept, int PerformanceSoFar, int PerformanceIfFinished,
    int LegendaryLine, bool Died, bool Finished)
{
    /// <summary>The score if the board is finished with only the sure bonuses (no HP share). A death scores performance alone.</summary>
    public int ProjectedSure => PerformanceIfFinished + Sure;

    /// <summary>The score if the board is finished keeping everything still possible (no HP share).</summary>
    public int ProjectedIfKept => PerformanceIfFinished + IfKept;

    /// <summary>Points short of Legendary with the sure bonuses (negative = above the line); null when the line is unknown.</summary>
    public int? GapSure => LegendaryLine > 0 ? LegendaryLine - ProjectedSure : null;

    /// <summary>Points short of Legendary keeping everything still possible (negative = above); null when the line is unknown.</summary>
    public int? GapIfKept => LegendaryLine > 0 ? LegendaryLine - ProjectedIfKept : null;
}

/// <summary>
/// The Crucible's bonus conditions and score figures (field research, docs/crucible-legendary.md). Pure. Values come
/// from the board (<see cref="CrucibleBoard.BonusValue"/>); only the conditions live here.
/// </summary>
public static class CrucibleBonusRules
{
    /// <summary>Beast Squad III: 1-3 beasts registered. Verified in the field.</summary>
    public const int BeastSquadIIIMaxRoster = 3;

    /// <summary>Beast Squad II: up to this many beasts registered. UNVERIFIED — a guess between III and the cap.</summary>
    public const int BeastSquadIIMaxRoster = 6;

    /// <summary>Beast Squad I: up to this many beasts registered. UNVERIFIED — taken as the roster cap (10).</summary>
    public const int BeastSquadIMaxRoster = 10;

    /// <summary>Willing Sacrifice II: at least this many familiars KO'd. UNVERIFIED (I = 1 KO was seen in the field).</summary>
    public const int WillingSacrificeIIMinKnockOuts = 2;

    /// <summary>Willing Sacrifice III: at least this many familiars KO'd. UNVERIFIED (a run with "several" KOs got III).</summary>
    public const int WillingSacrificeIIIMinKnockOuts = 3;

    /// <summary>Feed the Bold: fed at least this many times. UNVERIFIED — the real threshold is unknown.</summary>
    public const int FeedTheBoldMinFeeds = 1;

    /// <summary>Walking Armory: beast gear held at the end. UNVERIFIED — a community note says "10 beast gear = 500".</summary>
    public const int WalkingArmoryMinGear = 10;

    /// <summary>Habitual Hoarder: Crucible items held at the end, per the bonus's description.</summary>
    public const int HabitualHoarderMinItems = 10;

    /// <summary>Rolling in Riches: tokens held at the end, per the bonus's description.</summary>
    public const int RollingInRichesMinTokens = 2000;

    /// <summary>
    /// Performance for finishing (100% completion). The rest is per space cleared (<see cref="SpacePoints"/>) and a share
    /// of the beastmaster's remaining HP that is not linear (2,863 HP scored 1,224; 11,480 scored 2,023) and is left out.
    /// </summary>
    public const int CompletionPoints = 5000;

    /// <summary>Bonuses that pay at most one of their members. Earning one closes the others.</summary>
    public static readonly IReadOnlyList<CrucibleBonus[]> ExclusiveGroups =
    [
        [CrucibleBonus.NoBeastLeftBehind, CrucibleBonus.WillingSacrificeI, CrucibleBonus.WillingSacrificeII, CrucibleBonus.WillingSacrificeIII],
        [CrucibleBonus.BeastSquadI, CrucibleBonus.BeastSquadII, CrucibleBonus.BeastSquadIII],
        [CrucibleBonus.NamedayAttire, CrucibleBonus.WalkingArmory],
        [CrucibleBonus.StarveTheFever, CrucibleBonus.FeedTheBold],
        [
            CrucibleBonus.BeastkinGambit, CrucibleBonus.VilekinGambit, CrucibleBonus.CloudkinGambit, CrucibleBonus.SeedkinGambit,
            CrucibleBonus.WavekinGambit, CrucibleBonus.ScalekinGambit, CrucibleBonus.SoulkinGambit, CrucibleBonus.AshkinGambit,
        ],
        [CrucibleBonus.FirstDegree, CrucibleBonus.SecondDegree, CrucibleBonus.ThirdDegree],
        [CrucibleBonus.InfinityAndBeyondI, CrucibleBonus.InfinityAndBeyondII, CrucibleBonus.InfinityAndBeyondIII],
    ];

    /// <summary>
    /// The Legendary score on Standard difficulty, by board (XBMContent row index); 0 when unknown. Performance alone is
    /// ~7,500-8,600, so bonuses decide it.
    /// </summary>
    public static int LegendaryLine(int boardIndex) => boardIndex switch
    {
        1 => 17_500,
        2 => 17_500,
        3 => 17_875,
        4 => 18_750, // First Master's Board
        5 => 18_500, // Second Master's Board
        _ => 0,
    };

    /// <summary>
    /// Whether a status costs Unbeastable. By its English Status sheet name, so the adapter must read the English sheet
    /// in other client languages.
    /// </summary>
    public static bool BreaksUnbeastable(string statusName)
    {
        var name = statusName.Trim();
        return name.Equals("Damage Down", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Vulnerability Up", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The Beast Squad tier a roster of this size pays, or null (empty, or above the largest tier).</summary>
    public static CrucibleBonus? BeastSquadFor(int rosterSize) => rosterSize switch
    {
        <= 0 => null,
        <= BeastSquadIIIMaxRoster => CrucibleBonus.BeastSquadIII,
        <= BeastSquadIIMaxRoster => CrucibleBonus.BeastSquadII,
        <= BeastSquadIMaxRoster => CrucibleBonus.BeastSquadI,
        _ => null,
    };

    /// <summary>The Willing Sacrifice tier this many KOs pay, or null for none (No Beast Left Behind then).</summary>
    public static CrucibleBonus? WillingSacrificeFor(int knockOuts) => knockOuts switch
    {
        <= 0 => null,
        >= WillingSacrificeIIIMinKnockOuts => CrucibleBonus.WillingSacrificeIII,
        >= WillingSacrificeIIMinKnockOuts => CrucibleBonus.WillingSacrificeII,
        _ => CrucibleBonus.WillingSacrificeI,
    };

    /// <summary>Performance for clearing a space: enemy 250, elite 800, boss 1,000; nothing for the others.</summary>
    public static int SpacePoints(CrucibleStageType type) => type switch
    {
        CrucibleStageType.Enemy => 250,
        CrucibleStageType.Elite => 800,
        CrucibleStageType.Boss => 1000,
        _ => 0,
    };

    /// <summary>The least a space pays: a random one at its poorest outcome.</summary>
    public static int LeastPoints(CrucibleStage stage) =>
        stage.Type == CrucibleStageType.Random
            ? stage.Options.Count > 0 ? stage.Options.Min(o => SpacePoints(o.Type)) : 0
            : SpacePoints(stage.Type);

    /// <summary>A space where the beastmaster can rest (costs Sleepless Survival): a campsite, or a random one that can be one.</summary>
    public static bool CanRest(CrucibleStage stage) =>
        stage.Type == CrucibleStageType.Campsite
        || (stage.Type == CrucibleStageType.Random && stage.Options.Any(o => o.Type == CrucibleStageType.Campsite));
}

/// <summary>
/// The live bonus tracker for one Crucible board run: fed the run's events, it knows for every bonus the board pays
/// whether it is still intact, lost, earned or decided at the end, and projects the score (<see cref="Snapshot"/>).
/// Pure; one per run. Bonuses are paid only if the board is finished: a death scores performance alone.
/// <para>
/// Adapter order: create at board start with the registered roster; send events as they happen; call
/// <see cref="SpaceCleared"/> for every space left (battles and the rest), so the route ahead is known; before the
/// result, send <see cref="SetHeld"/> and <see cref="SetHp"/>, then <see cref="Finished"/>. Events after
/// <see cref="Finished"/> or <see cref="BeastmasterDied"/> are ignored.
/// </para>
/// </summary>
public sealed class CrucibleBonusTracker
{
    private readonly int _rosterSize;
    private readonly BeastClassification? _kin;

    private int _knockOuts;
    private bool _debuffed;
    private bool _rested;
    private int _feeds;
    private bool _gearAcquired;
    private bool _itemUsed;
    private bool _tokensSpent;
    private int _heldGear;
    private int _heldItems;
    private int _heldTokens;
    private float? _beastmasterPct;
    private IReadOnlyList<float>? _familiarPcts;
    private int _spacePoints;
    private int _lastMove;
    private bool _died;
    private bool _finished;

    /// <param name="rosterKins">The kin of each registered beast, one entry per beast. The roster cannot change mid-board.</param>
    public CrucibleBonusTracker(CrucibleBoard board, IEnumerable<BeastClassification> rosterKins)
    {
        Board = board;
        var kins = rosterKins.ToList();
        _rosterSize = kins.Count;
        // An unknown kin cannot be shown to match (GambitFor would read it as Ashkin).
        _kin = kins.Count > 0 && kins[0] != BeastClassification.Unknown && kins.All(k => k == kins[0]) ? kins[0] : null;
    }

    public CrucibleBoard Board { get; }

    private bool Over => _died || _finished;

    /// <summary>A familiar was knocked out. A retreat or Parting Blow is not a KO.</summary>
    public void FamiliarKnockedOut()
    {
        if (!Over)
            _knockOuts++;
    }

    /// <summary>The beastmaster gained a status, by its English Status sheet name.</summary>
    public void BeastmasterStatusGained(string statusName)
    {
        if (!Over && CrucibleBonusRules.BreaksUnbeastable(statusName))
            _debuffed = true;
    }

    /// <summary>Rested at a campsite. Leaving without resting is not a rest.</summary>
    public void Rested()
    {
        if (!Over)
            _rested = true;
    }

    /// <summary>A beast was fed.</summary>
    public void Fed()
    {
        if (!Over)
            _feeds++;
    }

    /// <summary>Beast gear was acquired (spoils, coffer, shop).</summary>
    public void GearAcquired()
    {
        if (!Over)
            _gearAcquired = true;
    }

    /// <summary>A Crucible item was used (potion, ash, reraiser, fang, sands…).</summary>
    public void ItemUsed()
    {
        if (!Over)
            _itemUsed = true;
    }

    /// <summary>Tokens were spent. Zero or less is ignored.</summary>
    public void TokensSpent(int amount)
    {
        if (!Over && amount > 0)
            _tokensSpent = true;
    }

    /// <summary>
    /// A space was left. <paramref name="type"/> is what it turned out to be (a random space that was an enemy is
    /// <see cref="CrucibleStageType.Enemy"/>); <paramref name="move"/> is its <see cref="CrucibleStage.Move"/>. A move
    /// already left is ignored, so a repeated event does not score twice.
    /// </summary>
    public void SpaceCleared(CrucibleStageType type, int move)
    {
        if (Over || move <= _lastMove)
            return;
        _spacePoints += CrucibleBonusRules.SpacePoints(type);
        _lastMove = move;
    }

    /// <summary>What is held now; the end-decided bonuses read the last values.</summary>
    public void SetHeld(int gearCount, int itemCount, int tokens)
    {
        if (Over)
            return;
        _heldGear = gearCount;
        _heldItems = itemCount;
        _heldTokens = tokens;
    }

    /// <summary>HP in percent (0-100) now. Full is exactly 100: compute as <c>hp * 100f / maxHp</c>.</summary>
    public void SetHp(float beastmasterPct, IReadOnlyList<float> familiarPcts)
    {
        if (Over)
            return;
        _beastmasterPct = beastmasterPct;
        _familiarPcts = familiarPcts.ToList();
    }

    /// <summary>The beastmaster died: the run ends, no bonus is paid.</summary>
    public void BeastmasterDied()
    {
        if (!Over)
            _died = true;
    }

    /// <summary>The board was finished (final boss beaten): the end-decided bonuses settle and completion is scored.</summary>
    public void Finished()
    {
        if (!Over)
            _finished = true;
    }

    /// <summary>Where every bonus the board pays stands, and the projected score.</summary>
    public CrucibleBonusSnapshot Snapshot()
    {
        var lines = new List<CrucibleBonusLine>();
        foreach (var bonus in Enum.GetValues<CrucibleBonus>())
        {
            var points = Board.BonusValue(bonus);
            if (points <= 0)
                continue;
            var (state, reason) = _died ? (CrucibleBonusState.Lost, CrucibleBonusReason.BeastmasterDied) : Settle(Judge(bonus));
            lines.Add(new CrucibleBonusLine(bonus, state, points, reason));
        }

        var performance = _spacePoints + (_finished ? CrucibleBonusRules.CompletionPoints : 0);
        var ifFinished = _died || _finished ? performance : performance + CrucibleBonusRules.CompletionPoints + LeastStillToClear();

        return new CrucibleBonusSnapshot(
            lines,
            Sure: SumOnePerGroup(lines, l => l.State == CrucibleBonusState.Earned),
            IfKept: SumOnePerGroup(lines, l => l.StillPossible),
            PerformanceSoFar: performance,
            PerformanceIfFinished: ifFinished,
            LegendaryLine: CrucibleBonusRules.LegendaryLine(Board.Index),
            Died: _died,
            Finished: _finished);
    }

    /// <summary>Once finished, what was on track is earned and what was at risk is lost.</summary>
    private (CrucibleBonusState, CrucibleBonusReason) Settle((CrucibleBonusState State, CrucibleBonusReason Reason) judged) =>
        !_finished ? judged : judged.State switch
        {
            CrucibleBonusState.Intact or CrucibleBonusState.DecidedAtEnd => (CrucibleBonusState.Earned, judged.Reason),
            CrucibleBonusState.AtRisk => (CrucibleBonusState.Lost, judged.Reason),
            _ => judged,
        };

    private (CrucibleBonusState, CrucibleBonusReason) Judge(CrucibleBonus bonus)
    {
        const CrucibleBonusReason ok = CrucibleBonusReason.None;
        (CrucibleBonusState, CrucibleBonusReason) LostTo(bool lost, CrucibleBonusReason reason) =>
            lost ? (CrucibleBonusState.Lost, reason) : (CrucibleBonusState.Intact, ok);
        (CrucibleBonusState, CrucibleBonusReason) AtEnd(bool met, CrucibleBonusReason reason) =>
            met ? (CrucibleBonusState.DecidedAtEnd, ok) : (CrucibleBonusState.AtRisk, reason);

        switch (bonus)
        {
            case CrucibleBonus.BeastSquadI or CrucibleBonus.BeastSquadII or CrucibleBonus.BeastSquadIII:
                return CrucibleBonusRules.BeastSquadFor(_rosterSize) == bonus
                    ? (CrucibleBonusState.Earned, ok)
                    : (CrucibleBonusState.Lost, CrucibleBonusReason.RosterSize);

            case >= CrucibleBonus.BeastkinGambit and <= CrucibleBonus.AshkinGambit:
                if (_kin is not { } kin)
                    return (CrucibleBonusState.Lost, CrucibleBonusReason.MixedKin);
                return CrucibleRunPlan.GambitFor(kin) == bonus
                    ? (CrucibleBonusState.Earned, ok)
                    : (CrucibleBonusState.Lost, CrucibleBonusReason.OtherKin);

            case CrucibleBonus.NoBeastLeftBehind:
                return LostTo(_knockOuts > 0, CrucibleBonusReason.FamiliarKnockedOut);

            case CrucibleBonus.WillingSacrificeI or CrucibleBonus.WillingSacrificeII or CrucibleBonus.WillingSacrificeIII:
                // A consolation nobody plays for, so it is never shown as reachable without a KO; the tier follows the
                // KO count, and more KOs can still raise it, so it is decided at the end.
                return CrucibleBonusRules.WillingSacrificeFor(_knockOuts) is not { } tier
                    ? (CrucibleBonusState.Lost, CrucibleBonusReason.NoKnockOut)
                    : tier == bonus
                        ? (CrucibleBonusState.DecidedAtEnd, ok)
                        : (CrucibleBonusState.Lost, CrucibleBonusReason.OtherTier);

            case CrucibleBonus.Unbeastable:
                return LostTo(_debuffed, CrucibleBonusReason.DebuffTaken);

            case CrucibleBonus.Selfless:
                return AtEnd(_familiarPcts is null || _familiarPcts.All(p => p >= 100f), CrucibleBonusReason.HpNotFull);

            case CrucibleBonus.SelfPreservation:
                return AtEnd(_beastmasterPct is null or >= 100f, CrucibleBonusReason.HpNotFull);

            case CrucibleBonus.SleeplessSurvival:
                if (_rested)
                    return (CrucibleBonusState.Lost, CrucibleBonusReason.Rested);
                // No campsite left on the route: it can no longer be lost.
                return Board.Route.Count > 0 && !Board.Route.Any(s => s.Move > _lastMove && CrucibleBonusRules.CanRest(s))
                    ? (CrucibleBonusState.Earned, ok)
                    : (CrucibleBonusState.Intact, ok);

            case CrucibleBonus.StarveTheFever:
                return LostTo(_feeds > 0, CrucibleBonusReason.Fed);

            case CrucibleBonus.FeedTheBold:
                // Unlike Willing Sacrifice it is a real plan (Board 3), so it stays reachable until the end.
                return _feeds >= CrucibleBonusRules.FeedTheBoldMinFeeds
                    ? (CrucibleBonusState.Earned, ok)
                    : (CrucibleBonusState.AtRisk, CrucibleBonusReason.NotFed);

            case CrucibleBonus.NamedayAttire:
                return LostTo(_gearAcquired, CrucibleBonusReason.GearAcquired);

            case CrucibleBonus.WalkingArmory:
                return AtEnd(_heldGear >= CrucibleBonusRules.WalkingArmoryMinGear, CrucibleBonusReason.NotEnoughHeld);

            case CrucibleBonus.EconomicEthos:
                return LostTo(_itemUsed, CrucibleBonusReason.ItemUsed);

            case CrucibleBonus.HabitualHoarder:
                return AtEnd(_heldItems >= CrucibleBonusRules.HabitualHoarderMinItems, CrucibleBonusReason.NotEnoughHeld);

            case CrucibleBonus.CoinClutcher:
                return LostTo(_tokensSpent, CrucibleBonusReason.TokensSpent);

            case CrucibleBonus.RollingInRiches:
                return AtEnd(_heldTokens >= CrucibleBonusRules.RollingInRichesMinTokens, CrucibleBonusReason.NotEnoughHeld);

            default: // Infinity and Beyond, the Degrees
                return (CrucibleBonusState.NotTracked, CrucibleBonusReason.OutOfScope);
        }
    }

    /// <summary>The least performance the moves ahead must pay: per move, its poorest space (a fork's poorer side).</summary>
    private int LeastStillToClear() =>
        Board.Route.Where(s => s.Move > _lastMove)
            .GroupBy(s => s.Move)
            .Sum(move => move.Min(CrucibleBonusRules.LeastPoints));

    /// <summary>The points of the lines that match, counting only the most valuable of each exclusive group.</summary>
    private static int SumOnePerGroup(IReadOnlyList<CrucibleBonusLine> lines, Func<CrucibleBonusLine, bool> counts)
    {
        var counted = lines.Where(counts).ToList();
        var grouped = CrucibleBonusRules.ExclusiveGroups
            .Sum(group => counted.Where(l => group.Contains(l.Bonus)).Select(l => l.Points).DefaultIfEmpty(0).Max());
        var ungrouped = counted
            .Where(l => !CrucibleBonusRules.ExclusiveGroups.Any(group => group.Contains(l.Bonus)))
            .Sum(l => l.Points);
        return grouped + ungrouped;
    }
}
