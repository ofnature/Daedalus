using Daedalus.Data;
using Daedalus.Rotation.HecateCore.Abilities;
using Xunit;

namespace Daedalus.Tests.Rotation.HecateCore.Modules;

/// <summary>
/// Astral Fire phase decisions against the Dawntrail rules.
/// - Astral Fire has no duration any more (Fire III / Despair / Manafont just "Grant Astral Fire III");
///   the gauge's EnochianTimer is the 30s Polyglot cycle (Trait "Enhanced Enochian"). Treating it as an
///   element timer cast Blizzard III mid-Fire every ~30s and gated Paradox on it.
/// - Despair doesn't spend Astral Soul: the "Fire IVs before Despair" line is the pre-Manafont Despair only,
///   otherwise every Manafont refill went straight back into Despair and Flare Star was never cast.
/// - Astral Fire doubles Fire IV's cost (800 → 1600) unless an Umbral Heart nullifies it.
/// </summary>
public class DamageModuleFirePhaseTests
{
    // --- Element timer is the Polyglot cycle, not an Astral Fire timer ---

    [Fact]
    public void AstralFire_LowEnochianTimer_KeepsCastingFireIV()
    {
        var scheduler = HecateDamageScenario.Run(level: 100, inAstralFire: true, elementTimer: 2f,
            currentMp: 10000, umbralHearts: 3);
        Assert.Contains(scheduler.InspectGcdQueue(), c => c.Behavior == HecateAbilities.Fire4);
        Assert.DoesNotContain(scheduler.InspectGcdQueue(), c => c.Behavior == HecateAbilities.Blizzard3);
    }

    [Fact]
    public void AstralFire_ParadoxUp_PushedRegardlessOfEnochianTimer()
    {
        var scheduler = HecateDamageScenario.Run(level: 100, inAstralFire: true, elementTimer: 20f,
            currentMp: 10000, umbralHearts: 3, hasParadox: true);
        Assert.Contains(scheduler.InspectGcdQueue(), c => c.Behavior == HecateAbilities.Paradox);
    }

    [Fact]
    public void AstralFire_ParadoxUp_NotPushedWhenMpCannotPayForIt()
    {
        // Paradox costs MP in Astral Fire (free only in Umbral Ice); at 1000 MP it would be refused.
        var scheduler = HecateDamageScenario.Run(level: 100, inAstralFire: true, elementTimer: 2f,
            currentMp: 1000, umbralHearts: 0, hasParadox: true);
        Assert.DoesNotContain(scheduler.InspectGcdQueue(), c => c.Behavior == HecateAbilities.Paradox);
    }

    [Fact]
    public void AstralFire_EnochianTimerZero_StillFireIV()
    {
        var scheduler = HecateDamageScenario.Run(level: 100, inAstralFire: true, elementTimer: 0f,
            currentMp: 10000, umbralHearts: 3);
        Assert.Contains(scheduler.InspectGcdQueue(), c => c.Behavior == HecateAbilities.Fire4);
    }

    // --- Despair / Astral Soul / Manafont ---

    [Fact]
    public void AstralSoul4_ManafontOnCooldown_CastsFireIV_NotDespair()
    {
        // After Manafont has been used, the refill must build Astral Soul to 6 for Flare Star.
        var scheduler = HecateDamageScenario.Run(level: 100, inAstralFire: true, astralSoulStacks: 4,
            currentMp: 10000, umbralHearts: 3, manafontReady: false);
        Assert.Contains(scheduler.InspectGcdQueue(), c => c.Behavior == HecateAbilities.Fire4);
        Assert.DoesNotContain(scheduler.InspectGcdQueue(), c => c.Behavior == HecateAbilities.Despair);
    }

