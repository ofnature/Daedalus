using Daedalus.Data;
using Daedalus.Services.Positional;
using Daedalus.Services.Positional.Navigation;

namespace Daedalus.Tests.Services.Positional;

public class SamuraiPositionalAnticipationProviderTests
{
    private readonly SamuraiPositionalAnticipationProvider _provider = new();

    [Fact]
    public void GetAnticipatedPositional_AfterHakazeMissingFugetsu_ReturnsEarlyRear()
    {
        var ctx = BaseContext with
        {
            LastComboAction = SAMActions.Hakaze.ActionId,
            PlayerLevel = SAMActions.Gekko.MinLevel,
            HasFugetsu = false,
            HasFuka = true,
            FukaRemainingSeconds = 30f,
        };

        var result = _provider.GetAnticipatedPositional(ctx);

        Assert.NotNull(result);
        Assert.Equal(PositionalType.Rear, result.Value.Required);
        Assert.Equal(SAMActions.Gekko.ActionId, result.Value.UpcomingFinisherActionId);
        Assert.Equal(PositionalAnticipationReason.ComboSetup, result.Value.Reason);
    }

    [Fact]
    public void GetAnticipatedPositional_AfterGyofuLowFugetsu_ReturnsEarlyRear()
    {
        var ctx = BaseContext with
        {
            LastComboAction = SAMActions.Gyofu.ActionId,
            PlayerLevel = 100,
            HasFugetsu = true,
            FugetsuRemainingSeconds = 5f,
            HasFuka = true,
            FukaRemainingSeconds = 30f,
        };

        var result = _provider.GetAnticipatedPositional(ctx);

        Assert.NotNull(result);
        Assert.Equal(PositionalType.Rear, result.Value.Required);
    }

    [Fact]
    public void GetAnticipatedPositional_AfterHakazeMissingFukaOnly_ReturnsEarlyFlank()
    {
        var ctx = BaseContext with
        {
            LastComboAction = SAMActions.Hakaze.ActionId,
            PlayerLevel = SAMActions.Kasha.MinLevel,
            HasFugetsu = true,
            FugetsuRemainingSeconds = 30f,
            HasFuka = false,
        };

        var result = _provider.GetAnticipatedPositional(ctx);

        Assert.NotNull(result);
        Assert.Equal(PositionalType.Flank, result.Value.Required);
        Assert.Equal(SAMActions.Kasha.ActionId, result.Value.UpcomingFinisherActionId);
        Assert.Equal(PositionalAnticipationReason.ComboSetup, result.Value.Reason);
    }

    [Fact]
    public void GetAnticipatedPositional_AfterHakazeBothBuffsHealthy_ReturnsNull()
    {
        var ctx = BaseContext with
        {
            LastComboAction = SAMActions.Hakaze.ActionId,
            PlayerLevel = 100,
            HasFugetsu = true,
            FugetsuRemainingSeconds = 30f,
            HasFuka = true,
            FukaRemainingSeconds = 30f,
        };

        Assert.Null(_provider.GetAnticipatedPositional(ctx));
    }

    [Fact]
    public void GetAnticipatedPositional_AfterHakazeBothBuffsLow_PrefersEarlyRear()
    {
        var ctx = BaseContext with
        {
            LastComboAction = SAMActions.Hakaze.ActionId,
            PlayerLevel = 100,
            HasFugetsu = false,
            HasFuka = false,
        };

        var result = _provider.GetAnticipatedPositional(ctx);

        Assert.NotNull(result);
        Assert.Equal(PositionalType.Rear, result.Value.Required);
    }

    [Fact]
    public void GetAnticipatedPositional_AfterJinpuOverridesEarlyFlankSignal_ReturnsRear()
    {
        var ctx = BaseContext with
        {
            LastComboAction = SAMActions.Jinpu.ActionId,
            PlayerLevel = 100,
            HasFugetsu = true,
            FugetsuRemainingSeconds = 30f,
            HasFuka = false,
        };

        var result = _provider.GetAnticipatedPositional(ctx);

        Assert.NotNull(result);
        Assert.Equal(PositionalType.Rear, result.Value.Required);
    }

