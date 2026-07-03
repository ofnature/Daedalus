using System;
using System.Linq;
using Daedalus.Data;
using Daedalus.Services.Action;
using Xunit;

namespace Daedalus.Tests.Data;

/// <summary>
/// Integrity tests for the Blue Academy reference loadouts and the slotted-set reader's
/// fail-open behavior. Guards against id typos — every loadout entry must resolve to a
/// real spellbook row or the Missing-window checklist renders "Action 12345".
/// </summary>
public sealed class BLULoadoutsTests
{
    [Fact]
    public void AllLoadoutIds_ExistInSpellbook()
    {
        var known = BLUSpellbook.All.Select(e => e.ActionId).ToHashSet();

        foreach (var loadout in BLULoadouts.All)
        {
            foreach (var id in loadout.Core.Concat(loadout.Flex))
                Assert.True(known.Contains(id), $"{loadout.Name} references unknown action id {id}");
        }
    }

    [Fact]
    public void CoreLists_HaveNoDuplicates_AndFitTheActiveSet()
    {
        foreach (var loadout in BLULoadouts.All)
        {
            Assert.Equal(loadout.Core.Length, loadout.Core.Distinct().Count());
            Assert.True(loadout.Core.Length <= BluLoadoutService.SlotCount,
                $"{loadout.Name} core has {loadout.Core.Length} spells — exceeds the 24-slot set");
        }
    }

    [Fact]
    public void EveryRole_SlotsMimicryAndTheRaise()
    {
        foreach (var loadout in BLULoadouts.All)
        {
            Assert.Contains(18322u, loadout.Core); // Aetheric Mimicry
            Assert.Contains(18317u, loadout.Core); // Angel Whisper
        }
    }

    [Fact]
    public void CoreAndFlex_DoNotOverlap()
    {
        foreach (var loadout in BLULoadouts.All)
        {
            var core = loadout.Core.ToHashSet();
            foreach (var id in loadout.Flex)
                Assert.False(core.Contains(id), $"{loadout.Name} lists {id} in both Core and Flex");
        }
    }

    // ── BluLoadoutService fail-open behavior ──

    [Fact]
    public void Service_NoSlotData_IsSlottedFailsOpen()
    {
        var service = new BluLoadoutService(() => 0);

        Assert.False(service.HasSlotData);
        Assert.True(service.IsSlotted(11386));  // rotation degrades to learned-only gating
        Assert.Empty(service.SlottedActionIds);
    }

    [Fact]
    public void Service_NonBluJob_ClearsAndReportsNoData()
    {
        var service = new BluLoadoutService(() => JobRegistry.Dancer);

        service.Update(); // never touches ActionManager off-BLU

        Assert.False(service.HasSlotData);
        Assert.Equal(0, service.SlottedCount);
        Assert.True(service.IsSlotted(11386));
    }
}
