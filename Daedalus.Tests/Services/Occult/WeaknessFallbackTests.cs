using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Daedalus.Rotation.Phantom;
using Daedalus.Services.Occult;
using Xunit;

namespace Daedalus.Tests.Services.Occult;

/// <summary>
/// Looking up an enemy's revealed weakness. The exact row wins; after that the fallbacks exist
/// because the game does not give one creature one identity — it gives it several NameIds, and a
/// creature living in both Horns gets a row per zone.
/// <para>
/// The whole chain silently never ran until 2026-09-03: the only caller passed an id and no name,
/// so the same-name tier returned immediately and nothing failed loudly. Hence the wiring tests at
/// the bottom, which are the ones that would have caught it.
/// </para>
/// </summary>
public sealed class WeaknessFallbackTests
{
    private const ushort South = 1252;
    private const ushort North = 1346;

    private static OccultWeaknessEntry Row(ushort zone, uint nameId, string name, OccultElement el)
        => new() { TerritoryId = zone, NameId = nameId, Name = name, Elements = el, MaxHp = 500_000 };

    private static OccultElement? Resolve(
        IEnumerable<OccultWeaknessEntry> rows, ushort zone, uint nameId, string? name = null)
        => ElementalWeaknessLog.ResolveFallback(rows, zone, nameId, name);

    /// <summary>
    /// Crescent Bomb, measured: known ice in South Horn, blank in North. Same id, same creature.
    /// </summary>
    [Fact]
    public void SameNameId_InTheOtherHorn_IsUsed()
    {
        var rows = new[] { Row(South, 13939, "Crescent Bomb", OccultElement.Ice) };
        Assert.Equal(OccultElement.Ice, Resolve(rows, North, 13939));
    }

    /// <summary>
    /// Crescent Void Viper is 13896 and 13907; Animated Doll is 13893 and 13894. The weakness
    /// belongs to the creature, not to the id it happened to spawn under.
    /// </summary>
    [Fact]
    public void SameName_DifferentId_InThisZone_IsUsed()
    {
        var rows = new[] { Row(South, 13896, "Crescent Void Viper", OccultElement.Ice) };
        Assert.Equal(OccultElement.Ice, Resolve(rows, South, 13907, "Crescent Void Viper"));
    }

    /// <summary>Without a name that tier cannot fire — which is exactly how it lay dead.</summary>
    [Fact]
    public void SameName_WithoutBeingToldTheName_CannotResolve()
    {
        var rows = new[] { Row(South, 13896, "Crescent Void Viper", OccultElement.Ice) };
        Assert.Null(Resolve(rows, South, 13907));
    }

    /// <summary>The name tier stays inside the zone: two Horns can name different creatures alike.</summary>
    [Fact]
    public void SameName_InTheOtherHorn_IsNotBorrowed()
    {
        var rows = new[] { Row(South, 13896, "Crescent Void Viper", OccultElement.Ice) };
        Assert.Null(Resolve(rows, North, 99999, "Crescent Void Viper"));
    }

    /// <summary>A row with no element is not an answer — it is the absence of one.</summary>
    [Fact]
    public void RowsWithNoElement_AreNotAnAnswer()
    {
        var rows = new[]
        {
            Row(North, 13939, "Crescent Bomb", OccultElement.None),
            Row(South, 13939, "Crescent Bomb", OccultElement.None),
        };
        Assert.Null(Resolve(rows, North, 13939, "Crescent Bomb"));
    }

    [Fact]
    public void NothingKnown_StaysNull()
        => Assert.Null(Resolve([], South, 12345, "Nobody"));

    /// <summary>Id beats name: it is the stronger claim, and both tiers can match at once.</summary>
    [Fact]
    public void SameId_IsPreferredOverSameName()
    {
        var rows = new[]
        {
            Row(South, 4242, "Twin", OccultElement.Wind),   // same id, other zone
            Row(North, 9999, "Twin", OccultElement.Fire),   // same name, this zone
        };
        Assert.Equal(OccultElement.Wind, Resolve(rows, North, 4242, "Twin"));
    }

    // ---- wiring: the half that was missing --------------------------------------------------

    /// <summary>
    /// The layer must hand over a NAME as well as an id, or the same-name tier is unreachable no
    /// matter how correct it is.
    /// </summary>
    [Fact]
    public void TheLayerAsksWithAName()
    {
        var property = typeof(PhantomActionLayer).GetProperty("TargetWeakness", BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(property);

        var args = property!.PropertyType.GetGenericArguments();
        Assert.Equal(3, args.Length);                       // uint, string?, OccultElement?
        Assert.Equal(typeof(uint), args[0]);
        Assert.Equal(typeof(string), args[1]);
    }

    /// <summary>And every call site must actually pass one.</summary>
    [Fact]
    public void EveryCallSitePassesTheName()
    {
        var source = ReadLayerSource();
        var calls = System.Text.RegularExpressions.Regex.Matches(source, @"TargetWeakness\?\.Invoke\(([^)]*)\)");
        Assert.NotEmpty(calls);
        foreach (System.Text.RegularExpressions.Match call in calls)
            Assert.Contains(",", call.Groups[1].Value);
    }

    private static string ReadLayerSource()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8 && dir is not null; i++)
        {
            var candidate = System.IO.Path.Combine(dir, "Daedalus", "Rotation", "Phantom", "PhantomActionLayer.cs");
            if (System.IO.File.Exists(candidate))
                return System.IO.File.ReadAllText(candidate);
            dir = System.IO.Directory.GetParent(dir)?.FullName;
        }

        throw new InvalidOperationException("PhantomActionLayer.cs not found from " + AppContext.BaseDirectory);
    }
}
