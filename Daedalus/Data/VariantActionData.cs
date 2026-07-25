using System.Collections.Generic;

namespace Daedalus.Data;

/// <summary>The logical variant actions (each may have several tier-specific action IDs).</summary>
public enum VariantAction : byte
{
    Cure,
    Ultimatum,
    Raise,
    RaiseII,
    SpiritDart,
    Rampart,
    EagleEyeShot,
}

/// <summary>
/// One variant action: the instance grants its Set status when the player selected it in
/// the V&amp;C Dungeon Finder; the action ID differs per dungeon tier (the duty-bar slot
/// resolves which). IsGcd tooltip-verified: Cure/Raise are Spells, the rest Abilities.
/// </summary>
public sealed record VariantActionDef(
    VariantAction Kind,
    string Name,
    uint SetStatusId,
    uint[] ActionIds,
    string SelectableBy,
    bool IsGcd);

/// <summary>
/// Variant dungeon duty-action data (docs/variant-actions-plan.md). Action/status IDs
/// verified against the RSR generated tables; role availability against the V&amp;C wiki;
/// cast/recast behavior against in-game tooltips (2026-07-25).
/// </summary>
public static class VariantActionData
{
    /// <summary>Variant/Criterion territories: Sil'dihn, Mount Rokkon, Aloalo Island,
    /// The Merchant's Tale (+ Advanced).</summary>
    public static readonly IReadOnlySet<ushort> VariantTerritoryIds = new HashSet<ushort>
    {
        1069, 1137, 1176, 1315, 1316,
    };

    // Debuffs/buffs the actions apply (variant-specific status rows, per RSR).
    public const uint SustainedDamageStatusId = 3359;   // Spirit Dart DoT (30s)
    public const uint RehabilitationStatusId = 3367;    // Cure regen (doubles the next Cure)
    public const uint VulnerabilityDownStatusId = 3360; // Rampart (60s)

    public static readonly IReadOnlyList<VariantActionDef> All =
    [
        new(VariantAction.Cure, "Variant Cure", 3565,
            [29729, 33862, 46939],
            "Tank, Melee, Phys. Ranged, Caster", IsGcd: true),

        new(VariantAction.Ultimatum, "Variant Ultimatum", 3566,
            [29730],
            "All roles", IsGcd: false),

        new(VariantAction.Raise, "Variant Raise", 3567,
            [29731],
            "Tank, Melee, Phys. Ranged, Caster", IsGcd: true),

        new(VariantAction.RaiseII, "Variant Raise II", 3567,
            [29734],
            "Criterion only", IsGcd: true),

        new(VariantAction.SpiritDart, "Variant Spirit Dart", 3568,
            [29732, 33863, 46940],
            "Tank, Healer", IsGcd: false),

        new(VariantAction.Rampart, "Variant Rampart", 3569,
            [29733, 33864, 46941],
            "Healer, Melee, Phys. Ranged, Caster", IsGcd: false),

        new(VariantAction.EagleEyeShot, "Variant Eagle Eye Shot", 4892,
            [46942],
            "All roles", IsGcd: false),
    ];

    /// <summary>Definition lookup by logical action.</summary>
    public static VariantActionDef Get(VariantAction kind)
    {
        foreach (var def in All)
        {
            if (def.Kind == kind)
                return def;
        }

        // Unreachable: All covers every enum member (locked by tests).
        return All[0];
    }
}
