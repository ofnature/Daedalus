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

/// <summary>What a final boss mostly hits with, for the damage reduction to take (<see cref="CrucibleAutoPilotRules.FinalBossDamage"/>).</summary>
public enum BossDamageKind
{
    Unknown,
    Physical,
    Magic,
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
    /// Taking everything closes the window by itself, so Leave only fires if it is still open. No tokens step when the
    /// window offers none (a Chemist's Satchel potion at a campsite): the game would not close "Take 0 territory tokens?"
    /// on Yes (field 2026-11-07, Saar: stuck on it for 37 minutes).
    /// </summary>
    public static CrucibleLootStep NextLootStep(bool takeItems, bool gearDone, bool tokensTried, bool tokensOffered = true)
    {
        if (takeItems && !gearDone)
            return CrucibleLootStep.TakeItem;
        if (!tokensTried && tokensOffered)
            return CrucibleLootStep.TakeTokens;
        return CrucibleLootStep.Leave;
    }

    /// <summary>The spoils window's tokens offered (<c>XBMContentsBooty</c> value 4).</summary>
    public const int LootTokensIndex = 4;

    /// <summary>
    /// "Take 0 territory tokens?": answered No whoever raised it — there is nothing to take, and Yes leaves it up.
    /// </summary>
    public static bool IsEmptyTokenPrompt(string prompt) =>
        prompt.StartsWith("Take 0 territory tokens", StringComparison.Ordinal);

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
    /// Fangs by HUD item id, strongest first: Fire 2,500, the other five elements 2,000 — each "to target and all enemies
    /// within 12 yalms of it" (XBMItem rows 128-133, read 2026-10-31). Not the Vampiric Fang (<see cref="VampiricFangItemId"/>).
    /// </summary>
    /// <para>Celestial Sand (row 139, HUD 243199) leads: 3,000 fire potency to the target and everything within 18
    /// yalms, the same kind of item (user, 2026-11-06: a held item nothing uses is sold, and this one is worth using).</para>
    public static readonly IReadOnlyList<uint> FangItemIds = [243199, 243188, 243189, 243190, 243191, 243192, 243193];

    /// <summary>
    /// The Vampiric Fang (row 134): 1,500-3,300 piercing to the target and all within 12 yalms, part absorbed as HP. Held,
    /// never spent on adds; it is the heal after the potions (user, 2026-11-07: "potion first, the vampire fang should
    /// be held").
    /// </summary>
    public const uint VampiricFangItemId = 243194;

    /// <summary>
    /// The HUD slot of the Vampiric Fang to heal with, or -1: only at or below the potion line, with no potion to drink
    /// (<paramref name="potionPick"/> &lt; 0) and an enemy targeted to hit.
    /// </summary>
    public static int PickVampiricFangHeal(
        IReadOnlyList<uint> heldItemIds, float selfHpPct, float atHpPct, int potionPick, bool enemyTargeted)
    {
        if (potionPick >= 0 || !enemyTargeted || selfHpPct <= 0f || selfHpPct > atHpPct)
            return -1;
        for (var slot = 0; slot < heldItemIds.Count; slot++)
        {
            if (heldItemIds[slot] == VampiricFangItemId)
                return slot;
        }

        return -1;
    }

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
    /// Legendary once an item has been used on the board (Economic Ethos already lost): drink at about half, as the
    /// guides do (docs/crucible-legendary-strats.md §9.4) — another potion costs nothing more.
    /// </summary>
    public const float LegendaryAfterEthosPotionAtHpPct = 0.45f;

    /// <summary>The First Board of the Unbroken (XBMContent index).</summary>
    public const int FirstBoardIndex = 1;

    /// <summary>
    /// Legendary drinks its last-resort potion on every board, the First included, when the potion can save the run
    /// (user, 2026-11-06: dying scores no bonuses at all, so a potion that saves the run is worth Economic Ethos; one that
    /// only delays the death is not). It can while any familiar is left to fight; with none left, only if the enemy dies
    /// before the beastmaster would with the potion's HP added, at the rates both are losing HP now. An unknown rate
    /// (too few samples, or nothing being lost) counts as "it can": the potion is drunk.
    /// </summary>
    /// <param name="selfPct">Beastmaster HP now (0-1).</param>
    /// <param name="healPct">What the potion restores (0-1).</param>
    /// <param name="selfLossPerSecond">Beastmaster HP lost per second (0-1 of max), or null when unknown.</param>
    /// <param name="enemyPct">The enemy's HP (0-1), or null when unknown.</param>
    /// <param name="enemyLossPerSecond">The enemy's HP lost per second (0-1 of max), or null when unknown.</param>
    public static bool PotionCanSaveRun(
        int familiarsLeft, float selfPct, float healPct, float? selfLossPerSecond, float? enemyPct, float? enemyLossPerSecond)
    {
        if (familiarsLeft > 0)
            return true;
        if (selfLossPerSecond is not > 0f || enemyPct is not { } enemy || enemyLossPerSecond is not > 0f)
            return true;
        var secondsToLive = (selfPct + healPct) / selfLossPerSecond.Value;
        var secondsToKill = enemy / enemyLossPerSecond.Value;
        return secondsToKill <= secondsToLive;
    }

    /// <summary>Samples older than this are dropped from an HP-loss rate.</summary>
    public const double HpRateWindowSeconds = 15;

    /// <summary>A rate needs samples spanning at least this long; shorter is unknown.</summary>
    public const double HpRateMinSpanSeconds = 5;

    /// <summary>
    /// HP lost per second (0-1 of max) over the samples (seconds, HP 0-1, oldest first): from the first to the last;
    /// null when they span under <see cref="HpRateMinSpanSeconds"/>. A gain over the window reads as 0.
    /// </summary>
    public static float? HpLossPerSecond(IReadOnlyList<(double Seconds, float Pct)> samples)
    {
        if (samples.Count < 2)
            return null;
        var span = samples[^1].Seconds - samples[0].Seconds;
        if (span < HpRateMinSpanSeconds)
            return null;
        return (float)Math.Max(0, (samples[0].Pct - samples[^1].Pct) / span);
    }

    /// <summary>The Legendary drinking line: last resort while Economic Ethos holds, about half once it is gone.</summary>
    public static float LegendaryDrinkAtHpPct(bool itemUsedOnBoard) =>
        itemUsedOnBoard ? LegendaryAfterEthosPotionAtHpPct : LegendaryPotionAtHpPct;

