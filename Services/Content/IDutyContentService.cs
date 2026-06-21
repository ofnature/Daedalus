namespace Olympus.Services.Content;

/// <summary>
/// Detects the current duty type and effective runtime configuration profile.
/// </summary>
public interface IDutyContentService
{
    /// <summary>Primary duty classification from Lumina territory data.</summary>
    DutyContentType CurrentDuty { get; }

    /// <summary>Effective tuning profile for the current zone (None when auto config is off or open world).</summary>
    EffectiveDutyProfile EffectiveProfile { get; }

    /// <summary>Human-readable duty label for UI/debug.</summary>
    string DutyLabel { get; }

    /// <summary>Re-evaluates duty classification when the territory changes.</summary>
    void OnTerritoryChanged(ushort territoryType, bool isHighEndZone, int partyMemberCount);
}
