using Daedalus.Config;
using Daedalus.Rotation.Phantom;
using System;
using System.IO;
using Daedalus.Data;
using Xunit;

namespace Daedalus.Tests.Rotation.Phantom;

/// <summary>
/// Tests for the variant duty-action band rules (docs/variant-actions-plan.md Phase 2),
/// especially the raise policy from the 2026-07-25 party-comp discussion.
/// </summary>
public class VariantBandRulesTests
{
    private static VariantConfig Cfg() => new();

    [Fact]
    public void Cure_FiresBelowConfiguredThreshold()
    {
        Assert.True(VariantBandRules.ShouldCure(Cfg(), 0.50f));   // default 0.60
        Assert.False(VariantBandRules.ShouldCure(Cfg(), 0.70f));
    }

    [Fact]
    public void SpiritDart_IsDotMaintenance_NotOnCooldownSpam()
    {
        // DoT missing (0s) or about to fall off → reapply; healthy DoT → hold.
        Assert.True(VariantBandRules.ShouldMaintainDart(Cfg(), 0f, float.MaxValue));
        Assert.True(VariantBandRules.ShouldMaintainDart(Cfg(), 2f, float.MaxValue));
        Assert.False(VariantBandRules.ShouldMaintainDart(Cfg(), 25f, float.MaxValue));

        var off = Cfg();
        off.UseSpiritDart = false;
        Assert.False(VariantBandRules.ShouldMaintainDart(off, 0f, float.MaxValue));
    }

    [Fact]
    public void SpiritDart_TtkGate_SkipsDyingTargets_FailsOpenWhenUnknown()
    {
        // Mob dying in 4s: the 30s DoT is a wasted weave.
        Assert.False(VariantBandRules.ShouldMaintainDart(Cfg(), 0f, targetTtkSeconds: 4f));
        // Healthy TTK → apply.
        Assert.True(VariantBandRules.ShouldMaintainDart(Cfg(), 0f, targetTtkSeconds: 30f));
        // Unknown TTK (MaxValue, e.g. fresh pull with no HP samples yet) → apply.
        Assert.True(VariantBandRules.ShouldMaintainDart(Cfg(), 0f, targetTtkSeconds: float.MaxValue));
    }

    [Fact]
    public void Rampart_PacedByItsBuff_UnlessSpamEnabled()
    {
        Assert.True(VariantBandRules.ShouldRampart(Cfg(), inCombat: true, buffActive: false));
        Assert.False(VariantBandRules.ShouldRampart(Cfg(), inCombat: true, buffActive: true));
        Assert.False(VariantBandRules.ShouldRampart(Cfg(), inCombat: false, buffActive: false));

        var spam = Cfg();
        spam.RampartSpamOnCooldown = true;
        Assert.True(VariantBandRules.ShouldRampart(spam, inCombat: true, buffActive: true));
    }

    // ── The raise policy (WAR/SAM/PCT + SGE comp): PCT raises the dead sage; while the
    //    sage lives, dead DPS are the sage's job — the PCT never burns 8s of casting. ──

    [Fact]
    public void Raise_DeadHealer_AlwaysRaised()
    {
        Assert.Equal(VariantRaiseDecision.RaiseHealer,
            VariantBandRules.DecideRaise(Cfg(), deadHealerPresent: true, deadOtherPresent: false, livingHealerPresent: false));
        // Even with another healer alive — a dead healer is always the priority pickup.
        Assert.Equal(VariantRaiseDecision.RaiseHealer,
            VariantBandRules.DecideRaise(Cfg(), deadHealerPresent: true, deadOtherPresent: true, livingHealerPresent: true));
    }

    /// <summary>
    /// The deferral itself is intact: with a healer up and a body down, the variant raise waits rather
    /// than burning eight seconds of DPS on something the healer will do for free.
    /// </summary>
    [Fact]
    public void Raise_DeadDps_LeftToTheLivingHealer()
    {
        Assert.Equal(VariantRaiseDecision.None,
            VariantBandRules.DecideRaise(Cfg(), deadHealerPresent: false, deadOtherPresent: true, livingHealerPresent: true));
    }

