using System.Collections.Generic;

namespace Daedalus.Data;

/// <summary>
/// Where to catch one Bestiary beast. <see cref="Zone"/> and the map coordinates are the first
/// overworld spot the guide gives; null when the beast only comes from a duty, a FATE without
/// coordinates, the Crucible vendor or the start of the job — those rows have text but no flag.
/// </summary>
public sealed record BstCaptureSpot(int BestiaryNo, string Where, string? Zone, float MapX, float MapY)
{
    /// <summary>There is an overworld spot to drop a map flag on.</summary>
    public bool CanFlag => Zone != null;
}

/// <summary>
/// Capture locations for the 50 beasts, from Icy Veins' <i>Beast Summary</i> (2026-09-10) and the
/// community's easier alternatives it lists. Coordinates are the map numbers players read
/// ("X: 22.9 Y: 15.9"). Full notes and the capture rules: docs/beastmaster-rotation-and-pets.md §4b.
/// </summary>
public static class BstCaptureSpots
{
    public static readonly IReadOnlyList<BstCaptureSpot> All =
    [
        new(1, "Starting beast, unlocked automatically", null, 0f, 0f),
        new(2, "Central Shroud (22.9, 15.9) · White squirrel B rank, Central Shroud", "Central Shroud", 22.9f, 15.9f),
        new(3, "Middle La Noscea (24.9, 24.1)", "Middle La Noscea", 24.9f, 24.1f),
        new(4, "Middle La Noscea (19.4, 18.5)", "Middle La Noscea", 19.4f, 18.5f),
        new(5, "North Shroud (28.3, 25.1)", "North Shroud", 28.3f, 25.1f),
        new(6, "Lower La Noscea (31.0, 15.0)", "Lower La Noscea", 31.0f, 15.0f),
        new(7, "Western Thanalan (23.4, 27.4)", "Western Thanalan", 23.4f, 27.4f),
        new(8, "Central Shroud (19.7, 18.6)", "Central Shroud", 19.7f, 18.6f),
        new(9, "Middle La Noscea (17.4, 13.9)", "Middle La Noscea", 17.4f, 13.9f),
        new(10, "Central Thanalan · Level 1 bees outside Ul'dah", null, 0f, 0f),
        new(11, "Western Thanalan", null, 0f, 0f),
        new(12, "Middle La Noscea (21.8, 18.4)", "Middle La Noscea", 21.8f, 18.4f),
        new(13, "Central Shroud (19.0, 27.0)", "Central Shroud", 19.0f, 27.0f),
        new(14, "Middle La Noscea (21.7, 19.5)", "Middle La Noscea", 21.7f, 19.5f),
        new(15, "Western Thanalan (16.9, 13.8) · Crucible vendor (gourds)", "Western Thanalan", 16.9f, 13.8f),
        new(16, "Western La Noscea (21.9, 23.4) · Halatali. The overworld spawns are heavily contested right now, so the dungeon is often faster.", "Western La Noscea", 21.9f, 23.4f),
        new(17, "Copperbell Mines · Guildhest: More than a Feeler (level 30) · Slime B rank, Western Thanalan · Crucible vendor (gourds)", null, 0f, 0f),
        new(18, "Halatali · Crucible vendor (gourds)", null, 0f, 0f),
        new(19, "Lower La Noscea (26.6, 15.3) · Bat B rank, Lower La Noscea", "Lower La Noscea", 26.6f, 15.3f),
        new(20, "Central Shroud (22.9, 26.1), look for Roselets", "Central Shroud", 22.9f, 26.1f),
        new(21, "Western La Noscea", null, 0f, 0f),
        new(22, "Western Thanalan (28.5, 24.9)", "Western Thanalan", 28.5f, 24.9f),
        new(23, "Southern Thanalan (23.8, 12.4), by the Sunken Temple of Qarn entrance · FATE: That Which Binds Us (16.5, 29.3), Lunar Golems · FATE: Soul Man, Southern Thanalan", "Southern Thanalan", 23.8f, 12.4f),
        new(24, "Eastern La Noscea (29.2, 35.1)", "Eastern La Noscea", 29.2f, 35.1f),
        new(25, "Central Thanalan (22.7, 29.3). Five spawns spread across the Spineless Basin.", "Central Thanalan", 22.7f, 29.3f),
        new(26, "Middle La Noscea (18.5, 17.2)", "Middle La Noscea", 18.5f, 17.2f),
        new(27, "Western Thanalan (17.5, 15.0)", "Western Thanalan", 17.5f, 15.0f),
        new(28, "Southern Thanalan (19.5, 32.4) · Crucible vendor (gourds)", "Southern Thanalan", 19.5f, 32.4f),
        new(29, "Central Thanalan (17.3, 23.4)", "Central Thanalan", 17.3f, 23.4f),
        new(30, "Lower La Noscea (21.1, 23.5). Spawns range from level 12 to 17 and are spread out, so the level 17 ones are the reliable pick.", "Lower La Noscea", 21.1f, 23.5f),
        new(31, "Lower La Noscea (23.9, 22.0)", "Lower La Noscea", 23.9f, 22.0f),
        new(32, "Eastern La Noscea (29.7, 24.7)", "Eastern La Noscea", 29.7f, 24.7f),
        new(33, "Upper La Noscea (9.2, 21.5), Master Coeurls · Coeurl camp near Camp Overlook", "Upper La Noscea", 9.2f, 21.5f),
        new(34, "Central Shroud (30.7, 20.1)", "Central Shroud", 30.7f, 20.1f),
        new(35, "Southern Thanalan (24.4, 39.3), Sundrakes · Crucible vendor (gourds)", "Southern Thanalan", 24.4f, 39.3f),
        new(36, "Central Shroud (27.9, 15.3)", "Central Shroud", 27.9f, 15.3f),
        new(37, "Cutter's Cry, first boss · Crucible vendor (gourds)", null, 0f, 0f),
        new(38, "Cutter's Cry, last boss · Trial: A Relic Reborn: The Chimera · FATE: Gorgimera, Northern Thanalan · Crucible vendor (gourds)", null, 0f, 0f),
        new(39, "Central Shroud (13.6, 21.8), Stropers · Halitostropers · Guildhest: More than a Feeler (level 30)", "Central Shroud", 13.6f, 21.8f),
        new(40, "Middle La Noscea (20.2, 18.7), Bogeys", "Middle La Noscea", 20.2f, 18.7f),
        new(41, "Central Shroud (26.6, 19.8), Black Efts", "Central Shroud", 26.6f, 19.8f),
        new(42, "Mor Dhona · Halatali, after the first boss", null, 0f, 0f),
        new(43, "A Relic Reborn: The Hydra · Crucible vendor (gourds)", null, 0f, 0f),
        new(44, "The Lost City of Amdapor · Crucible vendor (gourds)", null, 0f, 0f),
        new(45, "The Lost City of Amdapor · Crucible vendor (gourds)", null, 0f, 0f),
        new(46, "Pharos Sirius · Crucible vendor (gourds)", null, 0f, 0f),
        new(47, "Snowcloak · Crucible vendor (gourds)", null, 0f, 0f),
        new(48, "Sastasha (Hard) · Crucible vendor (gourds)", null, 0f, 0f),
        new(49, "The Second Coil of Bahamut - Turn 1 · Crucible vendor (gourds)", null, 0f, 0f),
        new(50, "The Labyrinth of the Ancients · Crucible vendor (gourds). Have everyone stop attacking around 5% so the whole alliance can land a Capture.", null, 0f, 0f),
    ];

    /// <summary>The spot for a Bestiary number (1–50), or null.</summary>
    public static BstCaptureSpot? ByBestiaryNo(int no) => no is >= 1 and <= 50 ? All[no - 1] : null;
}