    [Fact]
    public void GetAnticipatedPositional_AfterShifuOverridesEarlyRearSignal_ReturnsFlank()
    {
        var ctx = BaseContext with
        {
            LastComboAction = SAMActions.Shifu.ActionId,
            PlayerLevel = 100,
            HasFugetsu = false,
            HasFuka = true,
            FukaRemainingSeconds = 30f,
        };

        var result = _provider.GetAnticipatedPositional(ctx);

        Assert.NotNull(result);
        Assert.Equal(PositionalType.Flank, result.Value.Required);
    }

    [Fact]
    public void GetAnticipatedPositional_AfterJinpu_ReturnsRearGekko()
    {
        var ctx = BaseContext with
        {
            LastComboAction = SAMActions.Jinpu.ActionId,
            PlayerLevel = SAMActions.Gekko.MinLevel,
        };

        var result = _provider.GetAnticipatedPositional(ctx);

        Assert.NotNull(result);
        Assert.Equal(PositionalType.Rear, result.Value.Required);
        Assert.Equal(SAMActions.Gekko.ActionId, result.Value.UpcomingFinisherActionId);
        Assert.Equal(PositionalAnticipationReason.ComboSetup, result.Value.Reason);
    }

    [Fact]
    public void GetAnticipatedPositional_AfterShifu_ReturnsFlankKasha()
    {
        var ctx = BaseContext with
        {
            LastComboAction = SAMActions.Shifu.ActionId,
            PlayerLevel = SAMActions.Kasha.MinLevel,
        };

        var result = _provider.GetAnticipatedPositional(ctx);

        Assert.NotNull(result);
        Assert.Equal(PositionalType.Flank, result.Value.Required);
        Assert.Equal(SAMActions.Kasha.ActionId, result.Value.UpcomingFinisherActionId);
        Assert.Equal(PositionalAnticipationReason.ComboSetup, result.Value.Reason);
    }

    [Fact]
    public void GetAnticipatedPositional_MeikyoMissingGetsuHasKa_ReturnsRear()
    {
        var ctx = BaseContext with
        {
            PlayerLevel = 100,
            HasMeikyoShisui = true,
            HasGetsuSen = false,
            HasKaSen = true,
        };

        var result = _provider.GetAnticipatedPositional(ctx);

        Assert.NotNull(result);
        Assert.Equal(PositionalType.Rear, result.Value.Required);
        Assert.Equal(PositionalAnticipationReason.MeikyoSen, result.Value.Reason);
    }

    [Fact]
    public void GetAnticipatedPositional_MeikyoMissingKa_ReturnsFlank()
    {
        var ctx = BaseContext with
        {
            PlayerLevel = 100,
            HasMeikyoShisui = true,
            HasGetsuSen = true,
            HasKaSen = false,
        };

        var result = _provider.GetAnticipatedPositional(ctx);

        Assert.NotNull(result);
        Assert.Equal(PositionalType.Flank, result.Value.Required);
        Assert.Equal(PositionalAnticipationReason.MeikyoSen, result.Value.Reason);
    }

    [Fact]
    public void GetAnticipatedPositional_MeikyoBothSen_ReturnsNull()
    {
        var ctx = BaseContext with
        {
            PlayerLevel = 100,
            HasMeikyoShisui = true,
            HasGetsuSen = true,
            HasKaSen = true,
        };

        Assert.Null(_provider.GetAnticipatedPositional(ctx));
    }

