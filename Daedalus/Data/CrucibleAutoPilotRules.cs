using System;
using System.Collections.Generic;
using System.Linq;

namespace Daedalus.Data;

/// <summary>What to do with the spoils-of-battle window, one step at a time.</summary>
public enum CrucibleLootStep
{
    /// <summary>Take the first item (<c>XBMContentsBooty [4 | 0]</c>).</summary>
    TakeItem,

    /// <summary>Take the territory tokens (<c>[3]</c>).</summary>
    TakeTokens,

    /// <summary>Leave what is left (<c>[0]</c>).</summary>
    Leave,
}

/// <summary>One shop row: its index on the list, the XBMItem it sells, the price shown, and whether it is gone.</summary>
public readonly record struct CrucibleShopOffer(int Row, int ItemId, int Price, bool Sold);

/// <summary>
/// The Crucible autopilot's decisions, apart from the screens. Every click and prompt here was recorded from the
/// player's own runs (docs/crucible-screens.md); anything else is left alone. Pure.
/// </summary>
public static class CrucibleAutoPilotRules
{
    /// <summary>Wait after the horns are set before starting the battle — the player can still see and change them.</summary>
    public static readonly TimeSpan StartBattleAfter = TimeSpan.FromSeconds(1.5);

    /// <summary>
    /// The next loot step: beast gear while gear is wanted and not yet done, then the tokens (once), then leave.
    /// Taking everything closes the window by itself, so Leave only fires if it is still open.
    /// </summary>
    public static CrucibleLootStep NextLootStep(bool takeItems, bool gearDone, bool tokensTried)
    {
        if (takeItems && !gearDone)
            return CrucibleLootStep.TakeItem;
        if (!tokensTried)
            return CrucibleLootStep.TakeTokens;
        return CrucibleLootStep.Leave;
    }

    // The spoils window (XBMContentsBooty): the OFFERS are rows of five values from 7, the XBMItem id at 9 + 5·r (no
    // names). Taking row r is [4 | r]. Proven by the player's own picks (recording 2026-10-22): offers 77 (G2 beast
    // potion), 10 (twisted headband); [4 | 1] → "Take the twisted headband?". Values 27-76 are the Crucible items
    // held and 78-127 the beast gear held (Ring of Sacrifice there from the start; the Astral Wristlet appears only
    // after it was taken) — reading those as offers took a potion over the gear (field 2026-10-25, Saar).
    public const int LootFirstOfferId = 9;
    public const int LootRowStride = 5;
    public const int LootMaxRows = 4;

    public static int LootItemIdIndex(int row) => LootFirstOfferId + (LootRowStride * row);

    /// <summary>At most this many pieces of gear from one spoils window — a guard, the window offers at most four.</summary>
    public const int MaxGearTakes = 4;

    /// <summary>The beast gear already held: rows of five from 78, a flag at 78 + 5·k (up to ten).</summary>
    public const int HeldGearFirstFlag = 78;
    public const int HeldGearMaxRows = 10;

    public static int HeldGearFlagIndex(int row) => HeldGearFirstFlag + (LootRowStride * row);

    /// <summary>
    /// The Crucible items held, in the spoils window: ten slots of five values from 27; a slot's XBMItem row sits at
    /// 30 + 5·k and is 0 while the slot is empty (its placeholder name reads "Crucible Item N"). Recording 2026-10-25.
    /// </summary>
    public const int HeldItemFirstRow = 30;
    public const int HeldItemMaxRows = 10;

    public static int HeldItemRowIndex(int slot) => HeldItemFirstRow + (LootRowStride * slot);

    /// <summary>
    /// The Crucible items held, on the board HUD (XBMContentsMainHUD): ten slots of five values from 11; a slot's item id
    /// at 11 + 5·k, its XBMItem row at 12 + 5·k (0 = empty). Using one: HUD [i6, i slot, -] opens the item menu, then the
    /// menu's "Use" (its value 8) is [i0, i0, u0, -, -] — the potion fires as an action on the beastmaster at once
    /// (G3 Beast Potion 46961: 23% → 58%, recording Korha 2026-10-27).
    /// </summary>
    public const int HudFirstItemId = 11;

