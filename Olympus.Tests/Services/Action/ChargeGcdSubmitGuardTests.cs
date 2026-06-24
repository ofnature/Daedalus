using System;
using Olympus.Services.Action;
using Xunit;

namespace Olympus.Tests.Services.Action;

public sealed class ChargeGcdSubmitGuardTests
{
    private static readonly DateTime Base = new(2026, 6, 24, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ShouldBlock_WhenNoPriorSubmit_ReturnsFalse()
    {
        Assert.False(ChargeGcdSubmitGuard.ShouldBlock(DateTime.MinValue, 2.5f, Base));
    }

    [Fact]
    public void ShouldBlock_WithinGcdWindow_ReturnsTrue()
    {
        var last = Base.AddMilliseconds(-145);
        Assert.True(ChargeGcdSubmitGuard.ShouldBlock(last, 2.5f, Base));
    }

    [Fact]
    public void ShouldBlock_AfterFullGcdWindow_ReturnsFalse()
    {
        var last = Base.AddSeconds(-2.5);
        Assert.False(ChargeGcdSubmitGuard.ShouldBlock(last, 2.5f, Base));
    }

    [Fact]
    public void ShouldBlock_UsesMinimumTwoSecondFloor_WhenGcdDurationUnknown()
    {
        var last = Base.AddSeconds(-1.5);
        Assert.True(ChargeGcdSubmitGuard.ShouldBlock(last, 0f, Base));
    }
}
