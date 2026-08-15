using System.Linq;
using Daedalus.Data;
using Xunit;

namespace Daedalus.Tests.Data;

/// <summary>
/// The Blue Mage learn table is reference data, so the thing worth testing is that it stays in
/// step with the action catalog — a spell renamed or re-levelled in one place and not the other
/// would send someone hunting the wrong enemy, which is the entire point of the table.
/// </summary>
public sealed class PhantomBlueMageSourcesTests
{
    [Fact]
    public void EverySpell_IsARealPhantomBlueMageAction()
    {
        foreach (var s in PhantomBlueMageSources.All)
        {
            var def = PhantomActions.All.FirstOrDefault(a => a.ActionId == s.ActionId);
            Assert.True(def.ActionId != 0, $"{s.Spell} ({s.ActionId}) is not in the phantom catalog");
            Assert.Equal(PhantomJob.PhantomBlueMage, def.Job);
        }
    }

    /// <summary>Levels must match the catalog, or the greying-out in the UI lies.</summary>
    [Fact]
    public void RequiredLevels_MatchTheCatalog()
    {
        foreach (var s in PhantomBlueMageSources.All)
        {
            var def = PhantomActions.All.First(a => a.ActionId == s.ActionId);
            Assert.Equal(def.RequiredLevel, s.RequiredLevel);
        }
    }

    /// <summary>Names must match too — the table is useless if it calls a spell something else.</summary>
    [Fact]
    public void SpellNames_MatchTheCatalog()
    {
        foreach (var s in PhantomBlueMageSources.All)
        {
            var def = PhantomActions.All.First(a => a.ActionId == s.ActionId);
            Assert.Equal(def.Name, s.Spell);
        }
    }

    /// <summary>Every Blue Mage action in the catalog needs an entry, or the list has a hole.</summary>
    [Fact]
    public void EveryCatalogSpell_HasASource()
    {
        var catalog = PhantomActions.All
            .Where(a => a.Job == PhantomJob.PhantomBlueMage)
            .Select(a => a.ActionId)
            .OrderBy(id => id);

        var listed = PhantomBlueMageSources.All.Select(s => s.ActionId).OrderBy(id => id);
        Assert.Equal(catalog, listed);
    }

    [Fact]
    public void EntriesAreUnique_AndCarryASource()
    {
        Assert.Equal(
            PhantomBlueMageSources.All.Count,
            PhantomBlueMageSources.All.Select(s => s.ActionId).Distinct().Count());

        foreach (var s in PhantomBlueMageSources.All)
            Assert.False(string.IsNullOrWhiteSpace(s.Enemy), $"{s.Spell} has no source enemy");
    }

    /// <summary>
    /// Occult Aero is the one spell with no hunt attached; everything else must name a location
    /// or the critical encounter that spawns the teacher.
    /// </summary>
    [Fact]
    public void OnlyTheDefaultSpell_HasNoLocation()
    {
        foreach (var s in PhantomBlueMageSources.All)
        {
            if (s.Enemy == PhantomBlueMageSources.UnlockedByDefault)
                Assert.Equal(string.Empty, s.Where);
            else
                Assert.False(string.IsNullOrWhiteSpace(s.Where), $"{s.Spell} has no location");
        }
    }

    [Fact]
    public void For_ResolvesAKnownSpell_AndNothingElse()
    {
        Assert.Equal("Crescent Flame", PhantomBlueMageSources.For(49090)?.Enemy);
        Assert.Null(PhantomBlueMageSources.For(41621)); // Occult Slowga — a Time Mage action
    }
}
