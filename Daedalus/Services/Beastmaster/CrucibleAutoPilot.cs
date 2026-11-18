using System;
using System.Collections.Generic;
using System.Linq;
using Daedalus.Data;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using static Daedalus.Services.Beastmaster.CrucibleAddons;

namespace Daedalus.Services.Beastmaster;

/// <summary>
/// Steps through the Crucible board's screens the way the player did in the recordings (docs/crucible-screens.md):
/// starts each battle once the horn setter has set the horns, takes the tokens (and optionally the first item) from
/// the spoils, takes the best treasure (every pick a thief's knife allows), buys potions (or, on Legendary, the items that
/// finish Habitual Hoarder) at a shop and leaves it, rests the most hurt beast at a campsite, and
/// closes the result screen. It answers Yes only to the prompt its own click raised, and leaves every other screen
/// to the player. Walking from space to space (which is also how a fork is chosen) is still the player's, and
/// fighting is the rotation's.
/// </summary>
public sealed unsafe class CrucibleAutoPilot
{
    private static readonly TimeSpan StepEvery = TimeSpan.FromMilliseconds(700);
    private static readonly TimeSpan ResultCloseAfter = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan CampSettle = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan LootSettle = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan TreasureSettle = TimeSpan.FromSeconds(1.5);
    private static readonly TimeSpan ShopSettle = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ShopBuyTimeout = TimeSpan.FromSeconds(4);

    private readonly Action<string> _log;
    private DateTime _nextStepUtc;
    private string? _expectPrompt;
    private DateTime _expectUntilUtc;
    private int _startedBattle;
    private bool _lootGearDone, _lootTokensTried;
    private int _lootGearTakes;
    private int _lootHeldBefore;
    private readonly HashSet<int> _lootTakenRows = new();
    private bool _shopTried, _restTried;
    private DateTime _shopSinceUtc;
    private Queue<int>? _shopPlan;
    private int _shopPendingRow = -1;
    private int _shopPendingHeld;
    private DateTime _shopPendingUtc;
    /// <summary>
    /// The beast gear held (XBMItem rows), read off the board HUD whenever it is up and remembered while it is not —
    /// the HUD closes while a shop, coffer or spoils window is open, which is where the gear is chosen.
    /// </summary>
    private readonly List<int> _heldGear = new();
    private bool _heldGearKnown;
    private int _shopSellHeld = -1;
    private DateTime _shopSellUtc;
    private bool _shopSellGaveUp;
    private bool _boughtOnBoard;
    private bool _itemUsedOnBoard;
    private bool _hoarderPays;
    private readonly HashSet<int> _treasureTakenRows = new();
    private DateTime _treasureSinceUtc;
    private DateTime _resultSentUtc;
    private DateTime _campSinceUtc;
    private DateTime _lootSinceUtc;
    private float _selfHpPct = 1f;

    /// <summary>The battle in which "keeping the potion for the campsite" was last logged, so it is said once a battle.</summary>
    private int _potionKeptForCampBattle;

    /// <summary>The battle of the last enemy seen on this board: the one being fought, or the one just won.</summary>
    private int _lastBattle;

    /// <summary>A live enemy is the hard target (its HP was passed in): what the Vampiric Fang heal hits.</summary>
    private bool _enemyTargeted;

    // HP samples for the Legendary "can a potion save the run" check: the beastmaster's and the hard target's.
    private readonly List<(double Seconds, float Pct)> _selfSamples = new();
    private readonly List<(double Seconds, float Pct)> _enemySamples = new();
    private DateTime _nextHpSampleUtc;
    private bool _potionHopelessLogged;
    private IReadOnlyList<CrucibleTeamMember>? _teamSeen;
    private int _knockOutsAtTeam;
    private int _teamAlive;
    private bool _legendary;
    private bool _legendaryNoGear;
    private bool _gearTakenOnBoard;
    private int _boardIndex;

    private readonly Func<int, bool> _isBeastGear;
    private readonly Func<int, string> _itemName;
    private readonly Func<int, string?> _itemEffect;
    private readonly Func<int, bool> _isCrucibleItem;
    private bool _takeGearNow, _takePotionsNow, _usePotionsNow;
    private DateTime _potionMenuUntilUtc;
    private DateTime _nextPotionUtc;
    private DateTime _fangMenuUntilUtc;
    private DateTime _nextFangUtc;
    private DateTime _eyeMenuUntilUtc;
    private DateTime _nextEyeUtc;
    private uint _eyePressed;
    private int _eyePressedHeld;
    private DateTime _eyePressedUtc;
    private int _eyeBattle;

    /// <summary>Why the item being pressed is used, for its log line.</summary>
    private string _eyePressedWhy = "";
    private readonly HashSet<uint> _eyesUsed = new();
    private readonly Dictionary<uint, int> _eyeTries = new();

    // A pressed potion not yet seen leaving the HUD: which item, how many were held, when "Use" was pressed.
    private uint _pressedPotionId;
    private int _pressedPotionHeld;
    private DateTime _pressedPotionUtc;
    private float _pressedPotionHpPct;

    /// <param name="isBeastGear">Whether an XBMItem row is beast gear (what is taken from the spoils).</param>
    /// <param name="itemName">An XBMItem's name (the spoils window holds only ids).</param>
    /// <param name="itemEffect">An XBMItem's effect text, which ranks the treasure coffer's offers.</param>
    public CrucibleAutoPilot(
        Action<string> log, Func<int, bool> isBeastGear, Func<int, string> itemName, Func<int, string?>? itemEffect = null,
        Func<int, bool>? isCrucibleItem = null)
    {
        _isCrucibleItem = isCrucibleItem ?? (_ => false);
        _log = log;
        _isBeastGear = isBeastGear;
        _itemName = itemName;
        _itemEffect = itemEffect ?? (_ => null);
    }

    /// <summary>A piece of beast gear was taken (spoils or coffer) — the bonus tracker's Nameday Attire.</summary>
    public event System.Action? GearTaken;

    /// <summary>A rest at a campsite was confirmed — the bonus tracker's Sleepless Survival.</summary>
    public event System.Action? Rested;

    /// <summary>What it did last, for the team window.</summary>
    public string Status { get; private set; } = "";

