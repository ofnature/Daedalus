using System;
using System.Linq;
using System.Text.RegularExpressions;
using Daedalus.Data;
using Xunit;

namespace Daedalus.Tests.Rotation.Phantom;

/// <summary>
/// Phantom Monk's damage band. Occult Counter shipped in the catalog but was never pushed by any
/// band, so a Monk only ever had the Kick; and the Kick is a leap, which the stand-still cast
/// safety says nothing about. Both are shape assertions against the source, following the
/// PushDamage switch the way <see cref="PhantomDamageBandJobsTests"/> does.
/// </summary>
public sealed class PhantomMonkBandTests
{
    private const uint OccultCounter = 41596;
    private const uint PhantomKick = 41595;

    [Fact]
    public void OccultCounter_IsCatalogued_AsMonkLevelTwo()
    {
        var def = PhantomActions.All.Single(a => a.ActionId == OccultCounter);
        Assert.Equal(PhantomJob.Monk, def.Job);
        Assert.Equal(2, def.RequiredLevel);
    }

    /// <summary>
    /// "Can only be executed immediately after parrying an attack" is a game-side gate, and
    /// GetActionStatus is the only thing that reports it. Pushing the action unconditionally just
    /// burns a weave slot on a refusal for the rest of the fight.
    /// </summary>
    [Fact]
    public void OccultCounter_IsPushed_BehindTheActionManagerParryGate()
    {
        var body = MonkCase();
        Assert.Contains(OccultCounter.ToString(), body);
        Assert.Matches(new Regex(@"GetActionStatusCode\(\s*41596\s*,[^)]*\)\s*==\s*0"), body);
    }

    /// <summary>
    /// The parry window is one attack wide; the Kick is a 30s cooldown. If the two ever tie for a
    /// weave slot the Counter has to win, or the window is gone.
    /// </summary>
    [Fact]
    public void OccultCounter_OutranksTheKick()
    {
        var body = MonkCase();
        var counter = Regex.Match(body, @"41596,\s*job,\s*level,\s*(PrioDamage[^,]*),");
        var kick = Regex.Match(body, @"41595,\s*job,\s*level,\s*(PrioDamage[^,]*),");
        Assert.True(counter.Success && kick.Success, "both Monk actions should be pushed with a damage priority");
        Assert.Equal("PrioDamage", counter.Groups[1].Value.Trim());
        Assert.Equal("PrioDamage + 1", kick.Groups[1].Value.Trim());
    }

    /// <summary>A leap needs floor and a clear flight path — see TargetedDashGuard.</summary>
    [Fact]
    public void PhantomKick_IsGuarded_BeforeItLeaps()
    {
        var body = MonkCase();
        var guard = body.IndexOf("IsDashSafe(", StringComparison.Ordinal);
        var push = body.IndexOf(PhantomKick.ToString(), StringComparison.Ordinal);
        Assert.True(guard >= 0, "Phantom Kick should consult the dash guard");
        Assert.True(guard < push, "the dash guard has to be checked before the push, not after");
    }

    /// <summary>The Monk case, from its label to the next one.</summary>
    private static string MonkCase()
    {
        var source = ReadLayerSource();
        var body = source[source.IndexOf("private void PushDamage", StringComparison.Ordinal)..];
        var start = body.IndexOf("case PhantomJob.Monk:", StringComparison.Ordinal);
        Assert.True(start >= 0, "PushDamage should still have a Monk case");
        body = body[start..];
        var end = body.IndexOf("case PhantomJob.", start == 0 ? 1 : "case PhantomJob.Monk:".Length, StringComparison.Ordinal);
        return end >= 0 ? body[..end] : body;
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
