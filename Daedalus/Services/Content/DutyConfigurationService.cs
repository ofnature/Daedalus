using System;
using Daedalus;
using Daedalus.Config;

namespace Daedalus.Services.Content;

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

    /// <summary>
    /// Optional overlay for the live party target mode (Focus / Split / Kill Adds). Supplied by the
    /// plugin from <c>PartyTargetingCoordinator</c>; returns an action that mutates the effective
    /// targeting config for the local toon, or null when no mode is active / the toon is exempt.
    /// Applied last (after the per-fight override) and re-run on every <see cref="Refresh"/>; the
    /// bus triggers a Refresh whenever the mode changes so this stays current mid-duty.
    /// </summary>
    public Func<Action<TargetingConfig>?>? PartyModeOverlayProvider { get; set; }

    public void Refresh()
    {
        ConfigurationCopier.CopyOnto(RotationConfiguration, _savedConfiguration);

        if (_savedConfiguration.EnableAutoDutyConfig)
        {
            var profile = _dutyContentService.EffectiveProfile;
            if (profile != EffectiveDutyProfile.None)
                ConfigurationPresets.ApplyDutyProfile(RotationConfiguration, profile);
        }

        // Per-fight strategy override (MVP: targeting). Applied last so it wins over the duty profile
        // for the specific fight, and only while in that duty. Never mutates the saved config.
        var raidStrategy = _savedConfiguration.Raid.GetActiveTargeting(_dutyContentService.CurrentTerritoryType);
        raidStrategy?.ApplyOnto(RotationConfiguration.Targeting);

        // Party target mode overlay wins over everything — it's an explicit, live operator command.
        PartyModeOverlayProvider?.Invoke()?.Invoke(RotationConfiguration.Targeting);
    }
}
