using System.Linq;
using Daedalus.Config.DPS;
using Daedalus.Data;
using Daedalus.Services.Action;
using Xunit;

namespace Daedalus.Tests.Services.Action;

/// <summary>V2.4 loadout auto-apply: composing a reference loadout into the 24-slot array.</summary>
public class BluLoadoutComposerTests
{
    [Fact]
    public void Compose_AlwaysReturns24Slots()
    {
        var all = BluLoadoutComposer.Compose(BLULoadouts.Dps, _ => true);
        var none = BluLoadoutComposer.Compose(BLULoadouts.Dps, _ => false);
        Assert.Equal(BluLoadoutService.SlotCount, all.Length);
        Assert.Equal(BluLoadoutService.SlotCount, none.Length);
        Assert.All(none, s => Assert.Equal(0u, s));
    }

    [Fact]
    public void Compose_SkipsUnlearnedSpells()
    {
        var learned = new[] { BLULoadouts.Dps.Core[0], BLULoadouts.Dps.Core[1] }.ToHashSet();
        var slots = BluLoadoutComposer.Compose(BLULoadouts.Dps, learned.Contains);

        Assert.Equal(learned, slots.Where(s => s != 0).ToHashSet());
    }

    [Fact]
    public void Compose_CoreFillsBeforeFlex()
    {
        var slots = BluLoadoutComposer.Compose(BLULoadouts.Dps, _ => true);
        var filled = slots.Where(s => s != 0).ToArray();

        // Every core spell must be present before any flex spell is considered.
        foreach (var coreId in BLULoadouts.Dps.Core)
            Assert.Contains(coreId, filled);
        // 22 core + flex fills the remaining slots to exactly 24.
        Assert.Equal(BluLoadoutService.SlotCount, filled.Length);
        Assert.Equal(BLULoadouts.Dps.Core, filled.Take(BLULoadouts.Dps.Core.Length));
    }

    [Fact]
    public void Compose_NeverExceeds24_AndNeverDuplicates()
    {
        foreach (var loadout in BLULoadouts.All)
        {
            var slots = BluLoadoutComposer.Compose(loadout, _ => true);
            var filled = slots.Where(s => s != 0).ToArray();
            Assert.True(filled.Length <= BluLoadoutService.SlotCount);
            Assert.Equal(filled.Length, filled.Distinct().Count());
        }
    }

    [Theory]
    [InlineData(BluRole.Tank, "Tank")]
    [InlineData(BluRole.Healer, "Healer")]
    [InlineData(BluRole.Dps, "DPS")]
    public void ForRole_MapsToMatchingLoadout(BluRole role, string expectedName)
    {
        Assert.Equal(expectedName, BluLoadoutComposer.ForRole(role).Name);
    }

    // ── Apply handshake (game-call-free paths only; the mimicry strip is in-game validation) ──

    [Fact]
    public void RequestApply_BadSlotCount_FailsImmediately()
    {
        var svc = new BluLoadoutService(() => 0);
        svc.RequestApplyLoadout(new uint[3]);
        Assert.False(svc.IsApplyPending);
        Assert.Contains("bad slot array", svc.LastApplyResult);
    }

    [Fact]
    public void RequestApply_NotOnBlu_FailsOnUpdate()
    {
        var svc = new BluLoadoutService(
            jobIdProvider: () => 0, // not BLU
            activeMimicryProvider: System.Array.Empty<uint>);
        svc.RequestApplyLoadout(new uint[BluLoadoutService.SlotCount]);
        Assert.True(svc.IsApplyPending);

        svc.Update();

        Assert.False(svc.IsApplyPending);
        Assert.Contains("not on BLU", svc.LastApplyResult);
    }

    [Fact]
    public void RequestApply_MimicryActive_WaitsInsteadOfFailing()
    {
        // Mimicry can't be cancelled programmatically (field-verified 2026-07-11) — the apply
        // must WAIT for the user to drop it (job swap), surviving the off-BLU frames in between.
        var mimicry = new System.Collections.Generic.List<uint> { 2125u };
        uint jobId = Daedalus.Data.JobRegistry.BlueMage;
        var svc = new BluLoadoutService(
            jobIdProvider: () => jobId,
            activeMimicryProvider: () => mimicry);
        svc.RequestApplyLoadout(new uint[BluLoadoutService.SlotCount]);

        svc.Update();
        Assert.True(svc.IsApplyPending);
        Assert.True(svc.WaitingOnMimicry);

        // Mid job-swap (not BLU): the pending apply must survive.
        jobId = 1;
        svc.Update();
        Assert.True(svc.IsApplyPending);
        Assert.True(svc.WaitingOnMimicry);

        // Back on BLU with mimicry gone: the wait ends (the apply itself needs the live game).
        jobId = Daedalus.Data.JobRegistry.BlueMage;
        mimicry.Clear();
        svc.Update();
        Assert.False(svc.WaitingOnMimicry);
    }
}
