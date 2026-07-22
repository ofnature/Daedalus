using System;
using System.Collections.Generic;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Moq;
using Daedalus.Services.Positional.Navigation;
using Daedalus.Tests.Mocks;
using Xunit;

namespace Daedalus.Tests.Services.Positional.Navigation;

/// <summary>
/// Universal cast-vs-movement hold (field origin 2026-07-20, PCT walk-in loop: cast → BMR step →
/// interrupt, repeated until the toon reached BMR's stand distance). Hold only when nothing
/// dangerous lands before the cast completes; dodging always wins.
/// </summary>
public class CastHoldRulesTests
{
    [Fact]
    public void Holds_WhenNothingDangerousInbound()
    {
        // 3.5s motif cast, next damage 30s out, zones far from activating.
        Assert.True(CastHoldRules.ShouldHold(
            castRemainingSeconds: 3.5f,
            nextDamageInSeconds: 30f,
            forbiddenZoneActivationInSeconds: 25f,
            forbiddenZonesCount: 2));
    }

    [Fact]
    public void Releases_WhenDamageLandsInsideCastWindow()
    {
        // Raidwide in 3s, cast needs 3.5s + buffer — BMR must be free to react.
        Assert.False(CastHoldRules.ShouldHold(
            castRemainingSeconds: 3.5f,
            nextDamageInSeconds: 3f,
            forbiddenZoneActivationInSeconds: float.MaxValue,
            forbiddenZonesCount: 0));
    }

    [Fact]
    public void Releases_WhenZoneActivatesInsideCastWindow()
    {
        Assert.False(CastHoldRules.ShouldHold(
            castRemainingSeconds: 2f,
            nextDamageInSeconds: float.MaxValue,
            forbiddenZoneActivationInSeconds: 1.5f,
            forbiddenZonesCount: 1));
    }

    [Fact]
    public void Releases_WhenZoneAlreadyLive()
    {
        // Activation 0 (live zone) always releases — BMR may need to move NOW.
        Assert.False(CastHoldRules.ShouldHold(
            castRemainingSeconds: 1f,
            nextDamageInSeconds: float.MaxValue,
            forbiddenZoneActivationInSeconds: 0f,
            forbiddenZonesCount: 1));
    }

    [Fact]
    public void Holds_WhenNoBmrData()
    {
        // No BMR hints (MaxValue sentinels, zero zones): nothing known to dodge — hold freely.
        Assert.True(CastHoldRules.ShouldHold(
            castRemainingSeconds: 4f,
            nextDamageInSeconds: float.MaxValue,
            forbiddenZoneActivationInSeconds: float.MaxValue,
            forbiddenZonesCount: 0));
    }

    [Fact]
    public void BufferExtendsTheWindow()
    {
        // Damage 4.6s out, cast 3.5s: inside the 1.5s reaction buffer → release; just outside → hold.
        Assert.False(CastHoldRules.ShouldHold(3.5f, 4.6f, float.MaxValue, 0));
        Assert.True(CastHoldRules.ShouldHold(3.5f, 5.2f, float.MaxValue, 0));
    }
}

/// <summary>
/// Edge behavior of the service itself: pause exactly once per cast, always release (cast end,
/// danger, watchdog), and never engage when disabled or BMR is missing.
/// </summary>
public class CastMovementHoldServiceTests
{
    private readonly Mock<IBossModSafetyService> _bossMod = new();
    private readonly Mock<IObjectTable> _objectTable = new();
    private readonly Mock<IPlayerCharacter> _player;
    private readonly Configuration _config = new();
    private readonly List<bool> _pauseCalls = new();
    private readonly CastMovementHoldService _service;
    private DateTime _now = new(2026, 7, 20, 12, 0, 0, DateTimeKind.Utc);

    public CastMovementHoldServiceTests()
    {
        _bossMod.Setup(x => x.IsAvailable).Returns(true);
        _bossMod.Setup(x => x.NextDamageInSeconds).Returns(float.MaxValue);
        _bossMod.Setup(x => x.ForbiddenZoneActivationInSeconds).Returns(float.MaxValue);
        _bossMod.Setup(x => x.ForbiddenZonesCount).Returns(0);

        _player = MockBuilders.CreateMockPlayerCharacter();
        _objectTable.Setup(x => x.LocalPlayer).Returns(_player.Object);

        _service = new CastMovementHoldService(
            new Mock<IDalamudPluginInterface>().Object,
            _config,
            _bossMod.Object,
            _objectTable.Object,
            new Mock<IPluginLog>().Object)
        {
            UtcNow = () => _now,
            PauseInvokerOverride = paused => _pauseCalls.Add(paused),
        };
    }

    private void SetCasting(bool casting, float total = 3.5f, float progress = 0.5f)
    {
        _player.Setup(x => x.IsCasting).Returns(casting);
        _player.Setup(x => x.TotalCastTime).Returns(total);
        _player.Setup(x => x.CurrentCastTime).Returns(progress);
    }

    [Fact]
    public void PausesOncePerCast_ReleasesOnCastEnd()
    {
        SetCasting(true);
        _service.Update();
        _service.Update(); // still casting — no duplicate IPC spam
        Assert.Equal(new[] { true }, _pauseCalls);

        SetCasting(false);
        _service.Update();
        Assert.Equal(new[] { true, false }, _pauseCalls);
    }

    [Fact]
    public void ReleasesImmediately_WhenDangerAppearsMidCast()
    {
        SetCasting(true);
        _service.Update();
        Assert.Equal(new[] { true }, _pauseCalls);

        _bossMod.Setup(x => x.NextDamageInSeconds).Returns(1f);
        _service.Update();
        Assert.Equal(new[] { true, false }, _pauseCalls); // dodging wins; the cast dies
    }

    [Fact]
    public void Watchdog_ForceReleasesAndLatchesForTheCast()
    {
        SetCasting(true);
        _service.Update();

        _now = _now.AddSeconds(9);
        _service.Update();
        Assert.Equal(new[] { true, false }, _pauseCalls);

        // Same stuck cast keeps NOT re-pausing; a fresh cast may hold again.
        _service.Update();
        Assert.Equal(new[] { true, false }, _pauseCalls);

        SetCasting(false);
        _service.Update();
        SetCasting(true);
        _service.Update();
        Assert.Equal(new[] { true, false, true }, _pauseCalls);
    }

    [Fact]
    public void Disabled_NeverPauses()
    {
        _config.Nav.HoldBmrMovementWhileCasting = false;
        SetCasting(true);
        _service.Update();
        Assert.Empty(_pauseCalls);
    }

    [Fact]
    public void BmrMissing_NeverPauses()
    {
        _bossMod.Setup(x => x.IsAvailable).Returns(false);
        SetCasting(true);
        _service.Update();
        Assert.Empty(_pauseCalls);
    }

    [Fact]
    public void Dispose_ReleasesAnActiveHold()
    {
        SetCasting(true);
        _service.Update();
        _service.Dispose();
        Assert.Equal(new[] { true, false }, _pauseCalls);
    }
}
