namespace Olympus.Services.Content;

/// <summary>
/// Broad duty classification derived from Lumina ContentType sheet row ids.
/// </summary>
public enum DutyContentType : byte
{
    Unknown = 0,
    OpenWorld = 1,
    Dungeon = 2,
    Trial = 4,
    Raid = 5,
}