    public static int HudItemIdIndex(int slot) => HudFirstItemId + (LootRowStride * slot);

    public static int HudItemRowIndex(int slot) => HudItemIdIndex(slot) + 1;

    // Further HUD / campsite values (layouts as NightmareXIV/ECommons' MIT AtkReaders ReaderXBMContentsMainHUD and
    // ReaderXBMPetParty read them; cross-checked against another Crucible plugin's readers, 2026-10-28). Note the value at
    // 11 + 5·k that we call the item id is the item's ICON — it works as a key for the potions, just under that name.

    /// <summary>HUD: the tokens held, as text ("1,234").</summary>
    public const int HudTokensIndex = 8;

    /// <summary>HUD: whether a Crucible item slot can be used right now (10 + 5·k).</summary>
    public static int HudItemUsableIndex(int slot) => HudItemIdIndex(slot) - 1;

    /// <summary>HUD: beast gear held, ten slots — "owned" flag at 60 + 5·k.</summary>
    public const int HudGearSlots = 10;

    public static int HudGearHeldIndex(int slot) => 60 + (LootRowStride * slot);

    /// <summary>Campsite / team window (XBMPetParty): entry count at [5], rows of 77 values from 6.</summary>
    public const int PetPartyCountIndex = 5;

    public static int PetPartyFedIndex(int row) => 6 + (77 * row) + 72;

    /// <summary>"1,234" → 1234; null when it holds no digits.</summary>
    public static int? ParseTokens(string text)
    {
        var digits = new string(text.Where(char.IsDigit).ToArray());
        return digits.Length is > 0 and <= 9 ? int.Parse(digits, System.Globalization.CultureInfo.InvariantCulture) : null;
    }

    /// <summary>
    /// Tokens spent since the last reading: only a FALL counts (tokens rise from spoils and coffers and only fall at a
    /// shop). 0 for the first reading or a rise.
    /// </summary>
    public static int TokensSpent(int? before, int now) => before is { } b && now < b ? b - now : 0;

    /// <summary>
    /// The held potions offered to <see cref="PickPotion"/>: a slot the HUD says cannot be used right now (during
    /// another action — Korha 2026-10-27: two of three presses at critical HP did nothing) is left out.
    /// </summary>
    public static IReadOnlyList<uint> UsableOnly(IReadOnlyList<uint> heldItemIds, IReadOnlyList<bool> usable)
        => heldItemIds.Select((id, i) => i < usable.Count && !usable[i] ? 0u : id).ToList();

    /// <summary>The HUD's "Crucible item slot" event.</summary>
    public const int HudItemSlotEvent = 6;

    /// <summary>HP restored, % of max, by item id: beast potions G1-G4 (self or familiar), crucible ash G1-G3 (self and allies).</summary>
    public static readonly IReadOnlyDictionary<uint, int> PotionRestorePct = new Dictionary<uint, int>
    {
        [243136] = 10, [243137] = 23, [243138] = 36, [243139] = 50,
        [243140] = 10, [243141] = 25, [243142] = 40,
    };

    /// <summary>
    /// Fangs by HUD item id, strongest first: Fire 2,500, the other five elements 2,000, Vampiric 1,500 — each "to target
    /// and all enemies within 12 yalms of it" (XBMItem rows 128-134, read 2026-10-31).
    /// </summary>
    public static readonly IReadOnlyList<uint> FangItemIds = [243188, 243189, 243190, 243191, 243192, 243193, 243194];

    /// <summary>The Fang's 12 yalms, less a margin for where "within" is measured from.</summary>
    public const float FangReachYalms = 11f;

    /// <summary>
    /// Parting Blow's 8 yalms (the familiar's Aetheric Burst), less the same margin. Korha 2026-10-31: one Parting Blow
    /// killed the Siren's crawler and four Shambling Pieces bunched on the familiar — no item, so no Economic Ethos.
    /// </summary>
    public const float PartingBlowReachYalms = 7f;

    /// <summary>A Fang is not spent on a single add.</summary>
    public const int FangMinAdds = 2;

