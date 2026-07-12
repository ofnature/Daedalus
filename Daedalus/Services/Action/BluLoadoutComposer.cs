using System;
using System.Collections.Generic;
using Daedalus.Data;

namespace Daedalus.Services.Action;

/// <summary>
/// Turns a reference <see cref="BluLoadout"/> into a concrete 24-slot array for
/// <c>ActionManager.SetBlueMageActions</c>: learned Core spells first, then learned Flex,
/// unlearned spells skipped, remaining slots left empty (0). Pure — fully testable.
/// </summary>
public static class BluLoadoutComposer
{
    public static uint[] Compose(BluLoadout loadout, Func<uint, bool> isLearned)
    {
        var slots = new List<uint>(BluLoadoutService.SlotCount);

        void Add(uint id)
        {
            if (slots.Count >= BluLoadoutService.SlotCount) return;
            if (!isLearned(id)) return;
            if (slots.Contains(id)) return;
            slots.Add(id);
        }

        foreach (var id in loadout.Core) Add(id);
        foreach (var id in loadout.Flex) Add(id);

        while (slots.Count < BluLoadoutService.SlotCount)
            slots.Add(0);

        return slots.ToArray();
    }

    /// <summary>The reference loadout for a role dropdown value.</summary>
    public static BluLoadout ForRole(Daedalus.Config.DPS.BluRole role) => role switch
    {
        Daedalus.Config.DPS.BluRole.Tank => BLULoadouts.Tank,
        Daedalus.Config.DPS.BluRole.Healer => BLULoadouts.Healer,
        _ => BLULoadouts.Dps,
    };
}
