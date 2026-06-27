using Daedalus.Data;
using Daedalus.Rotation.NyxCore.Abilities;
using Xunit;

namespace Daedalus.Tests.Rotation.NyxCore.Modules;

/// <summary>
/// Regression: the Delirium combo (Scarlet Delirium → Comeuppance → Torcleaver) and the AoE Impalement
/// are button-replacements over Bloodspiller / Quietus during Delirium. They must carry ReplacementBaseId
/// + AdjustedActionProbe so the scheduler dispatches the live morph and the chain advances — otherwise the
/// combo deadlocks after step 1 (Comeuppance/Torcleaver never fire).
/// </summary>
public sealed class NyxDeliriumComboTests
{
    public static System.Collections.Generic.IEnumerable<object[]> StSteps()
    {
        yield return new object[] { NyxAbilities.ScarletDelirium };
        yield return new object[] { NyxAbilities.Comeuppance };
        yield return new object[] { NyxAbilities.Torcleaver };
    }

    [Theory]
    [MemberData(nameof(StSteps))]
    public void StComboSteps_RouteThroughBloodspillerSlot(Daedalus.Rotation.Common.Scheduling.AbilityBehavior step)
    {
        Assert.Equal(DRKActions.Bloodspiller.ActionId, step.ReplacementBaseId);
        Assert.Equal(DRKActions.Bloodspiller.ActionId, step.AdjustedActionProbe);
    }

    [Fact]
    public void Impalement_RoutesThroughQuietusSlot()
    {
        Assert.Equal(DRKActions.Quietus.ActionId, NyxAbilities.Impalement.ReplacementBaseId);
        Assert.Equal(DRKActions.Quietus.ActionId, NyxAbilities.Impalement.AdjustedActionProbe);
    }
}
