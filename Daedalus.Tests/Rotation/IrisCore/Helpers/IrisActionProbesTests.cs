using Moq;
using Daedalus.Data;
using Daedalus.Rotation.IrisCore.Abilities;
using Daedalus.Rotation.IrisCore.Helpers;
using Daedalus.Services.Action;
using Daedalus.Tests.Mocks;
using Xunit;

namespace Daedalus.Tests.Rotation.IrisCore.Helpers;

/// <summary>
/// Regression guards for PCT slot probes (RSR GetAdjustedActionId parity).
/// </summary>
public class IrisActionProbesTests
{
    private static Mock<IActionService> WithAdjust(uint baseId, uint adjustedId)
    {
        var mock = MockBuilders.CreateMockActionService();
        mock.Setup(x => x.GetAdjustedActionId(baseId)).Returns(adjustedId);
        return mock;
    }

    [Fact]
    public void IsHammerBrushReady_True_WhenStampSlotReplacedByBrush()
    {
        var svc = WithAdjust(PCTActions.HammerStamp.ActionId, PCTActions.HammerBrush.ActionId);
        Assert.True(IrisActionProbes.IsHammerBrushReady(svc.Object));
    }

    [Fact]
    public void IsPolishingHammerReady_True_WhenStampSlotReplacedByPolish()
    {
        var svc = WithAdjust(PCTActions.HammerStamp.ActionId, PCTActions.PolishingHammer.ActionId);
        Assert.True(IrisActionProbes.IsPolishingHammerReady(svc.Object));
    }

    [Fact]
    public void IsHammerMotifReady_True_WhenWeaponMotifReplacedByHammerMotif()
    {
        var svc = WithAdjust(PCTActions.WeaponMotif.ActionId, PCTActions.HammerMotif.ActionId);
        Assert.True(IrisActionProbes.IsHammerMotifReady(svc.Object));
    }

    [Fact]
    public void IsStarrySkyMotifReady_True_WhenLandscapeMotifReplacedByStarrySky()
    {
        var svc = WithAdjust(PCTActions.LandscapeMotif.ActionId, PCTActions.StarrySkyMotif.ActionId);
        Assert.True(IrisActionProbes.IsStarrySkyMotifReady(svc.Object));
    }

    [Fact]
    public void IsStarryMuseReady_True_WhenScenicMuseReplacedByStarryMuse()
    {
        var svc = WithAdjust(PCTActions.ScenicMuse.ActionId, PCTActions.StarryMuse.ActionId);
        Assert.True(IrisActionProbes.IsStarryMuseReady(svc.Object));
    }

    [Fact]
    public void IsStrikingMuseReady_True_WhenSteelMuseReplacedByStrikingMuse()
    {
        var svc = WithAdjust(PCTActions.SteelMuse.ActionId, PCTActions.StrikingMuse.ActionId);
        Assert.True(IrisActionProbes.IsStrikingMuseReady(svc.Object));
    }

    [Fact]
    public void IsLivingMuseReadyForCreature_True_WhenPomMuseProbeActive()
    {
        var svc = WithAdjust(PCTActions.LivingMuse.ActionId, PCTActions.PomMuse.ActionId);
        Assert.True(IrisActionProbes.IsLivingMuseReadyForCreature(svc.Object, PCTActions.CreatureMotifType.Pom));
    }

    [Fact]
    public void ResolveHammerComboStep_Returns2_WhenPolishProbeActive()
    {
        var svc = WithAdjust(PCTActions.HammerStamp.ActionId, PCTActions.PolishingHammer.ActionId);
        Assert.Equal(2, IrisActionProbes.ResolveHammerComboStep(svc.Object, 0, hasHammerTime: false, hammerTimeStacks: 0));
    }

    [Fact]
    public void ResolveHammerComboStep_Returns1_WhenBrushProbeActive()
    {
        var svc = WithAdjust(PCTActions.HammerStamp.ActionId, PCTActions.HammerBrush.ActionId);
        Assert.Equal(1, IrisActionProbes.ResolveHammerComboStep(svc.Object, 0, hasHammerTime: false, hammerTimeStacks: 0));
    }

    [Fact]
    public void ResolveHammerComboStep_Returns0_WhenHammerTimeAtThreeStacksAndStampReady()
    {
        var svc = WithAdjust(PCTActions.HammerStamp.ActionId, PCTActions.HammerStamp.ActionId);
        Assert.Equal(0, IrisActionProbes.ResolveHammerComboStep(svc.Object, 0, hasHammerTime: true, hammerTimeStacks: 3));
    }

    [Fact]
    public void CanStartHammerStamp_False_WhenOnlyTwoHammerTimeStacks()
    {
        var svc = WithAdjust(PCTActions.HammerStamp.ActionId, PCTActions.HammerStamp.ActionId);
        Assert.False(IrisActionProbes.CanStartHammerStamp(svc.Object, hasHammerTime: true, hammerTimeStacks: 2, hammerComboStep: 0));
    }

    [Fact]
    public void CanStartHammerStamp_True_WhenThreeStacksAndStampProbeReady()
    {
        var svc = WithAdjust(PCTActions.HammerStamp.ActionId, PCTActions.HammerStamp.ActionId);
        Assert.True(IrisActionProbes.CanStartHammerStamp(svc.Object, hasHammerTime: true, hammerTimeStacks: 3, hammerComboStep: 0));
    }

    [Fact]
    public void CanStartHammerStamp_True_WhenAlreadyInCombo()
    {
        var svc = MockBuilders.CreateMockActionService();
        Assert.True(IrisActionProbes.CanStartHammerStamp(svc.Object, hasHammerTime: false, hammerTimeStacks: 0, hammerComboStep: 1));
    }

