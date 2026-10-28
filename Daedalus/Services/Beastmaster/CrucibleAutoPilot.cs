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
/// the spoils, takes the best treasure (every pick a thief's knife allows), leaves the shop without buying, rests the most hurt beast at a campsite, and
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
    private readonly HashSet<int> _treasureTakenRows = new();
    private DateTime _treasureSinceUtc;
    private DateTime _resultSentUtc;
    private DateTime _campSinceUtc;
    private DateTime _lootSinceUtc;
    private float _selfHpPct = 1f;
    private bool _legendary;
    private bool _legendaryNoGear;
    private bool _gearTakenOnBoard;
    private int _boardIndex;

    private readonly Func<int, bool> _isBeastGear;
    private readonly Func<int, string> _itemName;
    private readonly Func<int, string?> _itemEffect;

    /// <param name="isBeastGear">Whether an XBMItem row is beast gear (what is taken from the spoils).</param>
    /// <param name="itemName">An XBMItem's name (the spoils window holds only ids).</param>
    /// <param name="itemEffect">An XBMItem's effect text, which ranks the treasure coffer's offers.</param>
    public CrucibleAutoPilot(
        Action<string> log, Func<int, bool> isBeastGear, Func<int, string> itemName, Func<int, string?>? itemEffect = null)
    {
        _log = log;
        _isBeastGear = isBeastGear;
        _itemName = itemName;
        _itemEffect = itemEffect ?? (_ => null);
    }

    /// <summary>What it did last, for the team window.</summary>
    public string Status { get; private set; } = "";

    /// <summary>Framework thread, every frame.</summary>
    /// <param name="selfHpPct">The beastmaster's HP as a fraction of max (decides resting alone at a campsite).</param>
    /// <param name="legendary">
    /// Score for Legendary: no rest at a campsite (Sleepless Survival); beast gear taken where holding it pays (Walking
    /// Armory) and refused elsewhere (Nameday Attire: none from the spoils, a coffer's non-gear offer over gear).
    /// </param>
    public void Update(
        bool enabled, CrucibleBoard? board, CrucibleHornSetter? horns, bool takeItems, float selfHpPct = 1f, bool legendary = false)
    {
        _selfHpPct = selfHpPct;
        // Legendary: gear only where holding it pays (Walking Armory); elsewhere none, for Nameday Attire.
        // Once gear has been taken on this board (a coffer offering only gear), Nameday Attire is gone: avoiding gear
        // after that only throws away the better offer (Korha 2026-10-27: a Reraiser over Heavy Axe).
        if (board?.Index != _boardIndex)
        {
            _boardIndex = board?.Index ?? 0;
            _gearTakenOnBoard = false;
        }

        _legendaryNoGear = legendary && board != null && !CrucibleAutoPilotRules.LegendaryTakesGear(board) && !_gearTakenOnBoard;
        _legendary = legendary;
        takeItems = legendary ? !_legendaryNoGear : takeItems;
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
            if (now < _expectUntilUtc && CrucibleAutoPilotRules.ShouldConfirm(prompt, _expectPrompt))
            {
                Fire(yesNo, 0);
                _expectPrompt = null;
                Done(now, $"Yes: {prompt.Replace(" / ", " ")}");
            }

            return;
        }

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

            switch (CrucibleAutoPilotRules.NextLootStep(takeItems, _lootGearDone, _lootTokensTried))
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
            TakeTreasure(now, treasure, CrucibleAutoPilotRules.WantedResistances(board.Index));
            return;
        }

        _treasureTakenRows.Clear();
        _treasureSinceUtc = default;

        var shop = Visible("XBMContentsItemShop");
        if (shop != null)
        {
            if (!_shopTried)
            {
                _shopTried = true;
                Expect("Conclude purchasing and leave", now);
                Fire(shop, 0);
                Done(now, "Leaving the shop.");
            }

            return;
        }

        _shopTried = false;

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
                names.Add($"{_itemName(id)}{(_isBeastGear(id) ? " (gear)" : "")}");
        }

        var held = 0;
        for (var k = 0; k < CrucibleAutoPilotRules.HeldGearMaxRows; k++)
        {
            if (CrucibleAddons.Bool(loot, CrucibleAutoPilotRules.HeldGearFlagIndex(k)))
                held++;
        }

        var pick = CrucibleAutoPilotRules.PickGear(offers, _isBeastGear, _lootTakenRows);
        var offeredText = names.Count == 0 ? "nothing" : string.Join(", ", names);
        var refused = _lootGearTakes > 0 && held <= _lootHeldBefore;
        if (CrucibleAutoPilotRules.LootGearDone(pick >= 0, _lootGearTakes, refused))
        {
            _lootGearDone = true;
            _log($"Crucible: spoils — {offeredText}; "
                 + (_lootGearTakes == 0 ? "no beast gear to take."
                     : refused ? $"the last piece was refused (gear held {held}); done after {_lootGearTakes - 1} piece(s)."
                     : $"done after {_lootGearTakes} piece(s) of gear (gear held {held})."));
            Done(now, _lootGearTakes == 0 ? "No beast gear in the spoils." : "Gear taken.");
            return;
        }

        var name = _itemName(offers[pick]);
        _log($"Crucible: spoils — {offeredText}; taking {name}.");
        _lootGearTakes++;
        _gearTakenOnBoard = true;
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
    private void TakeTreasure(DateTime now, AtkUnitBase* treasure, IReadOnlyList<string> wanted)
    {
        if (_treasureSinceUtc == default)
            _treasureSinceUtc = now;
        if (now - _treasureSinceUtc < TreasureSettle
            || treasure->AtkValuesCount <= CrucibleAutoPilotRules.TreasureItemIdIndex(CrucibleAutoPilotRules.TreasureMaxRows - 1))
            return;

        var offers = new List<int>(CrucibleAutoPilotRules.TreasureMaxRows);
        for (var r = 0; r < CrucibleAutoPilotRules.TreasureMaxRows; r++)
            offers.Add((int)Number(treasure, CrucibleAutoPilotRules.TreasureItemIdIndex(r)));

        var pick = _legendaryNoGear
            ? CrucibleAutoPilotRules.PickTreasureAvoidingGear(offers, _isBeastGear, _itemEffect, _treasureTakenRows, wanted)
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
        _treasureTakenRows.Add(pick);
        _treasureSinceUtc = now; // let the window update (or close) before the next pick
        Expect("Choose the ", now);
        Fire(treasure, 2, pick);
        Done(now, $"Choosing {name}.");
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