    /// <summary>
    /// Whether to top HP up at the final boss: the remaining-HP share is only scored at the end and low HP scores 0
    /// (docs/crucible-legendary-strats.md §1.3, §9.3). Legendary, once Economic Ethos is gone, and not while holding the
    /// ten items Habitual Hoarder still pays for.
    /// </summary>
    public static bool TopsOffForTheEnd(bool legendary, bool finalBoss, bool itemUsedOnBoard, bool hoarderPays, int itemsHeld) =>
        legendary && finalBoss && itemUsedOnBoard && !(hoarderPays && itemsHeld >= MaxCrucibleItemsHeld);

    /// <summary>The HUD slot of the strongest potion that would not overheal, or -1.</summary>
    public static int PickTopOff(IReadOnlyList<uint> heldItemIds, float selfHpPct)
    {
        if (selfHpPct <= 0f)
            return -1;
        var best = -1;
        var bestPct = 0;
        for (var slot = 0; slot < heldItemIds.Count; slot++)
        {
            if (PotionRestorePct.TryGetValue(heldItemIds[slot], out var pct) && pct > bestPct && selfHpPct + (pct / 100f) <= 1f)
            {
                best = slot;
                bestPct = pct;
            }
        }

        return best;
    }

    /// <summary>
    /// The HUD slot of the potion to drink, or -1: at or below <paramref name="atHpPct"/> (default
    /// <see cref="PotionAtHpPct"/>), the held potion that restores the most (the first such slot on a tie). Beast
    /// potions and crucible ash count, and with the Crimson Ribbon held (<paramref name="ribbonHeld"/>) every soul serum
    /// at 50% (<see cref="RibbonSerumRestorePct"/>). Serums in <paramref name="keepSerumRows"/> (the final boss's, before
    /// that fight) only when nothing else heals.
    /// </summary>
    public static int PickPotion(IReadOnlyList<uint> heldItemIds, float selfHpPct, float atHpPct = PotionAtHpPct,
        bool ribbonHeld = false, IReadOnlyCollection<int>? keepSerumRows = null)
    {
        if (selfHpPct <= 0f || selfHpPct > atHpPct)
            return -1;

        int best = -1, bestPct = 0, kept = -1, keptPct = 0;
        for (var slot = 0; slot < heldItemIds.Count; slot++)
        {
            var pct = HealRestorePct(heldItemIds[slot], ribbonHeld);
            if (pct <= 0)
                continue;
            var row = (int)heldItemIds[slot] - CrucibleItemIconOffset;
            if (IsSoulSerumRow(row) && keepSerumRows?.Contains(row) == true)
            {
                if (pct > keptPct)
                    (kept, keptPct) = (slot, pct);
            }
            else if (pct > bestPct)
            {
                (best, bestPct) = (slot, pct);
            }
        }

        return best >= 0 ? best : kept;
    }

    /// <summary>The Crimson Ribbon's XBMItem row (Petrification, Blind and Paralysis Resistance +100%).</summary>
    public const int CrimsonRibbonRow = 18;

    /// <summary>The soul serums' XBMItem rows: G1/G2 Antipoison, Antiparalysis, Antiblind, Antipetrification, Antisleep.</summary>
    public const int FirstSoulSerumRow = 86, LastSoulSerumRow = 95;

    /// <summary>
    /// What a soul serum restores with the Crimson Ribbon held: "When carrying a crimson ribbon, restores 50% of target's
    /// total HP" (every grade; TPLemont's D3 guides, user 2026-11-08).
    /// </summary>
    public const int RibbonSerumRestorePct = 50;

    public static bool IsSoulSerumRow(int row) => row is >= FirstSoulSerumRow and <= LastSoulSerumRow;

    /// <summary>What a held item (HUD id) heals, in percent of max HP; 0 for anything that does not.</summary>
    public static int HealRestorePct(uint itemId, bool ribbonHeld)
        => PotionRestorePct.TryGetValue(itemId, out var pct) ? pct
            : ribbonHeld && IsSoulSerumRow((int)itemId - CrucibleItemIconOffset) ? RibbonSerumRestorePct : 0;

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

    /// <summary>
    /// Selling (recording 2026-11-04, Korha, Second Master's Board): [3, k] sells the held Crucible item in slot k,
    /// [4, k] the beast gear in HUD gear slot k; each asks "Sell the … for N territory tokens?". Items sell for about
    /// 45% of their price, gear for about 20%.
    /// </summary>
    public const int ShopSellItemEvent = 3;
    public const int ShopSellGearEvent = 4;

    /// <summary>
    /// Temporal Sand's XBMItem row (the shop's held list and the director's inventory both read 138). Sold for 315
    /// tokens at any shop outside Legendary (user 2026-11-04: sell rather than keep it as a retry).
    /// </summary>
    public const int TemporalSandRow = 138;

    /// <summary>The Crucible items' XBMItem rows (76 G1 Beast Potion … 143 Steelsting Tome).</summary>
    public const int FirstCrucibleItemRow = 76, LastCrucibleItemRow = 143;

    /// <summary>
    /// Whether Daedalus uses a held Crucible item on this board: the potions and ashes, the Eyes, the Fangs and Celestial
    /// Sand, and the board's boss serums (<see cref="BossSerumRows"/>). Everything else is sold (user, 2026-11-06): the
    /// tomes, Temporal Sand (a fight restarted with the same worn beasts doesn't win), the kits, reraisers and potions it
    /// never drinks.
    /// </summary>
    /// <para>With the Crimson Ribbon held (<paramref name="ribbonHeld"/>) every soul serum is a 50% heal, so none is sold.</para>
    public static bool UsesItem(int row, int board, bool ribbonHeld = false) =>
        (ribbonHeld && IsSoulSerumRow(row))
        || PotionRestorePct.ContainsKey((uint)(row + CrucibleItemIconOffset))
        || EyeItemIds.Contains((uint)(row + CrucibleItemIconOffset))
        || FangItemIds.Contains((uint)(row + CrucibleItemIconOffset))
        || row + CrucibleItemIconOffset == VampiricFangItemId
        || (row == CrucibleAntidoteRow && board == AntidoteBoardIndex)
        // The First Board's only shop is before Pas de Seul, whose tankbusters are magic: a Tannin still helps there,
        // a Tome of the Impervious does not (it is sold, as before).
        || (row == CrucibleTanninRow && board == AntidoteBoardIndex)
        || BossSerumRows(board).Contains(row);