    /// <summary>
    /// ...but only for a while. Reported 2026-09-20: Variant Raise was never used. The deferral assumed
    /// the healer acts, so in any comp with a surviving healer — every normal run — livingHealerPresent
    /// stayed true forever and the variant raise never fired at all. The caller now drops the flag once
    /// the corpse has been down past the grace, exactly as the phantom layer already did.
    /// </summary>
    [Fact]
    public void Raise_DeadDps_VariantStepsInOnceTheHealerHasHadItsChance()
    {
        var deferred = VariantBandRules.DecideRaise(
            Cfg(), deadHealerPresent: false, deadOtherPresent: true, livingHealerPresent: true);
        var afterGrace = VariantBandRules.DecideRaise(
            Cfg(), deadHealerPresent: false, deadOtherPresent: true, livingHealerPresent: false);

        Assert.Equal(VariantRaiseDecision.None, deferred);
        Assert.Equal(VariantRaiseDecision.RaiseOther, afterGrace);
    }

    /// <summary>
    /// The grace has to outlast a healer's own attempt, or the variant raise races it and both are
    /// wasted; and it must be short enough that the body has not already released.
    /// </summary>
    [Fact]
    public void LivingHealerGrace_OutlastsAHardcastRaiseButNotTheCorpse()
    {
        Assert.True(VariantBandRules.LivingHealerGraceSeconds >= 8f,
            "shorter than an 8s hardcast raise and it races the healer");
        Assert.True(VariantBandRules.LivingHealerGraceSeconds <= 20f,
            "any longer and the corpse is released before the variant raise bothers");
    }

    /// <summary>Matches the phantom layer's grace — the same judgement, so the same number.</summary>
    [Fact]
    public void LivingHealerGrace_MatchesThePhantomLayer()
        => Assert.Equal(PhantomBandRules.LivingHealerGraceSeconds, VariantBandRules.LivingHealerGraceSeconds);

    /// <summary>
    /// The wiring, guarded at the source: the rule can only step in if the caller actually decays the
    /// flag, and the caller is inside a layer too game-coupled to instantiate here.
    /// </summary>
    [Fact]
    public void TheLayerDecaysTheLivingHealerFlagAfterTheGrace()
    {
        var source = File.ReadAllText(VariantLayerSourcePath());

        Assert.Matches(
            @"SecondsDown\(deadOther\.GameObjectId\)\s*>\s*VariantBandRules\.LivingHealerGraceSeconds",
            source);
        Assert.Contains("livingHealer = false;", source);
        // And it explains a deliberate wait, instead of reading as "nothing eligible".
        Assert.Contains("waiting on the healer", source);
    }


    // ── Raise only exists in one of the four duties ─────────────────────────────────────

    /// <summary>
    /// Raise carries ONE action id while Cure/Spirit Dart/Rampart carry three, and that is not because
    /// Raise is exclusive to one duty — it is the same action reused in every variant dungeon, which is
    /// why it has no numbered tiers (there is no "Variant Raise III"). Confirmed in-game 2026-09-20:
    /// Aloalo Island's picker offers Variant Raise, 8s cast, 30s recast.
    /// <para>
    /// Pinned because the shape invites the opposite reading — RSR has no per-tier VariantRaisePvE
    /// either, and I misread that as "Sil'dihn only" while chasing a report that a PCT never raised a
    /// dead SGE in Aloalo. Do not "fix" this by inventing per-tier ids; 29731 is the id everywhere.
    /// </para>
    /// </summary>
    [Fact]
    public void Raise_IsOneActionReusedAcrossEveryVariantDuty()
    {
        var raise = VariantActionData.Get(VariantAction.Raise);
        Assert.Single(raise.ActionIds);
        Assert.Equal(29731u, raise.ActionIds[0]);

        // Ultimatum is the same shape: one action, every duty.
        Assert.Single(VariantActionData.Get(VariantAction.Ultimatum).ActionIds);

        // The three that were genuinely re-issued per tier, for contrast.
        foreach (var kind in new[] { VariantAction.Cure, VariantAction.SpiritDart, VariantAction.Rampart })
            Assert.Equal(3, VariantActionData.Get(kind).ActionIds.Length);
    }