    /// <summary>Framework thread, every frame.</summary>
    /// <param name="selfHpPct">The beastmaster's HP as a fraction of max (decides resting alone at a campsite).</param>
    /// <param name="legendary">
    /// Score for Legendary: no rest at a campsite (Sleepless Survival); beast gear taken where holding it pays (Walking
    /// Armory) and refused elsewhere (Nameday Attire: none from the spoils, a coffer's non-gear offer over gear).
    /// </param>
    /// <param name="takePotions">Also take Crucible items from the spoils (potions, ash, reraisers…; never feed), up to ten.</param>
    /// <param name="enemyHpPct">The hard target's HP (0-1), or null: with the beastmaster's, it tells whether a
    /// Legendary potion can still save the run (<see cref="CrucibleAutoPilotRules.PotionCanSaveRun"/>).</param>
    public void Update(
        bool enabled, CrucibleBoard? board, CrucibleHornSetter? horns, bool takeItems, float selfHpPct = 1f, bool legendary = false,
        bool takePotions = false, bool usePotions = false, float? enemyHpPct = null)
    {
        _usePotionsNow = usePotions;
        _selfHpPct = selfHpPct;
        _enemyTargeted = enemyHpPct is > 0f;
        SampleHp(selfHpPct, enemyHpPct, horns);
        // Legendary: no gear, for Nameday Attire (CrucibleAutoPilotRules.LegendaryTakesGear).
        // Once gear has been taken on this board (a coffer offering only gear), Nameday Attire is gone: avoiding gear
        // after that only throws away the better offer (Korha 2026-10-27: a Reraiser over Heavy Axe).
        if (board?.Index != _boardIndex)
        {
            _boardIndex = board?.Index ?? 0;
            _gearTakenOnBoard = false;
            _boughtOnBoard = false;
            _itemUsedOnBoard = false;
            _lastBattle = 0;
            _potionKeptForCampBattle = 0;
            _heldGear.Clear();
            _heldGearKnown = false;
            CrucibleHints.CounterArmorHeld = false;
        }

        ReadHeldGear();

        _legendaryNoGear = legendary && board != null && !CrucibleAutoPilotRules.LegendaryTakesGear && !_gearTakenOnBoard;
        _legendary = legendary;
        _hoarderPays = board != null && board.BonusValue(CrucibleBonus.HabitualHoarder) > 0;
        takeItems = legendary ? !_legendaryNoGear : takeItems;
        _takeGearNow = takeItems;
        _takePotionsNow = takePotions;
        takeItems |= takePotions; // the spoils' "take" step runs for either
        if (!enabled || board == null)
        {
            if (board == null)
                _startedBattle = 0;
            _expectPrompt = null;
            return;
        }

        var now = DateTime.UtcNow;
        if (now < _nextStepUtc)
            return;

        try
        {
            Step(now, board, horns, takeItems);
        }
        catch (Exception ex)
        {
            Status = $"Autopilot failed: {ex.Message}";
        }
    }

    private void Step(DateTime now, CrucibleBoard board, CrucibleHornSetter? horns, bool takeItems)
    {
        // A prompt: Yes only to the one our own click raised, within a few seconds of raising it.
        var yesNo = Visible("SelectYesno");
        if (yesNo != null)
        {
            var prompt = Text(yesNo, 0);
            if (CrucibleAutoPilotRules.IsEmptyTokenPrompt(prompt))
            {
                Fire(yesNo, 1);
                _expectPrompt = null;
                Done(now, "No: nothing to take in tokens.");
            }
            else if (now < _expectUntilUtc && CrucibleAutoPilotRules.ShouldConfirm(prompt, _expectPrompt))
            {
                Fire(yesNo, 0);
                _expectPrompt = null;
                Done(now, $"Yes: {prompt.Replace(" / ", " ")}");
            }

            return;
        }

        if (_usePotionsNow && TryDrinkPotion(now))
            return;
        if (TryUseFang(now)) // only when Artemis says the Siren's adds are bunched; any goal (Legendary gives up Economic Ethos)
            return;
        if (TryUseEye(now))
            return;

        var result = Visible("XBMResult");
        if (result != null)
        {
            if (_resultSentUtc == default)
            {
                // [68] is the TOTAL and [69] its rank; [2] is only the performance part (completion + spaces) and was
                // logged as the score until 2026-10-27 (Korha: "Apex 7,550" for a 16,300 run). Bonus names sit at
                // 15+i and their points at 31+i, [14] of them; [13] is the bonus total.
                var bonusCount = Math.Clamp((int)Number(result, 14), 0, 16);
                var bonuses = string.Join(", ", Enumerable.Range(0, bonusCount)
                    .Select(i => $"{Text(result, 15 + i)} {Number(result, 31 + i):N0}"));
                _log($"Crucible: result — {Text(result, 69)} {Number(result, 68):N0} ({Text(result, 3)}): performance "
                     + $"{Number(result, 2):N0} + bonuses {Number(result, 13):N0}{(bonuses.Length > 0 ? $" ({bonuses})" : "")}; "
                     + $"{Number(result, 63)} {Text(result, 58)}");
                Fire(result, 0);
                _resultSentUtc = now;
                Done(now, "Closing the result.");
            }
            else if (now - _resultSentUtc > ResultCloseAfter)
            {
                Fire(result, -1);
                _resultSentUtc = now;
                Done(now, "Closing the result.");
            }

            return;
        }

        _resultSentUtc = default;

        var loot = Visible("XBMContentsBooty");
        if (loot != null)
        {
            // The window opens empty and fills its rows a moment later: reading it on the first frame saw no offers
            // and never took gear (field 2026-10-25, Saar: "spoils — nothing" every battle). Let it fill and stand.
            if (_lootSinceUtc == default)
                _lootSinceUtc = now;
            if (now - _lootSinceUtc < LootSettle || loot->AtkValuesCount <= CrucibleAutoPilotRules.LootItemIdIndex(CrucibleAutoPilotRules.LootMaxRows - 1))
                return;

            switch (CrucibleAutoPilotRules.NextLootStep(
                        takeItems, _lootGearDone, _lootTokensTried, Number(loot, CrucibleAutoPilotRules.LootTokensIndex) > 0))
            {
                case CrucibleLootStep.TakeItem:
                    TakeGear(now, loot);
                    break;
                case CrucibleLootStep.TakeTokens:
                    _lootTokensTried = true;
                    Expect("Take ", now);
                    Fire(loot, 3);
                    Done(now, "Taking the tokens.");
                    break;
                default:
                    Expect("Some loot remains unclaimed.", now);
                    Fire(loot, 0);
                    Done(now, "Leaving the rest of the loot.");
                    break;
            }

            return;
        }

        _lootGearDone = _lootTokensTried = false;
        _lootGearTakes = 0;
        _lootTakenRows.Clear();
        _lootSinceUtc = default;

        var treasure = Visible("XBMContentsTreasure");
        if (treasure != null)
        {
            TakeTreasure(now, treasure, board, WantedResistancesLeft(board));
            return;
        }

        _treasureTakenRows.Clear();
        _treasureSinceUtc = default;

        var shop = Visible("XBMContentsItemShop");
        if (shop != null)
        {
            Shop(now, shop, board);
            return;
        }

        _shopTried = false;
        _shopPlan = null;
        _shopPendingRow = -1;
        _shopSinceUtc = default;
        _shopSellHeld = -1;
        _shopSellGaveUp = false;

        var party = PartyAgent();
        var camp = party != null && party->Mode == 4 ? Visible("XBMPetParty") : null;
        if (camp != null)
        {
            // The selection flickers to the whole team as the screen opens: let it settle first.
            if (_campSinceUtc == default)
                _campSinceUtc = now;
            if (_legendary)
            {
                // Resting at all costs Sleepless Survival (1,000): Leave — the camp window's [4], then Yes to "Leave the
                // campsite without resting?" (recorded by hand on Korha, 2026-10-27).
                if (!_restTried && now - _campSinceUtc > CampSettle)
                {
                    _restTried = true;
                    _log("Crucible: Legendary — leaving the campsite without resting (Sleepless Survival).");
                    Expect("Leave the campsite", now);
                    Fire(camp, 4);
                    Done(now, "Legendary: leaving the campsite without resting.");
                }

                return;
            }

            if (!_restTried && now - _campSinceUtc > CampSettle)
                Rest(now, party, camp);
            return;
        }

        _restTried = false;
        _campSinceUtc = default;

        // The battle screen: start once the horns are set for it and have stood a moment.
        var stage = Visible("XBMStageDetailList");
        if (stage != null && horns != null)
        {
            var battle = CrucibleHornSetter.UpcomingBattles() is [var only] ? only : 0;
            if (CrucibleAutoPilotRules.ShouldStartBattle(battle, horns.ReadyBattle, now - horns.ReadySinceUtc, _startedBattle))
            {
                _startedBattle = battle;
                Fire(stage, 8);
                Done(now, "Starting the battle.");
            }
            else if (battle != 0 && horns.ReadyBattle != battle)
            {
                Status = "Waiting for the horns…";
            }
        }
    }

