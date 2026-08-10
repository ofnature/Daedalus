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
    /// Aero I/II/III are one button, but Blue Mage learns from ENEMIES, not levels — the layer
    /// pushes every grade best-first and lets the duty-bar gate pick the one actually known.
    /// Picking a single grade off the phantom level meant an unlearned Aero III silenced Aero
    /// entirely for a level-3 Blue Mage who only knew Aero I.
    /// </summary>
    [Fact]
    public void AeroGrades_ListEveryGradeBestFirst()
    {
        Assert.Equal(
            new[] { PhantomBandRules.OccultAeroIIIId, PhantomBandRules.OccultAeroIIId, PhantomBandRules.OccultAeroId },
            PhantomBandRules.AeroGradesDescending);
    }

    /// <summary>
    /// The descending push order must also descend in catalog tier, so the level gate trims the
    /// unreachable grades and the priorities favor the best one still in reach.
    /// </summary>
    [Fact]
    public void AeroGrades_CatalogTiersDescendWithTheOrder()
    {
        var tiers = PhantomBandRules.AeroGradesDescending.Select(id => Action(id).RequiredLevel).ToList();

        Assert.Equal(tiers.OrderByDescending(t => t), tiers);
    }

    /// <summary>
    /// White Wind heals the PARTY for the caster's CURRENT HP, so a full-HP caster with a dying
    /// party is the ideal case — the old self-HP band blocked exactly that.
    /// </summary>
    [Fact]
    public void WhiteWind_FullHpCasterWithDyingParty_Fires()
    {
        Assert.True(PhantomBandRules.ShouldWhiteWind(partyAvgHpPct: 0.35f, selfHpPct: 1.0f, inCombat: true));
    }

    [Fact]
    public void WhiteWind_HealthyParty_Holds()
    {
        Assert.False(PhantomBandRules.ShouldWhiteWind(partyAvgHpPct: 0.95f, selfHpPct: 1.0f, inCombat: true));
    }

    /// <summary>At near-zero self HP the copied heal is near-zero — not worth the 150s recast.</summary>
    [Fact]
    public void WhiteWind_CasterBelowFloor_Holds()
    {
        Assert.False(PhantomBandRules.ShouldWhiteWind(partyAvgHpPct: 0.35f, selfHpPct: 0.10f, inCombat: true));
    }

    [Fact]
    public void WhiteWind_OutOfCombat_Holds()
    {
        Assert.False(PhantomBandRules.ShouldWhiteWind(partyAvgHpPct: 0.35f, selfHpPct: 1.0f, inCombat: false));
    }
}