    /// <summary>
    /// A body on the floor and no raise has to come with a reason. The Set-status gate is silent by
    /// design — it fires every frame for the actions you did not pick — but silence in this one case is
    /// indistinguishable from a broken raise, which is exactly how this went unexplained.
    /// </summary>
    [Fact]
    public void TheLayerSaysWhenRaiseWasNotSelectedForTheRun()
    {
        var source = File.ReadAllText(VariantLayerSourcePath());
        Assert.Contains("Variant Raise was not selected for this run", source);
    }


    // ── Cure is targetable ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Variant Cure is a 30y heal on a target, not a self-heal. It used to read our own HP and cast on
    /// ourselves, so a Cure slotted on a DPS could never be spent on the tank dying next to it
    /// (reported 2026-09-20). The threshold question is now asked per member.
    /// </summary>
    [Theory]
    [InlineData(0.59f, true)]
    [InlineData(0.20f, true)]
    [InlineData(0.60f, false)]
    [InlineData(1.00f, false)]
    public void Cure_AsksTheThresholdOfWhicheverMemberIsOffered(float hpPct, bool expected)
        => Assert.Equal(expected, VariantBandRules.ShouldCure(Cfg(), hpPct));

    /// <summary>Default is to cure anyone; self-only is the opt-in for a toon that should not.</summary>
    [Fact]
    public void Cure_TargetsAnyoneByDefaultAndSelfOnlyWhenAsked()
    {
        var cfg = Cfg();
        Assert.False(cfg.CureSelfOnly);
        Assert.True(VariantBandRules.CureTargetAllowed(cfg, isSelf: true));
        Assert.True(VariantBandRules.CureTargetAllowed(cfg, isSelf: false));

        cfg.CureSelfOnly = true;
        Assert.True(VariantBandRules.CureTargetAllowed(cfg, isSelf: true));
        Assert.False(VariantBandRules.CureTargetAllowed(cfg, isSelf: false));
    }

    /// <summary>
    /// The wiring: Cure has to be pushed AT someone. Guarded at the source because the layer cannot be
    /// instantiated in a test — a targetless push goes to self, which is the bug.
    /// </summary>
    [Fact]
    public void TheLayerPushesCureAtATarget()
    {
        var source = File.ReadAllText(VariantLayerSourcePath());
        Assert.Matches(@"TryPush\(ctx, VariantAction\.Cure, PrioCure, target\.GameObjectId, target\)", source);
    }

    // ── Spirit Dart stacks per source ──────────────────────────────────────────────────

    /// <summary>
    /// Sustained Damage stacks per source — every toon's dart sits on the target at once — so "is MY
    /// dart still up" is the whole question. Status.SourceId carries the networked EntityId; the layer
    /// compared it to GameObjectId, which Dalamud documents as the local targeting handle and
    /// explicitly warns not to confuse with EntityId. Mismatched, our own dart always read as absent,
    /// so it was re-applied on every 2.5s recast instead of every ~27s.
    /// </summary>
    [Fact]
    public void SpiritDart_OwnershipIsCheckedAgainstTheEntityId()
    {
        var source = File.ReadAllText(VariantLayerSourcePath());

        Assert.Matches(@"status\.SourceId == ctx\.Player\.EntityId", source);
        Assert.DoesNotMatch(@"status\.SourceId == ctx\.Player\.GameObjectId", source);
    }

    /// <summary>A dart with time left is left alone; one about to fall off is refreshed.</summary>
    [Theory]
    [InlineData(0f, true)]
    [InlineData(VariantBandRules.DartRefreshSeconds - 0.1f, true)]
    [InlineData(VariantBandRules.DartRefreshSeconds, false)]
    [InlineData(25f, false)]
    public void SpiritDart_RefreshesOnlyNearTheEnd(float remaining, bool expected)
        => Assert.Equal(expected, VariantBandRules.ShouldMaintainDart(Cfg(), remaining, targetTtkSeconds: 60f));