    /// <summary>
    /// The shop: once it has settled, plan the purchases (<see cref="CrucibleAutoPilotRules.ShopBuysForPotions"/> then
    /// <see cref="CrucibleAutoPilotRules.ShopBuysForGear"/>, or on Legendary <see cref="CrucibleAutoPilotRules.ShopBuysForHoarder"/>), buy them one at a time — each must show up
    /// (the row sold, or one more item held) before the next — then leave.
    /// </summary>
    private void Shop(DateTime now, AtkUnitBase* shop, CrucibleBoard board)
    {
        if (_shopSinceUtc == default)
            _shopSinceUtc = now;
        if (now - _shopSinceUtc < ShopSettle || shop->AtkValuesCount <= CrucibleAutoPilotRules.ShopHeldItemRowIndex(CrucibleAutoPilotRules.MaxCrucibleItemsHeld - 1))
            return;

        var rows = Math.Clamp((int)Number(shop, CrucibleAutoPilotRules.ShopRowCountIndex), 0, CrucibleAutoPilotRules.ShopMaxRows);
        var offers = new List<CrucibleShopOffer>(rows);
        for (var r = 0; r < rows; r++)
        {
            var id = (int)Number(shop, CrucibleAutoPilotRules.ShopItemIdIndex(r));
            if (id != 0)
                offers.Add(new CrucibleShopOffer(r, id, CrucibleAutoPilotRules.ParsePrice(Text(shop, CrucibleAutoPilotRules.ShopPriceIndex(r))) ?? int.MaxValue,
                    CrucibleAddons.Bool(shop, CrucibleAutoPilotRules.ShopSoldIndex(r))));
        }

        var tokens = CrucibleAutoPilotRules.ParseTokens(Text(shop, CrucibleAutoPilotRules.ShopTokensIndex)) ?? 0;
        var held = 0;
        var fangHeld = false;
        for (var k = 0; k < CrucibleAutoPilotRules.MaxCrucibleItemsHeld; k++)
        {
            var heldRow = (int)Number(shop, CrucibleAutoPilotRules.ShopHeldItemRowIndex(k));
            if (heldRow == 0)
                continue;
            held++;
            fangHeld |= CrucibleAutoPilotRules.FangItemIds.Contains((uint)(heldRow + CrucibleAutoPilotRules.CrucibleItemIconOffset));
        }

        // Sell first, so the tokens it brings are there when the purchases are planned: every held item Daedalus has no
        // use for (UsesItem), one at a time. Not on Legendary: its shops buy to hold ten items (Habitual Hoarder).
        if (_shopPlan == null && !_legendary && !_shopSellGaveUp)
        {
            var bySlot = new List<int>(CrucibleAutoPilotRules.MaxCrucibleItemsHeld);
            for (var k = 0; k < CrucibleAutoPilotRules.MaxCrucibleItemsHeld; k++)
                bySlot.Add((int)Number(shop, CrucibleAutoPilotRules.ShopHeldItemRowIndex(k)));
            var sell = CrucibleAutoPilotRules.ShopSellSlot(bySlot, board.Index, RibbonHeld());
            if (sell >= 0)
            {
                // The last sale is done once one item fewer is held; until then wait for it.
                var pendingSale = _shopSellHeld >= 0 && held >= _shopSellHeld;
                if (pendingSale && now - _shopSellUtc < ShopBuyTimeout)
                    return;
                if (pendingSale)
                {
                    _log($"Crucible: shop — selling {_itemName(bySlot[sell])} did not go through; buying without it.");
                    _shopSellGaveUp = true;
                }
                else
                {
                    _shopSellHeld = held;
                    _shopSellUtc = now;
                    _log($"Crucible: shop — selling {_itemName(bySlot[sell])} for tokens.");
                    Expect("Sell the ", now);
                    Fire(shop, CrucibleAutoPilotRules.ShopSellItemEvent, sell);
                    Done(now, $"Selling {_itemName(bySlot[sell])}.");
                    return;
                }
            }
        }

        if (_shopPlan == null)
        {
            var plan = _legendary
                ? CrucibleAutoPilotRules.ShopBuysForHoarder(offers, tokens, held, _isCrucibleItem, ShopAhead(board),
                    board.BonusValue(CrucibleBonus.HabitualHoarder), _boughtOnBoard ? 0 : board.BonusValue(CrucibleBonus.CoinClutcher),
                    board.BonusValue(CrucibleBonus.RollingInRiches))
                : _usePotionsNow
                    ? CrucibleAutoPilotRules.ShopBuysForPotions(offers, tokens, held, _isCrucibleItem,
                        wantFang: board.Index == CrucibleAutoPilotRules.SirenBoardIndex && !fangHeld)
                    : [];
            if (!_legendary)
                plan = PlanShop(offers, tokens, held, fangHeld, board, shop);

            _shopPlan = new Queue<int>(plan);
            var stock = string.Join(", ", offers.Select(o => $"{_itemName(o.ItemId)} {o.Price}{(o.Sold ? " (sold)" : "")}"));
            _log($"Crucible: shop — {tokens:N0} tokens, {held} item(s) held; {stock}. "
                 + (plan.Count == 0
                     ? "Buying nothing" + (_legendary ? " (Legendary: only the last shop's items that finish Habitual Hoarder are worth Coin Clutcher)."
                         : _usePotionsNow || _takeGearNow ? "." : " (potions and gear are off).")
                     : "Buying " + string.Join(", ", plan.Select(r => _itemName(offers.First(o => o.Row == r).ItemId)))
                       + (_legendary ? " to hold ten for Habitual Hoarder (Coin Clutcher is given up)." : ".")));
        }

        if (_shopPendingRow >= 0)
        {
            var pending = offers.FirstOrDefault(o => o.Row == _shopPendingRow);
            if (pending.Sold || held > _shopPendingHeld)
            {
                _shopPendingRow = -1;
            }
            else if (now - _shopPendingUtc > ShopBuyTimeout)
            {
                _log($"Crucible: shop — buying {_itemName(pending.ItemId)} did not go through; leaving the rest.");
                _shopPendingRow = -1;
                _shopPlan.Clear();
            }
            else
            {
                return;
            }
        }

        if (_shopPlan.TryDequeue(out var row))
        {
            var offer = offers.FirstOrDefault(o => o.Row == row);
            _shopPendingRow = row;
            _shopPendingHeld = held;
            _shopPendingUtc = now;
            _boughtOnBoard = true;
            Expect("Purchase the ", now);
            Fire(shop, CrucibleAutoPilotRules.ShopBuyEvent, row);
            Done(now, $"Buying {_itemName(offer.ItemId)}.");
            return;
        }

        if (!_shopTried)
        {
            _shopTried = true;
            Expect("Conclude purchasing and leave", now);
            Fire(shop, CrucibleAutoPilotRules.ShopLeaveEvent);
            Done(now, "Leaving the shop.");
        }
    }