    /// <summary>The HUD slot of the strongest held Fang, or -1.</summary>
    public static int PickFang(IReadOnlyList<uint> heldItemIds)
    {
        foreach (var fang in FangItemIds)
        {
            for (var slot = 0; slot < heldItemIds.Count; slot++)
            {
                if (heldItemIds[slot] == fang)
                    return slot;
            }
        }

        return -1;
    }

    /// <summary>
    /// Who to aim a Fang at so it catches every living add, or 0 while they are still spread out (Ponzi's Third Board
    /// guide, the Siren: "wait a second for the tethered ad to move in", then a Fang kills them all). The hard target
    /// when it already covers them, else the first candidate (an add or the Siren) that does.
    /// </summary>
    /// <param name="reach">How far from the aim an add may stand: <see cref="FangReachYalms"/>, or
    /// <see cref="PartingBlowReachYalms"/> for Parting Blow.</param>
    public static ulong FangAim(
        IReadOnlyList<(ulong Id, System.Numerics.Vector2 At)> adds,
        IReadOnlyList<(ulong Id, System.Numerics.Vector2 At)> candidates, ulong hardTarget, float reach = FangReachYalms)
    {
        if (adds.Count < FangMinAdds)
            return 0;

        bool Covers(System.Numerics.Vector2 centre) => adds.All(a => System.Numerics.Vector2.Distance(a.At, centre) <= reach);
        foreach (var c in candidates)
        {
            if (c.Id == hardTarget && Covers(c.At))
                return c.Id;
        }

        foreach (var c in candidates)
        {
            if (Covers(c.At))
                return c.Id;
        }

        return 0;
    }

    /// <summary>Drink at or below this much HP (Clear and Level runs).</summary>
    public const float PotionAtHpPct = 0.40f;

    /// <summary>
    /// Legendary drinks only as a last resort, at or below this: a potion costs Economic Ethos (750), but dying ends the
    /// run with no bonuses at all (Korha 2026-10-27, Board 1 boss: 5,994 instead of ~16,000).
    /// </summary>
    public const float LegendaryPotionAtHpPct = 0.20f;

    /// <summary>
    /// The HUD slot of the potion to drink, or -1: at or below <paramref name="atHpPct"/> (default
    /// <see cref="PotionAtHpPct"/>), the held potion that restores the most (the first such slot on a tie). Only beast
    /// potions and crucible ash count.
    /// </summary>
    public static int PickPotion(IReadOnlyList<uint> heldItemIds, float selfHpPct, float atHpPct = PotionAtHpPct)
    {
        if (selfHpPct <= 0f || selfHpPct > atHpPct)
            return -1;

        var best = -1;
        var bestPct = 0;
        for (var slot = 0; slot < heldItemIds.Count; slot++)
        {
            if (PotionRestorePct.TryGetValue(heldItemIds[slot], out var pct) && pct > bestPct)
            {
                best = slot;
                bestPct = pct;
            }
        }

        return best;
    }

    /// <summary>
    /// How long a pressed "Use" has to take the potion out of the HUD before it counts as not gone off and is tried
    /// again. Korha 2026-10-27: two of three presses at critical HP did nothing (pressed during another action), and the
    /// old fixed 5s wait after each delayed the heal that worked by 10s.
    /// </summary>
    public const double PotionConfirmSeconds = 2.5;

    /// <summary>How many of the held slots carry this item.</summary>
    public static int CountHeld(IReadOnlyList<uint> heldItemIds, uint itemId)
    {
        var n = 0;
        foreach (var id in heldItemIds)
        {
            if (id == itemId)
                n++;
        }

        return n;
    }

    /// <summary>
    /// What became of a pressed potion: <c>true</c> drunk (fewer of it held than when pressed), <c>false</c> not gone off
    /// within <see cref="PotionConfirmSeconds"/> (try again), <c>null</c> still waiting to tell.
    /// </summary>
    public static bool? PotionOutcome(int heldWhenPressed, int heldNow, double secondsSincePress)
        => heldNow < heldWhenPressed ? true
            : secondsSincePress >= PotionConfirmSeconds ? false
            : null;

