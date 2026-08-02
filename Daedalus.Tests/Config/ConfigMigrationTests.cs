using Daedalus.Config;
using Xunit;

namespace Daedalus.Tests.Config;

/// <summary>
/// Config migrations exist because changing a DEFAULT does nothing for anyone who already has a
/// config — the persisted value wins. Both migrations so far fixed a shipped default that
/// nobody chose deliberately.
/// </summary>
public sealed class ConfigMigrationTests
{
    /// <summary>
    /// Bumping <see cref="Daedalus.Configuration.Version"/> without adding the matching
    /// migration block in Plugin silently skips it for every existing user, so the two are
    /// pinned together here.
    /// </summary>
    [Fact]
    public void CurrentConfigVersion_IsFour()
    {
        Assert.Equal(4, new Daedalus.Configuration().Version);
    }

    /// <summary>
    /// Measured 2026-07-31: phantom nukes land far above the character's own skills, so holding
    /// them for a burst window leaves the damage unspent. Field 2026-08-01: a Lv4 Phantom Red
    /// Mage cast nothing but Cure II for a whole fight with this on — healing ignores the hold
    /// and damage does not.
    /// </summary>
    [Fact]
    public void SaveDamageForBurst_DefaultsOff()
    {
        Assert.False(new PhantomConfig().SaveDamageForBurst);
    }

    [Fact]
    public void FreshConfig_NeedsNoMigration()
    {
        var config = new Daedalus.Configuration();

        Assert.False(config.Version < 4);
        Assert.False(config.Occult.SaveDamageForBurst);
    }
}