    // After any Iaijutsu the Sen are spent and Meikyo starts with both Getsu and Ka missing. This used to
    // anticipate nothing while the rotation fired Gekko, so the mover was never told to go behind: 16 of Saar's
    // 22 missed positionals on 2026-09-25 were that Gekko, thrown from the flank.
    [Fact]
    public void GetAnticipatedPositional_MeikyoNeitherSen_NotAtFlank_ReturnsRearGekko()
    {
        var ctx = BaseContext with
        {
            PlayerLevel = 100,
            HasMeikyoShisui = true,
            HasGetsuSen = false,
            HasKaSen = false,
        };

        var result = _provider.GetAnticipatedPositional(ctx);

        Assert.NotNull(result);
        Assert.Equal(PositionalType.Rear, result.Value.Required);
        Assert.Equal(SAMActions.Gekko.ActionId, result.Value.UpcomingFinisherActionId);
        Assert.Equal(PositionalAnticipationReason.MeikyoSen, result.Value.Reason);
    }

    [Fact]
    public void GetAnticipatedPositional_MeikyoNeitherSen_AtFlank_ReturnsFlankKasha()
    {
        var ctx = BaseContext with
        {
            PlayerLevel = 100,
            IsAtFlank = true,
            HasMeikyoShisui = true,
            HasGetsuSen = false,
            HasKaSen = false,
        };

        var result = _provider.GetAnticipatedPositional(ctx);

        Assert.NotNull(result);
        Assert.Equal(PositionalType.Flank, result.Value.Required);
        Assert.Equal(SAMActions.Kasha.ActionId, result.Value.UpcomingFinisherActionId);
    }

    [Fact]
    public void GetAnticipatedPositional_MeikyoNeitherSen_AtRear_ReturnsRearGekko()
    {
        var ctx = BaseContext with
        {
            PlayerLevel = 100,
            IsAtRear = true,
            HasMeikyoShisui = true,
        };

        Assert.Equal(PositionalType.Rear, _provider.GetAnticipatedPositional(ctx)!.Value.Required);
    }

    // The recorded case exactly: Gyofu, then Meikyo with no Sen, on the flank, Fugetsu low. The Hakaze/Gyofu
    // stage said Rear (Jinpu next), but the Meikyo stack goes first and it is Kasha from here.
    [Fact]
    public void GetAnticipatedPositional_MeikyoAfterGyofu_FollowsMeikyoNotEarlyRear()
    {
        var ctx = BaseContext with
        {
            LastComboAction = SAMActions.Gyofu.ActionId,
            PlayerLevel = 100,
            IsAtFlank = true,
            HasMeikyoShisui = true,
            HasFugetsu = true,
            FugetsuRemainingSeconds = 3f,
        };

        Assert.Equal(PositionalType.Flank, _provider.GetAnticipatedPositional(ctx)!.Value.Required);
    }

    [Fact]
    public void GetAnticipatedPositional_MeikyoOverridesJinpu_ReturnsMeikyoKasha()
    {
        var ctx = BaseContext with
        {
            LastComboAction = SAMActions.Jinpu.ActionId,
            PlayerLevel = 100,
            HasMeikyoShisui = true,
            HasGetsuSen = true,
            HasKaSen = false,
        };

        Assert.Equal(PositionalType.Flank, _provider.GetAnticipatedPositional(ctx)!.Value.Required);
    }

    [Fact]
    public void GetAnticipatedPositional_SuppressMeikyoAnticipation_SkipsMeikyoBranch()
    {
        var ctx = BaseContext with
        {
            PlayerLevel = 100,
            HasMeikyoShisui = true,
            HasGetsuSen = false,
            HasKaSen = true,
            SuppressMeikyoAnticipation = true,
        };

        Assert.Null(_provider.GetAnticipatedPositional(ctx));
    }

    [Fact]
    public void GetAnticipatedPositional_HasTrueNorth_ReturnsNull()
    {
        var ctx = BaseContext with
        {
            LastComboAction = SAMActions.Jinpu.ActionId,
            PlayerLevel = SAMActions.Gekko.MinLevel,
            HasTrueNorth = true,
        };

        Assert.Null(_provider.GetAnticipatedPositional(ctx));
    }

    private static PositionalAnticipationContext BaseContext => new(
        LastComboAction: 0,
        PlayerLevel: 100,
        HasTrueNorth: false,
        TargetHasPositionalImmunity: false,
        IsAtRear: false,
        IsAtFlank: false);
}
