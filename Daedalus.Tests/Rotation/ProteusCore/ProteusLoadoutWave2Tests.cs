using System;
using System.Linq;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Moq;
using Daedalus.Config.DPS;
using Daedalus.Data;
using Daedalus.Rotation.Common.Helpers;
using Daedalus.Rotation.ProteusCore.Abilities;
using Daedalus.Rotation.ProteusCore.Context;
using Daedalus.Rotation.ProteusCore.Modules;
using Daedalus.Services.Targeting;
using Daedalus.Tests.Mocks;
using Daedalus.Tests.Rotation.Common.Scheduling;
using Xunit;

namespace Daedalus.Tests.Rotation.ProteusCore;

/// <summary>
/// Loadout wave 2 (2026-07-11): the user's 24-slot kit — Bristle-snapshotted DoTs, Mortal Flame
/// once-latch, White Death / Cold Fog, Matra Magic, Surpanakha dump discipline, offensive oGCDs,
/// Basic Instinct / Toad Oil, healer kit (Pom Cure / Gobskin / Exuviation), Waning Nocturne
/// lockout, and the V2.5 freeze→shatter window.
/// </summary>
[Collection("BluStaticState")] // BluMimicryCommand is static — serialize with ProteusTests
public class ProteusLoadoutWave2Tests
{
    // ── DoT block ───────────────────────────────────────────────────────────

    [Fact]
    public void Damage_BristlePrecedesBreathOfMagic_WhenNoBoost()
    {
        var h = new Harness();
        h.Damage.CollectCandidates(h.Context, h.Scheduler, isMoving: false);

        var gcd = h.Scheduler.InspectGcdQueue();
        var bristle = gcd.First(c => c.Behavior == ProteusAbilities.Bristle);
        var bom = gcd.First(c => c.Behavior == ProteusAbilities.BreathOfMagic);
        Assert.True(bristle.Priority < bom.Priority, "Bristle must win the GCD before the DoT");
    }

    [Fact]
    public void Damage_BoostActive_SkipsBristle_DoTStillPushed()
    {
        var h = new Harness();
        Mock.Get(h.Context).Setup(x => x.HasBoost).Returns(true);
        h.Damage.CollectCandidates(h.Context, h.Scheduler, isMoving: false);

        var gcd = h.Scheduler.InspectGcdQueue();
        Assert.DoesNotContain(gcd, c => c.Behavior == ProteusAbilities.Bristle);
        Assert.Contains(gcd, c => c.Behavior == ProteusAbilities.BreathOfMagic);
        Assert.Contains(gcd, c => c.Behavior == ProteusAbilities.MortalFlame);
    }

    [Fact]
    public void Damage_BoostArmed_HoldsEverythingButTheDot()
    {
        // Bristle's boost is eaten by ANY next offensive spell — while armed for a DoT, filler
        // and weaves must wait (field: three Bristles, zero Breath of Magic).
        var h = new Harness();
        Mock.Get(h.Context).Setup(x => x.HasBoost).Returns(true);
        h.Damage.CollectCandidates(h.Context, h.Scheduler, isMoving: false);

        var gcd = h.Scheduler.InspectGcdQueue();
        Assert.Contains(gcd, c => c.Behavior == ProteusAbilities.BreathOfMagic);
        Assert.DoesNotContain(gcd, c => c.Behavior == ProteusAbilities.SonicBoom);
        Assert.DoesNotContain(gcd, c => c.Behavior == ProteusAbilities.TheRoseOfDestruction);
        Assert.Empty(h.Scheduler.InspectOgcdQueue()); // weaves are spells too — they'd eat it
    }

    [Fact]
    public void Damage_BristleInFlight_HoldsWeaves_BeforeBoostExists()
    {
        // The Boost only exists at Bristle's cast END — a weave pushed during the cast lands in
        // the tail and eats it (field: Glass Dance took the boost meant for Mortal Flame).
        var h = new Harness();
        h.Damage.CollectCandidates(h.Context, h.Scheduler, isMoving: false);
        Assert.NotEmpty(h.Scheduler.InspectOgcdQueue()); // pre-Bristle: weaves flow

        h.Scheduler.InspectGcdQueue().First(c => c.Behavior == ProteusAbilities.Bristle).OnDispatched!(h.Context);
        h.Scheduler.Reset();
        h.Damage.CollectCandidates(h.Context, h.Scheduler, isMoving: false); // HasBoost still false — cast in flight

        Assert.Empty(h.Scheduler.InspectOgcdQueue());
    }

    [Fact]
    public void Damage_BoostHold_Releases_AfterDotLands()
    {
        var h = new Harness();
        Mock.Get(h.Context).Setup(x => x.HasBoost).Returns(true);
        h.Damage.CollectCandidates(h.Context, h.Scheduler, isMoving: false);
        h.Scheduler.InspectGcdQueue().First(c => c.Behavior == ProteusAbilities.BreathOfMagic).OnDispatched!(h.Context);
        h.Scheduler.InspectGcdQueue().First(c => c.Behavior == ProteusAbilities.MortalFlame).OnDispatched!(h.Context);

        Mock.Get(h.Context).Setup(x => x.HasBoost).Returns(false); // consumed by the DoT
        h.Scheduler.Reset();
        h.Damage.CollectCandidates(h.Context, h.Scheduler, isMoving: false);

        Assert.Contains(h.Scheduler.InspectGcdQueue(), c => c.Behavior == ProteusAbilities.SonicBoom);
        Assert.NotEmpty(h.Scheduler.InspectOgcdQueue());
    }

    [Fact]
    public void Damage_MortalFlame_OnceLatch_NoSecondPush()
    {
        var h = new Harness();
        h.Damage.CollectCandidates(h.Context, h.Scheduler, isMoving: false);
        var mf = h.Scheduler.InspectGcdQueue().First(c => c.Behavior == ProteusAbilities.MortalFlame);

        mf.OnDispatched!(h.Context); // simulate the cast landing

        h.Scheduler.Reset();
        h.Damage.CollectCandidates(h.Context, h.Scheduler, isMoving: false);
        Assert.DoesNotContain(h.Scheduler.InspectGcdQueue(), c => c.Behavior == ProteusAbilities.MortalFlame);
    }

