using System;
using System.Text.RegularExpressions;
using Xunit;

namespace Daedalus.Tests.Rotation.Phantom;

/// <summary>
/// Wiring check for the Missile hold in the Phantom Blue Mage damage case. The coordination
/// itself is covered by PhantomActionCoordinationTests; what matters here is that the layer
/// actually asks before firing and actually tells the fleet after, since either half missing
/// leaves four toons volleying at one mob with no symptom in the logs.
/// </summary>
public sealed class PhantomMissileCoordinationTests
{
    private const string Missile = "49086";

    [Fact]
    public void Missile_AsksBeforeFiring()
    {
        var body = BlueMageCase();
        Assert.Matches(new Regex(@"IsPhantomActionReservedByOther\([^)]*,\s*49086\s*\)"), body);
    }

    /// <summary>
    /// Reserved on DISPATCH, not while deciding — a reservation taken at decision time goes out
    /// on the wire every frame the toon merely considers the cast.
    /// </summary>
    [Fact]
    public void Missile_TellsTheFleetOnDispatch()
    {
        var body = BlueMageCase();
        var push = body.IndexOf("onExtraDispatched", StringComparison.Ordinal);
        Assert.True(push >= 0, "the Missile push should carry an onExtraDispatched callback");
        Assert.Matches(new Regex(@"ReservePhantomAction\([^)]*,\s*49086\s*\)"), body);
    }

    /// <summary>The hold is reported, not silent — "it just never fires" is the bug this avoids.</summary>
    [Fact]
    public void HeldMissile_SaysSo()
    {
        Assert.Contains("_pushHolds.Add(\"Occult Missile", BlueMageCase());
    }

    /// <summary>The pre-existing gates still stand in front of the coordination.</summary>
    [Fact]
    public void CriticalEncounterAndFateGate_Survives()
    {
        var body = BlueMageCase();
        Assert.Contains("ShouldMissile(", body);
        Assert.Contains(Missile, body);
    }

    private static string BlueMageCase()
    {
        var source = ReadLayerSource();
        var body = source[source.IndexOf("private void PushDamage", StringComparison.Ordinal)..];
        var start = body.IndexOf("case PhantomJob.PhantomBlueMage:", StringComparison.Ordinal);
        Assert.True(start >= 0, "PushDamage should still have a Phantom Blue Mage case");
        body = body[start..];
        var end = body.IndexOf("case PhantomJob.", "case PhantomJob.PhantomBlueMage:".Length, StringComparison.Ordinal);
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
