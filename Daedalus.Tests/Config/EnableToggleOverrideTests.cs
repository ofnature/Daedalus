using Daedalus;
using Xunit;

namespace Daedalus.Tests.Config;

/// <summary>
/// Field report 2026-07-26: NIN kept fighting after the user hit Disable — the
/// automation bridge's ExternalCombatOverride bypassed the master switch
/// (EffectiveEnabled = Enabled || Override). Contract now: an explicit user Disable
/// ALWAYS stops the rotation, suppressing the current automation session's override;
/// the next session (or re-enabling) works normally.
/// </summary>
[Collection("ExternalCombatOverrideState")]
public class EnableToggleOverrideTests
{
    private static Configuration Fresh()
    {
        // Process-wide static — reset so tests are order-independent.
        ExternalCombatOverrideState.Active = false;
        ExternalCombatOverrideState.UserSuppressed = false;
        ExternalCombatOverrideState.Source = "";
        return new Configuration { Enabled = false };
    }

    [Fact]
    public void UserDisable_WhileAutomationHoldsOverride_StopsTheRotation()
    {
        var config = Fresh();
        config.ExternalCombatOverride = true; // quest bridge engaged
        Assert.True(config.EffectiveEnabled);

        config.SetEnabledByUser(false);       // the NIN field-report scenario

        Assert.False(config.EffectiveEnabled);
        Assert.False(config.ExternalCombatOverride);
    }

    [Fact]
    public void OverrideRelease_EndsTheSuppression_NextSessionWorks()
    {
        var config = Fresh();
        config.ExternalCombatOverride = true;
        config.SetEnabledByUser(false);
        Assert.False(config.EffectiveEnabled);

        config.ExternalCombatOverride = false; // quest ends — session over
        config.ExternalCombatOverride = true;  // next automation session

        Assert.True(config.EffectiveEnabled);  // zero-setup automation still works
    }

    [Fact]
    public void UserReEnable_ClearsTheSuppression()
    {
        var config = Fresh();
        config.ExternalCombatOverride = true;
        config.SetEnabledByUser(false);

        config.SetEnabledByUser(true);

        Assert.True(config.EffectiveEnabled);
        Assert.True(config.ExternalCombatOverride);
    }

    [Fact]
    public void DisableWithoutOverride_DoesNotPoisonLaterAutomation()
    {
        var config = Fresh();
        config.SetEnabledByUser(false);       // plain disable, no automation running

        config.ExternalCombatOverride = true; // automation starts later

        Assert.True(config.EffectiveEnabled); // zero-setup contract intact
    }

    [Fact]
    public void BridgeReassertingTrue_DoesNotClearSuppression()
    {
        var config = Fresh();
        config.ExternalCombatOverride = true;
        config.SetEnabledByUser(false);

        config.ExternalCombatOverride = true; // bridge re-asserts every frame

        Assert.False(config.EffectiveEnabled);
    }
}