    /// <summary>
    /// Any goal but Legendary: what to buy, in order. On Standard the boss serum, then potions, then gear that scores.
    /// On a degree (user, 2026-11-06 / 11-08): TPLemont's gear list (one axe, 1-2 crit, 1-2 special effects, 5-6 HP
    /// stacking, one paralysis/poison resist), then at the last shop a serum for each status that gear left uncovered,
    /// then potions, then every piece of gear whose downsides are countered. Fangs are not bought: every board gives one.
    /// </summary>
    private List<int> PlanShop(IReadOnlyList<CrucibleShopOffer> offers, int tokens, int held, bool fangHeld, CrucibleBoard board, AtkUnitBase* shop)
    {
        var degree = CrucibleHints.Degree > 0;
        var plan = new List<int>();
        int Left() => tokens - plan.Sum(r => offers.First(o => o.Row == r).Price);
        IReadOnlyList<CrucibleShopOffer> Unplanned() => offers.Where(o => !plan.Contains(o.Row)).ToList();
        var ownedEffects = _heldGear.Select(row => _itemEffect(row)).ToList();
        int? GearCount() => GearHeld() is { } g ? g + plan.Count(r => _isBeastGear(offers.First(o => o.Row == r).ItemId)) : null;
        IReadOnlyList<string?> GearEffects() => ownedEffects
            .Concat(plan.Select(r => offers.First(o => o.Row == r).ItemId).Where(_isBeastGear).Select(id => _itemEffect(id)))
            .ToList();

        // On a degree, TPLemont's D3 gear list (user, 2026-11-08): one axe, 1-2 crit, 1-2 special effects, 5-6 HP
        // stacking, one paralysis/poison resist.
        if (degree && _takeGearNow)
            plan.AddRange(CrucibleAutoPilotRules.ShopBuysForDegreeGear(offers, tokens, GearHeld(), _isBeastGear, _itemEffect,
                ownedEffects, AxeElementWanted(board), CrucibleAutoPilotRules.FinalBossDamage(board.Index),
                CrucibleAutoPilotRules.WantedResistances(board.Index)));

        // An unknown route counts as last.
        var lastShop = board.Route.Count == 0 || !ShopAhead(board);

        // Ten pieces of gear before the potions where the board pays Walking Armory (user, 2026-11-06).
        if (WantsTenGear(board))
            plan.AddRange(CrucibleAutoPilotRules.ShopBuysForGearCount(Unplanned(), Left(), GearCount(), _isBeastGear,
                _itemEffect, GearEffects(), lastShop));

        if (_usePotionsNow)
        {
            // The final boss's resistance serums before the potions (they need slots the potions might take). On a degree
            // only at the last shop, once the gear has had its chance to cover them.
            var heldRows = new List<int>();
            for (var k = 0; k < CrucibleAutoPilotRules.MaxCrucibleItemsHeld; k++)
            {
                if ((int)Number(shop, CrucibleAutoPilotRules.ShopHeldItemRowIndex(k)) is var heldRow and > 0)
                    heldRows.Add(heldRow);
            }

            var serums = !degree || lastShop
                ? CrucibleAutoPilotRules.ShopBuysForBossSerums(Unplanned(), Left(), heldRows, board.Index,
                    GearEffects().SelectMany(CrucibleAutoPilotRules.ResistancesOf).ToList())
                : [];
            plan.AddRange(serums);
            // First Board: an Antidote for the poison (Deadly Thrust, Cold Caress) before the potions — it cures and heals 25%.
            var antidote = CrucibleAutoPilotRules.ShopBuysForAntidote(Unplanned(), Left(), held + serums.Count, board.Index,
                GearEffects().SelectMany(CrucibleAutoPilotRules.ResistancesOf).ToList());
            plan.AddRange(antidote);
            held += antidote.Count;
            plan.AddRange(CrucibleAutoPilotRules.ShopBuysForPotions(Unplanned(), Left(), held + serums.Count, _isCrucibleItem,
                wantFang: board.Index == CrucibleAutoPilotRules.SirenBoardIndex && !fangHeld, ribbonHeld: RibbonHeld()));
        }

        // Gear with what is left (the shop only offers each piece once).
        if (_takeGearNow)
            plan.AddRange(CrucibleAutoPilotRules.ShopBuysForGear(Unplanned(), Left(), GearCount(), _isBeastGear, _itemEffect,
                CrucibleAutoPilotRules.WantedResistancesLeft(board.Index, GearEffects()), anyGear: degree, GearEffects()));
        return plan;
    }

    /// <summary>
    /// Refresh <see cref="_heldGear"/> from the board HUD when it is up: slot k owned at [60 + 5k], its XBMItem row at
    /// [63 + 5k] (recording 2026-11-04).
    /// </summary>
    private void ReadHeldGear()
    {
        var hud = CrucibleAddons.Visible("XBMContentsMainHUD");
        if (hud == null)
            return;
        _heldGear.Clear();
        for (var slot = 0; slot < CrucibleAutoPilotRules.HudGearSlots; slot++)
        {
            var index = CrucibleAutoPilotRules.HudGearHeldIndex(slot);
            if (CrucibleAddons.Bool(hud, index) || Number(hud, index) != 0)
                _heldGear.Add((int)Number(hud, index + 3));
        }

        _heldGearKnown = true;
        CrucibleHints.CounterArmorHeld = _heldGear.Any(row => CrucibleAutoPilotRules.CountersPhysicalHits(_itemEffect(row)));
    }

    /// <summary>Beast gear held (live off the HUD, else as last seen on this board); null before it has been seen.</summary>
    private int? GearHeld() => _heldGearKnown ? _heldGear.Count : null;

    /// <summary>The board's final boss's weakness (its axe), or None when unread.</summary>
    /// <summary>The element an axe should carry: the next battle ahead weak to one, else the final boss's weakness.</summary>
    private static CrucibleElement AxeElementWanted(CrucibleBoard board)
    {
        var player = FFXIVClientStructs.FFXIV.Client.Game.Control.Control.GetLocalPlayer();
        var move = 0;
        if (player != null)
        {
            System.Numerics.Vector3 at = player->Position;
            move = CrucibleWalkPlan.CurrentMove(board.Route, at);
        }

        return CrucibleAutoPilotRules.AxeElementAhead(board.Route, move,
            battle => CrucibleTracker.CurrentIndex.EnemiesInBattle(battle).FirstOrDefault()?.Weakness ?? CrucibleElement.None,
            FinalBossWeakness(board));
    }

    /// <summary>The Crimson Ribbon is among the gear held: soul serums are 50% heals.</summary>
    private bool RibbonHeld() => _heldGear.Contains(CrucibleAutoPilotRules.CrimsonRibbonRow);

    private static CrucibleElement FinalBossWeakness(CrucibleBoard board) =>
        CrucibleTracker.CurrentIndex.EnemiesInBattle(board.FinalBattle).FirstOrDefault()?.Weakness ?? CrucibleElement.None;

    /// <summary>The statuses the gear held resists.</summary>
    private IReadOnlyList<string> HeldResistances() =>
        _heldGear.SelectMany(row => CrucibleAutoPilotRules.ResistancesOf(_itemEffect(row))).Distinct().ToList();

