namespace Olympus.Services.Content;

/// <summary>
/// Builds the in-memory configuration snapshot used by rotations, overlaying duty-appropriate tuning.
/// </summary>
public interface IDutyConfigurationService
{
    /// <summary>Configuration snapshot consumed by rotations (stable reference, updated in-place).</summary>
    Configuration RotationConfiguration { get; }

    /// <summary>Re-syncs from saved configuration and re-applies the current duty profile overlay.</summary>
    void Refresh();
}
