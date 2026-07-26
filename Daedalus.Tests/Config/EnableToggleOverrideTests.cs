using Daedalus;
using Xunit;

namespace Daedalus.Tests.Config;

/// <summary>
/// Field reports 2026-07-26 (NIN, then GNB on v0.1.44): automation bridges bypassed the
/// master switch (EffectiveEnabled = Enabled || Override). A session-scoped suppression
/// wasn't enough — Questionable releases/reacquires the override PER MOB, so it expired
/// between kills, and disabling before the quest started never suppressed at all.
/// Contract now: an explicit Disable blocks automation-driven combat PERSISTENTLY until
/// the user re-enables; a never-touched toggle keeps the zero-setup automation contract.
/// </summary>
[Collection("ExternalCombatOverrideState")]
public class EnableToggleOverrideTests
{
    private static Configuration Fresh()
    {
        // Process-wide static — reset so tests are order-independent.
        ExternalCombatOverrideState.Active = false;
        ExternalCombatOverrideState.Source = "";
        return new Configuration { Enabled = false };
    }

    [Fact]
    public void UserDisable_WhileAutomationHoldsOverride_StopsTheRotation()
    {
        var config = Fresh();
        config.ExternalCombatOverride = true; // quest bridge engaged
        Assert.True(config.EffectiveEnabled);

        config.SetEnabledByUser(false);

        Assert.False(config.EffectiveEnabled);
        Assert.False(config.ExternalCombatOverride);
    }

    [Fact]
    public void PerMobReleaseReacquire_StaysBlocked()
    {
        // The GNB field report: Questionable drops and re-takes the override per kill.
        var config = Fresh();
        config.SetEnabledByUser(false);

        config.ExternalCombatOverride = true;  // mob 1
        Assert.False(config.EffectiveEnabled);
        config.ExternalCombatOverride = false; // "combat done."
        config.ExternalCombatOverride = true;  // mob 2

        Assert.False(config.EffectiveEnabled);
    }

    [Fact]
    public void DisableBeforeAutomationStarts_StillBlocks()
    {
        var config = Fresh();
        config.SetEnabledByUser(false);       // disabled while idle

        config.ExternalCombatOverride = true; // quest starts later

        Assert.False(config.EffectiveEnabled);
    }

    [Fact]
    public void UserReEnable_AutomationWorksAgain()
    {
        var config = Fresh();
        config.SetEnabledByUser(false);
        config.SetEnabledByUser(true);

        config.ExternalCombatOverride = true;

        Assert.True(config.EffectiveEnabled);
        Assert.True(config.ExternalCombatOverride);
    }

    [Fact]
    public void NeverTouchedToggle_ZeroSetupAutomationStillWorks()
    {
        var config = Fresh();                 // fresh install: Enabled false, never toggled

        config.ExternalCombatOverride = true;

        Assert.True(config.EffectiveEnabled);
    }

    [Fact]
    public void Suppression_IsPersistedConfigState()
    {
        // Survives restarts: the flag rides the saved config, not transient statics.
        var config = Fresh();
        config.SetEnabledByUser(false);

        Assert.True(config.AutomationSuppressedByDisable);

        var reloaded = new Configuration
        {
            Enabled = config.Enabled,
            AutomationSuppressedByDisable = config.AutomationSuppressedByDisable,
        };
        reloaded.ExternalCombatOverride = true;

        Assert.False(reloaded.EffectiveEnabled);
    }
}