    /// <summary>The game holds at most ten Crucible items (and Habitual Hoarder pays for ending with ten).</summary>
    public const int MaxCrucibleItemsHeld = 10;

    /// <summary>
    /// The first untaken offer that is wanted, or -1: beast gear when gear is being taken, Crucible items (not feed)
    /// when potions are and fewer than <see cref="MaxCrucibleItemsHeld"/> are held. Gear first — it keeps the run alive.
    /// </summary>
    public static int PickLoot(
        IReadOnlyList<int> offers, Func<int, bool> isGear, Func<int, bool> isCrucibleItem, bool takeGear, bool takeItems,
        int itemsHeld, IReadOnlySet<int>? takenRows = null)
    {
        if (takeGear && PickGear(offers, isGear, takenRows) is var gear and >= 0)
            return gear;
        if (!takeItems || itemsHeld >= MaxCrucibleItemsHeld)
            return -1;
        for (var i = 0; i < offers.Count; i++)
        {
            if (offers[i] != 0 && isCrucibleItem(offers[i]) && takenRows?.Contains(i) != true)
                return i;
        }

        return -1;
    }

    // The shop (XBMContentsItemShop, recording 2026-10-22): [1] tokens ("1,200"), [2] how many rows, then five values a
    // row from [3]: shown, XBMItem row, price ("130" or "64 (-50%)"), discounted, sold. Buying row r is [2, r] — by
    // index, so a row below the fold is bought like any other. The ten held Crucible items follow, their XBMItem row at
    // [157 + 5k] (0 = empty slot).
    public const int ShopTokensIndex = 1;
    public const int ShopRowCountIndex = 2;
    public const int ShopMaxRows = 30;
    public const int ShopBuyEvent = 2;
    public const int ShopLeaveEvent = 0;

    public static int ShopItemIdIndex(int row) => 4 + (LootRowStride * row);

    public static int ShopPriceIndex(int row) => ShopItemIdIndex(row) + 1;

    public static int ShopSoldIndex(int row) => ShopItemIdIndex(row) + 3;

    public static int ShopHeldItemRowIndex(int slot) => 157 + (LootRowStride * slot);

    /// <summary>
    /// Whether another shop (or a random space that can be one) lies past the shop being visited. That shop is on
    /// <paramref name="currentMove"/> when the player has reached its row, else on the move after. True with no route.
    /// </summary>
    public static bool ShopAhead(IReadOnlyList<CrucibleStage> route, int currentMove)
    {
        static bool CanBeShop(CrucibleStage s) =>
            s.Type == CrucibleStageType.Shop || s.Options.Any(o => o.Type == CrucibleStageType.Shop);
        if (route.Count == 0)
            return true;
        var here = route.Any(s => s.Move == currentMove && s.Type == CrucibleStageType.Shop) ? currentMove : currentMove + 1;
        return route.Any(s => s.Move > here && CanBeShop(s));
    }

    /// <summary>"130" or "64 (-50%)" → the price shown; null when it starts with no digits.</summary>
    public static int? ParsePrice(string text) => ParseTokens(text.Trim().Split(' ')[0]);

    /// <summary>Crucible items' XBMItem rows 76-143 carry the HUD icon ids 243136-243203 (the ids the potion table uses).</summary>
    public const int CrucibleItemIconOffset = 243_060;

    /// <summary>
    /// Thief's Eye (loot ×3) and Merchant's Eye (tokens ×2) for the next enemy space cleared, HUD icon ids. "Can only be
    /// used on Crucible battlegrounds" — so inside a battle.
    /// </summary>
    public static readonly IReadOnlyList<uint> EyeItemIds = [243160, 243161];

    /// <summary>
    /// Whether to use the Eyes in this battle: Clear and Level runs (on Legendary an item costs Economic Ethos), in a
    /// battle that pays spoils — not the final boss, whose win ends the board.
    /// </summary>
    public static bool ShouldUseEyes(bool legendary, CrucibleEnemy? fighting) =>
        !legendary && fighting is { IsFinalBoss: false };

