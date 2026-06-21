namespace Olympus.Services.Content;

/// <summary>
/// Maps detected duty signals to the effective runtime configuration profile.
/// </summary>
public static class DutyContentClassifier
{
    /// <summary>
    /// Resolves the effective duty profile from primary and secondary signals.
    /// Primary ContentType is authoritative when known; secondary signals apply only when primary is ambiguous.
    /// </summary>
    public static EffectiveDutyProfile Resolve(
        DutyContentType dutyType,
        bool isHighEnd,
        int partyMemberCount,
        bool trustOrEmptyParty)
    {
        if (isHighEnd && dutyType is DutyContentType.Raid or DutyContentType.Trial)
            return EffectiveDutyProfile.HighEndRaid;

        return dutyType switch
        {
            DutyContentType.Dungeon => EffectiveDutyProfile.Dungeon,
            DutyContentType.Trial => EffectiveDutyProfile.Trial,
            DutyContentType.Raid => EffectiveDutyProfile.Raid,
            DutyContentType.OpenWorld or DutyContentType.Unknown when trustOrEmptyParty =>
                EffectiveDutyProfile.Dungeon,
            DutyContentType.Unknown when partyMemberCount > 4 =>
                EffectiveDutyProfile.Raid,
            _ => EffectiveDutyProfile.None,
        };
    }

    public static DutyContentType FromContentTypeRowId(uint rowId) => rowId switch
    {
        ContentTypeIds.Dungeons => DutyContentType.Dungeon,
        ContentTypeIds.Trials => DutyContentType.Trial,
        ContentTypeIds.Raids => DutyContentType.Raid,
        0 => DutyContentType.OpenWorld,
        _ => DutyContentType.Unknown,
    };
}
