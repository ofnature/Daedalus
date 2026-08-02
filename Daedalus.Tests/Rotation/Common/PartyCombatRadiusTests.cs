using System.Numerics;
using Daedalus.Rotation.Common.Helpers;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Moq;
using Xunit;

namespace Daedalus.Tests.Rotation.Common;

/// <summary>
/// "Someone in my party is fighting" only counts as MY fight if they are near enough to be in it.
/// <para>
/// There was no distance limit, so one box pulling put every other box in combat anywhere in the
/// zone — each then auto-engaged whatever hostile was within 25y of ITSELF, and healers began
/// running heal logic while stood across the map. Field-reported 2026-08-02.
/// </para>
/// </summary>
public sealed class PartyCombatRadiusTests
{
    private static IPlayerCharacter PlayerAt(Vector3 position)
    {
        var mock = new Mock<IPlayerCharacter>();
        mock.Setup(x => x.Position).Returns(position);
        return mock.Object;
    }

    private static IBattleChara AllyAt(Vector3 position)
    {
        var mock = new Mock<IBattleChara>();
        mock.Setup(x => x.Position).Returns(position);
        return mock.Object;
    }

    [Fact]
    public void AnAllyFightingBesideYou_CountsAsYourFight()
    {
        Assert.True(PartyCombatHelper.IsWithinPartyCombatRadius(
            PlayerAt(Vector3.Zero), AllyAt(new Vector3(0, 0, 12))));
    }

    [Fact]
    public void AnAllyFightingAcrossTheZone_DoesNot()
    {
        Assert.False(PartyCombatHelper.IsWithinPartyCombatRadius(
            PlayerAt(Vector3.Zero), AllyAt(new Vector3(0, 0, 600))));
    }

    /// <summary>Wide enough for a real pull spread over a large arena, far short of a zone.</summary>
    [Fact]
    public void TheRadiusCoversAnArenaButNotAZone()
    {
        Assert.True(PartyCombatHelper.PartyCombatRadiusYalms >= 30f);
        Assert.True(PartyCombatHelper.PartyCombatRadiusYalms <= 100f);
    }

    [Fact]
    public void TheBoundaryIsInclusive()
    {
        var edge = PartyCombatHelper.PartyCombatRadiusYalms;

        Assert.True(PartyCombatHelper.IsWithinPartyCombatRadius(
            PlayerAt(Vector3.Zero), AllyAt(new Vector3(0, 0, edge))));
        Assert.False(PartyCombatHelper.IsWithinPartyCombatRadius(
            PlayerAt(Vector3.Zero), AllyAt(new Vector3(0, 0, edge + 1f))));
    }
}
