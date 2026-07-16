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

    // ── Death-immunity ledger ───────────────────────────────────────────────

    [Fact]
    public void DeathLedger_ResolveProbe_VerdictMath()
    {
        // Died/despawned during the window → the spell wasn't refused.
        Assert.Equal(Daedalus.Services.Blu.DeathImmunityVerdict.Vulnerable,
            Daedalus.Services.Blu.DeathImmunityLedger.ResolveProbe(50_000f, 0f, deadOrGone: true));
        // ~50% of current HP removed → the Missile landed.
        Assert.Equal(Daedalus.Services.Blu.DeathImmunityVerdict.Vulnerable,
            Daedalus.Services.Blu.DeathImmunityLedger.ResolveProbe(100_000f, 52_000f, deadOrGone: false));
        // HP essentially untouched → immune (or an invuln phase — a later hit corrects it).
        Assert.Equal(Daedalus.Services.Blu.DeathImmunityVerdict.Immune,
            Daedalus.Services.Blu.DeathImmunityLedger.ResolveProbe(100_000f, 97_000f, deadOrGone: false));
        // Middle band (heavy unrelated damage, no halving) → inconclusive, record nothing.
        Assert.Equal(Daedalus.Services.Blu.DeathImmunityVerdict.Unknown,
            Daedalus.Services.Blu.DeathImmunityLedger.ResolveProbe(100_000f, 80_000f, deadOrGone: false));
    }

    [Fact]
    public void DeathLedger_PersistsAndReloads()
    {
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "daedalus-test-" + System.Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(dir);
        try
        {
            var ledger = new Daedalus.Services.Blu.DeathImmunityLedger(dir, objectTable: null);
            ledger.NotifyProbeCast(1UL, 4242u, "Koshchei", 80_000, 80_000);
            // Target unresolvable (null object table) → dead/gone → Vulnerable; resolve is
            // time-gated at 3s, so we can't drive it synchronously here — instead verify the
            // load path with a hand-written file.
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(dir, "death-immunity-ledger.json"),
                "[{\"id\":4242,\"n\":\"Koshchei\",\"z\":\"The Stone Vigil\",\"tid\":168,\"v\":1,\"c\":3,\"hp\":80000,\"ts\":0}]");
            var reloaded = new Daedalus.Services.Blu.DeathImmunityLedger(dir, objectTable: null);
            Assert.Equal(Daedalus.Services.Blu.DeathImmunityVerdict.Vulnerable, reloaded.GetVerdict(4242u));
            Assert.Single(reloaded.Entries);
            Assert.Equal(Daedalus.Services.Blu.DeathImmunityVerdict.Unknown, reloaded.GetVerdict(9999u));
            // The Raid window's per-duty filter:
            Assert.Single(reloaded.EntriesForTerritory(168));
            Assert.Empty(reloaded.EntriesForTerritory(999));
            Assert.Empty(reloaded.EntriesForTerritory(0));
        }
        finally
        {
            try { System.IO.Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    // ── Death-immunity ledger: probe lifecycle (manual-cast feed, 2026-07-16) ──
    // The action-effect hook now feeds manual Missiles into NotifyProbeCast, so a
    // rotation-dispatched cast is seen TWICE (dispatch path + effect hook) — the ledger
    // dedups per target. The injectable clock drives the 3s resolve synchronously.

    private static Daedalus.Services.Blu.DeathImmunityLedger TempLedger(
        out string dir, Dalamud.Plugin.Services.IObjectTable? objectTable = null)
    {
        dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "daedalus-test-" + System.Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(dir);
        return new Daedalus.Services.Blu.DeathImmunityLedger(dir, objectTable);
    }

    [Fact]
    public void DeathLedger_SameTargetProbe_DedupedWithinWindow_NewTargetIsNot()
    {
        var ledger = TempLedger(out var dir);
        try
        {
            var now = System.DateTime.UtcNow;
            ledger.UtcNow = () => now;

            ledger.NotifyProbeCast(1UL, 4242u, "Gilgamesh", 1_000_000, 1_000_000); // dispatch path
            ledger.NotifyProbeCast(1UL, 4242u, "Gilgamesh", 1_000_000, 1_000_000); // effect hook, same cast
            Assert.Equal(1, ledger.PendingProbeCount);

            ledger.NotifyProbeCast(2UL, 777u, "Enkidu", 500_000, 500_000); // different target
            Assert.Equal(2, ledger.PendingProbeCount);

            now = now.AddSeconds(5); // past dedup + resolve windows — a NEW cast may probe again
            ledger.Update();         // resolves the old probes (null table → dead/gone)
            ledger.NotifyProbeCast(1UL, 4242u, "Gilgamesh", 1_000_000, 1_000_000);
            Assert.Equal(1, ledger.PendingProbeCount);
        }
        finally { try { System.IO.Directory.Delete(dir, recursive: true); } catch { } }
    }

    [Fact]
    public void DeathLedger_FullResist_ResolvesImmune()
    {
        // The user's Gilgamesh log: "Full resist! ... takes no damage" — HP untouched after the
        // cast resolves → Immune. Driven end-to-end through Update() via the injected clock.
        var target = new Moq.Mock<Dalamud.Game.ClientState.Objects.Types.IBattleChara>();
        target.Setup(x => x.IsDead).Returns(false);
        target.Setup(x => x.CurrentHp).Returns(1_000_000u); // untouched
        var table = new Moq.Mock<Dalamud.Plugin.Services.IObjectTable>();
        table.Setup(x => x.SearchById(Moq.It.IsAny<ulong>())).Returns(target.Object);

        var ledger = TempLedger(out var dir, table.Object);
        try
        {
            var now = System.DateTime.UtcNow;
            ledger.UtcNow = () => now;

            ledger.NotifyProbeCast(1UL, 4242u, "Gilgamesh", 1_000_000, 1_000_000);
            now = now.AddSeconds(3.5);
            ledger.Update();

            Assert.Equal(0, ledger.PendingProbeCount);
            Assert.Equal(Daedalus.Services.Blu.DeathImmunityVerdict.Immune, ledger.GetVerdict(4242u));
        }
        finally { try { System.IO.Directory.Delete(dir, recursive: true); } catch { } }
    }

    [Fact]
    public void DeathLedger_LandedHit_ResolvesVulnerable()
    {
        var target = new Moq.Mock<Dalamud.Game.ClientState.Objects.Types.IBattleChara>();
        target.Setup(x => x.IsDead).Returns(false);
        target.Setup(x => x.CurrentHp).Returns(490_000u); // ~50% of before — the Missile signature
        var table = new Moq.Mock<Dalamud.Plugin.Services.IObjectTable>();
        table.Setup(x => x.SearchById(Moq.It.IsAny<ulong>())).Returns(target.Object);

        var ledger = TempLedger(out var dir, table.Object);
        try
        {
            var now = System.DateTime.UtcNow;
            ledger.UtcNow = () => now;

            ledger.NotifyProbeCast(1UL, 4242u, "Gilgamesh", 1_000_000, 1_000_000);
            now = now.AddSeconds(3.5);
            ledger.Update();

            Assert.Equal(Daedalus.Services.Blu.DeathImmunityVerdict.Vulnerable, ledger.GetVerdict(4242u));
        }
        finally { try { System.IO.Directory.Delete(dir, recursive: true); } catch { } }
    }

    [Fact]
    public void DeathLedger_VulnerableEvidence_SurvivesLaterFullResist()
    {
        // Invuln-phase safeguard: once a real hit proved Vulnerable, a later full resist
        // (phase transition) must NOT flip the verdict to Immune.
        var currentHp = 490_000u;
        var target = new Moq.Mock<Dalamud.Game.ClientState.Objects.Types.IBattleChara>();
        target.Setup(x => x.IsDead).Returns(false);
        target.Setup(x => x.CurrentHp).Returns(() => currentHp);
        var table = new Moq.Mock<Dalamud.Plugin.Services.IObjectTable>();
        table.Setup(x => x.SearchById(Moq.It.IsAny<ulong>())).Returns(target.Object);

        var ledger = TempLedger(out var dir, table.Object);
        try
        {
            var now = System.DateTime.UtcNow;
            ledger.UtcNow = () => now;

            ledger.NotifyProbeCast(1UL, 4242u, "Gilgamesh", 1_000_000, 1_000_000); // lands (490k after)
            now = now.AddSeconds(3.5);
            ledger.Update();
            Assert.Equal(Daedalus.Services.Blu.DeathImmunityVerdict.Vulnerable, ledger.GetVerdict(4242u));

            currentHp = 490_000u; // untouched this time — full resist during a phase
            ledger.NotifyProbeCast(1UL, 4242u, "Gilgamesh", 1_000_000, 490_000);
            now = now.AddSeconds(3.5);
            ledger.Update();

            Assert.Equal(Daedalus.Services.Blu.DeathImmunityVerdict.Vulnerable, ledger.GetVerdict(4242u));
        }
        finally { try { System.IO.Directory.Delete(dir, recursive: true); } catch { } }
    }

    // ── Final Sting calculator ──────────────────────────────────────────────

    [Fact]
    public void StingCalc_UnbuffedScalesByPotencyRatio()
    {
        // 1000 observed on Sonic Boom (210p) → 2000p sting ≈ 9524 unbuffed.
        var est = Daedalus.Rotation.ProteusCore.Helpers.FinalStingCalculator.Estimate(1000f, 210f);
        Assert.Equal(1000f * (2000f / 210f), est, 1);
    }

    [Fact]
    public void StingCalc_BuffsStackMultiplicatively_AndBristleIsNotAnInput()
    {
        // Flute ×1.5 × Whistle ×1.8 × Off-guard ×1.05 × BI ×2 × MG ×0.6 = ×3.402.
        var baseEst = Daedalus.Rotation.ProteusCore.Helpers.FinalStingCalculator.Estimate(2000f, 2000f);
        var buffed = Daedalus.Rotation.ProteusCore.Helpers.FinalStingCalculator.Estimate(
            2000f, 2000f, waxingNocturne: true, harmonized: true, offGuard: true,
            basicInstinct: true, mightyGuard: true);
        Assert.Equal(baseEst * 1.5f * 1.8f * 1.05f * 2.0f * 0.6f, buffed, 0);
    }

    [Fact]
    public void StingCalc_ZeroBaseline_YieldsZero()
    {
        Assert.Equal(0f, Daedalus.Rotation.ProteusCore.Helpers.FinalStingCalculator.Estimate(0f, 210f));
        Assert.Equal(0f, Daedalus.Rotation.ProteusCore.Helpers.FinalStingCalculator.Estimate(1000f, 0f));
    }

    [Fact]
    public void StingCalc_StingersNeeded_CeilsWithSafetyFactor()
    {
        // 100k HP, 20k per sting at 0.75 safety → each credited 15k → 7 stingers.
        Assert.Equal(7, Daedalus.Rotation.ProteusCore.Helpers.FinalStingCalculator.StingersNeeded(100_000, 20_000f, 0.75f));
        // Exactly divisible: 60k / (20k × 1.0) = 3.
        Assert.Equal(3, Daedalus.Rotation.ProteusCore.Helpers.FinalStingCalculator.StingersNeeded(60_000, 20_000f, 1.0f));
        Assert.Equal(0, Daedalus.Rotation.ProteusCore.Helpers.FinalStingCalculator.StingersNeeded(0, 20_000f, 0.75f));
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