    /// <summary>The HUD slot of a held Eye not yet used this battle, or -1.</summary>
    public static int PickEye(IReadOnlyList<uint> heldItemIds, IReadOnlySet<uint> usedThisBattle)
    {
        for (var slot = 0; slot < heldItemIds.Count; slot++)
        {
            if (EyeItemIds.Contains(heldItemIds[slot]) && !usedThisBattle.Contains(heldItemIds[slot]))
                return slot;
        }

        return -1;
    }

    /// <summary>The board with the Siren, whose bunched adds a Fang clears (Third Board).</summary>
    public const int SirenBoardIndex = 3;

    /// <summary>The HP a Crucible item restores (<see cref="PotionRestorePct"/>), by its XBMItem row; 0 for anything else.</summary>
    public static int ShopRestorePct(int itemRow) =>
        PotionRestorePct.TryGetValue((uint)(itemRow + CrucibleItemIconOffset), out var pct) ? pct : 0;

    /// <summary>
    /// What to buy so the beastmaster has potions to drink (any goal but Legendary, with "use potions" on): the
    /// strongest healing items first, cheaper first on a tie, while there is a free item slot and tokens for it. Never
    /// feed or gear. With <paramref name="wantFang"/>, the strongest affordable Fang goes first. Rows in buying order.
    /// </summary>
    public static IReadOnlyList<int> ShopBuysForPotions(
        IReadOnlyList<CrucibleShopOffer> offers, int tokens, int itemsHeld, Func<int, bool> isCrucibleItem, bool wantFang = false)
    {
        var buys = new List<int>();
        // A Fang for the Siren's adds first, when asked for (the Third Board, none held): the strongest affordable one.
        if (wantFang && itemsHeld < MaxCrucibleItemsHeld
            && offers.Where(o => !o.Sold && o.Price <= tokens && FangItemIds.Contains((uint)(o.ItemId + CrucibleItemIconOffset)))
                .OrderBy(o => FangItemIds.ToList().IndexOf((uint)(o.ItemId + CrucibleItemIconOffset)))
                .FirstOrDefault() is { ItemId: > 0 } fang)
        {
            tokens -= fang.Price;
            buys.Add(fang.Row);
        }

        foreach (var offer in offers
                     .Where(o => !o.Sold && isCrucibleItem(o.ItemId) && ShopRestorePct(o.ItemId) > 0)
                     .OrderByDescending(o => ShopRestorePct(o.ItemId)).ThenBy(o => o.Price))
        {
            if (itemsHeld + buys.Count >= MaxCrucibleItemsHeld)
                break;
            if (offer.Price > tokens)
                continue;
            tokens -= offer.Price;
            buys.Add(offer.Row);
        }

        return buys;
    }

    /// <summary>
    /// What to buy on Legendary: the cheapest Crucible items that bring the held items to ten for Habitual Hoarder, at
    /// the last shop on the route only (spoils and coffers fill the slots for free until then) and only when this shop
    /// can finish the ten. Worth it only when Hoarder pays more than the purchase costs: Coin Clutcher (pass 0 once
    /// tokens were spent on the board) and Rolling in Riches when the bill takes the tokens under 2,000. Empty for none.
    /// </summary>
    public static IReadOnlyList<int> ShopBuysForHoarder(
        IReadOnlyList<CrucibleShopOffer> offers, int tokens, int itemsHeld, Func<int, bool> isCrucibleItem,
        bool shopAhead, int hoarderPoints, int coinClutcherPoints, int rollingInRichesPoints)
    {
        var need = MaxCrucibleItemsHeld - itemsHeld;
        if (shopAhead || need <= 0 || hoarderPoints <= 0)
            return [];
        var cheapest = offers.Where(o => !o.Sold && isCrucibleItem(o.ItemId)).OrderBy(o => o.Price).Take(need).ToList();
        var cost = cheapest.Sum(o => o.Price);
        if (cheapest.Count < need || cost > tokens)
            return [];
        var lost = coinClutcherPoints
                   + (rollingInRichesPoints > 0 && tokens >= CrucibleBonusRules.RollingInRichesMinTokens
                      && tokens - cost < CrucibleBonusRules.RollingInRichesMinTokens ? rollingInRichesPoints : 0);
        return hoarderPoints > lost ? cheapest.Select(o => o.Row).ToList() : [];
    }

