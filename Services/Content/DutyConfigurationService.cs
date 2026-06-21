using Olympus;
using Olympus.Config;

namespace Olympus.Services.Content;

/// <summary>
/// Maintains a rotation-facing configuration snapshot with optional duty-profile overlays.
/// Does not mutate the persisted user configuration.
/// </summary>
public sealed class DutyConfigurationService : IDutyConfigurationService
{
    private readonly Configuration _savedConfiguration;
    private readonly IDutyContentService _dutyContentService;

    public DutyConfigurationService(Configuration savedConfiguration, IDutyContentService dutyContentService)
    {
        _savedConfiguration = savedConfiguration;
        _dutyContentService = dutyContentService;
        RotationConfiguration = ConfigurationCopier.CreateRotationCopy(savedConfiguration);
        Refresh();
    }

    public Configuration RotationConfiguration { get; }

    public void Refresh()
    {
        ConfigurationCopier.CopyOnto(RotationConfiguration, _savedConfiguration);

        if (!_savedConfiguration.EnableAutoDutyConfig)
            return;

        var profile = _dutyContentService.EffectiveProfile;
        if (profile == EffectiveDutyProfile.None)
            return;

        ConfigurationPresets.ApplyDutyProfile(RotationConfiguration, profile);
    }
}