    /// <summary>
    /// Samples the beastmaster's and the hard target's HP twice a second for the potion check, and notes the team the
    /// horn screen last listed (each battle) with the knock-outs counted so far.
    /// </summary>
    private void SampleHp(float selfPct, float? enemyPct, CrucibleHornSetter? horns)
    {
        if (horns is { LastTeam: var team } && !ReferenceEquals(team, _teamSeen))
        {
            _teamSeen = team;
            _teamAlive = team.Count(m => m.Alive);
            _knockOutsAtTeam = CrucibleBonusWatcher.Tracker?.KnockOuts ?? 0;
            _potionHopelessLogged = false;
        }

        var now = DateTime.UtcNow;
        if (now < _nextHpSampleUtc)
            return;
        _nextHpSampleUtc = now + TimeSpan.FromSeconds(0.5);
        var t = (now - DateTime.UnixEpoch).TotalSeconds;
        Add(_selfSamples, selfPct);
        if (enemyPct is { } e)
        {
            // A new target (its HP jumps up) starts a new rate.
            if (_enemySamples.Count > 0 && e > _enemySamples[^1].Pct + 0.05f)
                _enemySamples.Clear();
            Add(_enemySamples, e);
        }
        else
        {
            _enemySamples.Clear();
        }

        void Add(List<(double Seconds, float Pct)> list, float pct)
        {
            list.Add((t, pct));
            list.RemoveAll(s => t - s.Seconds > CrucibleAutoPilotRules.HpRateWindowSeconds);
        }
    }

    /// <summary>The familiars still able to fight: alive on the last horn screen, less those knocked out since.</summary>
    private int FamiliarsLeft() =>
        Math.Max(0, _teamAlive - ((CrucibleBonusWatcher.Tracker?.KnockOuts ?? 0) - _knockOutsAtTeam));

    /// <summary>
    /// A degree run taking gear on a board that pays Walking Armory (the First Board does not), still short of ten pieces.
    /// </summary>
    private bool WantsTenGear(CrucibleBoard board) =>
        CrucibleHints.Degree > 0 && _takeGearNow && board.BonusValue(CrucibleBonus.WalkingArmory) > 0
        && GearHeld() is < CrucibleAutoPilotRules.HeldGearMaxRows;

    /// <summary>The board's wanted resistances that the gear held does not already cover.</summary>
    private IReadOnlyList<string> WantedResistancesLeft(CrucibleBoard board)
        => CrucibleAutoPilotRules.WantedResistancesLeft(board.Index, _heldGear.Select(row => _itemEffect(row)));

    /// <summary>Whether a shop space lies ahead on the route (by where the player stands); true when that is unknown.</summary>
    private static bool ShopAhead(CrucibleBoard board)
    {
        var player = FFXIVClientStructs.FFXIV.Client.Game.Control.Control.GetLocalPlayer();
        if (player == null)
            return true;
        System.Numerics.Vector3 at = player->Position;
        return CrucibleAutoPilotRules.ShopAhead(board.Route, CrucibleWalkPlan.CurrentMove(board.Route, at));
    }

    /// <summary>The spoils: take the first beast-gear row, and log what was offered either way.</summary>
    private void TakeGear(DateTime now, AtkUnitBase* loot)
    {
        var offers = new List<int>(CrucibleAutoPilotRules.LootMaxRows);
        var names = new List<string>();
        for (var r = 0; r < CrucibleAutoPilotRules.LootMaxRows; r++)
        {
            var id = (int)Number(loot, CrucibleAutoPilotRules.LootItemIdIndex(r));
            offers.Add(id);
            if (id != 0)
                names.Add($"{_itemName(id)}{(_isBeastGear(id) ? " (gear)" : _isCrucibleItem(id) ? " (item)" : "")}");
        }

        var gearHeld = 0;
        for (var k = 0; k < CrucibleAutoPilotRules.HeldGearMaxRows; k++)
        {
            if (CrucibleAddons.Bool(loot, CrucibleAutoPilotRules.HeldGearFlagIndex(k)))
                gearHeld++;
        }

        var itemsHeld = 0;
        for (var k = 0; k < CrucibleAutoPilotRules.HeldItemMaxRows; k++)
        {
            if (Number(loot, CrucibleAutoPilotRules.HeldItemRowIndex(k)) != 0)
                itemsHeld++;
        }

        // A take that worked adds to what is held — gear or item (a taken offer stays listed).
        var held = gearHeld + itemsHeld;
        var pick = CrucibleAutoPilotRules.PickLoot(
            offers, id => _isBeastGear(id) && !CrucibleAutoPilotRules.InflictsDoomOnSelf(_itemEffect(id)), _isCrucibleItem, _takeGearNow, _takePotionsNow, itemsHeld, _lootTakenRows);
        var offeredText = names.Count == 0 ? "nothing" : string.Join(", ", names);
        var refused = _lootGearTakes > 0 && held <= _lootHeldBefore;
        if (CrucibleAutoPilotRules.LootGearDone(pick >= 0, _lootGearTakes, refused))
        {
            _lootGearDone = true;
            _log($"Crucible: spoils — {offeredText}; "
                 + (_lootGearTakes == 0 ? "nothing wanted to take."
                     : refused ? $"the last take was refused (gear held {gearHeld}, items {itemsHeld}); done after {_lootGearTakes - 1}."
                     : $"done after {_lootGearTakes} take(s) (gear held {gearHeld}, items {itemsHeld})."));
            Done(now, _lootGearTakes == 0 ? "Nothing wanted in the spoils." : "Spoils taken.");
            return;
        }

        var name = _itemName(offers[pick]);
        _log($"Crucible: spoils — {offeredText}; taking {name}.");
        _lootGearTakes++;
        if (_isBeastGear(offers[pick]))
        {
            _gearTakenOnBoard = true;
            GearTaken?.Invoke();
        }
        _lootHeldBefore = held;
        _lootTakenRows.Add(pick);
        _lootSinceUtc = now; // let the window refill before reading it again
        Expect("Take ", now);
        Fire(loot, 4, pick);
        Done(now, $"Taking {name}.");
    }

