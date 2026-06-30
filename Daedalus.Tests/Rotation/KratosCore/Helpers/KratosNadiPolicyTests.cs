using Daedalus.Rotation.KratosCore.Helpers;
using Xunit;

namespace Daedalus.Tests.Rotation.KratosCore.Helpers;

/// <summary>
/// Tests for MNK Perfect Balance nadi building. The core invariant: each PB must build the nadi you're
/// MISSING so you eventually hold both and reach Phantom Rush — never rebuild the one you already have
/// (the prior bug, which left MNK stuck making the same nadi forever).
/// </summary>
public sealed class KratosNadiPolicyTests
{
    [Fact]
    public void NoNadi_BuildsSolarFirst()
    {
        // Balance "Solar-Lunar" opener: no nadi → build Solar (3 different) first.
        Assert.True(KratosNadiPolicy.ShouldBuildSolar(hasLunarNadi: false, hasSolarNadi: false));
    }

    [Fact]
    public void LunarOnly_BuildsSolar()
    {
        Assert.True(KratosNadiPolicy.ShouldBuildSolar(hasLunarNadi: true, hasSolarNadi: false));
    }

    [Fact]
    public void SolarOnly_BuildsLunar()
    {
        // Regression: the old default rebuilt Solar here, so Phantom Rush was never reached.
        Assert.False(KratosNadiPolicy.ShouldBuildSolar(hasLunarNadi: false, hasSolarNadi: true));
    }

    [Fact]
    public void BothNadi_BuildsLunarPattern_ForPhantomRush()
    {
        // Both → 3 identical Opo (Phantom Rush ignores the pattern, Opo is highest potency).
        Assert.False(KratosNadiPolicy.ShouldBuildSolar(hasLunarNadi: true, hasSolarNadi: true));
    }

    [Theory]
    [InlineData(false, false)] // no nadi → Solar
    [InlineData(true, false)]  // Lunar only → Solar
    public void Solar_FillsOneOfEachForm_InOrder(bool lunar, bool solar)
    {
        // Empty slots → Opo first.
        Assert.Equal(MnkPbForm.OpoOpo, KratosNadiPolicy.NextForm(lunar, solar, false, false, false));
        // Opo filled → Raptor next.
        Assert.Equal(MnkPbForm.Raptor, KratosNadiPolicy.NextForm(lunar, solar, true, false, false));
        // Opo + Raptor filled → Coeurl last.
        Assert.Equal(MnkPbForm.Coeurl, KratosNadiPolicy.NextForm(lunar, solar, true, true, false));
    }

    [Fact]
    public void Lunar_AlwaysOpo_RegardlessOfSlots()
    {
        // Solar-only → building Lunar = 3 identical Opo, no matter what slots are filled.
        Assert.Equal(MnkPbForm.OpoOpo, KratosNadiPolicy.NextForm(false, true, false, false, false));
        Assert.Equal(MnkPbForm.OpoOpo, KratosNadiPolicy.NextForm(false, true, true, false, false));
        Assert.Equal(MnkPbForm.OpoOpo, KratosNadiPolicy.NextForm(false, true, true, true, false));
    }

    [Fact]
    public void BothNadi_AlwaysOpo()
    {
        Assert.Equal(MnkPbForm.OpoOpo, KratosNadiPolicy.NextForm(true, true, true, false, false));
    }
}