    /// <summary>
    /// The held-item slot to sell at a shop: the first Crucible item <see cref="UsesItem"/> says is unused, or -1. Slots
    /// are in the shop's held order; 0 = empty.
    /// </summary>
    public static int ShopSellSlot(IReadOnlyList<int> heldRowsBySlot, int board, bool ribbonHeld = false)
    {
        for (var k = 0; k < heldRowsBySlot.Count; k++)
        {
            var row = heldRowsBySlot[k];
            if (row is >= FirstCrucibleItemRow and <= LastCrucibleItemRow && !UsesItem(row, board, ribbonHeld))
                return k;
        }

        return -1;
    }

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

    /// <summary>
    /// The resistance serums (XBMItem rows, best first) for a board's final boss (research 2026-11-01): Guttler and Lauda
    /// paralyse (Thunderbolt) — G2 Antiparalysis lasts 300 s, the whole fight, for you and the familiar; Borgny's
    /// Toxicosis is answered only by poison resistance (cleansing does not remove it). G2 covers "self and nearby
    /// allies", G1 only one; the Swiftness potion adds poison resistance for the battle. Pas de Seul (First Board) puts
    /// you to sleep with Nightmare: G2/G1 Antisleep. Empty elsewhere.
    /// </summary>
    /// <para>Pas de Seul also poisons: an Antipoison serum when no Angel Robe was found (user, 2026-11-06). Grouped by the
    /// status each answers (<see cref="BossSerumGroups"/>); this is every row of the board's groups.</para>
    public static IReadOnlyList<int> BossSerumRows(int board) => BossSerumGroups(board).SelectMany(g => g.Rows).ToList();

    /// <summary>The board's final-boss serums, one group per status, best row first.</summary>
    public static IReadOnlyList<(string Status, int[] Rows)> BossSerumGroups(int board) => board switch
    {
        1 => [("Sleep", [95, 94]), ("Poison", [87, 86])],   // Antisleep, Antipoison Soul Serums (G2, G1)
        3 or 5 => [("Paralysis", [89, 88])],                // Antiparalysis Soul Serum
        4 => [("Poison", [87, 86, 114])],                   // Antipoison; Potion of Breathtaking Swiftness
        _ => [],
    };

    /// <summary>With a campsite next, a potion is only drunk at or below this: the camp heals 90% anyway.</summary>
    public const float PotionBeforeCampAtHpPct = 0.20f;

    /// <summary>
    /// Whether the stop after <paramref name="battle"/> is a campsite: every space the battle's space leads to (the
    /// board's map), or, with no map, every space on the next move on its side. With <paramref name="campWinsFork"/>
    /// one campsite among them is enough: off Legendary the walk takes a campsite over anything
    /// (<see cref="CrucibleRoutePlan.SuggestedSide"/>). First Board: the Ogre leads to a treasure or a campsite.
    /// </summary>
    public static bool CampAfterBattle(IReadOnlyList<CrucibleStage> route, int battle, bool campWinsFork = false)
    {
        var here = battle == 0 ? null : route.FirstOrDefault(s => s.IsBattle && s.Battle == battle);
        if (here == null)
            return false;

        var next = here.Next is { Count: > 0 } nodes
            ? route.Where(s => nodes.Contains(s.Node)).ToList()
            : route.Where(s => s.Move == here.Move + 1 && (s.Side == 0 || here.Side == 0 || s.Side == here.Side)).ToList();
        return next.Count > 0 && (campWinsFork
            ? next.Any(s => s.Type == CrucibleStageType.Campsite)
            : next.All(s => s.Type == CrucibleStageType.Campsite));
    }

    /// <summary>
    /// The HP at or below which a potion is drunk in this battle: <paramref name="line"/>, lowered to
    /// <see cref="PotionBeforeCampAtHpPct"/> when a campsite follows. A run has about two potions, and one went in the
    /// Arch Demon fight at 36-40% HP, right before the camp. The other was gone by the Banemite's second Deadly Thrust:
    /// with a potion left there Korha cleared it, without one she died with it at 8%, twice (2026-11-15).
    /// <para>The battle won (<paramref name="fighting"/> false: its enemy is dead) with the campsite next, nothing is
    /// drunk at all: nothing hits on the way there. Korha, 2026-11-15 17:22: a potion healed 144 HP two seconds before
    /// the camp healed 1,323. <paramref name="fighting"/> is true from the moment the battle's enemy is there, the
    /// wait before the pull included.</para>
    /// </summary>
    public static float DrinkLine(float line, bool campNext, bool fighting = true)
        => !campNext ? line : fighting ? System.Math.Min(line, PotionBeforeCampAtHpPct) : 0f;

    /// <summary>The Poison the First Board's Deadly Thrust and Cold Caress leave (Status row; 3,338 recordings).</summary>
    public const uint CruciblePoisonStatusId = 5140;

    /// <summary>Poisoned at or below this, a held Antipoison Soul Serum is drunk whatever the battle.</summary>
    public const float CurePoisonAtHpPct = 0.60f;

    /// <summary>Tome of the Impervious (five stacks of Blink, 120 s) and Crucible Tannin (Tough Skin: damage taken -20%, 60 s): XBMItem rows.</summary>
    public const int TomeOfTheImperviousRow = 137, CrucibleTanninRow = 102;

    /// <summary>
    /// The First Board's tankbuster casts (action ids): the Banemite's Deadly Thrust, Pas de Seul's Cold Caress and
    /// Blood Sword. Artemis reports one being cast at the beastmaster (<c>CrucibleHints.BusterCastOnMe</c>).
    /// </summary>
    public static readonly IReadOnlySet<uint> TankbusterCastIds = new HashSet<uint> { 46906, 46935, 46934 };

    /// <summary>
    /// The ones Blink nullifies. Deadly Thrust: Korha, 2026-11-15, two Banemite fights with the same damage dealt. In
    /// one a Mirage Vest's Blink was up when the second Thrust landed — 0 damage, no Poison, fight won at 55% HP; in
    /// the other it hit for 473 and the Poison killed her 15 s later. Cold Caress and Blood Sword are magic.
    /// </summary>
    public static readonly IReadOnlySet<uint> PhysicalBusterCastIds = new HashSet<uint> { 46906 };

    /// <summary>Use an item against a tankbuster being cast at the beastmaster: not on Legendary, where an item costs Economic Ethos.</summary>
    public static bool ShouldGuardBuster(bool legendary, bool useItems, uint busterCastId)
        => !legendary && useItems && busterCastId != 0;

