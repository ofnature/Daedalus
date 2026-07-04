using Daedalus.Ipc;
using Xunit;

namespace Daedalus.Tests.Ipc;

public class RsrCompatIpcTests
{
    // The numeric layout is the IPC wire contract with Questionable / other RSR-driving plugins
    // (they declare their own enum copy and Dalamud converts by value). Reordering breaks it.
    [Fact]
    public void StateCommandType_NumericValues_MatchRsrContract()
    {
        Assert.Equal(0, (byte)RsrCompatIpc.StateCommandType.Off);
        Assert.Equal(1, (byte)RsrCompatIpc.StateCommandType.Auto);
        Assert.Equal(2, (byte)RsrCompatIpc.StateCommandType.TargetOnly);
        Assert.Equal(3, (byte)RsrCompatIpc.StateCommandType.Manual);
        Assert.Equal(4, (byte)RsrCompatIpc.StateCommandType.AutoDuty);
        Assert.Equal(5, (byte)RsrCompatIpc.StateCommandType.Henched);
    }

    [Theory]
    [InlineData(RsrCompatIpc.StateCommandType.Manual)]
    [InlineData(RsrCompatIpc.StateCommandType.Auto)]
    [InlineData(RsrCompatIpc.StateCommandType.AutoDuty)]
    [InlineData(RsrCompatIpc.StateCommandType.Henched)]
    public void MapsToEnabled_ActiveModes_EnableRotation(RsrCompatIpc.StateCommandType mode)
    {
        Assert.True(RsrCompatIpc.MapsToEnabled(mode));
    }

    [Theory]
    [InlineData(RsrCompatIpc.StateCommandType.Off)]
    [InlineData(RsrCompatIpc.StateCommandType.TargetOnly)] // "select targets but perform no actions"
    public void MapsToEnabled_PassiveModes_DisableRotation(RsrCompatIpc.StateCommandType mode)
    {
        Assert.False(RsrCompatIpc.MapsToEnabled(mode));
    }

    [Theory]
    [InlineData(541u)]   // striking dummy
    [InlineData(13078u)] // timeworn striking dummy
    public void IsTrainingDummy_DummyNameIds_True(uint nameId)
    {
        Assert.True(RsrCompatIpc.IsTrainingDummy(nameId));
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(1u)]
    [InlineData(540u)]
    [InlineData(542u)]
    public void IsTrainingDummy_OtherNameIds_False(uint nameId)
    {
        Assert.False(RsrCompatIpc.IsTrainingDummy(nameId));
    }

    [Fact]
    public void EffectiveEnabled_TruthTable()
    {
        var config = new Configuration();

        config.Enabled = false;
        config.ExternalCombatOverride = false;
        Assert.False(config.EffectiveEnabled);

        config.ExternalCombatOverride = true;
        Assert.True(config.EffectiveEnabled);

        config.Enabled = true;
        config.ExternalCombatOverride = false;
        Assert.True(config.EffectiveEnabled);

        config.ExternalCombatOverride = true;
        Assert.True(config.EffectiveEnabled);
    }

    [Fact]
    public void ExternalCombatOverride_DefaultsOff_AndIsNotPersisted()
    {
        var config = new Configuration();
        Assert.False(config.ExternalCombatOverride);

        // Transient by design: a crash mid-task must not leave the rotation enabled on next load.
        // Dalamud persists IPluginConfiguration via Newtonsoft — the override must never hit disk.
        config.ExternalCombatOverride = true;
        var json = Newtonsoft.Json.JsonConvert.SerializeObject(config);
        Assert.DoesNotContain(nameof(Configuration.ExternalCombatOverride), json);
        Assert.DoesNotContain(nameof(Configuration.EffectiveEnabled), json);
    }
}