    /// <summary>
    /// Whether the spoils' gear is done: no untaken gear left, the guard reached, or the last take did not add to
    /// the gear held (the game refused it — e.g. a full beast-gear inventory). Every piece of gear is taken, not just
    /// the first (user 2026-10-25). A TAKEN piece stays listed in the offers (recording 2026-10-25: the Ninja Suit
    /// still at row 0 after "Take the ninja suit?"), so taken rows are remembered rather than read back.
    /// </summary>
    public static bool LootGearDone(bool gearOffered, int takes, bool lastTakeRefused)
        => !gearOffered || takes >= MaxGearTakes || lastTakeRefused;

    /// <summary>
    /// The first offered row that is beast gear, or -1. Beast gear is what keeps the run alive (Ring of Sacrifice,
    /// Belt of Constitution, Mythril Armor…); Crucible items and feed are left. The rows are not sorted by type, so
    /// "row 0" was luck (field 2026-10-25: no gear taken on Saar's clear).
    /// </summary>
    public static int PickGear(IReadOnlyList<int> offers, Func<int, bool> isGear, IReadOnlySet<int>? takenRows = null)
    {
        for (var i = 0; i < offers.Count; i++)
        {
            if (offers[i] != 0 && isGear(offers[i]) && takenRows?.Contains(i) != true)
                return i;
        }

        return -1;
    }

    /// <summary>The treasure coffer's offers: an XBMItem id at 6 + 5·i, up to four; a pick is [2, i].</summary>
    public const int TreasureFirstOfferId = 6;
    public const int TreasureMaxRows = 4;

    public static int TreasureItemIdIndex(int row) => TreasureFirstOfferId + (LootRowStride * row);

    // What a point of each stat is worth in a coffer pick. Every Crucible death so far was HP attrition (no healing,
    // HP carried between battles), so staying alive outranks damage. A "taken"/"vulnerability" weight is negative
    // because lower is better there.
    private static readonly (string Stat, float Weight)[] TreasureWeights =
    [
        ("Maximum HP", 1.0f),
        ("Damage Taken", -1.5f),
        ("Physical Vulnerability", -1.0f),
        ("Magic Vulnerability", -0.6f),
        ("Physical Damage Dealt", 0.5f),
        ("Magic Damage Dealt", 0.15f),
        ("Damage Dealt", 0.6f),
        ("Haste", 0.4f),
        ("Critical Hit Rate", 0.3f),
        ("Evasion", 0.3f),
        ("Rapid Recast", 0.2f),
        ("Movement Speed", 0.2f),
        ("TP Accumulation", 0.1f),
        ("Block Strength", 0.05f),
        ("Loot Drop Rate", 0.05f),
        ("Territory Tokens Earned", 0.05f),
    ];