    /// <summary>And never onto something about to die — the DoT is 30s and the mob is not.</summary>
    [Theory]
    [InlineData(VariantBandRules.DartMinTtkSeconds - 0.1f, false)]
    [InlineData(VariantBandRules.DartMinTtkSeconds, true)]
    [InlineData(float.MaxValue, true)]   // unknown TTK reads as "fire"
    public void SpiritDart_RespectsTheTimeToKillGate(float ttk, bool expected)
        => Assert.Equal(expected, VariantBandRules.ShouldMaintainDart(Cfg(), dotRemainingSeconds: 0f, targetTtkSeconds: ttk));


    // ── the layer must not eat the healer's raise ──────────────────────────────────────

    /// <summary>
    /// Reported 2026-09-20: inside a variant dungeon nothing rezzed at all — not the healer, not the
    /// variant raise. The layer pre-empts the GCD ahead of the job's modules, so holding that window
    /// with a body on the floor means a Sage's Egeiro never gets cast, and only in the duties where
    /// this layer runs. The phantom layer hit precisely this ("Sage raises worked everywhere EXCEPT
    /// the Horns") and carries the same guard; this one never got it.
    /// </summary>
    [Theory]
    [InlineData(true, true, true)]     // healer with a body in range -> stand down
    [InlineData(true, false, false)]   // healer, nothing to raise -> take the GCD
    [InlineData(false, true, false)]   // a DPS cannot raise; the body is not its problem
    [InlineData(false, false, false)]
    public void YieldsTheGcdOnlyWhenTheJobItselfCanRaiseSomebody(
        bool jobCanRaise, bool corpseInRange, bool expected)
        => Assert.Equal(expected, VariantBandRules.ShouldYieldGcdForRaise(jobCanRaise, corpseInRange));

    /// <summary>
    /// The wiring, guarded at the source: both dispatch sites must honour the yield, and neither may
    /// block the layer's OWN raise once it has queued one.
    /// </summary>
    [Fact]
    public void BothGcdDispatchSitesHonourTheYield()
    {
        var source = File.ReadAllText(VariantLayerSourcePath());

        var guarded = System.Text.RegularExpressions.Regex.Matches(
            source,
            @"CanExecuteGcd && \(_raiseQueuedThisFrame \|\| !RaisePendingForJob\(ctx\)\)").Count;
        Assert.Equal(2, guarded);

        // And no unguarded GCD dispatch is left behind.
        Assert.DoesNotMatch(@"if \(_actionService\.CanExecuteGcd\)\s*?
\s*_scheduler\.DispatchGcd", source);
    }


    /// <summary>
    /// Raise must outrank Cure in the layer's own queue (the scheduler sorts ascending, so the lower
    /// number wins). Cure became party-wide on 2026-09-20; before that it only read our own HP and
    /// rarely collided, but afterwards any ally under the threshold would take the GCD ahead of a body
    /// on the floor — starving the raise the rest of this work exists to deliver.
    /// </summary>
    [Fact]
    public void RaiseOutranksCureInTheLayersQueue()
    {
        var source = File.ReadAllText(VariantLayerSourcePath());

        var raise = int.Parse(System.Text.RegularExpressions.Regex
            .Match(source, @"PrioRaise = (\d+);").Groups[1].Value);
        var cure = int.Parse(System.Text.RegularExpressions.Regex
            .Match(source, @"PrioCure = (\d+);").Groups[1].Value);

        Assert.True(raise < cure, $"Raise ({raise}) must sort ahead of Cure ({cure})");
    }

    private static string VariantLayerSourcePath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Daedalus.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "Daedalus", "Rotation", "Phantom", "VariantActionLayer.cs");
    }

    [Fact]
    public void Raise_DeadDps_NoHealerAlive_VariantRaiseFallsBackIn()
    {
        Assert.Equal(VariantRaiseDecision.RaiseOther,
            VariantBandRules.DecideRaise(Cfg(), deadHealerPresent: false, deadOtherPresent: true, livingHealerPresent: false));
    }

    [Fact]
    public void Raise_DisabledInConfig_NeverFires()
    {
        var cfg = Cfg();
        cfg.UseRaise = false;

        Assert.Equal(VariantRaiseDecision.None,
            VariantBandRules.DecideRaise(cfg, deadHealerPresent: true, deadOtherPresent: true, livingHealerPresent: false));
    }
}