    /// <summary>
    /// Treasure coffer: the best offer by its effect (survival first), and — while the window stays open, as it does
    /// for a thief's knife's second pick (field 2026-10-25, Saar: slot 1 "Acquired", the loop sat on the other three)
    /// — the next best, once the window has settled after each pick.
    /// </summary>
    private void TakeTreasure(DateTime now, AtkUnitBase* treasure, CrucibleBoard board, IReadOnlyList<string> wanted)
    {
        if (_treasureSinceUtc == default)
            _treasureSinceUtc = now;
        if (now - _treasureSinceUtc < TreasureSettle
            || treasure->AtkValuesCount <= CrucibleAutoPilotRules.TreasureItemIdIndex(CrucibleAutoPilotRules.TreasureMaxRows - 1))
            return;

        var offers = new List<int>(CrucibleAutoPilotRules.TreasureMaxRows);
        for (var r = 0; r < CrucibleAutoPilotRules.TreasureMaxRows; r++)
            offers.Add((int)Number(treasure, CrucibleAutoPilotRules.TreasureItemIdIndex(r)));

        // Legendary on a board where gear costs Nameday Attire: a coffer of only gear is closed, taking nothing.
        if (_legendaryNoGear && CrucibleAutoPilotRules.CloseCofferForNameday(offers, _isBeastGear, _treasureTakenRows))
        {
            _log("Crucible: Legendary — every offer in the coffer is beast gear; closing it (Nameday Attire).");
            _treasureTakenRows.Add(-1); // judged once
            _treasureSinceUtc = now;
            Expect("Close the coffer", now);
            Fire(treasure, CrucibleAutoPilotRules.TreasureCloseEvent);
            Done(now, "Legendary: closing a coffer of only beast gear.");
            return;
        }

        // Every offer left can Doom the beastmaster: close the coffer rather than take one.
        if (CrucibleAutoPilotRules.CloseCofferForNameday(
                offers, id => CrucibleAutoPilotRules.InflictsDoomOnSelf(_itemEffect(id)), _treasureTakenRows))
        {
            _log("Crucible: treasure — every offer left can Doom you on a hit; closing the coffer.");
            _treasureTakenRows.Add(-1); // judged once
            _treasureSinceUtc = now;
            Expect("Close the coffer", now);
            Fire(treasure, CrucibleAutoPilotRules.TreasureCloseEvent);
            Done(now, "Closing a coffer of only Doom-on-self gear.");
            return;
        }

        var pick = _legendaryNoGear
            ? CrucibleAutoPilotRules.PickTreasureAvoidingGear(offers, _isBeastGear, _itemEffect, _treasureTakenRows, wanted)
            : CrucibleHints.Degree > 0
                ? CrucibleAutoPilotRules.PickTreasureOnDegree(offers, _isBeastGear, _itemEffect,
                    _heldGear.Select(row => _itemEffect(row)).ToList(), AxeElementWanted(board), _treasureTakenRows, wanted,
                    CrucibleAutoPilotRules.FinalBossDamage(board.Index))
                : CrucibleAutoPilotRules.PickTreasure(offers, _itemEffect, _treasureTakenRows, wanted);
        if (pick >= 0 && _isBeastGear(offers[pick]))
            _gearTakenOnBoard = true;
        if (_legendaryNoGear && pick >= 0 && _isBeastGear(offers[pick]))
            _log("Crucible: Legendary — every offer in the coffer is beast gear; taking it costs Nameday Attire.");
        if (pick < 0)
        {
            Status = "Treasure: every offer tried — the rest is yours.";
            return;
        }

        var name = _itemName(offers[pick]);
        var offered = string.Join(", ", offers.Where(id => id != 0).Select(id =>
            $"{_itemName(id)} ({CrucibleAutoPilotRules.TreasureScore(_itemEffect(id), wanted):0})"));
        _log($"Crucible: treasure — {offered}; choosing {name}" + (_treasureTakenRows.Count > 0 ? " (another pick)." : "."));
        if (_isBeastGear(offers[pick]))
            GearTaken?.Invoke();
        _treasureTakenRows.Add(pick);
        _treasureSinceUtc = now; // let the window update (or close) before the next pick
        Expect("Choose the ", now);
        Fire(treasure, 2, pick);
        Done(now, $"Choosing {name}.");
    }

    /// <summary>
    /// At <see cref="CrucibleAutoPilotRules.PotionAtHpPct"/> or less (Legendary: the last-resort
    /// <see cref="CrucibleAutoPilotRules.LegendaryPotionAtHpPct"/>), drink the strongest held potion — click its HUD slot, then "Use" on the menu that opens (only a menu we opened, within two seconds).
    /// </summary>
    private bool TryDrinkPotion(DateTime now)
    {
        var menu = now < _potionMenuUntilUtc ? Visible("ContextMenu") : null;
        if (menu != null)
        {
            _potionMenuUntilUtc = default;
            if (Text(menu, 8) == "Use")
            {
                FireMixed(menu, 0, 0, 0u, null, null);
                _pressedPotionUtc = now; // logged once it leaves the HUD — a press can do nothing (busy in another action)
                _pressedPotionHpPct = _selfHpPct;
                Done(now, "Drinking a potion.");
            }
            else
            {
                _pressedPotionId = 0;
            }

            return true;
        }

        var hud = Visible("XBMContentsMainHUD");
        if (hud == null)
            return false;

        var held = new List<uint>(10);
        for (var slot = 0; slot < 10; slot++)
            held.Add(Number(hud, CrucibleAutoPilotRules.HudItemRowIndex(slot)) != 0
                ? (uint)Number(hud, CrucibleAutoPilotRules.HudItemIdIndex(slot))
                : 0u);

        // A pressed potion: drunk once it leaves the HUD; tried again at once when it did not go off.
        if (_pressedPotionId != 0 && _pressedPotionUtc != default)
        {
            var outcome = CrucibleAutoPilotRules.PotionOutcome(
                _pressedPotionHeld, CrucibleAutoPilotRules.CountHeld(held, _pressedPotionId), (now - _pressedPotionUtc).TotalSeconds);
            if (outcome == true)
            {
                _log((_pressedPotionId == CrucibleAutoPilotRules.VampiricFangItemId
                         ? "Crucible: used the Vampiric Fang to heal (HP "
                         : CrucibleAutoPilotRules.IsSoulSerumRow((int)_pressedPotionId - CrucibleAutoPilotRules.CrucibleItemIconOffset)
                             ? "Crucible: drank a soul serum to heal, Crimson Ribbon (HP "
                             : "Crucible: drank a potion (HP ") + (_pressedPotionHpPct * 100f).ToString("0") + "%)."
                     + (_legendary && !_itemUsedOnBoard ? " Legendary last resort: Economic Ethos is given up to stay alive." : ""));
                _itemUsedOnBoard = true;
                _nextPotionUtc = now + TimeSpan.FromSeconds(4); // one drink at a time; HP shows the effect first
            }
            else if (outcome == false)
            {
                _log("Crucible: the potion did not go off (HP " + (_selfHpPct * 100f).ToString("0") + "%) — trying again.");
                _nextPotionUtc = now;
            }

            if (outcome != null)
            {
                _pressedPotionId = 0;
                _pressedPotionUtc = default;
            }

            return outcome == null; // waiting to tell: hold the other chores
        }

        if (now < _nextPotionUtc)
            return false;
        if (Visible("XBMContentsBooty") != null || Visible("XBMContentsTreasure") != null || Visible("XBMContentsItemShop") != null
            || Visible("XBMResult") != null || Visible("XBMPetParty") != null || Visible("SelectYesno") != null)
            return false;

        var usable = new List<bool>(10);
        for (var slot = 0; slot < 10; slot++)
            usable.Add(HudFlag(hud, CrucibleAutoPilotRules.HudItemUsableIndex(slot)));
        var drinkable = CrucibleAutoPilotRules.UsableOnly(held, usable);
        var drinkLine = _legendary ? CrucibleAutoPilotRules.LegendaryDrinkAtHpPct(_itemUsedOnBoard) : CrucibleAutoPilotRules.PotionAtHpPct;
        // A campsite after this battle heals 90% anyway: the potion waits for a battle that has none.
        // Its enemy dead, the battle just fought still counts until the next one's enemy is there: on the way to the
        // camp nothing is drunk.
        if (CrucibleTracker.Current.Enemy is { } enemyNow)
            _lastBattle = enemyNow.Battle;
        var battleNow = _lastBattle;
        // "Fighting" from the moment the battle's enemy is there, the wait before the pull included: Korha stood in
        // front of the Ogre at 35% and nothing was drunk, because out of combat read as "on the way to the camp"
        // (2026-11-15 18:25; critical five seconds into the fight). Only a dead enemy means the battle is won.
        var fightingNow = CrucibleTracker.Current.Enemy != null;
        // The battle won, a campsite on a fork counts too: off Legendary the walk always takes it (after the Ogre:
        // treasure | campsite, and a potion went at 14% a second after the walk chose the camp).
        var campNext = CrucibleTracker.Current.Board is { } boardNow
                       && CrucibleAutoPilotRules.CampAfterBattle(boardNow.Route, battleNow, campWinsFork: !fightingNow && !_legendary);
        var usualLine = drinkLine;
        drinkLine = CrucibleAutoPilotRules.DrinkLine(drinkLine, campNext, fightingNow);
        if (campNext && _selfHpPct > drinkLine && _selfHpPct <= usualLine && _potionKeptForCampBattle != battleNow)
        {
            _potionKeptForCampBattle = battleNow;
            _log($"Crucible: keeping the potion (HP {_selfHpPct:P0}) — a campsite follows this battle; "
                 + (drinkLine > 0f ? $"drunk only at {drinkLine:P0}." : "nothing is drunk on the way to it."));
        }
        // With the Crimson Ribbon every soul serum heals 50%; the final boss's own serums wait for that fight.
        var pick = CrucibleAutoPilotRules.PickPotion(drinkable, _selfHpPct, drinkLine, RibbonHeld(),
            CrucibleTracker.Current.Enemy?.IsFinalBoss == true ? null : CrucibleAutoPilotRules.BossSerumRows(_boardIndex));
        // No potion left: the held Vampiric Fang heals instead, at whatever is targeted.
        if (pick < 0)
            pick = CrucibleAutoPilotRules.PickVampiricFangHeal(drinkable, _selfHpPct, drinkLine, pick, _enemyTargeted);
        // Legendary at the final boss, Economic Ethos already gone: keep HP topped up for the remaining-HP share.
        if (pick < 0 && CrucibleAutoPilotRules.TopsOffForTheEnd(
                _legendary, CrucibleTracker.Current.Enemy?.IsFinalBoss == true, _itemUsedOnBoard, _hoarderPays, held.Count(id => id != 0)))
            pick = CrucibleAutoPilotRules.PickTopOff(drinkable, _selfHpPct);
        if (pick < 0)
            return false;

        // Legendary's last resort only when it can save the run: a potion that only delays the death is not worth it.
        if (_legendary && !_itemUsedOnBoard
            && !CrucibleAutoPilotRules.PotionCanSaveRun(FamiliarsLeft(), _selfHpPct,
                CrucibleAutoPilotRules.PotionRestorePct.GetValueOrDefault(drinkable[pick]) / 100f,
                CrucibleAutoPilotRules.HpLossPerSecond(_selfSamples), _enemySamples.Count > 0 ? _enemySamples[^1].Pct : null,
                CrucibleAutoPilotRules.HpLossPerSecond(_enemySamples)))
        {
            if (!_potionHopelessLogged)
                _log($"Crucible: no potion — no familiar left and the enemy would outlast you even with it (HP {_selfHpPct:P0}).");
            _potionHopelessLogged = true;
            return false;
        }

        _pressedPotionId = held[pick];
        _pressedPotionHeld = CrucibleAutoPilotRules.CountHeld(held, held[pick]);
        _pressedPotionUtc = default; // set when "Use" is pressed
        _nextPotionUtc = now + TimeSpan.FromSeconds(1.5); // the menu did not open: try again soon
        _potionMenuUntilUtc = now + TimeSpan.FromSeconds(2);
        FireMixed(hud, CrucibleAutoPilotRules.HudItemSlotEvent, pick, null);
        Done(now, "Opening a potion…");
        return true;
    }