    [Fact]
    public void Damage_MortalFlame_LatchExpires_AfterRetryWindow()
    {
        var h = new Harness();
        var now = DateTime.UtcNow;
        h.Damage.UtcNow = () => now;

        h.Damage.CollectCandidates(h.Context, h.Scheduler, isMoving: false);
        h.Scheduler.InspectGcdQueue().First(c => c.Behavior == ProteusAbilities.MortalFlame).OnDispatched!(h.Context);

        h.Damage.UtcNow = () => now.AddSeconds(6); // past the 5s latency/retry window
        h.Scheduler.Reset();
        h.Damage.CollectCandidates(h.Context, h.Scheduler, isMoving: false);
        Assert.Contains(h.Scheduler.InspectGcdQueue(), c => c.Behavior == ProteusAbilities.MortalFlame);
    }

    // ── Cone facing (first-field-run fixes) ─────────────────────────────────

    [Fact]
    public void Damage_BreathOfMagic_HeldWhenNotFacingTarget()
    {
        // Self-anchored cone: dispatched on self, the game never auto-faces — firing while
        // faced away missed 12 straight casts in the field. Rotation 0 = facing +Z; enemy at -Z.
        var h = new Harness();
        h.Enemy.Setup(x => x.Position).Returns(new System.Numerics.Vector3(0f, 0f, -10f));
        h.Damage.CollectCandidates(h.Context, h.Scheduler, isMoving: false);

        var gcd = h.Scheduler.InspectGcdQueue();
        Assert.DoesNotContain(gcd, c => c.Behavior == ProteusAbilities.BreathOfMagic);
        Assert.Contains(gcd, c => c.Behavior == ProteusAbilities.SonicBoom); // targeted casts still flow
        Assert.Contains("turning to face", h.Context.Debug.DamageState);
        // The nudge must actively rotate us — the self-cast cone never triggers passive recovery.
        h.ActionService.Verify(x => x.NotifyFacingRejection(4242UL), Moq.Times.AtLeastOnce());
    }

    [Fact]
    public void Damage_BreathOfMagic_LatchPreventsChainCast()
    {
        // Fuse behind the facing gate: one cast per target per 10s window no matter what the
        // status read claims.
        var h = new Harness();
        h.Damage.CollectCandidates(h.Context, h.Scheduler, isMoving: false);
        h.Scheduler.InspectGcdQueue().First(c => c.Behavior == ProteusAbilities.BreathOfMagic).OnDispatched!(h.Context);

        h.Scheduler.Reset();
        h.Damage.CollectCandidates(h.Context, h.Scheduler, isMoving: false);
        Assert.DoesNotContain(h.Scheduler.InspectGcdQueue(), c => c.Behavior == ProteusAbilities.BreathOfMagic);
    }

    [Fact]
    public void Damage_BadBreath_HeldWhenNotFacingTarget()
    {
        var h = new Harness(packCount: 3);
        h.Config.BlueMage.EnableFreezeShatter = false;
        h.Enemy.Setup(x => x.Position).Returns(new System.Numerics.Vector3(0f, 0f, -10f));
        h.Damage.CollectCandidates(h.Context, h.Scheduler, isMoving: false);
        Assert.DoesNotContain(h.Scheduler.InspectGcdQueue(), c => c.Behavior == ProteusAbilities.BadBreath);
    }

    [Fact]
    public void Damage_ColdFog_HeldWhenPackDyingSoon()
    {
        // 90s cooldown for 15s of White Death — pointless on a melting pack (field run burned
        // it 2s before combat end).
        var h = new Harness();
        h.Targeting.Setup(x => x.FindNearestAggroedEnemy(It.IsAny<float>(), It.IsAny<IPlayerCharacter>()))
            .Returns(h.Enemy.Object);
        var now = DateTime.UtcNow;
        long packHp = 100_000;
        h.Damage.UtcNow = () => now;
        h.Targeting.Setup(x => x.SumEnemyCurrentHpInRange(It.IsAny<float>(), It.IsAny<IPlayerCharacter>()))
            .Returns(() => packHp);

        h.Damage.CollectCandidates(h.Context, h.Scheduler, isMoving: false);
        Assert.Contains(h.Scheduler.InspectGcdQueue(), c => c.Behavior == ProteusAbilities.ColdFog);

        packHp = 20_000; // melting: ~2s TTK
        now = now.AddSeconds(3);
        h.Scheduler.Reset();
        h.Damage.CollectCandidates(h.Context, h.Scheduler, isMoving: false);
        Assert.DoesNotContain(h.Scheduler.InspectGcdQueue(), c => c.Behavior == ProteusAbilities.ColdFog);
    }

    // ── Cold Fog / White Death ──────────────────────────────────────────────

    [Fact]
    public void Damage_WhiteDeath_PushedWhileTouchOfFrost_EvenMoving()
    {
        var h = new Harness();
        Mock.Get(h.Context).Setup(x => x.HasTouchOfFrost).Returns(true);
        h.Damage.CollectCandidates(h.Context, h.Scheduler, isMoving: true);
        Assert.Contains(h.Scheduler.InspectGcdQueue(), c => c.Behavior == ProteusAbilities.WhiteDeath);
    }

    [Fact]
    public void Damage_WhiteDeath_NotPushed_WithoutTouchOfFrost()
    {
        var h = new Harness();
        h.Damage.CollectCandidates(h.Context, h.Scheduler, isMoving: false);
        Assert.DoesNotContain(h.Scheduler.InspectGcdQueue(), c => c.Behavior == ProteusAbilities.WhiteDeath);
    }

    [Fact]
    public void Damage_ColdFog_OnlyWhenAggroedEnemyClose()
    {
        var h = new Harness();
        h.Damage.CollectCandidates(h.Context, h.Scheduler, isMoving: false);
        Assert.DoesNotContain(h.Scheduler.InspectGcdQueue(), c => c.Behavior == ProteusAbilities.ColdFog);

        h.Targeting.Setup(x => x.FindNearestAggroedEnemy(It.IsAny<float>(), It.IsAny<IPlayerCharacter>()))
            .Returns(h.Enemy.Object);
        h.Scheduler.Reset();
        h.Damage.CollectCandidates(h.Context, h.Scheduler, isMoving: false);
        Assert.Contains(h.Scheduler.InspectGcdQueue(), c => c.Behavior == ProteusAbilities.ColdFog);
    }

    // ── Nukes ───────────────────────────────────────────────────────────────

