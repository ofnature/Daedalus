using System.Numerics;
using Daedalus.Config;
using Daedalus.Services.Positional.Navigation;
using Moq;
using Xunit;

namespace Daedalus.Tests.Services.Positional.Navigation;

/// <summary>
/// "Movement polled from" (Settings ▸ General ▸ Boss handling): the steering reads go to the chosen
/// plugin; every safety question stays with the mechanics engine.
/// </summary>
public class MovementPollingRouterTests
{
    private readonly Mock<IBossModSafetyService> _bossMod = new();
    private readonly Mock<IBossModSafetyService> _minerva = new();
    private BossHandling _engine = BossHandling.BossMod;
    private MovementPolling _polling = MovementPolling.MechanicsEngine;

    public MovementPollingRouterTests()
    {
        _bossMod.Setup(s => s.IsBmrNavigating).Returns(true);
        _bossMod.Setup(s => s.BmrNaviTarget).Returns(new Vector3(1, 0, 1));
        _bossMod.Setup(s => s.NextDamageInSeconds).Returns(11f);
        _minerva.Setup(s => s.IsBmrNavigating).Returns(false);
        _minerva.Setup(s => s.NextDamageInSeconds).Returns(22f);
    }

    private BossHandlingRouter Router() => new(_bossMod.Object, _minerva.Object, () => _engine, () => _polling);

    [Fact]
    public void ByDefaultMovementFollowsTheEngine()
    {
        Assert.True(Router().IsBmrNavigating);
        _engine = BossHandling.Minerva;
        Assert.False(Router().IsBmrNavigating);
    }

    [Fact]
    public void NamingAPluginOverridesTheEngine()
    {
        _engine = BossHandling.Minerva;
        _polling = MovementPolling.BossMod;
        var router = Router();
        Assert.True(router.IsBmrNavigating);
        Assert.Equal(new Vector3(1, 0, 1), router.BmrNaviTarget);
    }

    [Fact]
    public void SafetyQuestionsStayWithTheEngine()
    {
        _engine = BossHandling.Minerva;
        _polling = MovementPolling.BossMod;
        Assert.Equal(22f, Router().NextDamageInSeconds);
    }

    /// <summary>vnavmesh only: no plugin is asked, so nothing can hold Daedalus's own movement.</summary>
    [Fact]
    public void VnavmeshOnlyNeverReportsSteering()
    {
        _polling = MovementPolling.VnavmeshOnly;
        var router = Router();
        Assert.False(router.IsBmrNavigating);
        Assert.Null(router.BmrNaviTarget);
        Assert.Equal("vnavmesh only", router.MovementPolledFrom);
    }

    /// <summary>The setting is read per call, so a change applies without a reload.</summary>
    [Fact]
    public void SwitchingTakesEffectAtOnce()
    {
        var router = Router();
        Assert.True(router.IsBmrNavigating);
        _polling = MovementPolling.Minerva;
        Assert.False(router.IsBmrNavigating);
    }

    /// <summary>Routers built without the setting (the BMR preset service's) keep following the engine.</summary>
    [Fact]
    public void WithoutTheSettingItFollowsTheEngine()
    {
        var router = new BossHandlingRouter(_bossMod.Object, _minerva.Object, () => BossHandling.BossMod);
        Assert.True(router.IsBmrNavigating);
        Assert.Equal("BossMod Reborn AI (mechanics engine)", router.MovementPolledFrom);
    }
}