    /// <summary>
    /// The HUD slot of the item for the tankbuster being cast at the beastmaster (held ids are HUD icon ids), or -1: a
    /// Tome of the Impervious when Blink nullifies it, else a Crucible Tannin.
    /// </summary>
    public static int PickBusterGuard(IReadOnlyList<uint> heldItemIds, uint busterCastId)
    {
        if (busterCastId == 0)
            return -1;

        int Slot(int row)
        {
            for (var slot = 0; slot < heldItemIds.Count; slot++)
            {
                if (heldItemIds[slot] == (uint)(row + CrucibleItemIconOffset))
                    return slot;
            }

            return -1;
        }

        var tome = PhysicalBusterCastIds.Contains(busterCastId) ? Slot(TomeOfTheImperviousRow) : -1;
        return tome >= 0 ? tome : Slot(CrucibleTanninRow);
    }

    /// <summary>The Crucible Antidote's XBMItem row: "cures most instances of poison, restoring 25% of maximum HP upon success".</summary>
    public const int CrucibleAntidoteRow = 83;

    /// <summary>The board whose poison (<see cref="CruciblePoisonStatusId"/>) an Antidote cures; the Fourth's Toxicosis it does not.</summary>
    public const int AntidoteBoardIndex = 1;

    /// <summary>
    /// What cures the poison once it is on (XBMItem rows): the Antidote first — it cures and heals a quarter — then the
    /// Antipoison Soul Serums, G2 then G1.
    /// </summary>
    public static readonly int[] AntipoisonSerumRows = [CrucibleAntidoteRow, 87, 86];

    /// <summary>
    /// The shop row of a Crucible Antidote to buy: on the First Board, with tokens and a free item slot, unless held
    /// gear already answers poison (the Angel Robe). Korha, First Board Third Degree, 2026-11-15: the last shop had one
    /// for 70 tokens and was left with 328 unspent; Pas de Seul's second Cold Caress took her to 54 HP and its poison
    /// killed her three seconds later, with Pas de Seul at 30%.
    /// </summary>
    public static IReadOnlyList<int> ShopBuysForAntidote(
        IReadOnlyList<CrucibleShopOffer> offers, int tokens, int itemsHeld, int board, IReadOnlyCollection<string>? coveredByGear = null)
        => board == AntidoteBoardIndex && itemsHeld < MaxCrucibleItemsHeld && coveredByGear?.Contains("Poison") != true
           && offers.FirstOrDefault(o => o.ItemId == CrucibleAntidoteRow && !o.Sold && o.Price <= tokens) is { ItemId: > 0 } offer
            ? [offer.Row]
            : [];

    /// <summary>
    /// Poisoned and hurt in any battle: drink the Antipoison serum now. It used to be kept for the final boss, and
    /// Korha died to the Banemite's Deadly Thrust poison with a G2 Antipoison Soul Serum in the bag (2026-11-15:
    /// 50% to 22% on the hit, dead 24 s later). Not on Legendary, where an item costs Economic Ethos.
    /// </summary>
    public static bool ShouldCurePoison(bool legendary, bool useItems, bool poisoned, float selfHpPct)
        => !legendary && useItems && poisoned && selfHpPct > 0f && selfHpPct <= CurePoisonAtHpPct;

    /// <summary>The HUD slot of the best held poison cure, <see cref="AntipoisonSerumRows"/> in order (held ids are HUD icon ids), or -1.</summary>
    public static int PickAntipoison(IReadOnlyList<uint> heldItemIds)
    {
        foreach (var row in AntipoisonSerumRows)
        {
            for (var slot = 0; slot < heldItemIds.Count; slot++)
            {
                if (heldItemIds[slot] == (uint)(row + CrucibleItemIconOffset))
                    return slot;
            }
        }

        return -1;
    }

    /// <summary>Whether to drink the boss's resistance serum now: not Legendary (it costs Economic Ethos), at the final boss.</summary>
    public static bool ShouldUseBossSerum(bool legendary, bool useItems, CrucibleEnemy? fighting) =>
        !legendary && useItems && fighting is { IsFinalBoss: true };

    /// <summary>
    /// The HUD slot of the best held serum for this board's final boss (held ids are HUD icon ids), or -1. One serum a
    /// fight for each status (<see cref="BossSerumGroups"/>).
    /// </summary>
    /// <param name="coveredByGear">Statuses the held gear resists ("Poison" for the Angel Robe): their serums are kept.</param>
    public static int PickBossSerum(IReadOnlyList<uint> heldItemIds, int board, IReadOnlySet<uint> usedThisBattle,
        IReadOnlyCollection<string>? coveredByGear = null)
    {
        foreach (var (status, rows) in BossSerumGroups(board))
        {
            if (usedThisBattle.Any(id => rows.Contains((int)id - CrucibleItemIconOffset)) || coveredByGear?.Contains(status) == true)
                continue;
            foreach (var row in rows)
            {
                for (var slot = 0; slot < heldItemIds.Count; slot++)
                {
                    if (heldItemIds[slot] == (uint)(row + CrucibleItemIconOffset))
                        return slot;
                }
            }
        }

        return -1;
    }

    /// <summary>
    /// The shop rows of the serums to buy for this board's final boss, one per status: the best affordable one for each
    /// status no held serum (XBMItem rows) and no held or planned gear (<paramref name="coveredByGear"/>, e.g. "Poison"
    /// for the Angel Robe) already answers, while item slots are free. Rows in buying order.
    /// </summary>
    public static IReadOnlyList<int> ShopBuysForBossSerums(
        IReadOnlyList<CrucibleShopOffer> offers, int tokens, IReadOnlyCollection<int> heldRows, int board,
        IReadOnlyCollection<string>? coveredByGear = null)
    {
        var buys = new List<int>();
        foreach (var (status, rows) in BossSerumGroups(board))
        {
            if (heldRows.Count + buys.Count >= MaxCrucibleItemsHeld || heldRows.Any(rows.Contains)
                || coveredByGear?.Contains(status) == true)
                continue;
            foreach (var row in rows)
            {
                if (offers.FirstOrDefault(o => o.ItemId == row && !o.Sold && o.Price <= tokens) is { ItemId: > 0 } offer)
                {
                    tokens -= offer.Price;
                    buys.Add(offer.Row);
                    break;
                }
            }
        }

        return buys;
    }

    /// <summary>Presses of one Eye a battle before it is left for the next (the game refuses one mid-action).</summary>
    public const int EyeMaxTries = 3;

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
    public static int ShopRestorePct(int itemRow, bool ribbonHeld = false) =>
        HealRestorePct((uint)(itemRow + CrucibleItemIconOffset), ribbonHeld);

