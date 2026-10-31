using System;
using System.Collections.Generic;
using System.Linq;
using Daedalus.Data;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Plugin.Services;
using static Daedalus.Services.Beastmaster.CrucibleAddons;

namespace Daedalus.Services.Beastmaster;

/// <summary>
/// Feeds a <see cref="CrucibleBonusTracker"/> from the game while a board runs, so the Crucible tab can show which bonuses
/// are still intact the moment one is lost (two Board 1 runs on 2026-10-27 missed Legendary by one lost bonus each).
/// Watched: a familiar knocked out (its HP reaching 0 in combat, unless Parting Blow or Tempered Release was just used —
/// a retreat, Wespe's Final Sting and a battle's end read 0 too), a new status on the beastmaster
/// (Damage Down / Vulnerability Up cost Unbeastable), a Crucible item leaving the HUD outside the loot and shop screens
/// (used), live HP, the HUD's held tokens / gear / items (a fall in tokens = spent), the campsite window's per-beast
/// "times fed" counters (a rise = a feed), and — from the autopilot — gear taken and a rest.
/// </summary>
public sealed unsafe class CrucibleBonusWatcher
{
    private readonly Func<uint, string> _statusName;
    private readonly Func<CrucibleBoard, IReadOnlyList<int>> _roster;
    private readonly Action<string> _log;

    private int _boardIndex;
    private readonly HashSet<uint> _statusesSeen = [];
    private ulong _familiarId;
    private bool _familiarDown;
    private int _heldItems = -1;
    private int? _tokens;
    private int _fedSeen = -1;

    /// <param name="statusName">English Status sheet name by id (the tracker matches "Damage Down" / "Vulnerability Up").</param>
    /// <param name="roster">The beasts registered for a board (the team set at Team Composition).</param>
    public CrucibleBonusWatcher(Func<uint, string> statusName, Func<CrucibleBoard, IReadOnlyList<int>> roster, Action<string> log)
    {
        _statusName = statusName;
        _roster = roster;
        _log = log;
    }

    /// <summary>The run being tracked (kept after the board ends, until the next one starts), or null.</summary>
    public static CrucibleBonusTracker? Tracker { get; private set; }

    /// <summary>The autopilot took a piece of beast gear.</summary>
    public void GearTaken() => Tracker?.GearAcquired();

    /// <summary>The autopilot rested at a campsite.</summary>
    public void Rested() => Tracker?.Rested();

    /// <summary>The board's result screen was read: the run finished.</summary>
    public void Finished() => Tracker?.Finished();

    /// <summary>
    /// Seconds since Parting Blow or Tempered Release (Wespe's Final Sting takes it out for the battle) went on cooldown,
    /// whichever was later; null when neither is cooling down.
    /// </summary>
    private static float? SecondsSinceRetreat()
    {
        var am = FFXIVClientStructs.FFXIV.Client.Game.ActionManager.Instance();
        if (am == null)
            return null;
        const FFXIVClientStructs.FFXIV.Client.Game.ActionType action = FFXIVClientStructs.FFXIV.Client.Game.ActionType.Action;
        float? latest = null;
        foreach (var id in (ReadOnlySpan<uint>)[BSTActions.PartingBlow.ActionId, BSTActions.TemperedRelease.ActionId])
        {
            if (am->IsRecastTimerActive(action, id) && am->GetRecastTimeElapsed(action, id) is var elapsed
                && (latest == null || elapsed < latest))
                latest = elapsed;
        }

        return latest;
    }