    /// <summary>
    /// The Siren's adds are bunched on the hard target (Artemis says so through <see cref="CrucibleHints.FangReady"/>):
    /// use the strongest held Fang — its HUD slot, then "Use", as a potion. Any goal (on Legendary it costs Economic Ethos).
    /// </summary>
    private bool TryUseFang(DateTime now)
    {
        var menu = now < _fangMenuUntilUtc ? Visible("ContextMenu") : null;
        if (menu != null)
        {
            _fangMenuUntilUtc = default;
            if (Text(menu, 8) == "Use")
            {
                FireMixed(menu, 0, 0, 0u, null, null);
                _itemUsedOnBoard = true;
                _log("Crucible: the adds are bunched; using a Fang on them"
                     + (_legendary ? " (Economic Ethos is given up)." : "."));
                _nextFangUtc = now + TimeSpan.FromSeconds(3);
                Done(now, "Using a Fang on the bunched adds.");
            }

            return true;
        }

        if (now < _nextFangUtc || !CrucibleHints.FangReady(now))
            return false;
        var hud = Visible("XBMContentsMainHUD");
        if (hud == null)
            return false;

        var held = new List<uint>(10);
        var usable = new List<bool>(10);
        for (var slot = 0; slot < 10; slot++)
        {
            held.Add(Number(hud, CrucibleAutoPilotRules.HudItemRowIndex(slot)) != 0
                ? (uint)Number(hud, CrucibleAutoPilotRules.HudItemIdIndex(slot))
                : 0u);
            usable.Add(HudFlag(hud, CrucibleAutoPilotRules.HudItemUsableIndex(slot)));
        }

        var pick = CrucibleAutoPilotRules.PickFang(CrucibleAutoPilotRules.UsableOnly(held, usable));
        if (pick < 0)
            return false;

        _nextFangUtc = now + TimeSpan.FromSeconds(1.5); // the menu did not open: try again soon
        _fangMenuUntilUtc = now + TimeSpan.FromSeconds(2);
        FireMixed(hud, CrucibleAutoPilotRules.HudItemSlotEvent, pick, null);
        Done(now, "Opening a Fang…");
        return true;
    }