    /// <summary>
    /// What to buy so the beastmaster has potions to drink (any goal but Legendary, with "use potions" on): the
    /// strongest healing items first, cheaper first on a tie, while there is a free item slot and tokens for it. Never
    /// feed or gear. With <paramref name="wantFang"/>, the strongest affordable Fang goes first. With the Crimson Ribbon
    /// held (<paramref name="ribbonHeld"/>) soul serums count as 50% heals. Rows in buying order.
    /// </summary>
    public static IReadOnlyList<int> ShopBuysForPotions(
        IReadOnlyList<CrucibleShopOffer> offers, int tokens, int itemsHeld, Func<int, bool> isCrucibleItem, bool wantFang = false,
        bool ribbonHeld = false)
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
                     .Where(o => !o.Sold && isCrucibleItem(o.ItemId) && ShopRestorePct(o.ItemId, ribbonHeld) > 0)
                     .OrderByDescending(o => ShopRestorePct(o.ItemId, ribbonHeld)).ThenBy(o => o.Price))
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
    /// The beast gear to buy with the tokens left after the potions (any goal but Legendary, with "take beast gear" on):
    /// the best by <see cref="TreasureScore"/> first, cheaper first on a tie, while it is affordable and fewer than ten
    /// pieces are held. Gear that scores nothing or less (a chance to Stun yourself) is never bought. Rows in buying
    /// order. Field 2026-10-31 (Korha, Level): every shop was left with its gear unbought — only potions were planned.
    /// </summary>
    /// <param name="gearHeld">Pieces held now; null when unknown (the game refuses an eleventh, and the buy times out).</param>
    /// <param name="anyGear">
    /// A degree above Standard (user, 2026-11-06): buy every piece whose downsides the held gear counters
    /// (<see cref="HasUncounteredDownside"/>), not just the ones that score; with <paramref name="wantedOnly"/>, only
    /// pieces giving a wanted resistance (the Angel Robe and anti-sleep gear go before anything else).
    /// </param>
    /// <param name="heldEffects">The effect texts of the gear held, for the counters.</param>
    public static IReadOnlyList<int> ShopBuysForGear(
        IReadOnlyList<CrucibleShopOffer> offers, int tokens, int? gearHeld, Func<int, bool> isGear,
        Func<int, string?> effectOf, IReadOnlyCollection<string>? wantedResistances = null, bool anyGear = false,
        IReadOnlyCollection<string?>? heldEffects = null, bool wantedOnly = false)
    {
        // Best first, re-scored after each buy: a resistance just bought stops counting for the next piece, and a counter
        // just bought (Soulreaper's Armor) lets the piece it answers (the Demonic Helm) through.
        var buys = new List<int>();
        var wanted = wantedResistances?.ToList() ?? [];
        var owned = heldEffects?.ToList() ?? [];
        var left = offers.Where(o => !o.Sold && isGear(o.ItemId)).ToList();
        while ((gearHeld ?? 0) + buys.Count < HeldGearMaxRows)
        {
            var best = left
                .Where(o => o.Price <= tokens)
                .Where(o => !wantedOnly || ResistancesOf(effectOf(o.ItemId)).Any(wanted.Contains))
                .Select(o => (Offer: o, Score: TreasureScore(effectOf(o.ItemId), wanted)))
                .Where(x => anyGear ? !HasUncounteredDownside(effectOf(x.Offer.ItemId), owned) : x.Score > 0)
                .OrderByDescending(x => x.Score).ThenBy(x => x.Offer.Price)
                .FirstOrDefault();
            if (best.Offer.ItemId == 0)
                break;
            tokens -= best.Offer.Price;
            buys.Add(best.Offer.Row);
            left.Remove(best.Offer);
            owned.Add(effectOf(best.Offer.ItemId));
            var covered = ResistancesOf(effectOf(best.Offer.ItemId)).ToHashSet(StringComparer.Ordinal);
            wanted.RemoveAll(covered.Contains);
        }

        return buys;
    }

    /// <summary>
    /// Gear bought to finish holding ten pieces for Walking Armory (user, 2026-11-06: on a degree, stay at ten), planned
    /// before the potions: the cheapest piece whose downsides the held gear counters, again and again, while fewer than
    /// ten are held and it is affordable. At the last shop only when this shop can finish the ten — short of ten the
    /// bonus is lost anyway, so the tokens go to potions. Empty when the gear held is unknown.
    /// </summary>
    public static IReadOnlyList<int> ShopBuysForGearCount(
        IReadOnlyList<CrucibleShopOffer> offers, int tokens, int? gearHeld, Func<int, bool> isGear,
        Func<int, string?> effectOf, IReadOnlyCollection<string?>? heldEffects, bool lastShop)
    {
        if (gearHeld is not { } held)
            return [];
        var buys = new List<int>();
        var owned = heldEffects?.ToList() ?? [];
        var left = offers.Where(o => !o.Sold && isGear(o.ItemId)).ToList();
        while (held + buys.Count < HeldGearMaxRows)
        {
            // Re-checked after each buy: a counter just bought lets the piece it answers through.
            var next = left
                .Where(o => o.Price <= tokens && !HasUncounteredDownside(effectOf(o.ItemId), owned))
                .OrderBy(o => o.Price)
                .FirstOrDefault();
            if (next.ItemId == 0)
                break;
            tokens -= next.Price;
            buys.Add(next.Row);
            left.Remove(next);
            owned.Add(effectOf(next.ItemId));
        }

        return lastShop && held + buys.Count < HeldGearMaxRows ? [] : buys;
    }

    /// <summary>
    /// The coffer pick on a degree (user, 2026-11-06: no items are used, the gear does the work): gear over any Crucible
    /// item, never a piece whose downside the held gear does not counter. Among the gear, the first open slot of
    /// TPLemont's D3 list (<see cref="DegreeSlotFor"/>; user, 2026-11-08) in its order, best in slot first
    /// (<see cref="DegreeSlotRank"/>), then the rest by <see cref="TreasureScore"/>. The usual <see cref="PickTreasure"/>
    /// when no gear is fit to take.
    /// </summary>
    public static int PickTreasureOnDegree(
        IReadOnlyList<int> offers, Func<int, bool> isGear, Func<int, string?> effectOf,
        IReadOnlyCollection<string?> heldEffects, CrucibleElement bossWeakness, IReadOnlySet<int>? takenRows = null,
        IReadOnlyCollection<string>? wantedResistances = null, BossDamageKind bossDamage = BossDamageKind.Unknown)
    {
        var best = -1;
        var bestKey = (Slot: int.MinValue, Rank: int.MinValue, Score: float.MinValue);
        for (var i = 0; i < offers.Count; i++)
        {
            var effect = offers[i] == 0 ? null : effectOf(offers[i]);
            if (offers[i] == 0 || takenRows?.Contains(i) == true || !isGear(offers[i])
                || InflictsDoomOnSelf(effect) || HasUncounteredDownside(effect, heldEffects))
                continue;
            var slot = DegreeSlotFor(effect, heldEffects, bossDamage, wantedResistances ?? []);
            var key = ((int)slot, DegreeSlotRank(slot, effect, bossWeakness, bossDamage), TreasureScore(effect, wantedResistances));
            if (key.CompareTo(bestKey) > 0)
            {
                bestKey = key;
                best = i;
            }
        }

        if (best >= 0)
            return best;
        // No gear fit to take: an item over a piece with an uncountered downside, that piece only when nothing else is left.
        var safe = offers.Select(id => id != 0 && isGear(id) && HasUncounteredDownside(effectOf(id), heldEffects) ? 0 : id).ToList();
        var item = PickTreasure(safe, effectOf, takenRows, wantedResistances);
        return item >= 0 ? item : PickTreasure(offers, effectOf, takenRows, wantedResistances);
    }

    // --- Axes and damage gear on a degree (user, 2026-11-06: D3 needs the damage; an axe off the enemy's weakness still
    // adds damage, so any axe without a downside is worth having) ---

    private static readonly System.Text.RegularExpressions.Regex AutoAttackAxeLine =
        new(@"Deals additional (?<element>[a-z]+) damage with a potency of \d+ after each auto-attack",
            System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// An elemental axe's element (Flame-wreathed … Windblown Axe: "Deals additional fire damage with a potency of 240 after
    /// each auto-attack"); <see cref="CrucibleElement.None"/> for anything else. Only one such piece takes effect.
    /// </summary>
    public static CrucibleElement AxeElement(string? effect)
    {
        var m = effect == null ? null : AutoAttackAxeLine.Match(effect);
        return m is not { Success: true } ? CrucibleElement.None : m.Groups["element"].Value switch
        {
            "fire" => CrucibleElement.Fire,
            "ice" => CrucibleElement.Ice,
            "lightning" => CrucibleElement.Lightning,
            "earth" => CrucibleElement.Earth,
            "water" => CrucibleElement.Water,
            "wind" => CrucibleElement.Wind,
            _ => CrucibleElement.None,
        };
    }

    /// <summary>
    /// Among damage gear: the axe of the weakness wanted (2), any other axe (1), the rest (0). Every guide puts one
    /// elemental axe first, and it scores 0 by <see cref="TreasureScore"/> (no "+N%" line), so without this the Green
    /// Beret's Damage Dealt +3% beat a water axe before the Ogre (Korha, 2026-11-08).
    /// </summary>
    public static int DamageGearRank(string? effect, CrucibleElement wantedElement)
    {
        var element = AxeElement(effect);
        return element == CrucibleElement.None ? 0 : wantedElement != CrucibleElement.None && element == wantedElement ? 2 : 1;
    }

    /// <summary>An element an axe can carry (the six magic ones; Blunt, Piercing and Slashing are not).</summary>
    public static bool IsAxeElement(CrucibleElement element)
        => element is CrucibleElement.Fire or CrucibleElement.Wind or CrucibleElement.Earth or CrucibleElement.Lightning
            or CrucibleElement.Ice or CrucibleElement.Water;

    /// <summary>
    /// The element to want an axe in: the weakness of the soonest battle ahead on the route (after
    /// <paramref name="currentMove"/>, either side of a fork) that an axe can hit, else the final boss's
    /// (<paramref name="finalBossWeakness"/>). On the First Board after the Banemite that is the Ogre's water; the final
    /// boss, Pas de Seul, is weak to piercing, which no axe carries.
    /// </summary>
    public static CrucibleElement AxeElementAhead(
        IReadOnlyList<CrucibleStage> route, int currentMove, Func<int, CrucibleElement> weaknessOf,
        CrucibleElement finalBossWeakness)
    {
        var ahead = route.Where(s => s.Move > currentMove).OrderBy(s => s.Move)
            .SelectMany(s => (s.IsBattle ? [s.Battle] : Array.Empty<int>())
                .Concat(s.Options.Where(o => o.Battle != 0).Select(o => o.Battle)));
        foreach (var battle in ahead)
        {
            var weakness = weaknessOf(battle);
            if (IsAxeElement(weakness))
                return weakness;
        }

        return finalBossWeakness;
    }

    /// <summary>
    /// What a board's final boss mostly hits with (TPLemont's D3 guides; fuurie for Pas de Seul, "heavy on fixed/magic
    /// damage"): the First Board's Pas de Seul and the First Master's Borgny magic, the Second and Third Boards' bosses
    /// physical. Unknown elsewhere (the Second Master's Lauda is not stated).
    /// </summary>
    public static BossDamageKind FinalBossDamage(int board) => board switch
    {
        1 or 4 => BossDamageKind.Magic,
        2 or 3 => BossDamageKind.Physical,
        _ => BossDamageKind.Unknown,
    };

    /// <summary>
    /// Whether a piece cuts the damage the boss deals: Damage Taken down, or Physical / Magic Vulnerability down to match
    /// <paramref name="kind"/> (only Damage Taken when the kind is unknown).
    /// </summary>
    public static bool ReducesBossDamage(string? effect, BossDamageKind kind) => BossDamageCut(effect, kind) > 0;

    /// <summary>How many percent of the boss's kind of damage a piece cuts (Damage Taken plus the matching Vulnerability).</summary>
    public static int BossDamageCut(string? effect, BossDamageKind kind)
        => -StatValue(effect, "Damage Taken")
           - (kind == BossDamageKind.Physical ? StatValue(effect, "Physical Vulnerability") : 0)
           - (kind == BossDamageKind.Magic ? StatValue(effect, "Magic Vulnerability") : 0);

    /// <summary>
    /// Gear that hits back each time the beastmaster takes physical damage (Briar Armor, "Deals unaspected damage with a
    /// potency of 360 each time you suffer physical damage"): with it the beastmaster should take the hits (user,
    /// 2026-11-06 — it stacks with the axe).
    /// </summary>
    public static bool CountersPhysicalHits(string? effect) =>
        effect != null && effect.Contains("each time you suffer physical damage", StringComparison.Ordinal);

    // --- The degree gear list (TPLemont's D3 beast-gear chapter, user 2026-11-08): 1 elemental axe; 1-2 crit (Beast
    // Earring, Beast Mask, Beastly Knife, Silver Specs); 1-2 special effects (Briar Armor, Wind Shield, Mirage Vest);
    // 5-6 HP stacking, HP over damage reduction; 1 paralysis/poison resist if needed. Pure Damage Dealt gear is not on
    // it. ---

    /// <summary>The list's slots, best first by value (a higher value is taken first).</summary>
    public enum DegreeGearSlot
    {
        None = 0,
        StatusResist = 1,
        HpStacking = 2,
        SpecialEffect = 3,
        Crit = 4,
        Axe = 5,
    }

    /// <summary>How many of each slot the list fills.</summary>
    public const int DegreeCritWanted = 2, DegreeSpecialWanted = 2, DegreeHpWanted = 6;

    /// <summary>Crit gear: Critical Hit Rate +8% or more (Beastly Knife's 8 and up; not the Crown of the Wild's 3).</summary>
    public static bool IsCritGear(string? effect) => StatValue(effect, "Critical Hit Rate") >= 8;

    /// <summary>A special effect: Blink (Wind Shield, Mirage Vest) or hitting back (Briar Armor).</summary>
    public static bool IsSpecialEffectGear(string? effect)
        => effect != null && (effect.Contains("Blink", StringComparison.Ordinal) || CountersPhysicalHits(effect));

    /// <summary>HP stacking: Maximum HP up, or a cut to the final boss's kind of damage.</summary>
    public static bool IsHpStackingGear(string? effect, BossDamageKind kind)
        => StatValue(effect, "Maximum HP") > 0 || ReducesBossDamage(effect, kind);

    /// <summary>
    /// The list slot a piece fills given the gear held, or <see cref="DegreeGearSlot.None"/> when its slot is full (or it
    /// has none). A piece fitting a full slot falls through to the next it fits: a third crit piece with Max HP counts as
    /// HP stacking. The resist is one piece for any status the board wants.
    /// </summary>
    public static DegreeGearSlot DegreeSlotFor(
        string? effect, IReadOnlyCollection<string?> heldEffects, BossDamageKind kind, IReadOnlyCollection<string> wantedResistances)
    {
        if (AxeElement(effect) != CrucibleElement.None)
            return heldEffects.Any(held => AxeElement(held) != CrucibleElement.None) ? DegreeGearSlot.None : DegreeGearSlot.Axe;
        if (IsCritGear(effect) && heldEffects.Count(IsCritGear) < DegreeCritWanted)
            return DegreeGearSlot.Crit;
        if (IsSpecialEffectGear(effect) && heldEffects.Count(IsSpecialEffectGear) < DegreeSpecialWanted)
            return DegreeGearSlot.SpecialEffect;
        if (IsHpStackingGear(effect, kind) && heldEffects.Count(held => IsHpStackingGear(held, kind)) < DegreeHpWanted)
            return DegreeGearSlot.HpStacking;
        if ((ResistancesOf(effect).Any(wantedResistances.Contains) || IsCrimsonRibbon(effect))
            && !heldEffects.Any(held => ResistancesOf(held).Any(wantedResistances.Contains) || IsCrimsonRibbon(held)))
            return DegreeGearSlot.StatusResist;
        return DegreeGearSlot.None;
    }

    /// <summary>
    /// The Crimson Ribbon, by its effect (Petrification, Blind and Paralysis Resistance): the list's resist on any board,
    /// first in its slot, since with it every soul serum is a 50% heal.
    /// </summary>
    public static bool IsCrimsonRibbon(string? effect)
    {
        var resists = ResistancesOf(effect).ToList();
        return resists.Contains("Petrification") && resists.Contains("Blind") && resists.Contains("Paralysis");
    }

    /// <summary>
    /// The best within a slot: the axe of the weakness ahead; the most crit; for HP stacking the most Max HP, then the
    /// biggest cut to the boss's damage ("HP stacking &gt; damage reduction"); a paralysis or poison resist over others
    /// (the list's "Para/Poison").
    /// </summary>
    public static int DegreeSlotRank(DegreeGearSlot slot, string? effect, CrucibleElement weakness, BossDamageKind kind) => slot switch
    {
        DegreeGearSlot.Axe => DamageGearRank(effect, weakness),
        DegreeGearSlot.Crit => StatValue(effect, "Critical Hit Rate"),
        DegreeGearSlot.HpStacking => (StatValue(effect, "Maximum HP") * 100) + BossDamageCut(effect, kind),
        DegreeGearSlot.StatusResist => IsCrimsonRibbon(effect) ? 2 : ResistancesOf(effect).Any(r => r is "Paralysis" or "Poison") ? 1 : 0,
        _ => 0,
    };

    /// <summary>
    /// The list's gear to buy on a degree, best slot first and re-judged after each buy: only pieces whose downsides the
    /// held gear counters, while fewer than ten are held and it is affordable (cheapest on a tie). Rows in buying order.
    /// </summary>
    public static IReadOnlyList<int> ShopBuysForDegreeGear(
        IReadOnlyList<CrucibleShopOffer> offers, int tokens, int? gearHeld, Func<int, bool> isGear,
        Func<int, string?> effectOf, IReadOnlyCollection<string?>? heldEffects, CrucibleElement weakness,
        BossDamageKind kind, IReadOnlyCollection<string> wantedResistances)
    {
        var buys = new List<int>();
        var owned = heldEffects?.ToList() ?? [];
        var left = offers.Where(o => !o.Sold && isGear(o.ItemId)).ToList();
        while ((gearHeld ?? 0) + buys.Count < HeldGearMaxRows)
        {
            var next = left
                .Where(o => o.Price <= tokens)
                .Select(o => (Offer: o, Effect: effectOf(o.ItemId)))
                .Where(x => !InflictsDoomOnSelf(x.Effect) && !HasUncounteredDownside(x.Effect, owned))
                .Select(x => (x.Offer, x.Effect, Slot: DegreeSlotFor(x.Effect, owned, kind, wantedResistances)))
                .Where(x => x.Slot != DegreeGearSlot.None)
                .OrderByDescending(x => x.Slot)
                .ThenByDescending(x => DegreeSlotRank(x.Slot, x.Effect, weakness, kind))
                .ThenBy(x => x.Offer.Price)
                .FirstOrDefault();
            if (next.Offer.ItemId == 0)
                break;
            tokens -= next.Offer.Price;
            buys.Add(next.Offer.Row);
            left.Remove(next.Offer);
            owned.Add(next.Effect);
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
    /// Lauda (anti-paralysis "mandatory"), King Ahriman's Dirty Eye Doom, Medusa and Deepeye petrification. First
    /// Board (2026-11-04, Third Degree research): Pas de Seul sleeps you (Nightmare — "anti-sleep before the fight") and
    /// poisons (Angel Robe makes you immune) — Black Cowl and Angel Robe are the gear for them.
    /// </summary>
    public static IReadOnlyList<string> WantedResistances(int board) => board switch
    {
        1 => ["Sleep", "Poison"],
        2 => ["Doom"],
        3 => ["Paralysis"],
        4 => ["Poison", "Paralysis"],
        5 => ["Paralysis", "Doom", "Petrification"],
        _ => [],
    };

    private static readonly System.Text.RegularExpressions.Regex RecoverAfterBattle =
        new(@"Recovers (?<n>\d+)% of maximum HP at the end of battles", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>The statuses a piece of gear resists ("Sleep" for "Sleep Resistance +100%"), from its effect text.</summary>
    public static IEnumerable<string> ResistancesOf(string? effect)
    {
        if (string.IsNullOrEmpty(effect))
            yield break;
        foreach (var raw in effect.Split('\n', '\r'))
        {
            var m = TreasureStatLine.Match(raw.Trim());
            if (m.Success && m.Groups["sign"].Value == "+" && m.Groups["stat"].Value.EndsWith(" Resistance", StringComparison.Ordinal))
                yield return m.Groups["stat"].Value[..^" Resistance".Length];
        }
    }

    /// <summary>
    /// The board's wanted resistances (<see cref="WantedResistances"/>) that the held gear does not already give: a
    /// second Sleep piece adds nothing, so it stops scoring as one.
    /// </summary>
    public static IReadOnlyList<string> WantedResistancesLeft(int board, IEnumerable<string?> heldGearEffects)
    {
        var held = heldGearEffects.SelectMany(ResistancesOf).ToHashSet(StringComparer.Ordinal);
        return WantedResistances(board).Where(r => !held.Contains(r)).ToList();
    }

    /// <summary>
    /// Gear that can Doom the beastmaster: the Demonic Helm, "1% chance to inflict Doom on self upon suffering damage.
    /// Duration: 2s" — a 2 s Doom is a KO, and the roll comes on every hit taken. Never taken from a coffer or the
    /// spoils and never bought (user 2026-11-03; Saar bought one for 1,321 tokens on 2026-11-02, as its +20% damage
    /// outscored the old "on self" penalty).
    /// </summary>
    public static bool InflictsDoomOnSelf(string? effect)
        => effect != null && effect.Contains("inflict Doom on self", StringComparison.OrdinalIgnoreCase);

    private static readonly System.Text.RegularExpressions.Regex OnSelfStatus =
        new(@"inflict (?<status>[A-Za-z ]+?) on self", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// Whether a piece of gear carries a downside the held gear (<paramref name="heldEffects"/>) does not counter (user,
    /// 2026-11-06: on a degree buy everything but that). Downsides, from the game's effect texts: a status on self
    /// (Thunder Axe's Stun, the Demonic Helm's Doom — countered by that status's resistance, Umbral Wristlet /
    /// Soulreaper's Armor; Flame Knife's Chains of Condemnation and any damage over time on self — nothing counters
    /// them), Slow (Ice Shield), and a good stat lowered (Genji Armor and Heavy Axe's Movement Speed −20% — countered by
    /// as much Movement Speed held, Mystic Boots +50%; Empress Hairpin's Max HP, Merchant's Cap's Critical Hit Rate).
    /// </summary>
    public static bool HasUncounteredDownside(string? effect, IReadOnlyCollection<string?> heldEffects)
    {
        if (string.IsNullOrEmpty(effect))
            return false;
        var resists = heldEffects.SelectMany(ResistancesOf).ToHashSet(StringComparer.Ordinal);
        var speed = heldEffects.Sum(e => StatValue(e, "Movement Speed"));
        foreach (var raw in effect.Split('\n', '\r'))
        {
            var line = raw.Trim();
            var onSelf = OnSelfStatus.Match(line);
            if (onSelf.Success)
            {
                if (!resists.Contains(onSelf.Groups["status"].Value))
                    return true;
                continue;
            }

            if (line.Contains("on self", StringComparison.Ordinal))
                return true;
            var m = TreasureStatLine.Match(line);
            if (!m.Success)
                continue;
            var stat = m.Groups["stat"].Value;
            var minus = m.Groups["sign"].Value == "-";
            if (stat == "Slow" && !minus)
                return true;
            if (!minus || !TreasureWeights.Any(w => w.Stat == stat && w.Weight > 0f))
                continue;
            if (stat == "Movement Speed" && speed >= int.Parse(m.Groups["n"].Value))
                continue;
            return true;
        }

        return false;
    }

    /// <summary>The sum of a stat's "+N%" / "-N%" lines in an effect text.</summary>
    private static int StatValue(string? effect, string stat)
    {
        if (string.IsNullOrEmpty(effect))
            return 0;
        var total = 0;
        foreach (var raw in effect.Split('\n', '\r'))
        {
            var m = TreasureStatLine.Match(raw.Trim());
            if (m.Success && m.Groups["stat"].Value == stat)
                total += int.Parse(m.Groups["n"].Value) * (m.Groups["sign"].Value == "-" ? -1 : 1);
        }

        return total;
    }

    /// <summary>What Doom-on-self gear scores: far below anything, so no pick or purchase ever lands on it.</summary>
    public const float DoomOnSelfScore = -1000f;

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
        if (InflictsDoomOnSelf(effect))
            return DoomOnSelfScore;

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
    /// never one that can Doom the beastmaster (<see cref="InflictsDoomOnSelf"/>), or -1. A thief's knife yields two picks from one coffer; the window stays open for the second.
    /// </summary>
    public static int PickTreasure(
        IReadOnlyList<int> offers, Func<int, string?> effectOf, IReadOnlySet<int>? takenRows = null,
        IReadOnlyCollection<string>? wantedResistances = null)
    {
        var best = -1;
        var bestScore = float.MinValue;
        for (var i = 0; i < offers.Count; i++)
        {
            if (offers[i] == 0 || takenRows?.Contains(i) == true || InflictsDoomOnSelf(effectOf(offers[i])))
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
    /// Legendary takes no beast gear on any board, for Nameday Attire (750). Walking Armory (500) needs ten pieces at the
    /// end, which no run in the 2026-10-31 research reached from free loot (every one that got it bought gear), so taking
    /// gear on Board 2 and up mostly lost Nameday for nothing (docs/crucible-legendary-strats.md §9.1).
    /// </summary>
    public static bool LegendaryTakesGear => false;

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
        "Sell the ",                      // selling: "Sell the pinch of temporal sand for 315 territory tokens?"
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