    [Fact]
    public void Damage_MatraMagic_PushedOffCooldown_HeldOnCooldown()
    {
        var h = new Harness();
        h.Damage.CollectCandidates(h.Context, h.Scheduler, isMoving: false);
        Assert.Contains(h.Scheduler.InspectGcdQueue(), c => c.Behavior == ProteusAbilities.MatraMagic);

        h.ActionService.Setup(x => x.GetCooldownRemaining(BLUActions.MatraMagic.ActionId)).Returns(60f);
        h.Scheduler.Reset();
        h.Damage.CollectCandidates(h.Context, h.Scheduler, isMoving: false);
        Assert.DoesNotContain(h.Scheduler.InspectGcdQueue(), c => c.Behavior == ProteusAbilities.MatraMagic);
    }

    // ── oGCD weaves ─────────────────────────────────────────────────────────

    [Fact]
    public void Damage_OffensiveOgcds_Pushed()
    {
        var h = new Harness();
        h.Damage.CollectCandidates(h.Context, h.Scheduler, isMoving: false);

        var ogcd = h.Scheduler.InspectOgcdQueue();
        Assert.Contains(ogcd, c => c.Behavior == ProteusAbilities.FeatherRain);
        Assert.Contains(ogcd, c => c.Behavior == ProteusAbilities.GlassDance);
        Assert.Contains(ogcd, c => c.Behavior == ProteusAbilities.BothEnds);
    }

    [Fact]
    public void Damage_Surpanakha_OnlyStartsAtFourCharges()
    {
        var h = new Harness();
        h.ActionService.Setup(x => x.GetCurrentCharges(BLUActions.Surpanakha.ActionId)).Returns(2u);
        h.Damage.CollectCandidates(h.Context, h.Scheduler, isMoving: false);
        Assert.DoesNotContain(h.Scheduler.InspectOgcdQueue(), c => c.Behavior == ProteusAbilities.Surpanakha);

        h.ActionService.Setup(x => x.GetCurrentCharges(BLUActions.Surpanakha.ActionId)).Returns(4u);
        h.Scheduler.Reset();
        h.Damage.CollectCandidates(h.Context, h.Scheduler, isMoving: false);
        Assert.Contains(h.Scheduler.InspectOgcdQueue(), c => c.Behavior == ProteusAbilities.Surpanakha);
    }

    [Fact]
    public void Damage_Surpanakha_NeverPressedAtGcdIdle()
    {
        // Idle press = pure clip: it delays the next GCD for 200p (run 7's combat-open press).
        var h = new Harness();
        h.ActionService.Setup(x => x.GcdRemaining).Returns(0f);
        h.ActionService.Setup(x => x.GetCurrentCharges(BLUActions.Surpanakha.ActionId)).Returns(4u);
        h.Damage.CollectCandidates(h.Context, h.Scheduler, isMoving: false);
        Assert.DoesNotContain(h.Scheduler.InspectOgcdQueue(), c => c.Behavior == ProteusAbilities.Surpanakha);
    }

    [Fact]
    public void Damage_SurpanakhaMidDump_SuppressesAllOtherWeaves()
    {
        // Fury stack live: the ONLY legal weave is the next Surpanakha — anything else drops it.
        var h = new Harness();
        Mock.Get(h.Context).Setup(x => x.HasSurpanakhasFury).Returns(true);
        h.ActionService.Setup(x => x.GetCurrentCharges(BLUActions.Surpanakha.ActionId)).Returns(1u);
        h.Damage.CollectCandidates(h.Context, h.Scheduler, isMoving: false);

        var ogcd = h.Scheduler.InspectOgcdQueue();
        Assert.Contains(ogcd, c => c.Behavior == ProteusAbilities.Surpanakha);
        Assert.DoesNotContain(ogcd, c => c.Behavior == ProteusAbilities.FeatherRain);
        Assert.DoesNotContain(ogcd, c => c.Behavior == ProteusAbilities.GlassDance);
        Assert.DoesNotContain(ogcd, c => c.Behavior == ProteusAbilities.BothEnds);
    }

    // ── Waning Nocturne lockout ─────────────────────────────────────────────

    [Fact]
    public void Damage_WaningNocturne_LocksEverything()
    {
        var h = new Harness();
        Mock.Get(h.Context).Setup(x => x.HasWaningNocturne).Returns(true);
        h.Damage.CollectCandidates(h.Context, h.Scheduler, isMoving: false);
        Assert.Empty(h.Scheduler.InspectGcdQueue());
        Assert.Empty(h.Scheduler.InspectOgcdQueue());
    }

    // ── Freeze→shatter (V2.5) ───────────────────────────────────────────────

    [Fact]
    public void FreezeShatter_StartsWindow_AtMinTargets()
    {
        var h = new Harness(packCount: 3);
        h.Damage.CollectCandidates(h.Context, h.Scheduler, isMoving: false);
        Assert.Contains(h.Scheduler.InspectGcdQueue(), c => c.Behavior == ProteusAbilities.TheRamsVoice);
    }

    [Fact]
    public void FreezeShatter_NoStart_BelowMinTargets()
    {
        var h = new Harness(packCount: 1);
        h.Damage.CollectCandidates(h.Context, h.Scheduler, isMoving: false);
        Assert.DoesNotContain(h.Scheduler.InspectGcdQueue(), c => c.Behavior == ProteusAbilities.TheRamsVoice);
    }

    [Fact]
    public void FreezeShatter_NoStart_WhenUltravibrationOnCooldown()
    {
        var h = new Harness(packCount: 3);
        h.ActionService.Setup(x => x.GetCooldownRemaining(BLUActions.Ultravibration.ActionId)).Returns(90f);
        h.Damage.CollectCandidates(h.Context, h.Scheduler, isMoving: false);
        Assert.DoesNotContain(h.Scheduler.InspectGcdQueue(), c => c.Behavior == ProteusAbilities.TheRamsVoice);
    }

    [Fact]
    public void FreezeShatter_FrozenEnemies_ShatterOnly_AllDamageSuppressed()
    {
        var h = new Harness(packCount: 3);
        h.Targeting.Setup(x => x.GetBestStatusRemainingOnAnyEnemy(
                It.IsAny<uint[]>(), It.IsAny<float>(), It.IsAny<IPlayerCharacter>()))
            .Returns(8f); // pack is frozen
        h.Damage.CollectCandidates(h.Context, h.Scheduler, isMoving: false);

        var gcd = h.Scheduler.InspectGcdQueue();
        Assert.Single(gcd);
        Assert.Equal(ProteusAbilities.Ultravibration, gcd[0].Behavior);
        Assert.Empty(h.Scheduler.InspectOgcdQueue()); // weaves would break the freeze too
    }

