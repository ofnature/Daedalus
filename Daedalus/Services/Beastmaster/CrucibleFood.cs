using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace Daedalus.Services.Beastmaster;

/// <summary>
/// Food before a Crucible run (user, 2026-11-06): the meals in the bags for the setting's list, how much Well Fed is
/// left, and eating one. The decision is <see cref="Daedalus.Data.CrucibleLoopRules.NextFood"/>.
/// </summary>
public static unsafe class CrucibleFood
{
    /// <summary>ItemUICategory "Meal".</summary>
    public const uint MealUiCategory = 46;

    private static readonly InventoryType[] Bags =
        [InventoryType.Inventory1, InventoryType.Inventory2, InventoryType.Inventory3, InventoryType.Inventory4];

    /// <summary>The meals in the four bags, by name (one entry per item, HQ or not).</summary>
    public static IReadOnlyList<(uint Id, string Name)> MealsInBags(IDataManager data)
    {
        var manager = InventoryManager.Instance();
        var sheet = data.GetExcelSheet<Lumina.Excel.Sheets.Item>();
        if (manager == null || sheet == null)
            return [];

        var found = new Dictionary<uint, string>();
        foreach (var bag in Bags)
        {
            var container = manager->GetInventoryContainer(bag);
            if (container == null)
                continue;
            for (var i = 0; i < container->Size; i++)
            {
                var slot = container->GetInventorySlot(i);
                if (slot == null || slot->ItemId == 0 || found.ContainsKey(slot->ItemId))
                    continue;
                if (sheet.GetRowOrDefault(slot->ItemId) is { } row && row.ItemUICategory.RowId == MealUiCategory)
                    found[slot->ItemId] = row.Name.ExtractText();
            }
        }

        return found.OrderBy(kv => kv.Value).Select(kv => (kv.Key, kv.Value)).ToList();
    }

    /// <summary>How many of the item the bags hold, HQ or NQ.</summary>
    public static int Held(uint itemId, bool hq)
    {
        var manager = InventoryManager.Instance();
        return manager == null ? 0 : System.Math.Max(0, manager->GetInventoryItemCount(itemId, hq));
    }

    /// <summary>Seconds of Well Fed left on the player, 0 without it.</summary>
    public static float WellFedSecondsLeft(IPlayerCharacter player)
    {
        foreach (var status in player.StatusList)
        {
            if (status.StatusId == Daedalus.Data.CrucibleLoopRules.WellFedStatusId)
                return status.RemainingTime;
        }

        return 0f;
    }

    /// <summary>Eat one (the hotbar's way: an item action on ourselves).</summary>
    public static bool Eat(uint itemId, bool hq)
    {
        var actions = ActionManager.Instance();
        return actions != null && actions->UseAction(ActionType.Item, hq ? itemId + 1_000_000u : itemId, 0xE0000000, 0xFFFF);
    }
}