    /// <summary>Framework thread, every frame.</summary>
    public void Update(CrucibleBoard? board, IPlayerCharacter? player, IObjectTable objects)
    {
        if (board == null || player == null)
        {
            _boardIndex = 0; // off the board: the next board entered is a new run
            return;
        }

        if (board.Index != _boardIndex)
        {
            _boardIndex = board.Index;
            var kins = _roster(board).Select(no => BstFamiliars.ByBestiaryNo((uint)no)?.Classification ?? BeastClassification.Unknown);
            Tracker = new CrucibleBonusTracker(board, kins);
            _statusesSeen.Clear();
            foreach (var s in player.StatusList)
                _statusesSeen.Add(s.StatusId); // what is already on at the start was not gained on the board
            _familiarId = 0;
            _familiarDown = false;
            _heldItems = -1;
            _tokens = null;
            _fedSeen = -1;
            _log($"Crucible: bonus tracker started for {board.Name} ({_roster(board).Count} beasts registered).");
        }

        var tracker = Tracker;
        if (tracker == null)
            return;

        // New statuses on the beastmaster.
        var now = new HashSet<uint>();
        foreach (var s in player.StatusList)
        {
            if (s.StatusId == 0)
                continue;
            now.Add(s.StatusId);
            if (_statusesSeen.Contains(s.StatusId))
                continue;
            var name = _statusName(s.StatusId);
            tracker.BeastmasterStatusGained(name);
            if (CrucibleBonusRules.BreaksUnbeastable(name))
                _log($"Crucible: {name} on the beastmaster — Unbeastable lost.");
        }

        _statusesSeen.Clear();
        _statusesSeen.UnionWith(now);

        // A familiar knocked out: its HP reaches 0 (a retreat removes it from the world instead).
        var familiar = FamiliarLookup.Find(objects, player);
        if (familiar != null)
        {
            if (familiar.GameObjectId != _familiarId)
            {
                _familiarId = familiar.GameObjectId;
                _familiarDown = false;
            }

            var down = familiar.IsDead || (familiar.MaxHp > 0 && familiar.CurrentHp == 0);
            if (down && !_familiarDown && CrucibleBonusRules.IsKnockOut(true,
                    player.StatusFlags.HasFlag(Dalamud.Game.ClientState.Objects.Enums.StatusFlags.InCombat),
                    SecondsSinceRetreat()))
            {
                tracker.FamiliarKnockedOut();
                _log($"Crucible: {familiar.Name} knocked out — No Beast Left Behind lost.");
            }

            _familiarDown = down;
        }

        tracker.SetHp(
            player.MaxHp > 0 ? player.CurrentHp * 100f / player.MaxHp : 0f,
            familiar is { MaxHp: > 0 } f ? [f.CurrentHp * 100f / f.MaxHp] : []);

        // Feeds: the campsite / team window counts each beast's feeds; a rise in the total is a feed.
        var camp = Visible("XBMPetParty");
        if (camp != null && camp->AtkValuesCount > CrucibleAutoPilotRules.PetPartyCountIndex)
        {
            var rows = (int)Math.Clamp(Number(camp, CrucibleAutoPilotRules.PetPartyCountIndex), 0, 15);
            var fed = 0;
            for (var r = 0; r < rows; r++)
                fed += (int)Math.Max(0, Number(camp, CrucibleAutoPilotRules.PetPartyFedIndex(r)));
            if (_fedSeen >= 0 && fed > _fedSeen)
            {
                for (var i = _fedSeen; i < fed; i++)
                    tracker.Fed();
                _log("Crucible: a beast was fed — Starve the Fever lost.");
            }

            if (rows > 0)
                _fedSeen = fed;
        }

        // A Crucible item leaving the HUD outside the loot, coffer, shop and camp screens was used.
        var hud = Visible("XBMContentsMainHUD");
        if (hud == null || hud->AtkValuesCount <= CrucibleAutoPilotRules.HudItemRowIndex(CrucibleAutoPilotRules.MaxCrucibleItemsHeld - 1))
            return;
        var held = 0;
        for (var slot = 0; slot < CrucibleAutoPilotRules.MaxCrucibleItemsHeld; slot++)
        {
            if (Number(hud, CrucibleAutoPilotRules.HudItemRowIndex(slot)) != 0)
                held++;
        }

        var screens = Visible("XBMContentsBooty") != null || Visible("XBMContentsTreasure") != null
                      || Visible("XBMContentsItemShop") != null || Visible("XBMPetParty") != null || Visible("XBMResult") != null;
        if (_heldItems >= 0 && held < _heldItems && !screens)
        {
            tracker.ItemUsed();
            _log("Crucible: a Crucible item was used — Economic Ethos lost.");
        }

        _heldItems = held;

        // Held gear and tokens (Walking Armory, Habitual Hoarder, Rolling in Riches); a fall in tokens was spent.
        var gear = 0;
        for (var slot = 0; slot < CrucibleAutoPilotRules.HudGearSlots; slot++)
        {
            if (Bool(hud, CrucibleAutoPilotRules.HudGearHeldIndex(slot)) || Number(hud, CrucibleAutoPilotRules.HudGearHeldIndex(slot)) != 0)
                gear++;
        }

        var tokens = CrucibleAutoPilotRules.ParseTokens(Text(hud, CrucibleAutoPilotRules.HudTokensIndex));
        if (tokens is { } t)
        {
            var spent = CrucibleAutoPilotRules.TokensSpent(_tokens, t);
            if (spent > 0)
            {
                tracker.TokensSpent(spent);
                _log($"Crucible: {spent:N0} tokens spent — Coin Clutcher lost.");
            }

            _tokens = t;
        }

        tracker.SetHeld(gear, held, _tokens ?? 0);
    }
}
