using System.Linq;
using Daedalus.Data;
using Daedalus.Rotation.Phantom;
using Xunit;

namespace Daedalus.Tests.Data;

/// <summary>
/// Phantom Blue Mage, tooltips field-captured 2026-08-02. It is the odd one out: its actions are
/// LEARNED FROM ENEMIES rather than unlocked by level, so the catalog levels are the trait tiers
/// that make each learnable and the duty-bar slot gate is what proves you actually have it.
/// </summary>
public sealed class PhantomBlueMageTests
{
    private static PhantomActionDef Action(uint id)
        => PhantomActions.All.Single(a => a.ActionId == id);

    [Theory]
    [InlineData(49085u, "Occult Aero")]
    [InlineData(49086u, "Occult Missile")]
    [InlineData(49087u, "Occult Aqua Breath")]
    [InlineData(49088u, "Occult Mighty Guard")]
    [InlineData(49089u, "Occult Aero II")]
    [InlineData(49090u, "Occult White Wind")]
    [InlineData(49091u, "Occult Aero III")]
    public void Catalog_CarriesTheWholeKit(uint id, string name)
    {
        var action = Action(id);

        Assert.Equal(name, action.Name);
        Assert.Equal(PhantomJob.PhantomBlueMage, action.Job);
    }

    /// <summary>The block sits immediately before Red Mage's and must not collide with it.</summary>
    [Fact]
    public void Catalog_DoesNotOverlapRedMage()
    {
        var blue = PhantomActions.All.Where(a => a.Job == PhantomJob.PhantomBlueMage).Select(a => a.ActionId).ToList();
        var red = PhantomActions.All.Where(a => a.Job == PhantomJob.PhantomRedMage).Select(a => a.ActionId).ToList();

        Assert.Empty(blue.Intersect(red));
        Assert.True(blue.Max() < red.Min(), "Blue Mage occupies 49085-49091, Red Mage 49092+");
    }

    /// <summary>
    /// Aero I/II/III are one button in ascending grades, so firing a lower grade is strictly
    /// wasted once a higher one is available.
    /// </summary>
    [Theory]
    [InlineData((byte)1, 49085u)]
    [InlineData((byte)2, 49089u)]
    [InlineData((byte)3, 49091u)]
    [InlineData((byte)6, 49091u)]
    public void BestAero_PicksTheHighestGradeReached(byte level, uint expected)
    {
        Assert.Equal(expected, PhantomBandRules.BestAero(level));
    }

    /// <summary>
    /// White Wind copies the caster's CURRENT HP, so its value RISES with health — saving it for
    /// death's door heals almost nothing, which is the opposite of the usual instinct.
    /// </summary>
    [Fact]
    public void WhiteWindBand_SitsWhereTheCasterStillHasHpWorthCopying()
    {
        Assert.True(PhantomBandRules.WhiteWindUpperHpPct > PhantomBandRules.WhiteWindLowerHpPct);
        Assert.True(PhantomBandRules.WhiteWindLowerHpPct > 0f,
            "firing at near-zero HP would heal for near-zero");
        Assert.True(PhantomBandRules.WhiteWindUpperHpPct < 1f,
            "no point firing at full health either");
    }
}