    [Fact]
    public void FreezeShatter_ArmedButFreezeNotLandedYet_HoldsAllDamage()
    {
        var h = new Harness(packCount: 3);
        h.Damage.CollectCandidates(h.Context, h.Scheduler, isMoving: false);
        h.Scheduler.InspectGcdQueue().First(c => c.Behavior == ProteusAbilities.TheRamsVoice).OnDispatched!(h.Context);

        // Next frame: freeze not applied yet (cast in flight) — nothing may break it.
        h.Scheduler.Reset();
        h.Damage.CollectCandidates(h.Context, h.Scheduler, isMoving: false);
        Assert.Empty(h.Scheduler.InspectGcdQueue());
        Assert.Empty(h.Scheduler.InspectOgcdQueue());
    }

    [Fact]
    public void FreezeShatter_NoFreezeLanded_MarksPackImmune_ResumesNormalChain()
    {
        var h = new Harness(packCount: 3);
        var now = DateTime.UtcNow;
        h.Damage.UtcNow = () => now;

        h.Damage.CollectCandidates(h.Context, h.Scheduler, isMoving: false);
        h.Scheduler.InspectGcdQueue().First(c => c.Behavior == ProteusAbilities.TheRamsVoice).OnDispatched!(h.Context);

        // 5s later, still no Deep Freeze anywhere → immune. Chain resumes, no Ram's Voice retry.
        h.Damage.UtcNow = () => now.AddSeconds(5);
        h.Scheduler.Reset();
        h.Damage.CollectCandidates(h.Context, h.Scheduler, isMoving: false);

        var gcd = h.Scheduler.InspectGcdQueue();
        Assert.DoesNotContain(gcd, c => c.Behavior == ProteusAbilities.TheRamsVoice);
        Assert.DoesNotContain(gcd, c => c.Behavior == ProteusAbilities.Ultravibration);
        Assert.Contains(gcd, c => c.Behavior == ProteusAbilities.SonicBoom);
        Assert.Contains("immune", h.Context.Debug.DamageState, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FreezeShatter_ImmuneMark_ResetsOnCombatEnd()
    {
        var h = new Harness(packCount: 3);
        var now = DateTime.UtcNow;
        h.Damage.UtcNow = () => now;

        h.Damage.CollectCandidates(h.Context, h.Scheduler, isMoving: false);
        h.Scheduler.InspectGcdQueue().First(c => c.Behavior == ProteusAbilities.TheRamsVoice).OnDispatched!(h.Context);
        h.Damage.UtcNow = () => now.AddSeconds(5);
        h.Scheduler.Reset();
        h.Damage.CollectCandidates(h.Context, h.Scheduler, isMoving: false); // marks immune

        Mock.Get(h.Context).Setup(x => x.InCombat).Returns(false);
        h.Scheduler.Reset();
        h.Damage.CollectCandidates(h.Context, h.Scheduler, isMoving: false); // combat end → reset

        Mock.Get(h.Context).Setup(x => x.InCombat).Returns(true);
        h.Scheduler.Reset();
        h.Damage.CollectCandidates(h.Context, h.Scheduler, isMoving: false);
        Assert.Contains(h.Scheduler.InspectGcdQueue(), c => c.Behavior == ProteusAbilities.TheRamsVoice);
    }

    [Fact]
    public void FreezeShatter_NotSlotted_NormalChainUnaffected()
    {
        var h = new Harness(packCount: 3);
        Mock.Get(h.Context).Setup(x => x.IsSpellUsable(BLUActions.Ultravibration.ActionId)).Returns(false);
        h.Damage.CollectCandidates(h.Context, h.Scheduler, isMoving: false);

        var gcd = h.Scheduler.InspectGcdQueue();
        Assert.DoesNotContain(gcd, c => c.Behavior == ProteusAbilities.TheRamsVoice);
        Assert.Contains(gcd, c => c.Behavior == ProteusAbilities.SonicBoom);
    }

    // ── Moon Flute window (V2.3) ────────────────────────────────────────────

    private static Harness FluteReadyHarness()
    {
        var h = new Harness();
        h.Config.BlueMage.EnableMoonFlute = true;
        h.ActionService.Setup(x => x.GetCurrentCharges(BLUActions.Surpanakha.ActionId)).Returns(4u);
        return h;
    }

    [Fact]
    public void MoonFlute_DefaultOff_NeverPushed()
    {
        var h = new Harness();
        h.ActionService.Setup(x => x.GetCurrentCharges(BLUActions.Surpanakha.ActionId)).Returns(4u);
        h.Damage.CollectCandidates(h.Context, h.Scheduler, isMoving: false);
        Assert.DoesNotContain(h.Scheduler.InspectGcdQueue(), c => c.Behavior == ProteusAbilities.MoonFlute);
    }

    [Fact]
    public void MoonFlute_PushedWhenAllSlottedPiecesReady()
    {
        var h = FluteReadyHarness();
        h.Damage.CollectCandidates(h.Context, h.Scheduler, isMoving: false);
        Assert.Contains(h.Scheduler.InspectGcdQueue(), c => c.Behavior == ProteusAbilities.MoonFlute);
    }

    [Fact]
    public void MoonFlute_Held_WhenSurpanakhaChargesMissing()
    {
        var h = FluteReadyHarness();
        h.ActionService.Setup(x => x.GetCurrentCharges(BLUActions.Surpanakha.ActionId)).Returns(3u);
        h.Damage.CollectCandidates(h.Context, h.Scheduler, isMoving: false);
        Assert.DoesNotContain(h.Scheduler.InspectGcdQueue(), c => c.Behavior == ProteusAbilities.MoonFlute);
    }

    [Fact]
    public void MoonFlute_Held_WhenBigCooldownRolling()
    {
        var h = FluteReadyHarness();
        h.ActionService.Setup(x => x.GetCooldownRemaining(BLUActions.MatraMagic.ActionId)).Returns(45f);
        h.Damage.CollectCandidates(h.Context, h.Scheduler, isMoving: false);
        Assert.DoesNotContain(h.Scheduler.InspectGcdQueue(), c => c.Behavior == ProteusAbilities.MoonFlute);
    }

    [Fact]
    public void MoonFlute_Held_WhenPackDiesBeforeTheWindowPays()
    {
        var h = FluteReadyHarness();
        var now = DateTime.UtcNow;
        long packHp = 100_000;
        h.Damage.UtcNow = () => now;
        h.Targeting.Setup(x => x.SumEnemyCurrentHpInRange(It.IsAny<float>(), It.IsAny<IPlayerCharacter>()))
            .Returns(() => packHp);

        h.Damage.CollectCandidates(h.Context, h.Scheduler, isMoving: false); // sample 1

        packHp = 40_000; // melting: 20k HP/s → ~2s TTK, far below the 30s default
        now = now.AddSeconds(3);
        h.Scheduler.Reset();
        h.Damage.CollectCandidates(h.Context, h.Scheduler, isMoving: false); // sample 2 + decision

        Assert.DoesNotContain(h.Scheduler.InspectGcdQueue(), c => c.Behavior == ProteusAbilities.MoonFlute);
    }

    [Fact]
    public void MoonFlute_NotRepushed_DuringWaxing_ChainKeepsRunning()
    {
        var h = FluteReadyHarness();
        Mock.Get(h.Context).Setup(x => x.HasWaxingNocturne).Returns(true);
        h.Damage.CollectCandidates(h.Context, h.Scheduler, isMoving: false);

        var gcd = h.Scheduler.InspectGcdQueue();
        Assert.DoesNotContain(gcd, c => c.Behavior == ProteusAbilities.MoonFlute);
        Assert.Contains(gcd, c => c.Behavior == ProteusAbilities.SonicBoom); // buffed chain continues
    }

    [Fact]
    public void MoonFlute_NakedKit_NotWorthTheWaning()
    {
        var h = FluteReadyHarness();
        foreach (var id in new[]
                 {
                     BLUActions.MatraMagic.ActionId, BLUActions.TheRoseOfDestruction.ActionId,
                     BLUActions.BothEnds.ActionId, BLUActions.GlassDance.ActionId,
                     BLUActions.FeatherRain.ActionId, BLUActions.Surpanakha.ActionId,
                 })
        {
            Mock.Get(h.Context).Setup(x => x.IsSpellUsable(id)).Returns(false);
        }

        h.Damage.CollectCandidates(h.Context, h.Scheduler, isMoving: false);
        Assert.DoesNotContain(h.Scheduler.InspectGcdQueue(), c => c.Behavior == ProteusAbilities.MoonFlute);
    }

    [Fact]
    public void Waxing_MortalFlame_BuffedRecastExactlyOnce()
    {
        var h = new Harness();

        // Unbuffed cast latches the target.
        h.Damage.CollectCandidates(h.Context, h.Scheduler, isMoving: false);
        h.Scheduler.InspectGcdQueue().First(c => c.Behavior == ProteusAbilities.MortalFlame).OnDispatched!(h.Context);

        // Waxing: the unbuffed snapshot is deliberately replaced — one buffed recast allowed.
        Mock.Get(h.Context).Setup(x => x.HasWaxingNocturne).Returns(true);
        h.Scheduler.Reset();
        h.Damage.CollectCandidates(h.Context, h.Scheduler, isMoving: false);
        var buffed = h.Scheduler.InspectGcdQueue().First(c => c.Behavior == ProteusAbilities.MortalFlame);
        buffed.OnDispatched!(h.Context);

        // Still Waxing: the buffed snapshot is never touched again.
        h.Scheduler.Reset();
        h.Damage.CollectCandidates(h.Context, h.Scheduler, isMoving: false);
        Assert.DoesNotContain(h.Scheduler.InspectGcdQueue(), c => c.Behavior == ProteusAbilities.MortalFlame);
    }

    [Fact]
    public void Waxing_ColdFog_Suppressed()
    {
        var h = new Harness();
        h.Targeting.Setup(x => x.FindNearestAggroedEnemy(It.IsAny<float>(), It.IsAny<IPlayerCharacter>()))
            .Returns(h.Enemy.Object);
        Mock.Get(h.Context).Setup(x => x.HasWaxingNocturne).Returns(true);
        h.Damage.CollectCandidates(h.Context, h.Scheduler, isMoving: false);
        Assert.DoesNotContain(h.Scheduler.InspectGcdQueue(), c => c.Behavior == ProteusAbilities.ColdFog);
    }

    // ── Missile cheese ──────────────────────────────────────────────────────

    [Fact]
    public void Damage_Missile_ProbesUnknownBosses_AndRespectsImmuneVerdict()
    {
        // Unknown (no ledger data) → probe fires.
        var h = new Harness();
        h.Damage.CollectCandidates(h.Context, h.Scheduler, isMoving: false);
        Assert.Contains(h.Scheduler.InspectGcdQueue(), c => c.Behavior == ProteusAbilities.Missile);

        // Ledger says Immune → never wasted again.
        var ledger = new Mock<Daedalus.Services.Blu.IDeathImmunityLedger>();
        ledger.Setup(x => x.GetVerdict(It.IsAny<uint>()))
            .Returns(Daedalus.Services.Blu.DeathImmunityVerdict.Immune);
        Mock.Get(h.Context).Setup(x => x.DeathLedger).Returns(ledger.Object);
        h.Scheduler.Reset();
        h.Damage.CollectCandidates(h.Context, h.Scheduler, isMoving: false);
        Assert.DoesNotContain(h.Scheduler.InspectGcdQueue(), c => c.Behavior == ProteusAbilities.Missile);
    }

    [Fact]
    public void Damage_Missile_StopsAtHpFloor_AndIgnoresTrash()
    {
        // Below the HP floor the normal rotation finishes faster.
        var low = new Harness();
        low.Enemy.Setup(x => x.CurrentHp).Returns(20_000u); // 20% < default 30% floor
        low.Damage.CollectCandidates(low.Context, low.Scheduler, isMoving: false);
        Assert.DoesNotContain(low.Scheduler.InspectGcdQueue(), c => c.Behavior == ProteusAbilities.Missile);

        // Small max-HP targets (trash) never qualify.
        var trash = new Harness();
        trash.Enemy.Setup(x => x.MaxHp).Returns(15_000u);
        trash.Enemy.Setup(x => x.CurrentHp).Returns(15_000u);
        trash.Damage.CollectCandidates(trash.Context, trash.Scheduler, isMoving: false);
        Assert.DoesNotContain(trash.Scheduler.InspectGcdQueue(), c => c.Behavior == ProteusAbilities.Missile);
    }

    // ── Bad Breath ──────────────────────────────────────────────────────────

    [Fact]
    public void Damage_BadBreath_OncePerPack_ViaMalodorousOnTarget()
    {
        // Freeze→shatter would swallow the pack first — isolate Bad Breath by disabling it.
        var h = new Harness(packCount: 3);
        h.Config.BlueMage.EnableFreezeShatter = false;
        h.Damage.CollectCandidates(h.Context, h.Scheduler, isMoving: false);
        Assert.Contains(h.Scheduler.InspectGcdQueue(), c => c.Behavior == ProteusAbilities.BadBreath);
        // The once-marker is Malodorous on the target — status reads on mocks are empty, so the
        // positive path is covered here and the suppression path is the live-status read.
    }

    // ── Manual mimicry (BLU Mimicry window) ─────────────────────────────────

    [Fact]
    public void Mimicry_ManualRequest_BypassesAutoToggleOff()
    {
        Daedalus.Rotation.ProteusCore.Helpers.BluMimicryCommand.Clear();
        Daedalus.Rotation.ProteusCore.Helpers.BluMimicryCommand.ClearSuppression();
        try
        {
            var h = new Harness();
            h.Config.BlueMage.EnableMimicry = false; // auto OFF — buttons must still work
            Mock.Get(h.Context).Setup(x => x.HasCorrectMimicry).Returns(false);
            Daedalus.Rotation.ProteusCore.Helpers.BluMimicryCommand.Request(BluRole.Tank);

            h.Buff.CollectCandidates(h.Context, h.Scheduler, isMoving: false);

            // No Tank in the mocked area — the request must have driven a TANK scan (not the
            // config role), and the in-duty auto gate must not have eaten it.
            Assert.Contains("Tank", h.Context.Debug.MimicryState);
            Assert.DoesNotContain("BEFORE queuing", h.Context.Debug.MimicryState);
        }
        finally
        {
            Daedalus.Rotation.ProteusCore.Helpers.BluMimicryCommand.Clear();
        }
    }

    [Fact]
    public void Mimicry_AutoSuppressed_AfterManualRemoval()
    {
        Daedalus.Rotation.ProteusCore.Helpers.BluMimicryCommand.Clear();
        Daedalus.Rotation.ProteusCore.Helpers.BluMimicryCommand.SuppressAuto(15);
        try
        {
            var h = new Harness();
            Mock.Get(h.Context).Setup(x => x.HasCorrectMimicry).Returns(false);

            h.Buff.CollectCandidates(h.Context, h.Scheduler, isMoving: false);

            Assert.DoesNotContain(h.Scheduler.InspectGcdQueue(), c => c.Behavior == ProteusAbilities.AethericMimicry);
            Assert.Contains("paused", h.Context.Debug.MimicryState);
        }
        finally
        {
            Daedalus.Rotation.ProteusCore.Helpers.BluMimicryCommand.ClearSuppression();
        }
    }

    // ── Loadout apply handshake ─────────────────────────────────────────────

    [Fact]
    public void Buff_MimicryHeld_WhileLoadoutApplyPending()
    {
        // The apply deliberately cancels mimicry (game refuses set changes while it's up);
        // recasting mid-handshake would deadlock it.
        var h = new Harness();
        Mock.Get(h.Context).Setup(x => x.HasCorrectMimicry).Returns(false);
        var svc = new Mock<Daedalus.Services.Action.IBluLoadoutService>();
        svc.Setup(x => x.IsApplyPending).Returns(true);
        Mock.Get(h.Context).Setup(x => x.LoadoutService).Returns(svc.Object);

        h.Buff.CollectCandidates(h.Context, h.Scheduler, isMoving: false);

        Assert.DoesNotContain(h.Scheduler.InspectGcdQueue(), c => c.Behavior == ProteusAbilities.AethericMimicry);
        Assert.Equal("Holding (loadout apply in progress)", h.Context.Debug.MimicryState);
    }

    // ── Buff module: Basic Instinct / Toad Oil ──────────────────────────────

    [Fact]
    public void Buff_BasicInstinct_PushedWhenSolo()
    {
        var h = new Harness();
        h.Buff.CollectCandidates(h.Context, h.Scheduler, isMoving: false);
        Assert.Contains(h.Scheduler.InspectGcdQueue(), c => c.Behavior == ProteusAbilities.BasicInstinct);
    }

    [Fact]
    public void Buff_BasicInstinct_NotWhenPartyPresent_AndSaysWhy()
    {
        var h = new Harness(partySize: 4);
        h.Buff.CollectCandidates(h.Context, h.Scheduler, isMoving: false);
        Assert.DoesNotContain(h.Scheduler.InspectGcdQueue(), c => c.Behavior == ProteusAbilities.BasicInstinct);
        Assert.Contains("party present", h.Context.Debug.BuffState);
    }

    [Fact]
    public void Buff_BasicInstinct_NotWhenBuffActive()
    {
        var h = new Harness();
        Mock.Get(h.Context).Setup(x => x.HasBasicInstinctBuff).Returns(true);
        h.Buff.CollectCandidates(h.Context, h.Scheduler, isMoving: false);
        Assert.DoesNotContain(h.Scheduler.InspectGcdQueue(), c => c.Behavior == ProteusAbilities.BasicInstinct);
    }

    [Fact]
    public void Buff_ToadOil_TankRoleAndSolo_NotForPartyDps()
    {
        var tank = new Harness(role: BluRole.Tank, partySize: 4);
        tank.Buff.CollectCandidates(tank.Context, tank.Scheduler, isMoving: false);
        Assert.Contains(tank.Scheduler.InspectGcdQueue(), c => c.Behavior == ProteusAbilities.ToadOil);

        var soloDps = new Harness(role: BluRole.Dps, partySize: 0);
        soloDps.Buff.CollectCandidates(soloDps.Context, soloDps.Scheduler, isMoving: false);
        Assert.Contains(soloDps.Scheduler.InspectGcdQueue(), c => c.Behavior == ProteusAbilities.ToadOil);

        var partyDps = new Harness(role: BluRole.Dps, partySize: 4);
        partyDps.Buff.CollectCandidates(partyDps.Context, partyDps.Scheduler, isMoving: false);
        Assert.DoesNotContain(partyDps.Scheduler.InspectGcdQueue(), c => c.Behavior == ProteusAbilities.ToadOil);
    }

    // ── Solo role ───────────────────────────────────────────────────────────

    [Fact]
    public void Solo_MightyGuard_WaitsForBasicInstinct()
    {
        // MG before BI would cut damage -40% for nothing; BI's +100% cancels the penalty.
        var h = new Harness(role: BluRole.Solo);
        h.Buff.CollectCandidates(h.Context, h.Scheduler, isMoving: false);
        var gcd = h.Scheduler.InspectGcdQueue();
        Assert.Contains(gcd, c => c.Behavior == ProteusAbilities.BasicInstinct);
        Assert.DoesNotContain(gcd, c => c.Behavior == ProteusAbilities.MightyGuard);

        var h2 = new Harness(role: BluRole.Solo);
        Mock.Get(h2.Context).Setup(x => x.HasBasicInstinctBuff).Returns(true);
        h2.Buff.CollectCandidates(h2.Context, h2.Scheduler, isMoving: false);
        Assert.Contains(h2.Scheduler.InspectGcdQueue(), c => c.Behavior == ProteusAbilities.MightyGuard);
    }

    [Fact]
    public void Solo_FinalSting_OnlyWithinItsWindow()
    {
        // Default OFF — never fires even in a perfect window.
        var off = new Harness(role: BluRole.Solo);
        off.Enemy.Setup(x => x.CurrentHp).Returns(20_000u); // 20%
        off.Damage.CollectCandidates(off.Context, off.Scheduler, isMoving: false);
        Assert.DoesNotContain(off.Scheduler.InspectGcdQueue(), c => c.Behavior == ProteusAbilities.FinalSting);

        // Enabled + Solo + last enemy at 20% → fires.
        var on = new Harness(role: BluRole.Solo);
        on.Config.BlueMage.EnableFinalSting = true;
        on.Enemy.Setup(x => x.CurrentHp).Returns(20_000u);
        on.Damage.CollectCandidates(on.Context, on.Scheduler, isMoving: false);
        Assert.Contains(on.Scheduler.InspectGcdQueue(), c => c.Behavior == ProteusAbilities.FinalSting);

        // Enabled but target healthy → held.
        var healthy = new Harness(role: BluRole.Solo);
        healthy.Config.BlueMage.EnableFinalSting = true;
        healthy.Damage.CollectCandidates(healthy.Context, healthy.Scheduler, isMoving: false);
        Assert.DoesNotContain(healthy.Scheduler.InspectGcdQueue(), c => c.Behavior == ProteusAbilities.FinalSting);

        // Enabled but not Solo role → never (you don't suicide in a party).
        var dps = new Harness(role: BluRole.Dps);
        dps.Config.BlueMage.EnableFinalSting = true;
        dps.Enemy.Setup(x => x.CurrentHp).Returns(20_000u);
        dps.Damage.CollectCandidates(dps.Context, dps.Scheduler, isMoving: false);
        Assert.DoesNotContain(dps.Scheduler.InspectGcdQueue(), c => c.Behavior == ProteusAbilities.FinalSting);
    }

    [Fact]
    public void Solo_WhiteWindSelfSustain_AndDiamondbackPanic()
    {
        var h = new Harness(role: BluRole.Solo, playerHpPercent: 40);
        h.Healing.CollectCandidates(h.Context, h.Scheduler, isMoving: false);
        Assert.Contains(h.Scheduler.InspectGcdQueue(), c => c.Behavior == ProteusAbilities.WhiteWind);

        h.Scheduler.Reset();
        h.Mitigation.CollectCandidates(h.Context, h.Scheduler, isMoving: false);
        Assert.Contains(h.Scheduler.InspectGcdQueue(), c => c.Behavior == ProteusAbilities.Diamondback);
    }

    [Fact]
    public void Solo_GoblinPunchFiller_AndLoadoutMapping()
    {
        var h = new Harness(role: BluRole.Solo);
        h.Damage.CollectCandidates(h.Context, h.Scheduler, isMoving: false);
        Assert.Contains(h.Scheduler.InspectGcdQueue(), c => c.Behavior == ProteusAbilities.GoblinPunch);

        var solo = Daedalus.Services.Action.BluLoadoutComposer.ForRole(BluRole.Solo);
        Assert.Equal("Solo", solo.Name);
        Assert.Equal(24, solo.Core.Length); // exactly the 24 slots
        Assert.Contains(BLUActions.BasicInstinct.ActionId, solo.Core);
        Assert.Contains(BLUActions.MightyGuard.ActionId, solo.Core);
        Assert.Contains(BLUActions.WhiteWind.ActionId, solo.Core);
        Assert.Contains(BLUActions.FinalSting.ActionId, solo.Core);
        Assert.DoesNotContain(18316u, solo.Core); // Revenge Blast swapped out
        Assert.DoesNotContain(11411u, solo.Core); // Off-guard swapped out
    }

    // ── Healing module: Pom Cure / Gobskin / Exuviation ─────────────────────

    [Fact]
    public void Heal_PomCure_RequiresHealerMimicry()
    {
        // 100p without the mimicry — the module must refuse even with an injured ally.
        var h = new Harness(role: BluRole.Healer, playerHpPercent: 40);
        h.Healing.CollectCandidates(h.Context, h.Scheduler, isMoving: false);
        Assert.DoesNotContain(h.Scheduler.InspectGcdQueue(), c => c.Behavior == ProteusAbilities.PomCure);
    }

    [Fact]
    public void Heal_PomCure_PushedOnInjuredAlly_WithMimicry()
    {
        var h = new Harness(role: BluRole.Healer, playerHpPercent: 40);
        Mock.Get(h.Context).Setup(x => x.HasHealerMimicry).Returns(true);
        h.Healing.CollectCandidates(h.Context, h.Scheduler, isMoving: false);
        Assert.Contains(h.Scheduler.InspectGcdQueue(), c => c.Behavior == ProteusAbilities.PomCure);
    }

    [Fact]
    public void Heal_Gobskin_RefreshedWhenMissingAndPartyInjured()
    {
        var h = new Harness(role: BluRole.Healer);
        Mock.Get(h.Context).Setup(x => x.PartyHealthMetrics).Returns((0.8f, 0.7f, 2));
        h.Healing.CollectCandidates(h.Context, h.Scheduler, isMoving: false);
        Assert.Contains(h.Scheduler.InspectGcdQueue(), c => c.Behavior == ProteusAbilities.Gobskin);

        Mock.Get(h.Context).Setup(x => x.HasGobskin).Returns(true);
        h.Scheduler.Reset();
        h.Healing.CollectCandidates(h.Context, h.Scheduler, isMoving: false);
        Assert.DoesNotContain(h.Scheduler.InspectGcdQueue(), c => c.Behavior == ProteusAbilities.Gobskin);
    }

    [Fact]
    public void Heal_Exuviation_PushedWhenNearbyPartyMemberDispellable()
    {
        var h = new Harness(role: BluRole.Healer);
        Mock.Get(h.Context).Setup(x => x.DebuffDetectionService).Returns(
            MockBuilders.CreateMockDebuffDetectionService(
                _ => (1234u, Daedalus.Services.Debuff.DebuffPriority.High, 10f)).Object);
        h.Healing.CollectCandidates(h.Context, h.Scheduler, isMoving: false);
        Assert.Contains(h.Scheduler.InspectGcdQueue(), c => c.Behavior == ProteusAbilities.Exuviation);
    }

    [Fact]
    public void Heal_HealerKit_InactiveOutsideHealerRole()
    {
        var h = new Harness(role: BluRole.Dps, playerHpPercent: 40);
        Mock.Get(h.Context).Setup(x => x.HasHealerMimicry).Returns(true);
        Mock.Get(h.Context).Setup(x => x.DebuffDetectionService).Returns(
            MockBuilders.CreateMockDebuffDetectionService(
                _ => (1234u, Daedalus.Services.Debuff.DebuffPriority.High, 10f)).Object);
        h.Healing.CollectCandidates(h.Context, h.Scheduler, isMoving: false);

        var gcd = h.Scheduler.InspectGcdQueue();
        Assert.DoesNotContain(gcd, c => c.Behavior == ProteusAbilities.PomCure);
        Assert.DoesNotContain(gcd, c => c.Behavior == ProteusAbilities.Exuviation);
        Assert.DoesNotContain(gcd, c => c.Behavior == ProteusAbilities.Gobskin);
    }

    // ── Harness ─────────────────────────────────────────────────────────────

    private sealed class Harness
    {
        public IProteusContext Context { get; }
        public Daedalus.Rotation.Common.Scheduling.RotationScheduler Scheduler { get; }
        public DamageModule Damage { get; } = new();
        public BuffModule Buff { get; } = new();
        public HealingModule Healing { get; } = new();
        public MitigationModule Mitigation { get; } = new();
        public Mock<ITargetingService> Targeting { get; }
        public Mock<Daedalus.Services.Action.IActionService> ActionService { get; }
        public Mock<IBattleNpc> Enemy { get; }
        public Configuration Config { get; }

        public Harness(BluRole role = BluRole.Dps, int packCount = 1, int partySize = 0, int playerHpPercent = 100)
        {
            Config = new Configuration();
            Config.BlueMage.Role = role;

            Enemy = new Mock<IBattleNpc>();
            Enemy.Setup(x => x.GameObjectId).Returns(4242UL);
            Enemy.Setup(x => x.CurrentHp).Returns(100000u);
            Enemy.Setup(x => x.MaxHp).Returns(100000u);

            Targeting = MockBuilders.CreateMockTargetingService();
            Targeting.Setup(x => x.IsDamageTargetingPaused()).Returns(false);
            Targeting.Setup(x => x.FindEnemy(
                    It.IsAny<EnemyTargetingStrategy>(), It.IsAny<float>(), It.IsAny<IPlayerCharacter>()))
                .Returns(Enemy.Object);
            Targeting.Setup(x => x.FindEnemyNeedingDot(
                    It.IsAny<uint>(), It.IsAny<float>(), It.IsAny<float>(), It.IsAny<IPlayerCharacter>()))
                .Returns((IBattleNpc?)null);
            Targeting.Setup(x => x.CountEnemiesInRange(It.IsAny<float>(), It.IsAny<IPlayerCharacter>()))
                .Returns(packCount);

            ActionService = MockBuilders.CreateMockActionService();
            ActionService.Setup(x => x.IsActionLearned(It.IsAny<uint>())).Returns(true);
            ActionService.Setup(x => x.GcdRemaining).Returns(1.2f); // mid-roll: weave slots open

            var hp = (uint)(100000L * playerHpPercent / 100);
            var player = MockBuilders.CreateMockPlayerCharacter(level: 80, currentHp: hp, maxHp: 100000);

            var objectTable = MockBuilders.CreateMockObjectTable();
            var partyList = MockBuilders.CreateMockPartyList(length: partySize);

            var mock = new Mock<IProteusContext>();
            mock.Setup(x => x.Player).Returns(player.Object);
            mock.Setup(x => x.InCombat).Returns(true);
            mock.Setup(x => x.Configuration).Returns(Config);
            mock.Setup(x => x.ActionService).Returns(ActionService.Object);
            mock.Setup(x => x.TargetingService).Returns(Targeting.Object);
            mock.Setup(x => x.Role).Returns(role);
            mock.Setup(x => x.HasCorrectMimicry).Returns(true);
            mock.Setup(x => x.IsSpellUsable(It.IsAny<uint>())).Returns(true);
            mock.Setup(x => x.CurrentMp).Returns(10000);
            mock.Setup(x => x.PartyHealthMetrics).Returns((1f, 1f, 0));
            mock.Setup(x => x.Debug).Returns(new ProteusDebugState());
            mock.Setup(x => x.PartyList).Returns(partyList.Object);
            mock.Setup(x => x.ObjectTable).Returns(objectTable.Object);
            mock.Setup(x => x.PartyHelper).Returns(new CasterPartyHelper(objectTable.Object, partyList.Object));
            mock.Setup(x => x.DebuffDetectionService).Returns(MockBuilders.CreateMockDebuffDetectionService().Object);

            Context = mock.Object;
            Scheduler = SchedulerFactory.CreateForTest(actionService: ActionService, config: Config);
        }
    }
}
