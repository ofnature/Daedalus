using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Daedalus.Data;
using Daedalus.Rotation.Phantom;
using Xunit;

namespace Daedalus.Tests.Rotation.Phantom;

/// <summary>
/// The damage band reports "damage held for burst window" only for jobs listed in
/// DamageBandJobs. When a job has damage actions but is missing from that list, the hold
/// returns SILENTLY and the Duty tab reads "idle — nothing eligible" instead.
/// <para>
/// Field 2026-08-01: every North Horn job was missing. A Lv4 Phantom Red Mage on an ice-weak
/// mob with Blizzard slotted fired nothing but Cure II for a whole fight — survival ignores the
/// hold, so it looked like the damage band was broken rather than held.
/// </para>
/// </summary>
public sealed class PhantomDamageBandJobsTests
{
    private static HashSet<PhantomJob> DamageBandJobs()
    {
        var field = typeof(PhantomActionLayer).GetField(
            "DamageBandJobs", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(field);
        return (HashSet<PhantomJob>)field!.GetValue(null)!;
    }

    [Theory]
    [InlineData(PhantomJob.PhantomRedMage)]
    [InlineData(PhantomJob.PhantomBlackMage)]
    [InlineData(PhantomJob.PhantomWhiteMage)]
    [InlineData(PhantomJob.PhantomNinja)]
    [InlineData(PhantomJob.PhantomDragoon)]
    [InlineData(PhantomJob.PhantomSummoner)]
    public void NorthHornDamageJobs_AreReported(PhantomJob job)
    {
        Assert.Contains(job, DamageBandJobs());
    }

    [Theory]
    [InlineData(PhantomJob.Berserker)]
    [InlineData(PhantomJob.Cannoneer)]
    [InlineData(PhantomJob.Thief)]
    public void SouthHornDamageJobs_StayReported(PhantomJob job)
    {
        Assert.Contains(job, DamageBandJobs());
    }

    /// <summary>Drain Touch fires before the hold, so claiming it was held would be a lie.</summary>
    [Fact]
    public void Necromancer_IsDeliberatelyExcluded()
    {
        Assert.DoesNotContain(PhantomJob.Necromancer, DamageBandJobs());
    }

    /// <summary>
    /// The list has to stay in step with the switch in PushDamage — it drifted once already and
    /// cost a whole job its damage output with no diagnostic. Read the case labels straight out
    /// of the source so adding a job without listing it fails here.
    /// </summary>
    [Fact]
    public void EveryJobWithADamageCase_IsInTheList()
    {
        var source = ReadLayerSource();
        var body = source[source.IndexOf("private void PushDamage", StringComparison.Ordinal)..];
        body = body[..body.IndexOf("private void PushNecromancerDoomNukes", StringComparison.Ordinal)];

        var cases = Regex.Matches(body, @"case PhantomJob\.(\w+):")
            .Select(m => Enum.Parse<PhantomJob>(m.Groups[1].Value))
            .Where(j => j != PhantomJob.Necromancer)
            .Distinct()
            .ToList();

        Assert.NotEmpty(cases);
        var listed = DamageBandJobs();
        foreach (var job in cases)
            Assert.Contains(job, listed);
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
