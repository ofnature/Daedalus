using System;
using System.Collections.Generic;
using System.Linq;
using Daedalus.Data;
using Xunit;

namespace Daedalus.Tests.Data;

/// <summary>
/// Tests for the Variant dungeon duty-action data (Phase 1,
/// docs/variant-actions-plan.md). IDs verified against RSR; roles against the
/// V&amp;C wiki; GCD-vs-weave against in-game tooltips (2026-07-25).
/// </summary>
public class VariantActionDataTests
{
    [Fact]
    public void Catalog_CoversEveryLogicalAction_WithUniqueActionIds()
    {
        var kinds = VariantActionData.All.Select(d => d.Kind).ToList();
        var allIds = VariantActionData.All.SelectMany(d => d.ActionIds).ToList();

        Assert.Equal(Enum.GetValues<VariantAction>().Length, kinds.Count);
        Assert.Equal(kinds.Count, kinds.Distinct().Count());
        Assert.Equal(allIds.Count, allIds.Distinct().Count());
    }

    [Fact]
    public void Catalog_RsrVerifiedIdsAndSetStatuses()
    {
        var cure = VariantActionData.Get(VariantAction.Cure);
        Assert.Equal(3565u, cure.SetStatusId);
        Assert.Equal(new uint[] { 29729, 33862, 46939 }, cure.ActionIds);

        var dart = VariantActionData.Get(VariantAction.SpiritDart);
        Assert.Equal(3568u, dart.SetStatusId);
        Assert.Equal(new uint[] { 29732, 33863, 46940 }, dart.ActionIds);

        var rampart = VariantActionData.Get(VariantAction.Rampart);
        Assert.Equal(3569u, rampart.SetStatusId);

        Assert.Equal(4892u, VariantActionData.Get(VariantAction.EagleEyeShot).SetStatusId);
        // Raise and Raise II share one Set status.
        Assert.Equal(3567u, VariantActionData.Get(VariantAction.Raise).SetStatusId);
        Assert.Equal(3567u, VariantActionData.Get(VariantAction.RaiseII).SetStatusId);
    }

    [Fact]
    public void Catalog_GcdClassificationMatchesTooltips()
    {
        // Spells (GCDs): Cure, Raise, Raise II. Abilities (weaves): the rest.
        Assert.True(VariantActionData.Get(VariantAction.Cure).IsGcd);
        Assert.True(VariantActionData.Get(VariantAction.Raise).IsGcd);
        Assert.True(VariantActionData.Get(VariantAction.RaiseII).IsGcd);
        Assert.False(VariantActionData.Get(VariantAction.SpiritDart).IsGcd);
        Assert.False(VariantActionData.Get(VariantAction.Rampart).IsGcd);
        Assert.False(VariantActionData.Get(VariantAction.Ultimatum).IsGcd);
        Assert.False(VariantActionData.Get(VariantAction.EagleEyeShot).IsGcd);
    }

    [Fact]
    public void Catalog_RoleAvailability_WikiVerifiedKeyFacts()
    {
        // Spirit Dart is the tank/healer pick; healers can never take Cure or Raise;
        // Rampart is not offered to tanks.
        Assert.Equal("Tank, Healer", VariantActionData.Get(VariantAction.SpiritDart).SelectableBy);
        Assert.DoesNotContain("Healer", VariantActionData.Get(VariantAction.Cure).SelectableBy);
        Assert.DoesNotContain("Healer", VariantActionData.Get(VariantAction.Raise).SelectableBy);
        Assert.DoesNotContain("Tank", VariantActionData.Get(VariantAction.Rampart).SelectableBy);
    }

    [Fact]
    public void Territories_CoverAllFiveVariantDungeons()
    {
        var expected = new ushort[] { 1069, 1137, 1176, 1315, 1316 };
        foreach (var territory in expected)
            Assert.Contains(territory, (IEnumerable<ushort>)VariantActionData.VariantTerritoryIds);
        Assert.Equal(expected.Length, VariantActionData.VariantTerritoryIds.Count);
    }

    [Fact]
    public void StatusIds_ForDotAndBuffGates()
    {
        Assert.Equal(3359u, VariantActionData.SustainedDamageStatusId);
        Assert.Equal(3367u, VariantActionData.RehabilitationStatusId);
        Assert.Equal(3360u, VariantActionData.VulnerabilityDownStatusId);
    }
}