    private static readonly System.Text.RegularExpressions.Regex TreasureStatLine =
        new(@"^(?<stat>[A-Za-z' ]+?) (?<sign>[+-])(?<n>\d+)%", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>Per point of a resistance the board calls for ("Paralysis Resistance +100%" scores 35).</summary>
    public const float WantedResistanceWeight = 0.35f;

    /// <summary>
    /// The statuses each board's fights make worth resisting, by board (XBMContent row, = CrucibleBoard.Index). Research 2026-10-26
    /// (docs/crucible-boards-guide.md): Board 2's Taurus/Ahriman Doom; Board 3's Guttler Thunderbolt and Sahagin spikes
    /// (uncleansable Paralysis); First Master's Borgny (poison resistance "trivializes" it) and Morbol; Second Master's
    /// Lauda (anti-paralysis "mandatory"), King Ahriman's Dirty Eye Doom, Medusa and Deepeye petrification.
    /// </summary>
    public static IReadOnlyList<string> WantedResistances(int board) => board switch
    {
        2 => ["Doom"],
        3 => ["Paralysis"],
        4 => ["Poison", "Paralysis"],
        5 => ["Paralysis", "Doom", "Petrification"],
        _ => [],
    };

    private static readonly System.Text.RegularExpressions.Regex RecoverAfterBattle =
        new(@"Recovers (?<n>\d+)% of maximum HP at the end of battles", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// How much a piece of beast gear is worth from a coffer, read from its effect text (XBMItem column 12, e.g.
    /// "Maximum HP +20%"): survival first (Max HP, damage taken down, HP back after battles, the revive), then damage.
    /// A chance to Stun, Doom or chain yourself costs points. Unknown text scores 0.
    /// </summary>
    /// <para>A resistance the board calls for (<paramref name="wantedResistances"/>, e.g. "Paralysis" for
    /// "Paralysis Resistance +100%") scores <see cref="WantedResistanceWeight"/> per point.</para>
    public static float TreasureScore(string? effect, IReadOnlyCollection<string>? wantedResistances = null)
    {
        if (string.IsNullOrEmpty(effect))
            return 0f;

        var score = 0f;
        foreach (var raw in effect.Split('\n', '\r'))
        {
            var line = raw.Trim();
            var m = TreasureStatLine.Match(line);
            if (m.Success)
            {
                var stat = m.Groups["stat"].Value;
                var value = int.Parse(m.Groups["n"].Value) * (m.Groups["sign"].Value == "-" ? -1 : 1);
                if (wantedResistances != null && stat.EndsWith(" Resistance", StringComparison.Ordinal)
                    && wantedResistances.Contains(stat[..^" Resistance".Length]))
                {
                    score += WantedResistanceWeight * value;
                    continue;
                }

                foreach (var (name, weight) in TreasureWeights)
                {
                    if (string.Equals(stat, name, StringComparison.Ordinal))
                    {
                        score += weight * value;
                        break;
                    }
                }

                continue;
            }

            var recover = RecoverAfterBattle.Match(line);
            if (recover.Success)
                score += 1.2f * int.Parse(recover.Groups["n"].Value);
            else if (line.StartsWith("Revives beastmaster", StringComparison.Ordinal))
                score += 40f;
            else if (line.Contains("on self", StringComparison.Ordinal))
                score -= 8f;
        }

        return score;
    }

    /// <summary>
    /// The coffer offer to pick: the best <see cref="TreasureScore"/> among offers not yet taken (the first on a tie),
    /// or -1. A thief's knife yields two picks from one coffer; the window stays open for the second.
    /// </summary>
    public static int PickTreasure(
        IReadOnlyList<int> offers, Func<int, string?> effectOf, IReadOnlySet<int>? takenRows = null,
        IReadOnlyCollection<string>? wantedResistances = null)
    {
        var best = -1;
        var bestScore = float.MinValue;
        for (var i = 0; i < offers.Count; i++)
        {
            if (offers[i] == 0 || takenRows?.Contains(i) == true)
                continue;
            var score = TreasureScore(effectOf(offers[i]), wantedResistances);
            if (score > bestScore)
            {
                bestScore = score;
                best = i;
            }
        }

        return best;
    }

    /// <summary>
    /// For Legendary, whether to take beast gear on this board: where holding 10 pieces pays (Walking Armory, Boards 2/3
    /// and up) the gear is taken, as the Legendary runs found do (Meoni's series, Merton9999 on the forum) — it also keeps
    /// the three beasts alive; where it pays nothing (Board 1) none is taken, for Nameday Attire.
    /// </summary>
    public static bool LegendaryTakesGear(CrucibleBoard board) => board.BonusValue(CrucibleBonus.WalkingArmory) > 0;

    /// <summary>The coffer's close button ([0], then Yes to "Close the coffer without receiving any items?"; recording 2026-10-27).</summary>
    public const int TreasureCloseEvent = 0;

    /// <summary>
    /// For Legendary where gear costs Nameday Attire: close the coffer when every untaken offer is beast gear (taking
    /// nothing keeps the bonus). False when nothing is offered.
    /// </summary>
    public static bool CloseCofferForNameday(IReadOnlyList<int> offers, Func<int, bool> isGear, IReadOnlySet<int>? takenRows = null)
    {
        var any = false;
        for (var i = 0; i < offers.Count; i++)
        {
            if (offers[i] == 0 || takenRows?.Contains(i) == true)
                continue;
            if (!isGear(offers[i]))
                return false;
            any = true;
        }

        return any;
    }

    /// <summary>
    /// For Legendary: the best offer that is NOT beast gear (Nameday Attire is lost the moment gear is acquired); the best
    /// gear only when every remaining offer is gear (a coffer has to be taken from).
    /// </summary>
    public static int PickTreasureAvoidingGear(
        IReadOnlyList<int> offers, Func<int, bool> isGear, Func<int, string?> effectOf, IReadOnlySet<int>? takenRows = null,
        IReadOnlyCollection<string>? wantedResistances = null)
    {
        var nonGear = offers.Select(id => isGear(id) ? 0 : id).ToList();
        var pick = PickTreasure(nonGear, effectOf, takenRows, wantedResistances);
        return pick >= 0 ? pick : PickTreasure(offers, effectOf, takenRows, wantedResistances);
    }

    /// <summary>The start of each Yes/No prompt the autopilot's own clicks raise; any other prompt is the player's.</summary>
    public static readonly string[] KnownPrompts =
    [
        "Take ",                          // "Take 203 territory tokens?" / "Take the astral wristlet?"
        "Some loot remains unclaimed.",   // leaving the spoils
        "Choose the ",                    // treasure coffer
        "Conclude purchasing and leave",  // leaving the shop
        "Purchase the ",                  // buying a Crucible item, should the shop ask (never a feed: those are not bought)
        "Rest ",                          // campsite: "Rest and recover 45%…" / "Rest alone while your familiars keep watch…"
        "Leave the campsite",             // campsite, Legendary: "Leave the campsite without resting? / You cannot return…"
        "Close the coffer",               // coffer, Legendary: "Close the coffer without receiving any items? / You cannot reopen…"
    ];

    /// <summary>Answer Yes only to a prompt the autopilot itself just raised.</summary>
    public static bool ShouldConfirm(string prompt, string? expected) =>
        expected != null && prompt.StartsWith(expected, StringComparison.Ordinal) && KnownPrompts.Contains(expected)
        && !prompt.Contains(" and feed it ", StringComparison.Ordinal); // a feed's purchase picks a beast: never ours

    /// <summary>Under this much HP the beastmaster rests alone: 90% for you, against 45% when a beast shares it.</summary>
    public const float RestAloneBelowSelfHpPct = 0.50f;

    /// <summary>
    /// Who rests at a campsite with you: nobody (-1) when you are under half HP — your HP is the run, and alone you
    /// recover 90% instead of 45% (field 2026-10-25: rested Goobbue with the beastmaster at 13%, died the next fight
    /// at 37%); otherwise the standing team member with the least HP left, as a fraction of its max. -1 also when
    /// nobody on the team is standing.
    /// </summary>
    public static int CampsitePick(IReadOnlyList<(uint Hp, uint MaxHp)> team, float selfHpPct = 1f)
    {
        if (selfHpPct < RestAloneBelowSelfHpPct)
            return -1;

        var best = -1;
        var bestFraction = double.MaxValue;
        for (var i = 0; i < team.Count; i++)
        {
            var (hp, max) = team[i];
            if (hp == 0 || max == 0)
                continue;
            var fraction = (double)hp / max;
            if (fraction < bestFraction)
            {
                bestFraction = fraction;
                best = i;
            }
        }

        return best;
    }

    /// <summary>Start the battle once the horns have been set for it and have stood a moment.</summary>
    public static bool ShouldStartBattle(int upcomingBattle, int hornsReadyFor, TimeSpan sinceHornsReady, int alreadyStarted) =>
        upcomingBattle != 0
        && hornsReadyFor == upcomingBattle
        && sinceHornsReady >= StartBattleAfter
        && alreadyStarted != upcomingBattle;
}