    [Fact]
    public void AstralSoul5_ManafontOnCooldown_CastsFireIV_NotDespair()
    {
        var scheduler = HecateDamageScenario.Run(level: 100, inAstralFire: true, astralSoulStacks: 5,
            currentMp: 6000, umbralHearts: 0, manafontReady: false);
        Assert.Contains(scheduler.InspectGcdQueue(), c => c.Behavior == HecateAbilities.Fire4);
        Assert.DoesNotContain(scheduler.InspectGcdQueue(), c => c.Behavior == HecateAbilities.Despair);
    }

    [Fact]
    public void AstralSoul6_QueuesFlareStar()
    {
        var scheduler = HecateDamageScenario.Run(level: 100, inAstralFire: true, astralSoulStacks: 6,
            currentMp: 10000, umbralHearts: 3, manafontReady: false);
        Assert.Contains(scheduler.InspectGcdQueue(), c => c.Behavior == HecateAbilities.FlareStar);
    }

    [Fact]
    public void AstralSoul4_ManafontReady_DespairsBeforeManafont()
    {
        // The configured pre-Manafont line (FireIVsBeforeDespair = 4) is kept.
        var scheduler = HecateDamageScenario.Run(level: 100, inAstralFire: true, astralSoulStacks: 4,
            currentMp: 4400, umbralHearts: 0, manafontReady: true);
        Assert.Contains(scheduler.InspectGcdQueue(), c => c.Behavior == HecateAbilities.Despair);
    }

    // --- Fire IV MP cost without Umbral Hearts ---

    [Fact]
    public void Lv65_NoHearts_1200Mp_TransitionsToIce()
    {
        // Fire IV costs 1600 in Astral Fire without hearts: at 1200 MP it would be refused and the old
        // "< 800 MP" transition never fired — the rotation stalled.
        var scheduler = HecateDamageScenario.Run(level: 65, inAstralFire: true, currentMp: 1200, umbralHearts: 0);
        Assert.Contains(scheduler.InspectGcdQueue(), c => c.Behavior == HecateAbilities.Blizzard3);
        Assert.DoesNotContain(scheduler.InspectGcdQueue(), c => c.Behavior == HecateAbilities.Fire4);
    }

    [Fact]
    public void Lv100_DespairOff_NoHearts_1200Mp_TransitionsToIce()
    {
        var scheduler = HecateDamageScenario.Run(level: 100, inAstralFire: true, currentMp: 1200, umbralHearts: 0,
            configure: c => c.EnableDespair = false);
        Assert.Contains(scheduler.InspectGcdQueue(), c => c.Behavior == HecateAbilities.Blizzard3);
        Assert.DoesNotContain(scheduler.InspectGcdQueue(), c => c.Behavior == HecateAbilities.Fire4);
    }

    [Fact]
    public void Lv65_NoHearts_2000Mp_CastsFireIV()
    {
        var scheduler = HecateDamageScenario.Run(level: 65, inAstralFire: true, currentMp: 2000, umbralHearts: 0);
        Assert.Contains(scheduler.InspectGcdQueue(), c => c.Behavior == HecateAbilities.Fire4);
    }

    [Fact]
    public void Lv65_OneHeart_1200Mp_CastsFireIV()
    {
        // An Umbral Heart nullifies Astral Fire's cost increase: Fire IV is 800 here.
        var scheduler = HecateDamageScenario.Run(level: 65, inAstralFire: true, currentMp: 1200, umbralHearts: 1);
        Assert.Contains(scheduler.InspectGcdQueue(), c => c.Behavior == HecateAbilities.Fire4);
    }

    // --- Quest-locked Fire IV ---

    [Fact]
    public void Lv70_FireIVNotLearned_CastsFire()
    {
        var scheduler = HecateDamageScenario.Run(level: 70, inAstralFire: true, currentMp: 10000,
            notLearned: new[] { BLMActions.Fire4.ActionId });
        Assert.Contains(scheduler.InspectGcdQueue(), c => c.Behavior == HecateAbilities.Fire);
        Assert.DoesNotContain(scheduler.InspectGcdQueue(), c => c.Behavior == HecateAbilities.Fire4);
    }
}