    [Fact]
    public void GetNextCreatureMotif_PrefersClawProbeAtHighLevel()
    {
        var svc = WithAdjust(PCTActions.CreatureMotif.ActionId, PCTActions.ClawMotif.ActionId);
        var motif = IrisActionProbes.GetNextCreatureMotif(svc.Object, level: 100, livingMuseCharges: 0);
        Assert.Equal(PCTActions.ClawMotif.ActionId, motif.ActionId);
    }

    [Fact]
    public void ResolveBaseComboStep_Returns1_WhenFireSlotReplacedByAero()
    {
        var svc = WithAdjust(PCTActions.FireInRed.ActionId, PCTActions.AeroInGreen.ActionId);
        IrisActionProbes.ResolveBaseComboStep(
            svc.Object, shouldUseAoe: false, level: 100, useSubtractiveRoute: false,
            out var step, out var isSubtractive);

        Assert.Equal(1, step);
        Assert.False(isSubtractive);
    }

    [Fact]
    public void ResolveBaseComboStep_Returns2_WhenFireSlotReplacedByWater()
    {
        var svc = WithAdjust(PCTActions.FireInRed.ActionId, PCTActions.WaterInBlue.ActionId);
        IrisActionProbes.ResolveBaseComboStep(
            svc.Object, shouldUseAoe: false, level: 100, useSubtractiveRoute: false,
            out var step, out _);

        Assert.Equal(2, step);
    }

    [Fact]
    public void ResolveBaseComboStep_UsesSubtractiveChain_WhenRouteActive()
    {
        var svc = WithAdjust(PCTActions.BlizzardInCyan.ActionId, PCTActions.StoneInYellow.ActionId);
        IrisActionProbes.ResolveBaseComboStep(
            svc.Object, shouldUseAoe: false, level: 100, useSubtractiveRoute: true,
            out var step, out var isSubtractive);

        Assert.Equal(1, step);
        Assert.True(isSubtractive);
    }

    [Fact]
    public void ResolveBaseComboStep_Subtractive_Returns2_WhenStarterReplacedByThunder()
    {
        // Regression: only the combo STARTER (Blizzard in Cyan) morphs. After Stone in Yellow the starter
        // slot reports Thunder in Magenta — step must resolve to 2 (was stuck at 0, dispatching Blizzard
        // and stalling on its AdjustedActionProbe).
        var svc = WithAdjust(PCTActions.BlizzardInCyan.ActionId, PCTActions.ThunderInMagenta.ActionId);
        IrisActionProbes.ResolveBaseComboStep(
            svc.Object, shouldUseAoe: false, level: 100, useSubtractiveRoute: true,
            out var step, out var isSubtractive);

        Assert.Equal(2, step);
        Assert.True(isSubtractive);
    }

    [Fact]
    public void ResolveBaseComboStep_SubtractiveAoE_Returns2_WhenStarterReplacedByThunderII()
    {
        // The exact in-game stall: AoE subtractive, starter (Blizzard II) slot shows Thunder II (34661).
        var svc = WithAdjust(PCTActions.Blizzard2InCyan.ActionId, PCTActions.Thunder2InMagenta.ActionId);
        IrisActionProbes.ResolveBaseComboStep(
            svc.Object, shouldUseAoe: true, level: 100, useSubtractiveRoute: true,
            out var step, out var isSubtractive);

        Assert.Equal(2, step);
        Assert.True(isSubtractive);
    }

    [Fact]
    public void ResolveBaseComboStep_SubtractiveAoE_Returns1_WhenStarterReplacedByStoneII()
    {
        var svc = WithAdjust(PCTActions.Blizzard2InCyan.ActionId, PCTActions.Stone2InYellow.ActionId);
        IrisActionProbes.ResolveBaseComboStep(
            svc.Object, shouldUseAoe: true, level: 100, useSubtractiveRoute: true,
            out var step, out var isSubtractive);

        Assert.Equal(1, step);
        Assert.True(isSubtractive);
    }

    [Fact]
    public void SubtractiveThunder_ProbesTheStarter_NotStone()
    {
        // The ST Thunder behavior must probe the combo starter (only it morphs), matching Stone in Yellow
        // and the AoE Thunder II behavior. Probing Stone in Yellow never passed → ST subtractive stalled.
        Assert.Equal(PCTActions.BlizzardInCyan.ActionId, IrisAbilities.ThunderInMagenta.AdjustedActionProbe);
        Assert.Equal(PCTActions.Blizzard2InCyan.ActionId, IrisAbilities.Thunder2InMagenta.AdjustedActionProbe);
    }

    [Fact]
    public void ComboFollowups_UseReplacementBaseId_OnChainStarter()
    {
        Assert.Equal(PCTActions.FireInRed.ActionId, IrisAbilities.AeroInGreen.ReplacementBaseId);
        Assert.Equal(PCTActions.FireInRed.ActionId, IrisAbilities.WaterInBlue.ReplacementBaseId);
        Assert.Equal(PCTActions.Fire2InRed.ActionId, IrisAbilities.Aero2InGreen.ReplacementBaseId);
        Assert.Equal(PCTActions.Fire2InRed.ActionId, IrisAbilities.Water2InBlue.ReplacementBaseId);
        Assert.Null(IrisAbilities.FireInRed.ReplacementBaseId);
        Assert.Null(IrisAbilities.Fire2InRed.ReplacementBaseId);
    }
}