    /// <summary>
    /// Clear and Level: in each battle but the final boss, use a held Thief's Eye (loot ×3) and Merchant's Eye (tokens
    /// ×2) once each — HUD slot, then "Use", as a potion (<see cref="CrucibleAutoPilotRules.ShouldUseEyes"/>).
    /// </summary>
    private bool TryUseEye(DateTime now)
    {
        var menu = now < _eyeMenuUntilUtc ? Visible("ContextMenu") : null;
        if (menu != null)
        {
            _eyeMenuUntilUtc = default;
            if (Text(menu, 8) == "Use")
            {
                FireMixed(menu, 0, 0, 0u, null, null);
                _eyePressedUtc = now; // counted once it leaves the HUD: "Cannot execute at this time." mid-action (2026-10-31)
                Done(now, "Using an Eye.");
            }
            else
            {
                _eyePressed = 0;
            }

            return true;
        }

        var fighting = CrucibleTracker.Current.Enemy;
        var eyes = CrucibleAutoPilotRules.ShouldUseEyes(_legendary, fighting);
        var serum = CrucibleAutoPilotRules.ShouldUseBossSerum(_legendary, _usePotionsNow, fighting);
        // Poisoned and hurt in any battle: the Antipoison serum goes now, not kept for the final boss.
        var self = FFXIVClientStructs.FFXIV.Client.Game.Control.Control.GetLocalPlayer();
        var poisoned = self != null && self->GetStatusManager()->HasStatus(CrucibleAutoPilotRules.CruciblePoisonStatusId);
        var cure = fighting != null && CrucibleAutoPilotRules.ShouldCurePoison(_legendary, _usePotionsNow, poisoned, _selfHpPct);
        // A tankbuster is being cast at the beastmaster: a Tome of the Impervious (Blink) or a Crucible Tannin.
        var busterCast = CrucibleHints.BusterCastNow(now);
        var guard = fighting != null && CrucibleAutoPilotRules.ShouldGuardBuster(_legendary, _usePotionsNow, busterCast);
        if (!eyes && !serum && !cure && !guard)
            return false;
        if (fighting!.Battle != _eyeBattle)
        {
            _eyeBattle = fighting.Battle;
            _eyesUsed.Clear();
            _eyeTries.Clear();
        }

        var player = FFXIVClientStructs.FFXIV.Client.Game.Control.Control.GetLocalPlayer();
        if (now < _nextEyeUtc || player == null || !player->InCombat) // battlegrounds only: a press before the pull is wasted
            return false;
        var hud = Visible("XBMContentsMainHUD");
        if (hud == null || Visible("XBMContentsBooty") != null || Visible("XBMContentsTreasure") != null
            || Visible("XBMContentsItemShop") != null || Visible("XBMResult") != null || Visible("XBMPetParty") != null)
            return false;

        var held = new List<uint>(10);
        var usable = new List<bool>(10);
        for (var slot = 0; slot < 10; slot++)
        {
            held.Add(Number(hud, CrucibleAutoPilotRules.HudItemRowIndex(slot)) != 0
                ? (uint)Number(hud, CrucibleAutoPilotRules.HudItemIdIndex(slot))
                : 0u);
            usable.Add(HudFlag(hud, CrucibleAutoPilotRules.HudItemUsableIndex(slot)));
        }

        // A pressed Eye: used once it leaves the HUD; pressed again (up to EyeMaxTries a battle) when it did not go off.
        if (_eyePressed != 0 && _eyePressedUtc != default)
        {
            var outcome = CrucibleAutoPilotRules.PotionOutcome(
                _eyePressedHeld, CrucibleAutoPilotRules.CountHeld(held, _eyePressed), (now - _eyePressedUtc).TotalSeconds);
            if (outcome == null)
                return true;
            var name = _eyePressed == CrucibleAutoPilotRules.EyeItemIds[0] ? "Thief's Eye (loot ×3)"
                : _eyePressed == CrucibleAutoPilotRules.EyeItemIds[1] ? "Merchant's Eye (tokens ×2)"
                : _itemName((int)_eyePressed - CrucibleAutoPilotRules.CrucibleItemIconOffset) + _eyePressedWhy;
            var tries = _eyeTries[_eyePressed] = _eyeTries.GetValueOrDefault(_eyePressed) + 1;
            if (outcome == true)
            {
                _eyesUsed.Add(_eyePressed);
                _itemUsedOnBoard = true;
                _log($"Crucible: used {name} for this battle.");
            }
            else if (tries >= CrucibleAutoPilotRules.EyeMaxTries)
            {
                _eyesUsed.Add(_eyePressed);
                _log($"Crucible: {name} did not go off after {tries} tries — leaving it for the next battle.");
            }
            else
            {
                _log($"Crucible: {name} did not go off (try {tries}/{CrucibleAutoPilotRules.EyeMaxTries}) — trying again.");
            }

            _eyePressed = 0;
            _eyePressedUtc = default;
            _nextEyeUtc = now + TimeSpan.FromSeconds(1);
            return true;
        }

        var usableHeld = CrucibleAutoPilotRules.UsableOnly(held, usable);
        // The tankbuster first: its cast is under five seconds.
        var pick = guard ? CrucibleAutoPilotRules.PickBusterGuard(usableHeld, busterCast) : -1;
        if (pick >= 0 && _eyesUsed.Contains(held[pick]))
            pick = -1; // one already went this battle
        var why = " (a tankbuster is being cast at you)";
        if (pick < 0 && eyes)
        {
            pick = CrucibleAutoPilotRules.PickEye(usableHeld, _eyesUsed);
            why = "";
        }

        if (pick < 0 && serum)
        {
            pick = CrucibleAutoPilotRules.PickBossSerum(usableHeld, CrucibleTracker.Current.Board?.Index ?? 0, _eyesUsed,
                HeldResistances());
            why = " (for the boss)";
        }

        if (pick < 0 && cure)
        {
            pick = CrucibleAutoPilotRules.PickAntipoison(usableHeld);
            if (pick >= 0 && _eyesUsed.Contains(held[pick]))
                pick = -1; // one already went this battle and did not take: not a second
            why = " (poisoned)";
        }

        if (pick < 0)
            return false;

        _eyePressedWhy = why;

        _eyePressed = held[pick];
        _eyePressedHeld = CrucibleAutoPilotRules.CountHeld(held, held[pick]);
        _eyePressedUtc = default; // set when "Use" is pressed
        _nextEyeUtc = now + TimeSpan.FromSeconds(1.5); // the menu did not open: try again soon
        _eyeMenuUntilUtc = now + TimeSpan.FromSeconds(2);
        FireMixed(hud, CrucibleAutoPilotRules.HudItemSlotEvent, pick, null);
        Done(now, "Opening an Eye…");
        return true;
    }

    /// <summary>
    /// A HUD flag: true unless the value is a flag or number saying no. Anything else (missing, text) reads as true, so a
    /// misread layout can never stop potions altogether — it only falls back to pressing as before.
    /// </summary>
    internal static bool HudFlag(FFXIVClientStructs.FFXIV.Component.GUI.AtkUnitBase* unit, int index)
    {
        if (index >= unit->AtkValuesCount)
            return true;
        var value = unit->AtkValues[index];
        return value.Type switch
        {
            FFXIVClientStructs.FFXIV.Component.GUI.AtkValueType.Bool => value.Byte != 0,
            FFXIVClientStructs.FFXIV.Component.GUI.AtkValueType.Int => value.Int != 0,
            FFXIVClientStructs.FFXIV.Component.GUI.AtkValueType.UInt => value.UInt != 0,
            _ => true,
        };
    }

    /// <summary>Campsite: select the most hurt standing beast, then rest.</summary>
    private void Rest(DateTime now, AgentXBMPetParty* party, AtkUnitBase* camp)
    {
        var pets = party->SelectedPets.AsSpan();
        var team = new List<(uint Hp, uint MaxHp)>(pets.Length);
        for (var i = 0; i < pets.Length && i < 15; i++)
            team.Add((party->PetCurrentHealth[i], party->PetMaxHealth[i]));

        var pick = CrucibleAutoPilotRules.CampsitePick(team, _selfHpPct);
        var selected = party->SelectedPetIds.AsSpan();
        if (pick >= 0 && selected.Length == 0)
        {
            Fire(camp, 1, pick);
            Done(now, $"Resting with team member {pick + 1}.");
            return;
        }

        if (pick < 0 && selected.Length > 0)
        {
            Fire(camp, 1, (int)selected[0].PetId); // resting alone: take the selected beast off first
            Done(now, "Resting alone…");
            return;
        }

        _restTried = true;
        Expect("Rest ", now); // with a beast: "Rest and recover 45%…"; alone: "Rest alone while…" (field 2026-10-25)
        Fire(camp, 3);
        Rested?.Invoke();
        Done(now, "Resting.");
    }

    private void Expect(string prompt, DateTime now)
    {
        _expectPrompt = prompt;
        _expectUntilUtc = now + TimeSpan.FromSeconds(5);
    }

    private void Done(DateTime now, string status)
    {
        Status = status;
        _nextStepUtc = now + StepEvery;
    }

    private static AgentXBMPetParty* PartyAgent()
    {
        var module = AgentModule.Instance();
        var agent = module == null ? null : (AgentXBMPetParty*)module->GetAgentByInternalId(AgentId.XBMPetParty);
        return agent != null && ((AgentInterface*)agent)->IsAgentActive() ? agent : null;
    }
}
